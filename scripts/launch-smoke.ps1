param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateRange(1, 45)]
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
$applicationName = "MEmuScriptStudio.App"
$expectedWindowTitle = "MEmu Script Studio"
$requiredStableChecks = 4

if (-not ("SmokeWindowNativeMethods" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class SmokeWindowNativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rectangle);

    [DllImport("kernel32.dll")]
    public static extern void SetLastError(uint errorCode);
}
"@
}

function Invoke-WindowHandleEnumeration {
    param(
        [AllowNull()][scriptblock]$EnumWindowsInvoker,
        [AllowNull()][scriptblock]$LastErrorProvider,
        [AllowNull()][scriptblock]$CallbackTestHook
    )

    $windowHandles = [System.Collections.Generic.List[IntPtr]]::new()
    $callbackState = [pscustomobject]@{ Exception = $null }
    $callback = [SmokeWindowNativeMethods+EnumWindowsProc]{
        param([IntPtr]$windowHandle, [IntPtr]$state)

        try {
            if ($null -ne $CallbackTestHook) {
                & $CallbackTestHook $windowHandle
            }
            $windowHandles.Add($windowHandle)
        }
        catch {
            if ($null -eq $callbackState.Exception) {
                $callbackState.Exception = $_.Exception
            }
        }
        return $true
    }

    if ($null -eq $EnumWindowsInvoker) {
        [SmokeWindowNativeMethods]::SetLastError(0)
        $enumerationSucceeded = [SmokeWindowNativeMethods]::EnumWindows($callback, [IntPtr]::Zero)
        $nativeErrorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    }
    else {
        $enumerationSucceeded = & $EnumWindowsInvoker $callback
        $nativeErrorCode = if ($null -eq $LastErrorProvider) { 0 } else { & $LastErrorProvider }
    }
    [GC]::KeepAlive($callback)

    if ($null -ne $callbackState.Exception -or (-not $enumerationSucceeded -and $nativeErrorCode -ne 0)) {
        $nativeMessage = [ComponentModel.Win32Exception]::new([int]$nativeErrorCode).Message
        $callbackMessage = if ($null -eq $callbackState.Exception) { "<none>" } else { $callbackState.Exception.Message }
        $failureKind = if (-not $enumerationSucceeded -and $nativeErrorCode -ne 0) { "EnumWindows failed" } else { "EnumWindows callback failed" }
        throw "$failureKind. NativeErrorCode=$nativeErrorCode NativeErrorMessage='$nativeMessage' CallbackException='$callbackMessage'"
    }

    return @($windowHandles)
}

function Get-ProcessTopLevelWindows {
    param([int]$ProcessId)

    $windows = [System.Collections.Generic.List[object]]::new()
    $windowHandles = @(Invoke-WindowHandleEnumeration)
    foreach ($windowHandle in $windowHandles) {
        [uint32]$windowProcessId = 0
        [void][SmokeWindowNativeMethods]::GetWindowThreadProcessId($windowHandle, [ref]$windowProcessId)
        if ($windowProcessId -ne $ProcessId) {
            continue
        }

        $titleLength = [SmokeWindowNativeMethods]::GetWindowTextLength($windowHandle)
        $titleBuilder = [System.Text.StringBuilder]::new([Math]::Max(1, $titleLength + 1))
        [void][SmokeWindowNativeMethods]::GetWindowText($windowHandle, $titleBuilder, $titleBuilder.Capacity)

        $classBuilder = [System.Text.StringBuilder]::new(512)
        [void][SmokeWindowNativeMethods]::GetClassName($windowHandle, $classBuilder, $classBuilder.Capacity)

        $rectangle = [SmokeWindowNativeMethods+RECT]::new()
        $hasRectangle = [SmokeWindowNativeMethods]::GetWindowRect($windowHandle, [ref]$rectangle)
        $rectangleText = if ($hasRectangle) {
            "[$($rectangle.Left),$($rectangle.Top),$($rectangle.Right),$($rectangle.Bottom)]"
        }
        else {
            "Unavailable"
        }

        $windows.Add([pscustomobject]@{
            Handle = $windowHandle
            ProcessId = [int]$windowProcessId
            Title = $titleBuilder.ToString()
            ClassName = $classBuilder.ToString()
            IsWindow = [SmokeWindowNativeMethods]::IsWindow($windowHandle)
            IsVisible = [SmokeWindowNativeMethods]::IsWindowVisible($windowHandle)
            IsEnabled = [SmokeWindowNativeMethods]::IsWindowEnabled($windowHandle)
            Owner = [SmokeWindowNativeMethods]::GetWindow($windowHandle, 4)
            Rectangle = $rectangleText
        })
    }
    return @($windows)
}

function Test-MainWindowCandidate {
    param(
        [AllowNull()][object]$Candidate,
        [int]$ExpectedProcessId
    )

    if ($null -eq $Candidate) {
        return $false
    }

    $isWpfMainClass = $Candidate.ClassName -eq "HwndWrapper" -or
        $Candidate.ClassName.StartsWith("HwndWrapper[", [StringComparison]::Ordinal)
    return $Candidate.Handle -ne [IntPtr]::Zero -and
        $Candidate.ProcessId -eq $ExpectedProcessId -and
        $Candidate.Title -ceq $expectedWindowTitle -and
        $isWpfMainClass -and
        $Candidate.IsWindow -and
        $Candidate.IsVisible -and
        $Candidate.IsEnabled -and
        $Candidate.Owner -eq [IntPtr]::Zero
}

