using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MemuListVmsParserTests
{
    private readonly MemuListVmsParser parser = new();

    [TestMethod]
    public void Parse_ReadsVerifiedRunningFixture()
    {
        var result = parser.Parse("0,MASTER,12126050,1,5676");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0, result[0].Index);
        Assert.AreEqual("MASTER", result[0].Name);
        Assert.IsTrue(result[0].IsRunning);
        Assert.AreEqual(5676, result[0].ProcessId);
    }

    [TestMethod]
    public void Parse_ReadsVerifiedStoppedFixture()
    {
        var result = parser.Parse("0,MASTER,0,0,0");

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(result[0].IsRunning);
        Assert.IsNull(result[0].ProcessId);
    }

    [TestMethod]
    public void Parse_ReadsMultipleMachinesAndQuotedTitle()
    {
        const string output = "0,MASTER,0,0,0\r\n7,\"QA, Android 9\",998877,1,4321";

        var result = parser.Parse(output);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(7, result[1].Index);
        Assert.AreEqual("QA, Android 9", result[1].Name);
        Assert.IsTrue(result[1].IsRunning);
        Assert.AreEqual(4321, result[1].ProcessId);
    }

    [TestMethod]
    public void Parse_IgnoresBlankLines()
    {
        const string output = "\r\n0,MASTER,0,0,0\r\n\r\n2,WORK,123,1,999\r\n";

        Assert.AreEqual(2, parser.Parse(output).Count);
    }

    [TestMethod]
    public void Parse_IgnoresMalformedLinesWithoutGuessingSchema()
    {
        const string output = "header\r\n" +
                              "1,TOO,FEW,0\r\n" +
                              "x,NAME,0,0,0\r\n" +
                              "1,NAME,handle,0,0\r\n" +
                              "1,NAME,0,running,0\r\n" +
                              "1,NAME,0,2,0\r\n" +
                              "1,NAME,0,1,pid\r\n" +
                              "1,\"UNFINISHED,0,1,22\r\n" +
                              "1,\"NAME\"junk,0,1,22\r\n" +
                              "1,NA\"ME,0,1,22\r\n" +
                              "3,VALID,0,0,0";

        var result = parser.Parse(output);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(3, result[0].Index);
    }

    [TestMethod]
    public void Parse_ReturnsEmptyForBlankOutput()
    {
        Assert.AreEqual(0, parser.Parse(" \r\n").Count);
    }
}
