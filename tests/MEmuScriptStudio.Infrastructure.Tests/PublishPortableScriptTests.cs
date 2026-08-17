using System.Diagnostics;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class PublishPortableScriptTests
{
    [TestMethod]
    public async Task VersionValidation_AcceptsThreePartVersionsAndRejectsUnsafeValues()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            foreach ($validVersion in @('0.0.0', '1.0.0', '12.34.56')) {
                Assert-ValidReleaseVersion -Value $validVersion
            }

            foreach ($invalidVersion in @('', '1', '1.0', '01.0.0', '1.00.0', '1.0.0-beta', '1.0.0/../escape')) {
                $wasRejected = $false
                try { Assert-ValidReleaseVersion -Value $invalidVersion }
                catch { $wasRejected = $true }
                if (-not $wasRejected) { throw "Unsafe version was accepted: '$invalidVersion'" }
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task ReleasePathValidation_AllowsOnlyDirectChildrenOfPortableRoot()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $portableRoot = Join-Path ([IO.Path]::GetTempPath()) 'portable-path-policy'
            $expected = [IO.Path]::GetFullPath((Join-Path $portableRoot 'MEmuScriptStudio-1.0.0-win-x64'))
            $actual = Assert-DirectChildPath -CandidatePath $expected -ExpectedParent $portableRoot
            if ($actual -ne $expected) { throw "Safe path changed unexpectedly: '$actual'" }

            foreach ($unsafePath in @(
                $portableRoot,
                (Join-Path $portableRoot 'nested\release'),
                (Join-Path ([IO.Path]::GetDirectoryName($portableRoot)) 'portable-sibling\release')
            )) {
                $wasRejected = $false
                try { [void](Assert-DirectChildPath -CandidatePath $unsafePath -ExpectedParent $portableRoot) }
                catch { $wasRejected = $true }
                if (-not $wasRejected) { throw "Unsafe path was accepted: '$unsafePath'" }
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task ReleaseCleanup_RejectsJunctionWithoutTouchingItsTarget()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('portable-junction-policy-' + [Guid]::NewGuid().ToString('N'))
            $portableRoot = Join-Path $testRoot 'portable'
            $outsideTarget = Join-Path $testRoot 'outside-target'
            $releasePath = Join-Path $portableRoot 'MEmuScriptStudio-1.0.0-win-x64'
            [IO.Directory]::CreateDirectory($portableRoot) | Out-Null
            [IO.Directory]::CreateDirectory($outsideTarget) | Out-Null
            $markerPath = Join-Path $outsideTarget 'must-survive.txt'
            [IO.File]::WriteAllText($markerPath, 'keep')
            try {
                [void](New-Item -ItemType Junction -Path $releasePath -Target $outsideTarget)
                $wasRejected = $false
                try { Remove-SafeReleaseOutput -CandidatePath $releasePath -ExpectedParent $portableRoot -TrustedRoot $testRoot }
                catch { $wasRejected = $_.Exception.Message -like '*reparse point*' }
                if (-not $wasRejected) { throw 'Release cleanup did not reject the junction.' }
                if (-not [IO.File]::Exists($markerPath)) { throw 'Release cleanup traversed the junction target.' }
            }
            finally {
                if ([IO.Directory]::Exists($releasePath)) { [IO.Directory]::Delete($releasePath) }
                if ([IO.Directory]::Exists($testRoot)) { [IO.Directory]::Delete($testRoot, $true) }
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task ReleaseCleanup_RejectsJunctionInPortableParentChain()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('portable-parent-junction-policy-' + [Guid]::NewGuid().ToString('N'))
            $repositoryRoot = Join-Path $testRoot 'repository'
            $artifactsPath = Join-Path $repositoryRoot 'artifacts'
            $outsideTarget = Join-Path $testRoot 'outside-target'
            $portableRoot = Join-Path $artifactsPath 'portable'
            $releasePath = Join-Path $portableRoot 'MEmuScriptStudio-1.0.0-win-x64'
            [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
            $outsideReleasePath = Join-Path $outsideTarget 'portable\MEmuScriptStudio-1.0.0-win-x64'
            [IO.Directory]::CreateDirectory($outsideReleasePath) | Out-Null
            $markerPath = Join-Path $outsideReleasePath 'must-survive.txt'
            [IO.File]::WriteAllText($markerPath, 'keep')
            try {
                [void](New-Item -ItemType Junction -Path $artifactsPath -Target $outsideTarget)
                $wasRejected = $false
                try {
                    Remove-SafeReleaseOutput -CandidatePath $releasePath -ExpectedParent $portableRoot -TrustedRoot $repositoryRoot
                }
                catch { $wasRejected = $_.Exception.Message -like '*path chain contains a reparse point*' }
                if (-not $wasRejected) { throw 'Release cleanup did not reject the junction ancestor.' }
                if (-not [IO.File]::Exists($markerPath)) { throw 'Release cleanup traversed the junction ancestor.' }
            }
            finally {
                if ([IO.Directory]::Exists($artifactsPath)) { [IO.Directory]::Delete($artifactsPath) }
                if ([IO.Directory]::Exists($testRoot)) { [IO.Directory]::Delete($testRoot, $true) }
            }
            """);

        AssertPowerShellPassed(result);
    }

    [TestMethod]
    public async Task PortableAudit_RequiresMinimalAdbBundleGuideAndLegalFiles()
    {
        var result = await RunPowerShellAssertionsAsync(
            """
            $portableRoot = Join-Path ([IO.Path]::GetTempPath()) ('portable-adb-audit-' + [Guid]::NewGuid().ToString('N'))
            try {
                foreach ($relativePath in @(
                    'MEmuScriptStudio.exe',
                    'README.txt',
                    'Create Desktop Shortcut.cmd',
                    'HUONG-DAN-SU-DUNG.md',
                    'tools\adb\adb.exe',
                    'tools\adb\AdbWinApi.dll',
                    'tools\adb\AdbWinUsbApi.dll',
                    'tools\adb\LICENSE.txt',
                    'tools\adb\NOTICE.txt'
                )) {
                    $path = Join-Path $portableRoot $relativePath
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
                    [IO.File]::WriteAllText($path, 'fixture')
                }

                Assert-PortableContents -PortableDirectory $portableRoot

                $unexpectedPath = Join-Path $portableRoot 'tools\adb\fastboot.exe'
                [IO.File]::WriteAllText($unexpectedPath, 'not allowed')
                $wasRejected = $false
                try { Assert-PortableContents -PortableDirectory $portableRoot }
                catch { $wasRejected = $_.Exception.Message -like '*unexpected file*' }
                if (-not $wasRejected) { throw 'Portable audit accepted an extra Android SDK tool.' }
            }
            finally {
                if ([IO.Directory]::Exists($portableRoot)) { [IO.Directory]::Delete($portableRoot, $true) }
            }
            """);

        AssertPowerShellPassed(result);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunPowerShellAssertionsAsync(
        string assertions)
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "publish-portable.ps1");
        var escapedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        var command = $"$ErrorActionPreference = 'Stop'; . '{escapedScriptPath}'; {assertions}";
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
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
            Assert.Fail("PowerShell portable release policy test timed out.");
        }

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
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
