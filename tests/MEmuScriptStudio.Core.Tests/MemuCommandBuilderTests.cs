using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MemuCommandBuilderTests
{
    private readonly MemuCommandBuilder builder = new();

    [TestMethod]
    public void BuildListVms_UsesDirectExecutableAndSingleArgument()
    {
        var command = builder.BuildListVms(@"C:\Program Files\Microvirt\MEmu\memuc.exe");

        Assert.AreEqual(@"C:\Program Files\Microvirt\MEmu\memuc.exe", command.ExecutablePath);
        CollectionAssert.AreEqual(new[] { "listvms" }, command.Arguments.ToArray());
        Assert.AreEqual("\"C:\\Program Files\\Microvirt\\MEmu\\memuc.exe\" listvms", command.Preview);
    }

    [TestMethod]
    public void BuildAndroidShell_KeepsShellCommandAsOneArgument()
    {
        var command = builder.BuildAndroidShell(
            @"C:\MEmu\memuc.exe",
            7,
            "am start -a android.intent.action.VIEW -d \"https://example.com/a b\"");

        CollectionAssert.AreEqual(
            new[] { "-i", "7", "execcmd", "am start -a android.intent.action.VIEW -d \"https://example.com/a b\"" },
            command.Arguments.ToArray());
    }

    [TestMethod]
    public void Preview_QuotesEmbeddedQuotesAndTrailingBackslashesUsingWindowsRules()
    {
        var command = builder.BuildAndroidShell(
            @"C:\Program Files\MEmu\memuc.exe",
            2,
            "echo \"C:\\Folder With Space\\\"");

        Assert.AreEqual(
            "\"C:\\Program Files\\MEmu\\memuc.exe\" -i 2 execcmd \"echo \\\"C:\\Folder With Space\\\\\\\"\"",
            command.Preview);
    }

    [TestMethod]
    public void BuildAndroidShell_RejectsMissingTarget()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            builder.BuildAndroidShell(@"C:\MEmu\memuc.exe", -1, "input keyevent HOME"));
    }

    [TestMethod]
    public void BuildGetAppInfoList_UsesVerifiedDirectArgumentOrder()
    {
        var command = builder.BuildGetAppInfoList(@"C:\MEmu\memuc.exe", 7);
        CollectionAssert.AreEqual(new[] { "-i", "7", "getappinfolist" }, command.Arguments.ToArray());
    }

    [TestMethod]
    public void BuildListVms_RejectsDifferentExecutable()
    {
        Assert.ThrowsException<ArgumentException>(() => builder.BuildListVms(@"C:\MEmu\other.exe"));
    }
}
