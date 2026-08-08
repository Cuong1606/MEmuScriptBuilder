using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class ScriptStepCommandBuilderTests
{
    private readonly ScriptStepCommandBuilder builder = new(new MemuCommandBuilder());

    [TestMethod]
    public void BuildProcessCommand_GeneratesEveryExecutableMvpStep()
    {
        var cases = new (ScriptStep Step, string Expected)[]
        {
            (new AndroidShellStep { Name = "Shell", Command = "settings get system" }, "settings get system"),
            (new ForceStopStep { Name = "Stop", PackageName = "com.example.app" }, "am force-stop com.example.app"),
            (new OpenAppStep { Name = "Open", PackageName = "com.example.app", ActivityName = ".MainActivity" }, "am start -n com.example.app/.MainActivity"),
            (new TapStep { Name = "Tap", X = 12, Y = 34 }, "input tap 12 34"),
            (new HoldStep { Name = "Hold", X = 12, Y = 34, DurationMilliseconds = 750 }, "input swipe 12 34 12 34 750"),
            (new SwipeStep { Name = "Swipe", X1 = 1, Y1 = 2, X2 = 3, Y2 = 4, DurationMilliseconds = 500 }, "input swipe 1 2 3 4 500"),
            (new InputTextStep { Name = "Text", Text = "hello world%" }, "input text hello%sworld%25"),
            (new AndroidClipboardPasteStep { Name = "Paste" }, "input keyevent 279"),
            (new KeyEventStep { Name = "Home", Key = AndroidKeyEvent.Home }, "input keyevent KEYCODE_HOME")
        };

        foreach (var item in cases)
        {
            var command = builder.BuildProcessCommand(item.Step, @"C:\MEmu\memuc.exe", 7);
            CollectionAssert.AreEqual(new[] { "-i", "7", "execcmd", item.Expected }, command.Arguments.ToArray());
        }
    }

    [TestMethod]
    public void BuildPreview_RepresentsDelayNoteDisabledAndMissingTarget()
    {
        Assert.AreEqual("[Chờ 2 giây]", builder.BuildPreview(new DelayStep { Name = "Wait", DurationMilliseconds = 2000 }, null, null));
        Assert.AreEqual("[Chờ 1 phút 40 giây]", builder.BuildPreview(new DelayStep { Name = "Wait", DurationMilliseconds = 100_000 }, null, null));
        Assert.AreEqual("[Note] Explain", builder.BuildPreview(new NoteStep { Name = "Note", Text = "Explain" }, null, null));
        Assert.AreEqual("[Đã tắt]", builder.BuildPreview(new TapStep { Name = "Tap", IsEnabled = false }, null, null));
        Assert.AreEqual(
            "Chọn memuc.exe và một instance để xem preview.",
            builder.BuildPreview(new TapStep { Name = "Tap" }, null, null));
    }

    [TestMethod]
    public void InputTextWithEnter_BuildsTwoSeparateCommandsAndShowsBothInPreview()
    {
        var step = new InputTextStep
        {
            Name = "Submit",
            Text = "hello world",
            PressEnterAfterInput = true
        };

        var commands = builder.BuildProcessCommands(step, @"C:\MEmu\memuc.exe", 2);
        var preview = builder.BuildPreview(step, @"C:\MEmu\memuc.exe", 2);

        Assert.AreEqual(2, commands.Count);
        Assert.AreEqual("input text hello%sworld", commands[0].Arguments[^1]);
        Assert.AreEqual("input keyevent KEYCODE_ENTER", commands[1].Arguments[^1]);
        StringAssert.Contains(preview, commands[0].Preview);
        StringAssert.Contains(preview, commands[1].Preview);
        StringAssert.Contains(preview, Environment.NewLine);
    }

    [TestMethod]
    public void AndroidClipboardPasteWithEnter_BuildsTwoSeparateNumericKeyEvents()
    {
        var step = new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true };

        var commands = builder.BuildProcessCommands(step, @"C:\MEmu\memuc.exe", 2);

        Assert.AreEqual(2, commands.Count);
        Assert.AreEqual("input keyevent 279", commands[0].Arguments[^1]);
        Assert.AreEqual("input keyevent 66", commands[1].Arguments[^1]);
    }

    [TestMethod]
    public void BuildProcessCommand_RejectsNonProcessAndInvalidValues()
    {
        Assert.ThrowsException<InvalidOperationException>(() => builder.BuildProcessCommand(
            new DelayStep { Name = "Wait", DurationMilliseconds = 1 }, @"C:\MEmu\memuc.exe", 1));
        Assert.ThrowsException<ArgumentException>(() => builder.BuildProcessCommand(
            new ForceStopStep { Name = "Stop", PackageName = " " }, @"C:\MEmu\memuc.exe", 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => builder.BuildProcessCommand(
            new SwipeStep { Name = "Swipe", DurationMilliseconds = -1 }, @"C:\MEmu\memuc.exe", 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => builder.BuildProcessCommand(
            new HoldStep { Name = "Hold", DurationMilliseconds = 0 }, @"C:\MEmu\memuc.exe", 1));
    }

    [TestMethod]
    public void Validate_IgnoresHiddenTimeoutForDelayAndNote()
    {
        builder.Validate(new DelayStep { Name = "Wait", DurationMilliseconds = 100, TimeoutSeconds = 0 });
        builder.Validate(new NoteStep { Name = "Note", Text = "Info", TimeoutSeconds = 0 });
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            builder.Validate(new DelayStep { Name = "Wait", DurationMilliseconds = -1, TimeoutSeconds = 0 }));
    }

    [TestMethod]
    public void KeyEvent_RecentAppsAndLegacyMenu_UseRequiredNumericKeycodes()
    {
        var recentStep = new KeyEventStep { Name = "Recent", Key = AndroidKeyEvent.RecentApps };
        var recentCommand = builder.BuildProcessCommand(recentStep, @"C:\MEmu\memuc.exe", 7);
        var recentPreview = builder.BuildPreview(recentStep, @"C:\MEmu\memuc.exe", 7);
        var menuCommand = builder.BuildProcessCommand(
            new KeyEventStep { Name = "Menu", Key = AndroidKeyEvent.Menu }, @"C:\MEmu\memuc.exe", 7);

        Assert.AreEqual("input keyevent 187", recentCommand.Arguments[^1]);
        Assert.AreEqual(recentCommand.Preview, recentPreview);
        StringAssert.Contains(recentPreview, "input keyevent 187");
        Assert.AreEqual("input keyevent 82", menuCommand.Arguments[^1]);
    }

    [DataTestMethod]
    [DataRow("hello;reboot")]
    [DataRow("hello&reboot")]
    [DataRow("hello|reboot")]
    [DataRow("hello$USER")]
    [DataRow("hello`id`")]
    [DataRow("hello>file")]
    [DataRow("hello\nreboot")]
    [DataRow("hello\\reboot")]
    [DataRow("hello\"reboot")]
    public void BuildProcessCommand_RejectsShellMetacharactersInInputText(string text)
    {
        Assert.ThrowsException<ArgumentException>(() => builder.BuildProcessCommand(
            new InputTextStep { Name = "Text", Text = text }, @"C:\MEmu\memuc.exe", 1));
    }

    [DataTestMethod]
    [DataRow("com.example.app;reboot", ".MainActivity")]
    [DataRow("com.example.app", ".MainActivity|reboot")]
    [DataRow("com.example.$(id)", ".MainActivity")]
    [DataRow("com.example.$USER", ".MainActivity")]
    [DataRow("com.example.app", ".Main$PATH")]
    public void BuildProcessCommand_RejectsUnsafePackageOrActivity(string packageName, string activityName)
    {
        Assert.ThrowsException<ArgumentException>(() => builder.BuildProcessCommand(
            new OpenAppStep { Name = "Open", PackageName = packageName, ActivityName = activityName }, @"C:\MEmu\memuc.exe", 1));
    }
}
