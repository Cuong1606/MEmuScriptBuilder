using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class ScriptStepDisplayNameTests
{
    [DataTestMethod]
    [DataRow(0, "Chờ · 0 ms")]
    [DataRow(3_000, "Chờ · 3 giây")]
    [DataRow(100_000, "Chờ · 1 phút 40 giây")]
    [DataRow(3_723_400, "Chờ · 1 giờ 2 phút 3 giây 400 ms")]
    public void DelayDisplayName_UsesSharedDurationFormatter(int durationMilliseconds, string expected)
    {
        var step = new DelayStep { Name = "Tên Delay cũ", DurationMilliseconds = durationMilliseconds };

        Assert.AreEqual(expected, ScriptStepDisplayName.Get(step));
        Assert.AreEqual("Tên Delay cũ", step.Name, "Displaying a legacy Delay must not mutate loaded JSON data.");
    }

    [TestMethod]
    public void NormalizeDelayName_ChangesOnlyDelayCanonicalName()
    {
        var delay = new DelayStep { Name = "Tên tùy chỉnh", DurationMilliseconds = 3_000 };
        var note = new NoteStep { Name = "Tên ghi chú", Text = "Nội dung" };

        Assert.IsTrue(ScriptStepDisplayName.NormalizeDelayName(delay));
        Assert.AreEqual("Chờ", delay.Name);
        Assert.IsFalse(ScriptStepDisplayName.NormalizeDelayName(delay));
        Assert.IsFalse(ScriptStepDisplayName.NormalizeDelayName(note));
        Assert.AreEqual("Tên ghi chú", ScriptStepDisplayName.Get(note));
        Assert.AreEqual("Tên ghi chú", note.Name);
    }
}
