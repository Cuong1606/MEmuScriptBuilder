param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateRange(1, 45)]
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
$applicationName = "MEmuScriptStudio.App"
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
$windowHandle = [IntPtr]::Zero
$windowTitle = ""
do {
    try {
        $process.Refresh()
        if ($process.HasExited) {
            Write-Output "TIMEOUT process exited before a window was ready. PID=$($process.Id)"
            exit 1
        }

        $responding = $process.Responding
        $windowHandle = $process.MainWindowHandle
        $windowTitle = $process.MainWindowTitle
        if ($windowHandle -ne [IntPtr]::Zero) {
            Write-Output "READY PID=$($process.Id) Responding=$responding MainWindowHandle=$windowHandle MainWindowTitle=$windowTitle"
            exit 0
        }
    }
    catch {
        Write-Output "TIMEOUT process state could not be read. PID=$($process.Id) Error=$($_.Exception.Message)"
        exit 1
    }

    Start-Sleep -Milliseconds 250
} while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

Write-Output "TIMEOUT PID=$($process.Id) Responding=$responding MainWindowHandle=$windowHandle MainWindowTitle=$windowTitle"
exit 1
