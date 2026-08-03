using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class ScriptTemplateAndCloneTests
{
    [TestMethod]
    public void CreateRestartChrome_HasExactThreeStepLogic()
    {
        var script = ScriptTemplateFactory.CreateRestartChrome();
        var commandBuilder = new ScriptStepCommandBuilder(new MemuCommandBuilder());

        Assert.AreEqual("Khởi động lại Chrome", script.Name);
        Assert.AreEqual(3, script.Steps.Count);
        Assert.AreEqual("am force-stop com.android.chrome", commandBuilder.BuildProcessCommand(script.Steps[0], @"C:\MEmu\memuc.exe", 4).Arguments[3]);
        Assert.AreEqual(2000, ((DelayStep)script.Steps[1]).DurationMilliseconds);
        Assert.AreEqual(
            "am start -n com.android.chrome/com.google.android.apps.chrome.Main",
            commandBuilder.BuildProcessCommand(script.Steps[2], @"C:\MEmu\memuc.exe", 4).Arguments[3]);
    }

    [TestMethod]
    public void Clone_CreatesIndependentIdsAndPreservesStepValues()
    {
        var source = ScriptTemplateFactory.CreateRestartChrome();

        var clone = ScriptCloner.Clone(source);

        Assert.AreNotEqual(source.Id, clone.Id);
        Assert.AreEqual(source.Steps.Count, clone.Steps.Count);
        Assert.IsTrue(source.Steps.Zip(clone.Steps).All(pair => pair.First.Id != pair.Second.Id));
        Assert.AreEqual(((ForceStopStep)source.Steps[0]).PackageName, ((ForceStopStep)clone.Steps[0]).PackageName);
    }

    [TestMethod]
    public void Clone_PreservesInputTextEnterOption()
    {
        var source = new InputTextStep
        {
            Name = "Submit",
            Text = "hello",
            PressEnterAfterInput = true
        };

        var clone = (InputTextStep)ScriptCloner.CloneStep(source);

        Assert.AreNotEqual(source.Id, clone.Id);
        Assert.AreEqual(source.Text, clone.Text);
        Assert.IsTrue(clone.PressEnterAfterInput);
    }

    [TestMethod]
    public void Clone_PreservesHoldCoordinatesAndDuration()
    {
        var source = new HoldStep { Name = "Hold", X = 12, Y = 34, DurationMilliseconds = 900 };

        var clone = (HoldStep)ScriptCloner.CloneStep(source);

        Assert.AreNotEqual(source.Id, clone.Id);
        Assert.AreEqual(source.X, clone.X);
        Assert.AreEqual(source.Y, clone.Y);
        Assert.AreEqual(source.DurationMilliseconds, clone.DurationMilliseconds);
    }

    [TestMethod]
    public void Clone_PreservesAndroidClipboardPasteEnterOption()
    {
        var source = new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true };

        var clone = (AndroidClipboardPasteStep)ScriptCloner.CloneStep(source);

        Assert.AreNotEqual(source.Id, clone.Id);
        Assert.IsTrue(clone.PressEnterAfterPaste);
    }
}
