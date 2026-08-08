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
        Assert.AreEqual("Chờ", script.Steps[1].Name);
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

    [TestMethod]
    public void Clone_PreservesApplicationDisplayNameWithoutChangingExecutionFields()
    {
        var source = new OpenAppStep
        {
            Name = "Mở app",
            ApplicationDisplayName = "Ứng dụng thân thiện",
            PackageName = "com.example.app",
            ActivityName = ".Main"
        };

        var clone = (OpenAppStep)ScriptCloner.CloneStep(source);

        Assert.AreEqual("Ứng dụng thân thiện", clone.ApplicationDisplayName);
        Assert.AreEqual("com.example.app", clone.PackageName);
        Assert.AreEqual(".Main", clone.ActivityName);
    }

    [TestMethod]
    public void LegacyJsonWithoutApplicationDisplayNameLoadsSafely()
    {
        const string json = """
            {
              "Name": "Legacy",
              "Steps": [
                { "$type": "openApp", "Name": "Open", "PackageName": "com.example.app", "ActivityName": ".Main" },
                { "$type": "forceStop", "Name": "Stop", "PackageName": "com.example.app" }
              ]
            }
            """;

        var script = System.Text.Json.JsonSerializer.Deserialize<ScriptDefinition>(json)!;

        Assert.IsNull(((OpenAppStep)script.Steps[0]).ApplicationDisplayName);
        Assert.IsNull(((ForceStopStep)script.Steps[1]).ApplicationDisplayName);
    }

    [TestMethod]
    public void Clone_NormalizesLegacyDelayNameAndLeavesOtherStepNamesUntouched()
    {
        var delay = new DelayStep { Name = "Tên Delay cũ", DurationMilliseconds = 100_000 };
        var note = new NoteStep { Name = "Tên ghi chú", Text = "Nội dung" };

        var delayClone = (DelayStep)ScriptCloner.CloneStep(delay);
        var noteClone = (NoteStep)ScriptCloner.CloneStep(note);

        Assert.AreEqual("Chờ", delayClone.Name);
        Assert.AreEqual(100_000, delayClone.DurationMilliseconds);
        Assert.AreEqual("Tên ghi chú", noteClone.Name);
    }

    [TestMethod]
    public void ExecutionLibrarySnapshot_IsolatedFromSourceAndMaterializesIndependentGraphs()
    {
        var child = new ScriptDefinition
        {
            Name = "Child",
            Steps = [new ForceStopStep { Name = "Stop", PackageName = "com.example.original" }]
        };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var snapshot = ExecutionScriptLibrarySnapshot.Create([child, composite]);

        ((ForceStopStep)child.Steps[0]).PackageName = "com.example.edited";
        var first = snapshot.CreateExecutionGraph(composite.Id);
        var second = snapshot.CreateExecutionGraph(composite.Id);

        Assert.AreEqual(2, snapshot.Count);
        Assert.AreNotSame(first.ScriptLibrary, second.ScriptLibrary);
        Assert.AreNotSame(first.RootScript, second.RootScript);
        Assert.AreNotSame(first.ScriptLibrary[child.Id], second.ScriptLibrary[child.Id]);
        Assert.AreEqual(
            "com.example.original",
            ((ForceStopStep)first.ScriptLibrary[child.Id].Steps[0]).PackageName);
        Assert.AreSame(first.RootScript, first.ScriptLibrary[composite.Id]);
        Assert.AreSame(second.RootScript, second.ScriptLibrary[composite.Id]);
    }
}
