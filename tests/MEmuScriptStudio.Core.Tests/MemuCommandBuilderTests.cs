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
        StringAssert.StartsWith(command.Preview, "\"C:\\Program Files\\Microvirt\\MEmu\\memuc.exe\"");
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
    public void BuildAndroidShell_RejectsMissingTarget()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            builder.BuildAndroidShell(@"C:\MEmu\memuc.exe", -1, "input keyevent HOME"));
    }

    [TestMethod]
    public void BuildListVms_RejectsDifferentExecutable()
    {
        Assert.ThrowsException<ArgumentException>(() => builder.BuildListVms(@"C:\MEmu\other.exe"));
    }
}
