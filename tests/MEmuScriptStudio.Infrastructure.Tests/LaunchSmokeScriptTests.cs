using System.Diagnostics;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class LaunchSmokeScriptTests
{
    [TestMethod]
    public async Task WindowEnumeration_CallbackAlwaysContinuesAndCollectsEveryHandle()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $callbackReturns = [System.Collections.Generic.List[bool]]::new()
            $invoker = {
                param($callback)
                $callbackReturns.Add($callback.Invoke([IntPtr]101, [IntPtr]::Zero))
                $callbackReturns.Add($callback.Invoke([IntPtr]202, [IntPtr]::Zero))
                return $true
            }

            $handles = @(Invoke-WindowHandleEnumeration -EnumWindowsInvoker $invoker -LastErrorProvider { 0 })
            if ($callbackReturns.Count -ne 2 -or $callbackReturns.Contains($false)) {
                throw 'The callback actively stopped enumeration.'
            }
            if ($handles.Count -ne 2 -or $handles[0] -ne [IntPtr]101 -or $handles[1] -ne [IntPtr]202) {
                throw 'The callback did not collect every HWND before filtering.'
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task WindowEnumeration_CallbackExceptionIsCapturedReportedAndDoesNotCrossNativeBoundary()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $callbackReturns = [System.Collections.Generic.List[bool]]::new()
            $invoker = {
                param($callback)
                $callbackReturns.Add($callback.Invoke([IntPtr]101, [IntPtr]::Zero))
                $callbackReturns.Add($callback.Invoke([IntPtr]202, [IntPtr]::Zero))
                return $true
            }

            $message = $null
            try {
                [void](Invoke-WindowHandleEnumeration -EnumWindowsInvoker $invoker -LastErrorProvider { 0 } -CallbackTestHook { throw 'collector exploded' })
            }
            catch {
                $message = $_.Exception.Message
            }

            if ($callbackReturns.Count -ne 2 -or $callbackReturns.Contains($false)) {
                throw 'A callback exception escaped or stopped native enumeration.'
            }
            if ($message -notlike '*EnumWindows callback failed*' -or
                $message -notlike '*NativeErrorCode=0*' -or
                $message -notlike "*CallbackException='collector exploded'*") {
                throw "Callback diagnostics were incomplete: $message"
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task WindowEnumeration_FalseWithZeroNativeErrorIsNotReportedAsFailure()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $handles = @(Invoke-WindowHandleEnumeration -EnumWindowsInvoker { param($callback) return $false } -LastErrorProvider { 0 })
            if ($handles.Count -ne 0) { throw 'Unexpected HWND was returned.' }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task WindowEnumeration_FalseWithNativeErrorReportsWin32Diagnostics()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $message = $null
            try {
                [void](Invoke-WindowHandleEnumeration -EnumWindowsInvoker { param($callback) return $false } -LastErrorProvider { 5 })
            }
            catch {
                $message = $_.Exception.Message
            }

            if ($message -notlike '*EnumWindows failed*' -or
                $message -notlike '*NativeErrorCode=5*' -or
                $message -notlike '*NativeErrorMessage=*' -or
                $message -notlike "*CallbackException='<none>'*") {
                throw "Native diagnostics were incomplete: $message"
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task MainWindowCandidate_RejectsEmptyTitleHiddenAndHelperWindows()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            function New-Candidate([string]$title, [string]$className, [bool]$visible, [bool]$enabled, [IntPtr]$owner) {
                [pscustomobject]@{
                    Handle = [IntPtr]123
                    ProcessId = 42
                    Title = $title
                    ClassName = $className
                    IsWindow = $true
                    IsVisible = $visible
                    IsEnabled = $enabled
                    Owner = $owner
                }
            }

            $emptyTitle = New-Candidate '' 'HwndWrapper[app;;id]' $true $true ([IntPtr]::Zero)
            $hidden = New-Candidate 'MEmu Script Studio' 'HwndWrapper[app;;id]' $false $true ([IntPtr]::Zero)
            $helperClass = New-Candidate 'MEmu Script Studio' 'HwndWrapperHelper' $true $true ([IntPtr]::Zero)
            $ownedHelper = New-Candidate 'MEmu Script Studio' 'HwndWrapper[app;;id]' $true $true ([IntPtr]456)

            if (Test-MainWindowCandidate $emptyTitle 42) { throw 'Accepted empty-title HWND.' }
            if (Test-MainWindowCandidate $hidden 42) { throw 'Accepted hidden HWND.' }
            if (Test-MainWindowCandidate $helperClass 42) { throw 'Accepted helper class HWND.' }
            if (Test-MainWindowCandidate $ownedHelper 42) { throw 'Accepted owned helper HWND.' }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task ReadyGate_AcceptsOnlyTheExactMainWindowAfterFourStableChecks()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $candidate = [pscustomobject]@{
                Handle = [IntPtr]123
                ProcessId = 42
                Title = 'MEmu Script Studio'
                ClassName = 'HwndWrapper[app;;id]'
                IsWindow = $true
                IsVisible = $true
                IsEnabled = $true
                Owner = [IntPtr]::Zero
            }
            if (-not (Test-MainWindowCandidate $candidate 42)) { throw 'Rejected the exact MainWindow.' }
            if (Test-MainWindowCandidate $candidate 41) { throw 'Accepted a window from another PID.' }

            $state = [pscustomobject]@{ Handle = [IntPtr]::Zero; Count = 0; IsReady = $false }
            $state = Update-ReadyWindowStability $candidate $state.Handle $state.Count 4 $false
            if ($state.IsReady -or $state.Count -ne 0) { throw 'Non-responding process advanced stability.' }
            1..3 | ForEach-Object {
                $state = Update-ReadyWindowStability $candidate $state.Handle $state.Count 4 $true
                if ($state.IsReady) { throw "READY before check $_." }
            }
            $state = Update-ReadyWindowStability $candidate $state.Handle $state.Count 4 $true
            if (-not $state.IsReady -or $state.Count -ne 4) { throw 'Did not become READY on check 4.' }

            $replacement = $candidate.PSObject.Copy()
            $replacement.Handle = [IntPtr]789
            $state = Update-ReadyWindowStability $replacement $state.Handle $state.Count 4 $true
            if ($state.IsReady -or $state.Count -ne 1) { throw 'A changed HWND did not reset stability.' }
            """);

        AssertPowerShellPassed(result);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunPowerShellAssertionsAsync(string assertions)
    {
        var repositoryRoot = FindRepositoryRoot();
        var launcherPath = Path.Combine(repositoryRoot, "scripts", "launch-smoke.ps1");
        var escapedLauncherPath = launcherPath.Replace("'", "''", StringComparison.Ordinal);
        var command = $"$ErrorActionPreference = 'Stop'; . '{escapedLauncherPath}'; {assertions}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("PowerShell launcher predicate test timed out.");
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return (process.ExitCode, standardOutput, standardError);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MEmuScriptStudio.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static void AssertPowerShellPassed((int ExitCode, string StandardOutput, string StandardError) result)
    {
        Assert.AreEqual(
            0,
            result.ExitCode,
            $"PowerShell assertion failed. stdout: {result.StandardOutput} stderr: {result.StandardError}");
    }
}
