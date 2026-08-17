using MEmuScriptStudio.Infrastructure.Android;
using MEmuScriptStudio.Infrastructure.MEmu;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class PathDiscoveryTests
{
    [TestMethod]
    public void AdbDiscovery_PrefersBundledRuntimeBeforeInstalledPlatformToolsAndMemu()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Portable\tools\adb\adb.exe",
            @"C:\AndroidSdk\platform-tools\adb.exe",
            @"C:\PathTools\adb.exe",
            @"C:\MEmu\adb.exe"
        };
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANDROID_SDK_ROOT"] = @"C:\AndroidSdk",
            ["PATH"] = @"C:\PathTools"
        };
        var discovery = new AdbPathDiscovery(
            name => environment.GetValueOrDefault(name),
            folder => folder == Environment.SpecialFolder.LocalApplicationData ? @"C:\Local" : @"C:\Programs",
            existing.Contains,
            () => @"C:\Portable");

        var result = discovery.FindAdbPath(@"C:\MEmu\memuc.exe");

        Assert.AreEqual(@"C:\Portable\tools\adb\adb.exe", result);
    }

    [TestMethod]
    public void AdbDiscovery_PrefersInstalledPlatformToolsWhenBundledRuntimeIsUnavailable()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\AndroidSdk\platform-tools\adb.exe",
            @"C:\MEmu\adb.exe"
        };
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANDROID_SDK_ROOT"] = @"C:\AndroidSdk"
        };
        var discovery = new AdbPathDiscovery(
            name => environment.GetValueOrDefault(name),
            _ => @"C:\Missing",
            existing.Contains,
            () => @"C:\Portable");

        var result = discovery.FindAdbPath(@"C:\MEmu\memuc.exe");

        Assert.AreEqual(@"C:\AndroidSdk\platform-tools\adb.exe", result);
    }

    [TestMethod]
    public void AdbDiscovery_FallsBackToMemuSiblingWhenPlatformToolsAreUnavailable()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\MEmu\adb.exe"
        };
        var discovery = new AdbPathDiscovery(
            _ => null,
            _ => @"C:\Missing",
            existing.Contains);

        var result = discovery.FindAdbPath(@"C:\MEmu\memuc.exe");

        Assert.AreEqual(@"C:\MEmu\adb.exe", result);
    }

    [TestMethod]
    public void MemucDiscovery_FindsStandardMicrovirtInstallation()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Microvirt\MEmu\memuc.exe"
        };
        var discovery = new MemucPathDiscovery(
            _ => null,
            folder => folder == Environment.SpecialFolder.ProgramFiles
                ? @"C:\Program Files"
                : @"C:\Program Files (x86)",
            existing.Contains);

        var result = discovery.FindMemucPath();

        Assert.AreEqual(@"C:\Program Files\Microvirt\MEmu\memuc.exe", result);
    }
}
