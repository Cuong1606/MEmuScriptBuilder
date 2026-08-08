using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.Android;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class AndroidAdbDeviceServiceTests
{
    private const string AdbPath = @"C:\Tools\adb.exe";
    private const string Serial = "OJYL65LF5X8LCECY";

    [TestMethod]
    public void Classifier_RecognizesMicrovirtManufacturerAsMemu()
    {
        var result = new AndroidTargetClassifier().Classify("127.0.0.1:21503",
            new Dictionary<string, string> { ["ro.product.manufacturer"] = "Microvirt" });

        Assert.AreEqual(AndroidTargetClassification.MEmuBackedAdb, result);
    }

    [TestMethod]
    public void Classifier_RecognizesConfirmedMemuOwnedLocalListenerWithGenericAndroidProperties()
    {
        var classifier = new AndroidTargetClassifier(new FixedEndpointEvidenceProvider(
            new LocalAdbEndpointEvidence(
                true,
                true,
                5152,
                "MEmuHeadless.exe",
                @"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe")));

        var result = classifier.Classify("127.0.0.1:21503",
            new Dictionary<string, string> { ["ro.product.model"] = "A5010" });

        Assert.AreEqual(AndroidTargetClassification.MEmuBackedAdb, result);
    }

    [TestMethod]
    public void Classifier_RecognizesMemuProductIdentityAsMemu()
    {
        var result = new AndroidTargetClassifier().Classify("emulator-5554",
            new Dictionary<string, string> { ["ro.product.model"] = "MEmu 9" });

        Assert.AreEqual(AndroidTargetClassification.MEmuBackedAdb, result);
    }

    [TestMethod]
    public void Classifier_LocalhostWithoutMemuEvidenceRemainsUnknown()
    {
        var result = new AndroidTargetClassifier().Classify("127.0.0.1:5555",
            new Dictionary<string, string> { ["ro.product.model"] = "BoxPhone" });

        Assert.AreEqual(AndroidTargetClassification.Unknown, result);
    }

    [TestMethod]
    public void Classifier_BoxPhoneLikeLocalhostRemainsVisibleWithoutPositiveHostEvidence()
    {
        var classifier = new AndroidTargetClassifier(new FixedEndpointEvidenceProvider(
            new LocalAdbEndpointEvidence(true, false, 9000, "boxphone.exe", @"C:\BoxPhone\boxphone.exe")));

        var result = classifier.Classify("127.0.0.1:5555",
            new Dictionary<string, string>
            {
                ["ro.product.manufacturer"] = "Xiaowei",
                ["ro.product.model"] = "BoxPhone"
            });

        Assert.AreEqual(AndroidTargetClassification.Unknown, result);
    }

    [DataTestMethod]
    [DataRow("MEmuHeadless.exe", @"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe", true)]
    [DataRow("MEmuSVC.exe", @"E:\Microvirt\MEmu\MEmuSVC.exe", true)]
    [DataRow("adb.exe", @"E:\Microvirt\MEmu\adb.exe", false)]
    [DataRow("MEmuHeadless.exe", @"C:\OtherEmulator\MEmuHeadless.exe", false)]
    [DataRow("MEmuHeadless.exe", @"C:\Microvirt\OtherProduct\MEmuHeadless.exe", false)]
    public void WindowsEndpointEvidence_RequiresAllowlistedProcessAndMicrovirtPath(
        string executableName,
        string executablePath,
        bool expected) =>
        Assert.AreEqual(expected,
            WindowsLocalAdbEndpointEvidenceProvider.IsPositiveMemuOwner(executableName, executablePath));

    [TestMethod]
    public void Classifier_EmulatorSerialWithoutMemuEvidenceRemainsUnknown()
    {
        var result = new AndroidTargetClassifier().Classify("emulator-5554",
            new Dictionary<string, string> { ["ro.product.model"] = "sdk_gphone64_x86_64" });

        Assert.AreEqual(AndroidTargetClassification.Unknown, result);
    }

    [TestMethod]
    public void Classifier_UsbXiaomiIsExternalAndroid()
    {
        var result = new AndroidTargetClassifier().Classify(Serial,
            new Dictionary<string, string>
            {
                ["ro.product.manufacturer"] = "Xiaomi",
                ["ro.product.model"] = "M2006C3MG"
            });

        Assert.AreEqual(AndroidTargetClassification.ExternalAndroid, result);
    }

    [TestMethod]
    public void Classifier_UsbWithoutReadablePropertiesRemainsUnknown()
    {
        var result = new AndroidTargetClassifier().Classify(Serial);

        Assert.AreEqual(AndroidTargetClassification.Unknown, result);
    }

    [TestMethod]
    public void Classifier_UnrelatedPropertyCannotFalseHideDevice()
    {
        var result = new AndroidTargetClassifier().Classify("127.0.0.1:5555",
            new Dictionary<string, string> { ["persist.example.note"] = "Microvirt MEmu" });

        Assert.AreEqual(AndroidTargetClassification.Unknown, result);
    }

    [TestMethod]
    public async Task Discovery_HidesOnlyPositiveMemuEvidenceAndKeepsExternalAndUnknown()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\nUSB device model:Phone\n127.0.0.1:21503 device model:MEmu\n127.0.0.1:5555 device model:BoxPhone\n"),
            ["-s", "USB", "shell", "getprop"] => Success("[ro.product.manufacturer]: [Xiaomi]\n[ro.product.model]: [Phone]"),
            ["-s", "127.0.0.1:21503", "shell", "getprop"] => Success("[ro.product.manufacturer]: [Microvirt]\n[ro.product.model]: [MEmu]"),
            ["-s", "127.0.0.1:5555", "shell", "getprop"] => Success("[ro.product.manufacturer]: [Acme]\n[ro.product.model]: [BoxPhone]"),
            ["-s", "USB" or "127.0.0.1:5555", "shell", "wm", "size"] => Success("Physical size: 720x1600"),
            ["-s", "USB" or "127.0.0.1:5555", "shell", "wm", "density"] => Success("Physical density: 320"),
            ["-s", "USB" or "127.0.0.1:5555", "shell", "settings", "get", "system", "user_rotation"] => Success("0"),
            _ => throw new AssertFailedException($"Unexpected process request: {string.Join(' ', request.Arguments)}")
        });

        var devices = await new AndroidAdbDeviceService(runner, new AdbCommandBuilder(), new AdbDevicesParser())
            .GetDevicesAsync(AdbPath, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "USB", "127.0.0.1:5555" }, devices.Select(device => device.Serial).ToArray());
        Assert.AreEqual(AndroidTargetClassification.ExternalAndroid, devices.Single(device => device.Serial == "USB").Classification);
        Assert.AreEqual(AndroidTargetClassification.Unknown, devices.Single(device => device.Serial == "127.0.0.1:5555").Classification);
        Assert.IsFalse(runner.Requests.Any(request => request.Arguments.Contains("127.0.0.1:21503") && request.Arguments.Contains("wm")),
            "A positively identified MEmu endpoint should be filtered immediately after getprop.");
    }

    [TestMethod]
    public async Task Discovery_HidesGenericLocalhostOnlyWhenHostListenerIsConfirmedMemuOwned()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\n127.0.0.1:21503 device model:A5010\n"),
            ["-s", "127.0.0.1:21503", "shell", "getprop"] =>
                Success("[ro.product.manufacturer]: [Android]\n[ro.product.model]: [A5010]"),
            _ => throw new AssertFailedException($"Unexpected process request: {string.Join(' ', request.Arguments)}")
        });
        var classifier = new AndroidTargetClassifier(new FixedEndpointEvidenceProvider(
            new LocalAdbEndpointEvidence(
                true,
                true,
                5152,
                "MEmuHeadless.exe",
                @"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe")));

        var devices = await new AndroidAdbDeviceService(
                runner,
                new AdbCommandBuilder(),
                new AdbDevicesParser(),
                classifier)
            .GetDevicesAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(0, devices.Count);
        Assert.AreEqual(2, runner.Requests.Count);
    }

    [TestMethod]
    public async Task Discovery_MetadataFailureOnLocalhostStaysVisibleAsUnknown()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\n127.0.0.1:5555 device model:BoxPhone\n"),
            ["-s", "127.0.0.1:5555", "shell", "getprop"] => Result(1, string.Empty, "metadata unavailable"),
            _ => throw new AssertFailedException("Unexpected request")
        });

        var device = (await new AndroidAdbDeviceService(runner, new AdbCommandBuilder(), new AdbDevicesParser())
            .GetDevicesAsync(AdbPath, CancellationToken.None)).Single();

        Assert.AreEqual("127.0.0.1:5555", device.Serial);
        Assert.AreEqual(AndroidTargetClassification.Unknown, device.Classification);
        Assert.AreEqual(AndroidConnectionState.Device, device.ConnectionState);
    }

    [TestMethod]
    public async Task Discovery_HostConfirmedMemuStaysHiddenWhenGetpropFails()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\n127.0.0.1:21503 device model:A5010\n"),
            ["-s", "127.0.0.1:21503", "shell", "getprop"] => Result(1, string.Empty, "metadata unavailable"),
            _ => throw new AssertFailedException("Unexpected request")
        });
        var classifier = new AndroidTargetClassifier(new FixedEndpointEvidenceProvider(
            new LocalAdbEndpointEvidence(true, true, 5152, "MEmuHeadless.exe", @"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe")));

        var devices = await new AndroidAdbDeviceService(
                runner,
                new AdbCommandBuilder(),
                new AdbDevicesParser(),
                classifier)
            .GetDevicesAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(0, devices.Count);
    }

    [TestMethod]
    public async Task Discovery_HostConfirmedOfflineMemuStaysHiddenWithoutAdbMetadata()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\n127.0.0.1:21503 offline model:A5010\n"),
            _ => throw new AssertFailedException("Unexpected request")
        });
        var classifier = new AndroidTargetClassifier(new FixedEndpointEvidenceProvider(
            new LocalAdbEndpointEvidence(true, true, 5152, "MEmuHeadless.exe", @"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe")));

        var devices = await new AndroidAdbDeviceService(
                runner,
                new AdbCommandBuilder(),
                new AdbDevicesParser(),
                classifier)
            .GetDevicesAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(0, devices.Count);
        Assert.AreEqual(1, runner.Requests.Count);
    }

    [TestMethod]
    public async Task Discovery_MapsDeviceMetadataAndLeavesUnauthorizedUnavailable()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success($"List of devices attached\n{Serial} device product:angelica_global model:M2006C3MG device:angelica transport_id:49\nLOCKED unauthorized usb:1-2\n"),
            ["-s", Serial, "shell", "getprop"] => Success("""
                [ro.product.manufacturer]: [Xiaomi]
                [ro.product.model]: [M2006C3MG]
                [ro.build.version.release]: [10]
                [ro.build.version.sdk]: [29]
                """),
            ["-s", Serial, "shell", "wm", "size"] => Success("Physical size: 720x1600"),
            ["-s", Serial, "shell", "wm", "density"] => Success("Physical density: 320"),
            ["-s", Serial, "shell", "settings", "get", "system", "user_rotation"] => Success("0"),
            _ => throw new AssertFailedException($"Unexpected process request: {string.Join(' ', request.Arguments)}")
        });
        var service = new AndroidAdbDeviceService(runner, new AdbCommandBuilder(), new AdbDevicesParser());

        var devices = await service.GetDevicesAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(2, devices.Count);
        var phone = devices.Single(device => device.Serial == Serial);
        Assert.AreEqual("Xiaomi", phone.Manufacturer);
        Assert.AreEqual("M2006C3MG", phone.Model);
        Assert.AreEqual("10", phone.AndroidVersion);
        Assert.AreEqual(29, phone.AndroidSdk);
        Assert.AreEqual(720, phone.ScreenWidth);
        Assert.AreEqual(1600, phone.ScreenHeight);
        Assert.AreEqual(320, phone.DensityDpi);
        Assert.AreEqual(0, phone.Orientation);
        Assert.AreEqual(AndroidConnectionState.Unauthorized, devices.Single(device => device.Serial == "LOCKED").ConnectionState);
        Assert.IsFalse(runner.Requests.Any(request => request.Arguments.Contains("LOCKED") && request.Arguments.Contains("getprop")));
        Assert.AreEqual(5, runner.Requests.Count, "Full UI discovery must retain devices + four metadata reads for the connected device.");
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(20)]
    [DataRow(100)]
    public async Task TransportSnapshotUsesOneDevicesCommandAndNoPerDeviceMetadata(int deviceCount)
    {
        var output = "List of devices attached\n" + string.Join(
            '\n',
            Enumerable.Range(0, deviceCount).Select(index => $"SERIAL-{index:D3} device model:Phone-{index:D3}"));
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success(output),
            _ => throw new AssertFailedException($"Execution preflight must not read metadata: {string.Join(' ', request.Arguments)}")
        });
        IAndroidAdbTransportService service = new AndroidAdbDeviceService(
            runner,
            new AdbCommandBuilder(),
            new AdbDevicesParser());

        var transports = await service.GetTransportsAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(deviceCount, transports.Count);
        Assert.AreEqual(1, runner.Requests.Count);
        CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.Requests[0].Arguments.ToArray());
        Assert.IsTrue(transports.All(transport => transport.State == AndroidConnectionState.Device));
    }

    [DataTestMethod]
    [DataRow("device", 0, AndroidConnectionState.Device)]
    [DataRow("offline", 0, AndroidConnectionState.Offline)]
    [DataRow("", 1, AndroidConnectionState.Unauthorized, "error: device unauthorized")]
    public async Task StateProbe_MapsTransportState(
        string output,
        int exitCode,
        AndroidConnectionState expected,
        string error = "")
    {
        var runner = new ScriptedRunner(_ => Result(exitCode, output, error));
        var service = new AndroidAdbDeviceService(runner, new AdbCommandBuilder(), new AdbDevicesParser());

        var state = await service.CheckStateAsync(AdbPath, Serial, CancellationToken.None);

        Assert.AreEqual(expected, state.State);
        CollectionAssert.AreEqual(new[] { "-s", Serial, "get-state" }, runner.Requests.Single().Arguments.ToArray());
    }

    [TestMethod]
    public async Task MetadataFailureIsIsolatedToThatDevice()
    {
        var runner = new ScriptedRunner(request => request.Arguments.ToArray() switch
        {
            ["devices", "-l"] => Success("List of devices attached\nGOOD device model:Good\nBAD device model:Bad\n"),
            ["-s", "BAD", "shell", "getprop"] => Result(1, string.Empty, "disconnected"),
            ["-s", _, "shell", "getprop"] => Success("[ro.product.manufacturer]: [Maker]\n[ro.build.version.release]: [12]\n[ro.build.version.sdk]: [31]"),
            ["-s", _, "shell", "wm", "size"] => Success("Physical size: 1080x1920"),
            ["-s", _, "shell", "wm", "density"] => Success("Physical density: 420"),
            ["-s", _, "shell", "settings", "get", "system", "user_rotation"] => Success("0"),
            _ => throw new AssertFailedException("Unexpected request")
        });
        var service = new AndroidAdbDeviceService(runner, new AdbCommandBuilder(), new AdbDevicesParser());

        var devices = await service.GetDevicesAsync(AdbPath, CancellationToken.None);

        Assert.AreEqual(AndroidConnectionState.Device, devices.Single(device => device.Serial == "GOOD").ConnectionState);
        var partial = devices.Single(device => device.Serial == "BAD");
        Assert.AreEqual(AndroidConnectionState.Device, partial.ConnectionState);
        Assert.AreEqual(AndroidTargetClassification.Unknown, partial.Classification);
        Assert.IsTrue(partial.IsRunning);
        StringAssert.Contains(partial.Diagnostic, "disconnected");
    }

    private static ProcessResult Success(string output) => Result(0, output, string.Empty);

    private static ProcessResult Result(int exitCode, string output, string error)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(exitCode, output, error, now, now);
    }

    private sealed class ScriptedRunner(Func<ProcessRequest, ProcessResult> run) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(run(request));
        }
    }

    private sealed class FixedEndpointEvidenceProvider(LocalAdbEndpointEvidence evidence)
        : ILocalAdbEndpointEvidenceProvider
    {
        public LocalAdbEndpointEvidence Inspect(string serial) => evidence;
    }
}