function Get-ReadyMainWindowCandidate {
    param([int]$ProcessId)

    return @(Get-ProcessTopLevelWindows -ProcessId $ProcessId |
        Where-Object { Test-MainWindowCandidate -Candidate $_ -ExpectedProcessId $ProcessId } |
        Select-Object -First 1)[0]
}

function Update-ReadyWindowStability {
    param(
        [AllowNull()][object]$Candidate,
        [IntPtr]$PreviousHandle,
        [int]$ConsecutiveCount,
        [int]$RequiredCount = 4,
        [bool]$IsResponding = $true
    )

    if ($null -eq $Candidate -or -not $IsResponding) {
        return [pscustomobject]@{ Handle = [IntPtr]::Zero; Count = 0; IsReady = $false }
    }

    $nextCount = if ($Candidate.Handle -eq $PreviousHandle) { $ConsecutiveCount + 1 } else { 1 }
    return [pscustomobject]@{
        Handle = $Candidate.Handle
        Count = $nextCount
        IsReady = $nextCount -ge $RequiredCount
    }
}

function Get-ShortWindowDiagnostic {
    param([int]$ProcessId)

    $windows = @(Get-ProcessTopLevelWindows -ProcessId $ProcessId)
    if ($windows.Count -eq 0) {
        return "TopLevelWindows=0"
    }

    $summaries = @($windows | Select-Object -First 4 | ForEach-Object {
        "HWND=$($_.Handle) Title='$($_.Title)' Class='$($_.ClassName)' Visible=$($_.IsVisible) Enabled=$($_.IsEnabled) Owner=$($_.Owner)"
    })
    return "TopLevelWindows=$($windows.Count) " + ($summaries -join "; ")
}

function Stop-LaunchedOrphan {
    param([System.Diagnostics.Process]$Process)

    try {
        $Process.Refresh()
        if ($Process.HasExited) {
            return "OrphanStopped=False"
        }
        $Process.Kill()
        [void]$Process.WaitForExit(5000)
        return "OrphanStopped=$($Process.HasExited)"
    }
    catch {
        return "OrphanStopped=False StopError='$($_.Exception.Message)'"
    }
}

if ($MyInvocation.InvocationName -eq ".") {
    return
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $repositoryRoot "src\MEmuScriptStudio.App\bin\$Configuration\net8.0-windows\MEmuScriptStudio.App.exe"

if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    Write-Output "TIMEOUT executable not found: $applicationPath"
    exit 1
}

$existingProcesses = @(Get-Process -Name $applicationName -ErrorAction SilentlyContinue)
if ($existingProcesses.Count -gt 0) {
    $existingIds = ($existingProcesses | Select-Object -ExpandProperty Id) -join ","
    Write-Output "TIMEOUT application is already running; no new process opened. Existing PID(s): $existingIds"
    exit 1
}

try {
    $process = Start-Process -FilePath $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath) -PassThru
}
catch {
    Write-Output "TIMEOUT application could not be opened: $($_.Exception.Message)"
    exit 1
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$responding = $false
$stableHandle = [IntPtr]::Zero
$stableChecks = 0
do {
    try {
        $process.Refresh()
        if ($process.HasExited) {
            Write-Output "TIMEOUT process exited before MainWindow was ready. PID=$($process.Id) ExitCode=$($process.ExitCode)"
            exit 1
        }

        $responding = $process.Responding
        $candidate = Get-ReadyMainWindowCandidate -ProcessId $process.Id
        $stability = Update-ReadyWindowStability -Candidate $candidate -PreviousHandle $stableHandle -ConsecutiveCount $stableChecks -RequiredCount $requiredStableChecks -IsResponding $responding
        $stableHandle = $stability.Handle
        $stableChecks = $stability.Count
        if ($stability.IsReady) {
            Write-Output "READY PID=$($process.Id) Responding=$responding HWND=$($candidate.Handle) Title='$($candidate.Title)' Class='$($candidate.ClassName)' Rectangle=$($candidate.Rectangle) StableChecks=$stableChecks"
            exit 0
        }
    }
    catch {
        $stateError = $_.Exception.Message
        $diagnostic = try { Get-ShortWindowDiagnostic -ProcessId $process.Id } catch { "WindowDiagnosticError='$($_.Exception.Message)'" }
        $cleanup = Stop-LaunchedOrphan -Process $process
        Write-Output "TIMEOUT process/window state could not be read. PID=$($process.Id) Error='$stateError' $diagnostic $cleanup"
        exit 1
    }

    Start-Sleep -Milliseconds 250
} while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

$diagnostic = try { Get-ShortWindowDiagnostic -ProcessId $process.Id } catch { "WindowDiagnosticError='$($_.Exception.Message)'" }
$cleanup = Stop-LaunchedOrphan -Process $process
Write-Output "TIMEOUT PID=$($process.Id) Responding=$responding StableChecks=$stableChecks $diagnostic $cleanup"
exit 1
