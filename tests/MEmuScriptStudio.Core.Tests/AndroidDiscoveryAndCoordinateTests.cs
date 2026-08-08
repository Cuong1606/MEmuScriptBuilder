using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class AndroidDiscoveryAndCoordinateTests
{
    [TestMethod]
    public void LauncherParser_AcceptsOnlyUnambiguousComponents()
    {
        var parser = new AndroidLauncherActivityParser();
        var applications = parser.Parse("2 activities found:\ncom.android.chrome/.Main\nnoise\ncom.example.app/com.example.app.Start");

        Assert.AreEqual(2, applications.Count);
        Assert.AreEqual("com.android.chrome", applications[0].PackageName);
        Assert.AreEqual(".Main", applications[0].ActivityName);
    }

    [TestMethod]
    public void LauncherParser_BlankObservedGetAppInfoOutputIsEmpty()
    {
        Assert.AreEqual(0, new AndroidLauncherActivityParser().Parse(" \r\n").Count);
    }

    [TestMethod]
    public void LauncherParser_RejectsActivityThatCommandBuilderCannotSafelyExecute()
    {
        Assert.AreEqual(0, new AndroidLauncherActivityParser().Parse("com.example/.MainActivity$Alias").Count);
    }

    [TestMethod]
    public void ApplicationLabelParser_UsesOnlyExplicitNonLocalizedLabel()
    {
        var labels = new AndroidApplicationLabelParser().Parse("""
            ActivityInfo:
              packageName=com.example.notes
              labelRes=0x7f12001a
              nonLocalizedLabel=Notes
            ActivityInfo:
              packageName=com.example.unknown
              labelRes=0x7f120001
              nonLocalizedLabel=null
            """);

        Assert.AreEqual("Notes", labels["com.example.notes"]);
        Assert.IsFalse(labels.ContainsKey("com.example.unknown"));
    }

    [TestMethod]
    public void ApplicationInfo_DoesNotPresentPackageAsResolvedApplicationName()
    {
        foreach (var unresolvedLabel in new string?[] { null, string.Empty, "   " })
        {
            var application = new MemuApplicationInfo("com.example.unknown", ".Launcher", unresolvedLabel);

            Assert.IsFalse(application.HasResolvedApplicationLabel);
            Assert.AreEqual("Chưa xác định", application.DisplayName);
            Assert.AreEqual("com.example.unknown", application.PackageName);
        }
    }

    [TestMethod]
    public void ApplicationInfo_TrimsResolvedApplicationLabel()
    {
        var application = new MemuApplicationInfo("com.example.notes", ".Launcher", "  Ghi chú  ");

        Assert.IsTrue(application.HasResolvedApplicationLabel);
        Assert.AreEqual("Ghi chú", application.DisplayName);
    }

    [TestMethod]
    public void AndroidApplicationInfo_DoesNotPresentPackageAsFriendlyName()
    {
        var application = new AndroidApplicationInfo("com.android.chrome", ".Main");

        Assert.IsFalse(application.HasResolvedApplicationLabel);
        Assert.AreEqual("Không xác định", application.DisplayName);
        Assert.AreEqual("com.android.chrome", application.PackageName);
    }

    [DataTestMethod]
    [DataRow("mResumedActivity: ActivityRecord{abc u0 com.example.app/.MainActivity t42}", "com.example.app", ".MainActivity")]
    [DataRow("mCurrentFocus=Window{abc u0 com.example.notes/com.example.notes.Editor}", "com.example.notes", "com.example.notes.Editor")]
    public void ForegroundApplicationParser_ReadsKnownDumpsysComponents(string output, string packageName, string activityName)
    {
        var application = new AndroidForegroundApplicationParser().Parse(output);

        Assert.IsNotNull(application);
        Assert.AreEqual(packageName, application.PackageName);
        Assert.AreEqual(activityName, application.ActivityName);
    }

    [TestMethod]
    public void ForegroundApplicationParser_DoesNotMistakeBackgroundActivityForForeground()
    {
        var output = "ActivityRecord{abc u0 com.example.background/.Main t1}";

        Assert.IsNull(new AndroidForegroundApplicationParser().Parse(output));
    }

    [TestMethod]
    public void SwipePointSelection_AllowsAdjustmentAndRequiresBothPoints()
    {
        var selection = new SwipePointSelection();
        selection.SelectStart(new ScreenPoint(10, 20));
        Assert.ThrowsException<InvalidOperationException>(() => selection.Confirm());

        selection.SelectEnd(new ScreenPoint(30, 40));
        selection.SelectStart(new ScreenPoint(11, 21));

        Assert.AreEqual(new CapturedSwipe(11, 21, 30, 40), selection.Confirm());
    }

    [TestMethod]
    public void TapPointSelection_AllowsReselectionAndRequiresConfirmation()
    {
        var selection = new TapPointSelection();
        Assert.ThrowsException<InvalidOperationException>(() => selection.Confirm());

        selection.Select(new ScreenPoint(10, 20));
        selection.Select(new ScreenPoint(30, 40));

        Assert.AreEqual(new CapturedTap(30, 40), selection.Confirm());
    }

    [TestMethod]
    public void InputCaptureKeyPolicy_SuppressesConfirmationAndCancellationKeyPairs()
    {
        Assert.AreEqual(InputCaptureKeyAction.Suppress,
            InputCaptureKeyPolicy.Resolve(true, InputCaptureKey.Enter, isKeyDown: true, canConfirm: false));
        Assert.AreEqual(InputCaptureKeyAction.Confirm,
            InputCaptureKeyPolicy.Resolve(true, InputCaptureKey.Enter, isKeyDown: true, canConfirm: true));
        Assert.AreEqual(InputCaptureKeyAction.Suppress,
            InputCaptureKeyPolicy.Resolve(true, InputCaptureKey.Enter, isKeyDown: false, canConfirm: true));
        Assert.AreEqual(InputCaptureKeyAction.Cancel,
            InputCaptureKeyPolicy.Resolve(true, InputCaptureKey.Escape, isKeyDown: true, canConfirm: false));
        Assert.AreEqual(InputCaptureKeyAction.Suppress,
            InputCaptureKeyPolicy.Resolve(true, InputCaptureKey.Escape, isKeyDown: false, canConfirm: false));
        Assert.AreEqual(InputCaptureKeyAction.PassThrough,
            InputCaptureKeyPolicy.Resolve(false, InputCaptureKey.Enter, isKeyDown: true, canConfirm: true));
    }

    [TestMethod]
    public void InputCaptureKeyLatch_CompletesOnlyOnMatchingKeyUp()
    {
        var latch = new InputCaptureKeyLatch();
        latch.Begin(InputCaptureKey.Enter);

        Assert.IsFalse(latch.Release(InputCaptureKey.Escape));
        Assert.AreEqual(InputCaptureKey.Enter, latch.PendingKey);
        Assert.IsTrue(latch.Release(InputCaptureKey.Enter));
        Assert.IsNull(latch.PendingKey);
    }

    [TestMethod]
    public void ScreenSizeParser_PrefersOverrideResolution()
    {
        var size = AndroidScreenSizeParser.Parse("Physical size: 1080x1920\nOverride size: 720x1280");
        Assert.AreEqual((720, 1280), size);
    }

    [TestMethod]
    public void CoordinateMapper_ViewportModelsRemainAvailableForOverlayAndScaleAfterResize()
    {
        var firstViewport = MemuCoordinateMapper.FitViewport(new ScreenRectangle(100, 50, 800, 600), 1080, 1920);
        var resizedViewport = MemuCoordinateMapper.FitViewport(new ScreenRectangle(-200, 100, 540, 960), 1080, 1920);

        Assert.AreEqual(new ScreenRectangle(331, 50, 338, 600), firstViewport);
        Assert.AreEqual(new ScreenPoint(540, 960), MemuCoordinateMapper.ToGuest(
            new ScreenPoint(firstViewport.Left + firstViewport.Width / 2, firstViewport.Top + firstViewport.Height / 2),
            firstViewport, 1080, 1920));
        Assert.AreEqual(new ScreenRectangle(-200, 100, 540, 960), resizedViewport);
    }

    [TestMethod]
    public void UniformImageCoordinateMapper_MapsCenterAndNativeEdgesExactly()
    {
        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(180, 400), 360, 800, 720, 1600, out var center));
        Assert.AreEqual(new ScreenPoint(360, 800), center);

        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(0, 0), 360, 800, 720, 1600, out var first));
        Assert.AreEqual(new ScreenPoint(0, 0), first);

        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(359.999, 799.999), 360, 800, 720, 1600, out var last));
        Assert.AreEqual(new ScreenPoint(719, 1599), last);
    }

    [TestMethod]
    public void UniformImageCoordinateMapper_RejectsLetterboxAndExclusiveFarEdges()
    {
        var image = UniformImageCoordinateMapper.GetImageRectangle(800, 800, 720, 1600);
        Assert.AreEqual(new DisplayRectangle(220, 0, 360, 800), image);

        Assert.IsFalse(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(219.999, 400), 800, 800, 720, 1600, out _));
        Assert.IsFalse(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(580, 400), 800, 800, 720, 1600, out _));
        Assert.IsFalse(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(400, 800), 800, 800, 720, 1600, out _));
    }

    [TestMethod]
    public void UniformImageCoordinateMapper_IsStableAcrossDipResizeAndDpiScale()
    {
        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(90, 200), 360, 800, 720, 1600, out var normal));
        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(135, 300), 540, 1200, 720, 1600, out var scaled));

        Assert.AreEqual(normal, scaled);
        Assert.AreEqual(new ScreenPoint(180, 400), normal);
    }

    [TestMethod]
    public void UniformImageCoordinateMapper_ToDisplayRoundTripsPixelCenterAfterResize()
    {
        var displayed = UniformImageCoordinateMapper.ToDisplay(
            new ScreenPoint(719, 1599), 1000, 800, 720, 1600);

        Assert.IsTrue(UniformImageCoordinateMapper.TryToNative(
            displayed, 1000, 800, 720, 1600, out var mapped));
        Assert.AreEqual(new ScreenPoint(719, 1599), mapped);
    }

    [TestMethod]
    public void ViewportSelector_IgnoresSmallToolbarWithMatchingAspectRatio()
    {
        var root = new ScreenRectangle(0, 0, 1000, 800);
        var smallToolbar = new ScreenRectangle(10, 10, 120, 213);
        var renderer = new ScreenRectangle(300, 0, 450, 800);

        var viewport = MemuViewportSelector.Select(root, [smallToolbar, renderer], 1080, 1920);

        Assert.AreEqual(renderer, viewport);
    }
}
