using MEmuScriptStudio.Core.Formatting;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class DurationFormatterTests
{
    [DataTestMethod]
    [DataRow(0, "0 ms")]
    [DataRow(999, "999 ms")]
    [DataRow(100_000, "1 phút 40 giây")]
    [DataRow(3_723_400, "1 giờ 2 phút 3 giây 400 ms")]
    [DataRow(int.MaxValue, "596 giờ 31 phút 23 giây 647 ms")]
    public void FormatMilliseconds_OmitsZeroUnitsButKeepsZeroDuration(int value, string expected) =>
        Assert.AreEqual(expected, DurationFormatter.FormatMilliseconds(value));

    [TestMethod]
    public void FormatMilliseconds_RejectsNegativeDuration() =>
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => DurationFormatter.FormatMilliseconds(-1));
}
