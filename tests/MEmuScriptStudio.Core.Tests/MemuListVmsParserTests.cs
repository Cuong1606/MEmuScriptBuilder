using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MemuListVmsParserTests
{
    private readonly MemuListVmsParser parser = new();

    [TestMethod]
    public void Parse_ReadsIndexNameRunningStateAndPid()
    {
        const string output = "0,MEmu,1,4321\r\n7,Work profile,0,0\r\n";

        var result = parser.Parse(output);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(0, result[0].Index);
        Assert.AreEqual("MEmu", result[0].Name);
        Assert.IsTrue(result[0].IsRunning);
        Assert.AreEqual(4321, result[0].ProcessId);
        Assert.AreEqual(7, result[1].Index);
        Assert.IsFalse(result[1].IsRunning);
        Assert.IsNull(result[1].ProcessId);
    }

    [TestMethod]
    public void Parse_SupportsQuotedNamesAndTextState()
    {
        const string output = "12,\"QA, Android 9\",running,9988";

        var result = parser.Parse(output);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("QA, Android 9", result[0].Name);
        Assert.IsTrue(result[0].IsRunning);
    }

    [TestMethod]
    public void Parse_IgnoresDiagnosticLines()
    {
        const string output = "MEmu console header\r\ninvalid,line\r\n2,Valid,started\r\n";

        var result = parser.Parse(output);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].Index);
    }

    [TestMethod]
    public void Parse_ReturnsEmptyForBlankOutput()
    {
        Assert.AreEqual(0, parser.Parse(" \r\n").Count);
    }
}
