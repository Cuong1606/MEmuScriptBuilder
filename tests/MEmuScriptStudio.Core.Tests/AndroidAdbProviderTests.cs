using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class AndroidAdbProviderTests
{
    private const string AdbPath = @"C:\Tools\adb.exe";
    private const string Serial = "OJYL65LF5X8LCECY";

    [TestMethod]
    public void DevicesParser_ParsesMetadataAndConnectionStates()
    {
        var devices = new AdbDevicesParser().Parse("""
            List of devices attached
            OJYL65LF5X8LCECY device product:angelica_global model:M2006C3MG device:angelica transport_id:49
            SECOND unauthorized usb:1-2
            THIRD offline transport_id:51
            """);

        Assert.AreEqual(3, devices.Count);
        Assert.AreEqual(Serial, devices[0].Serial);
        Assert.AreEqual(AndroidConnectionState.Device, devices[0].State);
        Assert.AreEqual("angelica_global", devices[0].Product);
        Assert.AreEqual("M2006C3MG", devices[0].Model);
        Assert.AreEqual(AndroidConnectionState.Unauthorized, devices[1].State);
        Assert.AreEqual(AndroidConnectionState.Offline, devices[2].State);
    }

    [TestMethod]
    public void AndroidTarget_UsesSerialIdentityAndCarriesMetadata()
    {
        var target = Device(Serial);

        Assert.AreEqual(DeviceKind.AndroidAdb, target.Kind);
        Assert.AreEqual($"android-adb:{Serial}", target.TargetKey);
        Assert.AreEqual(Serial, target.Identifier);
        Assert.AreEqual("Xiaomi M2006C3MG", target.Name);
        Assert.AreEqual("720x1600", target.ResolutionText);
        Assert.IsTrue(target.IsRunning);
    }

    [TestMethod]
    public void AndroidTargetAlias_ChangesDisplayNameButNotSerialIdentity()
    {
        var target = Device(Serial) with { Alias = "Redmi chính" };

        Assert.AreEqual("Redmi chính", target.Name);
        Assert.AreEqual(Serial, target.Serial);
        Assert.AreEqual(Serial, target.Identifier);
        Assert.AreEqual($"android-adb:{Serial}", target.TargetKey);
    }

    [TestMethod]
    public void MetadataParser_ReadsAndroidVersionResolutionDensityAndOrientation()
    {
        var properties = AndroidAdbMetadataParser.ParseProperties("""
            [ro.product.manufacturer]: [Xiaomi]
            [ro.product.model]: [M2006C3MG]
            [ro.build.version.release]: [10]
            [ro.build.version.sdk]: [29]
            """);

        Assert.AreEqual("Xiaomi", properties["ro.product.manufacturer"]);
        Assert.AreEqual("10", properties["ro.build.version.release"]);
        Assert.AreEqual((720, 1600), AndroidAdbMetadataParser.ParseSize("Physical size: 1080x2400\nOverride size: 720x1600"));
        Assert.AreEqual(320, AndroidAdbMetadataParser.ParseDensity("Physical density: 440\nOverride density: 320"));
        Assert.AreEqual(0, AndroidAdbMetadataParser.ParseInteger("0\r\n"));
    }

    [TestMethod]
    public void CommandBuilder_AlwaysTargetsExactSerial()
    {
        var command = new AdbCommandBuilder().BuildStepCommands(
            new TapStep { Name = "Tap", X = 12, Y = 34 }, AdbPath, Serial).Single();

        CollectionAssert.AreEqual(
            new[] { "-s", Serial, "shell", "input", "tap", "12", "34" },
            command.Arguments.ToArray());
    }

    [TestMethod]
    public void Hold_IsSupportedAndBuildsExactSerialScopedLongPressWithoutBecomingTap()
    {
        var step = new HoldStep
        {
            Name = "Hold",
            X = 360,
            Y = 800,
            DurationMilliseconds = 1500
        };
        var builder = new AdbCommandBuilder();

        Assert.IsTrue(AndroidScriptCapabilities.IsSupported(step));
        var command = builder.BuildStepCommands(step, AdbPath, "ABC").Single();

        CollectionAssert.AreEqual(
            new[] { "-s", "ABC", "shell", "input", "swipe", "360", "800", "360", "800", "1500" },
            command.Arguments.ToArray());
        Assert.IsFalse(command.Arguments.Contains("tap"));
        Assert.AreEqual(command.Preview, builder.BuildPreview(step, AdbPath, "ABC"));
    }

    [TestMethod]
    public void Hold_UsesExistingPositiveDurationValidationContract()
    {
        var invalid = new HoldStep { Name = "Hold", X = 360, Y = 800, DurationMilliseconds = 0 };

        Assert.IsTrue(AndroidScriptCapabilities.IsSupported(invalid));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            new AdbCommandBuilder().BuildStepCommands(invalid, AdbPath, "ABC"));
    }

    [TestMethod]
    public void CommandBuilder_BuildsTapAndSwipeWithoutCoordinateScaling()
    {
        var builder = new AdbCommandBuilder();
        var tap = builder.BuildStepCommands(new TapStep { Name = "Tap", X = 700, Y = 1500 }, AdbPath, Serial).Single();
        var swipe = builder.BuildStepCommands(new SwipeStep
        {
            Name = "Swipe", X1 = 1, Y1 = 2, X2 = 719, Y2 = 1599, DurationMilliseconds = 450
        }, AdbPath, Serial).Single();

        CollectionAssert.AreEqual(new[] { "-s", Serial, "shell", "input", "tap", "700", "1500" }, tap.Arguments.ToArray());
        CollectionAssert.AreEqual(
            new[] { "-s", Serial, "shell", "input", "swipe", "1", "2", "719", "1599", "450" },
            swipe.Arguments.ToArray());
    }

    [DataTestMethod]
    [DataRow(AndroidKeyEvent.Home, "KEYCODE_HOME")]
    [DataRow(AndroidKeyEvent.Back, "KEYCODE_BACK")]
    [DataRow(AndroidKeyEvent.RecentApps, "KEYCODE_APP_SWITCH")]
    public void CommandBuilder_BuildsSupportedNavigationKeys(AndroidKeyEvent key, string expected)
    {
        var command = new AdbCommandBuilder().BuildStepCommands(
            new KeyEventStep { Name = "Key", Key = key }, AdbPath, Serial).Single();

        Assert.AreEqual(expected, command.Arguments[^1]);
    }

    [TestMethod]
    public void CommandBuilder_EncodesInputTextAndOptionalEnterSafely()
    {
        var commands = new AdbCommandBuilder().BuildStepCommands(new InputTextStep
        {
            Name = "Text", Text = "hello world%", PressEnterAfterInput = true
        }, AdbPath, Serial);

        Assert.AreEqual(2, commands.Count);
        CollectionAssert.AreEqual(
            new[] { "-s", Serial, "shell", "input", "text", "hello%sworld%" },
            commands[0].Arguments.ToArray());
        Assert.AreEqual("KEYCODE_ENTER", commands[1].Arguments[^1]);
        Assert.ThrowsException<ArgumentException>(() => new AdbCommandBuilder().BuildStepCommands(
            new InputTextStep { Name = "Unsafe", Text = "a;b" }, AdbPath, Serial));
        Assert.ThrowsException<ArgumentException>(() => new AdbCommandBuilder().BuildStepCommands(
            new InputTextStep { Name = "Ambiguous", Text = "literal%s" }, AdbPath, Serial));
    }

    [TestMethod]
    public void CommandBuilder_UsesExistingPackageActivityContractForOpenApp()
    {
        var command = new AdbCommandBuilder().BuildStepCommands(new OpenAppStep
        {
            Name = "Open", PackageName = "com.example.app", ActivityName = ".MainActivity"
        }, AdbPath, Serial).Single();

        CollectionAssert.AreEqual(
            new[] { "-s", Serial, "shell", "am", "start", "-n", "com.example.app/.MainActivity" },
            command.Arguments.ToArray());
    }

    [TestMethod]
    public void FriendlyApplicationName_DoesNotChangeAndroidOrMemuExecutionCommands()
    {
        var step = new OpenAppStep
        {
            Name = "Open",
            ApplicationDisplayName = "Tên thân thiện",
            PackageName = "com.example.app",
            ActivityName = ".MainActivity"
        };

        var android = new AdbCommandBuilder().BuildStepCommands(step, AdbPath, Serial).Single();
        var memu = new ScriptStepCommandBuilder(new MemuCommandBuilder())
            .BuildProcessCommand(step, @"C:\MEmu\memuc.exe", 4);

        CollectionAssert.AreEqual(
            new[] { "-s", Serial, "shell", "am", "start", "-n", "com.example.app/.MainActivity" },
            android.Arguments.ToArray());
        CollectionAssert.AreEqual(
            new[] { "-i", "4", "execcmd", "am start -n com.example.app/.MainActivity" },
            memu.Arguments.ToArray());
    }

    [TestMethod]
    public void CommandBuilder_EscapesNestedClassActivityForRemoteAndroidShell()
    {
        var command = new AdbCommandBuilder().BuildStepCommands(new OpenAppStep
        {
            Name = "YouTube",
            PackageName = "com.google.android.youtube",
            ActivityName = ".app.honeycomb.Shell$HomeActivity"
        }, AdbPath, "ABC").Single();

        CollectionAssert.AreEqual(
            new[]
            {
                "-s", "ABC", "shell", "am", "start", "-n",
                "com.google.android.youtube/.app.honeycomb.Shell\\$HomeActivity"
            },
            command.Arguments.ToArray());
        Assert.AreEqual(command.Preview,
            new AdbCommandBuilder().BuildPreview(
                new OpenAppStep
                {
                    Name = "YouTube",
                    PackageName = "com.google.android.youtube",
                    ActivityName = ".app.honeycomb.Shell$HomeActivity"
                },
                AdbPath,
                "ABC"));
    }

    [TestMethod]
    public void LauncherParser_ParsesRealAndroid10ComponentsWithoutPackageFallbackLabel()
    {
        var applications = new AndroidLauncherApplicationParser().Parse("""
            3 activities found:
            com.android.chrome/com.google.android.apps.chrome.Main
            com.google.android.youtube/.app.honeycomb.Shell$HomeActivity
            ignored package line
            """);

        Assert.AreEqual(2, applications.Count);
        var chrome = applications.Single(application => application.PackageName == "com.android.chrome");
        Assert.AreEqual("com.google.android.apps.chrome.Main", chrome.ActivityName);
        Assert.AreEqual("Không xác định", chrome.DisplayName);
        Assert.AreEqual(".app.honeycomb.Shell$HomeActivity",
            applications.Single(application => application.PackageName == "com.google.android.youtube").ActivityName);
    }

    [TestMethod]
    public void CommandBuilder_BuildsExactSerialScopedLauncherDiscovery()
    {
        var command = new AdbCommandBuilder().BuildQueryLauncherActivities(AdbPath, "SERIAL-B");

        CollectionAssert.AreEqual(
            new[]
            {
                "-s", "SERIAL-B", "shell", "cmd", "package", "query-activities", "--brief", "--components",
                "--user", "0", "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER"
            },
            command.Arguments.ToArray());
    }

    [TestMethod]
    public void ForegroundParser_PrefersVerifiedActivityManagerMarkers()
    {
        var result = new AndroidForegroundActivityParser().ParseActivityManager("""
            mLastPausedActivity: ActivityRecord{aaa u0 com.example.old/.Old t1}
            mResumedActivity: ActivityRecord{bbb u0 com.android.chrome/com.google.android.apps.chrome.Main t2}
            ResumedActivity: ActivityRecord{bbb u0 com.android.chrome/com.google.android.apps.chrome.Main t2}
            """);

        Assert.IsNotNull(result);
        Assert.AreEqual("com.android.chrome", result.PackageName);
        Assert.AreEqual("com.google.android.apps.chrome.Main", result.ActivityName);
    }

    [TestMethod]
    public void ForegroundParser_ReadsWindowManagerOnlyThroughKnownFallbackMarkers()
    {
        var result = new AndroidForegroundActivityParser().ParseWindowManager("""
            Window #2 Window{aaa u0 com.example.background/.Background}
            mCurrentFocus=Window{bbb u0 com.example.notes/com.example.notes.Editor}
            mFocusedApp=AppWindowToken{ActivityRecord{bbb u0 com.example.notes/.Editor}}
            """);

        Assert.IsNotNull(result);
        Assert.AreEqual("com.example.notes", result.PackageName);
        Assert.AreEqual("com.example.notes.Editor", result.ActivityName);
    }

    [TestMethod]
    public void ForegroundParser_RejectsMalformedAndBackgroundOnlyComponents()
    {
        var parser = new AndroidForegroundActivityParser();

        Assert.IsNull(parser.ParseActivityManager(
            "ActivityRecord{aaa u0 com.example.background/.Background t1}"));
        Assert.IsNull(parser.ParseActivityManager(
            "ActivityRecord{aaa u0 com.example.background/.ResumedActivity t1}"));
        Assert.IsNull(parser.ParseWindowManager(
            "Window{aaa u0 com.example.background/.mCurrentFocus}"));
        Assert.IsNull(parser.ParseActivityManager("mResumedActivity: not-a-component"));
        Assert.IsNull(parser.ParseWindowManager("mCurrentFocus=null"));
    }

    [TestMethod]
    public void CommandBuilder_BuildsExactSerialScopedForegroundQueries()
    {
        var builder = new AdbCommandBuilder();

        CollectionAssert.AreEqual(
            new[] { "-s", "SERIAL-B", "shell", "dumpsys", "activity", "activities" },
            builder.BuildQueryForegroundActivity(AdbPath, "SERIAL-B").Arguments.ToArray());
        CollectionAssert.AreEqual(
            new[] { "-s", "SERIAL-B", "shell", "dumpsys", "window" },
            builder.BuildQueryForegroundWindow(AdbPath, "SERIAL-B").Arguments.ToArray());
    }

    [TestMethod]
    public void CommandBuilder_BuildsForceStopWithoutActivityAndNeverBecomesOpenApp()
    {
        var step = new ForceStopStep { Name = "Stop", PackageName = "com.example.app" };

        Assert.IsTrue(AndroidScriptCapabilities.IsSupported(step));
        var command = new AdbCommandBuilder().BuildStepCommands(step, AdbPath, "ABC").Single();

        CollectionAssert.AreEqual(
            new[] { "-s", "ABC", "shell", "am", "force-stop", "com.example.app" },
            command.Arguments.ToArray());
        Assert.IsFalse(command.Arguments.Contains("start"));
        Assert.IsFalse(command.Arguments.Contains("-n"));
        Assert.AreEqual(command.Preview, new AdbCommandBuilder().BuildPreview(step, AdbPath, "ABC"));
    }

    [TestMethod]
    public void CommandBuilder_BuildsClipboardPasteAndOptionalEnterAsSeparateSerialScopedKeyEvents()
    {
        var builder = new AdbCommandBuilder();
        var pasteOnly = builder.BuildStepCommands(
            new AndroidClipboardPasteStep { Name = "Paste" }, AdbPath, "ABC");
        var pasteAndEnter = builder.BuildStepCommands(
            new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true }, AdbPath, "ABC");

        Assert.IsTrue(AndroidScriptCapabilities.IsSupported(new AndroidClipboardPasteStep { Name = "Paste" }));
        Assert.AreEqual(1, pasteOnly.Count);
        CollectionAssert.AreEqual(
            new[] { "-s", "ABC", "shell", "input", "keyevent", "KEYCODE_PASTE" },
            pasteOnly[0].Arguments.ToArray());
        Assert.AreEqual(2, pasteAndEnter.Count);
        Assert.AreEqual("KEYCODE_PASTE", pasteAndEnter[0].Arguments[^1]);
        Assert.AreEqual("KEYCODE_ENTER", pasteAndEnter[1].Arguments[^1]);
        Assert.IsTrue(pasteAndEnter.All(command => command.Arguments[0] == "-s" && command.Arguments[1] == "ABC"));
        Assert.AreEqual(
            string.Join(Environment.NewLine, pasteAndEnter.Select(command => command.Preview)),
            builder.BuildPreview(
                new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true },
                AdbPath,
                "ABC"));
    }

    [TestMethod]
    public void CloseAllChromeTabs_IsExplicitlyUnsupportedOnAndroidWhileMemuCommandContractsRemainUnchanged()
    {
        var closeTabs = new CloseChromeTabsStep { Name = "Close tabs" };
        var memuBuilder = new ScriptStepCommandBuilder(new MemuCommandBuilder());

        Assert.IsFalse(AndroidScriptCapabilities.IsSupported(closeTabs));
        Assert.AreEqual("Đóng tất cả tab Chrome chưa hỗ trợ Android / ADB.",
            AndroidScriptCapabilities.UnsupportedMessage(closeTabs));
        Assert.AreEqual("am force-stop com.example.app",
            memuBuilder.BuildProcessCommand(
                new ForceStopStep { Name = "Stop", PackageName = "com.example.app" },
                @"C:\MEmu\memuc.exe",
                4).Arguments[^1]);
        CollectionAssert.AreEqual(
            new[] { "input keyevent 279", "input keyevent 66" },
            memuBuilder.BuildProcessCommands(
                    new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true },
                    @"C:\MEmu\memuc.exe",
                    4)
                .Select(command => command.Arguments[^1])
                .ToArray());
    }

    [TestMethod]
    public async Task Scheduler_DisconnectBetweenStepsBecomesUnavailableAndStopsLaterCommands()
    {
        var runner = new RecordingProcessRunner();
        var state = new SequenceStateProbe(AndroidConnectionState.Device, AndroidConnectionState.Offline);
        var regular = new ScriptExecutionEngine(
            runner,
            new ScriptStepCommandBuilder(new MemuCommandBuilder()),
            new ImmediateDelayProvider(),
            adbCommandBuilder: new AdbCommandBuilder(),
            androidStateProbe: state);
        var scheduler = Scheduler(new CompositeScriptExecutionEngine(regular, new ImmediateDelayProvider(), androidStateProbe: state), state);
        var script = Script(
            new TapStep { Name = "First", X = 1, Y = 2 },
            new TapStep { Name = "Second", X = 3, Y = 4 });

        var result = await scheduler.Start(Request(script, [Device(Serial)])).Completion;

        Assert.AreEqual(InstanceExecutionStatus.Unavailable, result.Instances.Single().Status);
        Assert.AreEqual(1, runner.Requests.Count);
        StringAssert.Contains(result.Instances.Single().Message, "offline");
    }

    [TestMethod]
    public async Task ExecutionEngine_PreservesCapturedHoldCoordinatesDurationAndExactSerial()
    {
        var runner = new RecordingProcessRunner();
        var state = new ConstantStateProbe();
        var engine = new ScriptExecutionEngine(
            runner,
            new ScriptStepCommandBuilder(new MemuCommandBuilder()),
            new ImmediateDelayProvider(),
            adbCommandBuilder: new AdbCommandBuilder(),
            androidStateProbe: state);
        var target = Device("ABC");
        var script = Script(new HoldStep
        {
            Name = "Captured hold",
            X = 360,
            Y = 800,
            DurationMilliseconds = 1500
        });

        var result = await engine.ExecuteAsync(new ExecutionRequest
        {
            Script = script,
            InstanceIndex = target.Index,
            Target = target,
            AdbPath = AdbPath
        }, null, CancellationToken.None);

        Assert.AreEqual(StepExecutionStatus.Succeeded, result.Steps.Single().Status);
        var request = runner.Requests.Single();
        Assert.AreEqual(AdbPath, request.FileName);
        CollectionAssert.AreEqual(
            new[] { "-s", "ABC", "shell", "input", "swipe", "360", "800", "360", "800", "1500" },
            request.Arguments.ToArray());
    }

    [TestMethod]
    public async Task Scheduler_MultipleAndroidDevicesRemainIsolated()
    {
        var engine = new SelectiveExecutionEngine("BAD");
        var state = new ConstantStateProbe();
        var targets = new[] { Device("GOOD"), Device("BAD") };
        var scheduler = Scheduler(engine, state, targets);

        var result = await scheduler.Start(Request(Script(new TapStep { Name = "Tap", X = 1, Y = 2 }), targets)).Completion;

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, result.Instances.Single(item => item.Target.Identifier == "GOOD").Status);
        Assert.AreEqual(InstanceExecutionStatus.Failed, result.Instances.Single(item => item.Target.Identifier == "BAD").Status);
        CollectionAssert.AreEquivalent(new[] { "GOOD", "BAD" }, engine.Serials.ToArray());
    }

    [TestMethod]
    public async Task Scheduler_BlocksUnsupportedAndroidStepAtAdmission()
    {
        var engine = new SelectiveExecutionEngine(null);
        var state = new ConstantStateProbe();
        var target = Device(Serial);
        var scheduler = Scheduler(engine, state, [target]);

        var result = await scheduler.Start(Request(
            Script(
                new HoldStep { Name = "Supported hold", X = 360, Y = 800, DurationMilliseconds = 1500 },
                new CloseChromeTabsStep { Name = "Close tabs" }),
            [target])).Completion;

        var instance = result.Instances.Single();
        Assert.AreEqual(InstanceExecutionStatus.Failed, instance.Status);
        Assert.IsNotNull(instance.Message);
        Assert.AreEqual("Đóng tất cả tab Chrome chưa hỗ trợ Android / ADB.", instance.Message);
        Assert.IsFalse(instance.Message.Contains("Supported hold", StringComparison.Ordinal));
        Assert.AreEqual(0, engine.Serials.Count);
    }

    [TestMethod]
    public async Task Scheduler_StopTargetWinsTerminalCommitRaceWithoutCancellingOtherDevice()
    {
        var engine = new GateExecutionEngine();
        var state = new ConstantStateProbe();
        var first = Device("FIRST");
        var second = Device("SECOND");
        var scheduler = Scheduler(engine, state, [first, second]);
        var session = scheduler.Start(Request(Script(new TapStep { Name = "Tap", X = 1, Y = 2 }), [first, second]));
        await engine.BothStarted.Task;

        Assert.IsTrue(session.StopTarget(first.TargetKey));
        engine.Release.TrySetResult();
        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, result.Instances.Single(item => item.Target.TargetKey == first.TargetKey).Status);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, result.Instances.Single(item => item.Target.TargetKey == second.TargetKey).Status);
    }

    [TestMethod]
    public void ExistingMemuTargetIdentityAndCommandRemainIndexScoped()
    {
        var target = new MemuInstance(4, "VM 4", true, 100);
        var command = new ScriptStepCommandBuilder(new MemuCommandBuilder()).BuildProcessCommand(
            new TapStep { Name = "Tap", X = 5, Y = 6 }, @"C:\MEmu\memuc.exe", 4);

        Assert.AreEqual("memu:4", target.TargetKey);
        CollectionAssert.AreEqual(new[] { "-i", "4", "execcmd", "input tap 5 6" }, command.Arguments.ToArray());
    }

    private static AndroidAdbDevice Device(string serial) => new(
        serial, "Xiaomi", "M2006C3MG", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device);

    private static ScriptDefinition Script(params ScriptStep[] steps) => new()
    {
        Name = "Android script",
        Steps = steps.ToList()
    };

    private static MultiInstanceExecutionRequest Request(ScriptDefinition script, IReadOnlyList<AndroidAdbDevice> targets) => new()
    {
        Script = script,
        AdbPath = AdbPath,
        Targets = targets,
        FixedSpacing = TimeSpan.Zero,
        RandomMinimumSpacing = TimeSpan.Zero,
        RandomMaximumSpacing = TimeSpan.Zero
    };

    private static MultiInstanceExecutionScheduler Scheduler(
        IScriptExecutionEngine engine,
        IAndroidAdbStateProbe state,
        IReadOnlyList<AndroidAdbDevice>? devices = null) => new(
            new EmptyMemuService(),
            engine,
            new ImmediateLaunchDelayProvider(),
            new FixedRandom(),
            androidTransportService: new FixedAndroidTransportService(devices ?? [Device(Serial)]),
            androidStateProbe: state);

    private sealed class EmptyMemuService : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemuInstance>>([]);
    }

    private sealed class FixedAndroidTransportService(IReadOnlyList<AndroidAdbDevice> devices) : IAndroidAdbTransportService
    {
        public Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(
            string adbPath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdbDeviceListEntry>>(devices
                .Select(device => new AdbDeviceListEntry(
                    device.Serial,
                    device.ConnectionState,
                    device.Product,
                    device.Model,
                    device.Device))
                .ToList());
    }

    private sealed class ConstantStateProbe : IAndroidAdbStateProbe
    {
        public Task<AndroidAdbStateResult> CheckStateAsync(string adbPath, string serial, CancellationToken cancellationToken) =>
            Task.FromResult(new AndroidAdbStateResult(AndroidConnectionState.Device));
    }

    private sealed class SequenceStateProbe(params AndroidConnectionState[] states) : IAndroidAdbStateProbe
    {
        private int index;
        public Task<AndroidAdbStateResult> CheckStateAsync(string adbPath, string serial, CancellationToken cancellationToken)
        {
            var state = states[Math.Min(Interlocked.Increment(ref index) - 1, states.Length - 1)];
            return Task.FromResult(new AndroidAdbStateResult(state, state == AndroidConnectionState.Device ? null : "ADB offline"));
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, now, now));
        }
    }

    private sealed class SelectiveExecutionEngine(string? failedSerial) : IScriptExecutionEngine
    {
        public List<string> Serials { get; } = [];
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            var serial = ((AndroidAdbDevice)request.Target).Serial;
            lock (Serials) Serials.Add(serial);
            var failed = string.Equals(serial, failedSerial, StringComparison.Ordinal);
            return Task.FromResult(new ExecutionResult
            {
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
                Steps = [new StepExecutionResult
                {
                    StepId = request.Script.Steps[0].Id,
                    Status = failed ? StepExecutionStatus.Failed : StepExecutionStatus.Succeeded
                }]
            });
        }
    }

    private sealed class GateExecutionEngine : IScriptExecutionEngine
    {
        private int started;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 2) BothStarted.TrySetResult();
            await Release.Task;
            return new ExecutionResult
            {
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
                Steps = [new StepExecutionResult { StepId = request.Script.Steps[0].Id, Status = StepExecutionStatus.Succeeded }]
            };
        }
    }

    private sealed class ImmediateDelayProvider : IDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ImmediateLaunchDelayProvider : ILaunchDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedRandom : ILaunchSpacingRandom
    {
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) => minimumMilliseconds;
    }
}
