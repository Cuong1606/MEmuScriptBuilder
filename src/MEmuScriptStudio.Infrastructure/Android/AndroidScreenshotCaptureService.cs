using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AndroidScreenshotCaptureService(
    IBinaryProcessRunner processRunner,
    AdbCommandBuilder commandBuilder) : IAndroidScreenshotCaptureService
{
    public async Task<AndroidScreenshotData> CaptureAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        var command = commandBuilder.BuildScreenCapture(adbPath, serial);
        var result = await processRunner.RunAsync(
            new BinaryProcessRequest(
                command.ExecutablePath,
                command.Arguments,
                TimeSpan.FromSeconds(15),
                "AndroidCapture:screencap"),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(BuildFailureMessage(result.StandardError, result.ExitCode));
        if (result.StandardOutputTruncated)
            throw new InvalidDataException("Ảnh chụp Android vượt quá giới hạn 32 MiB.");
        AndroidPngHeaderValidator.ValidateAndReadDimensions(result.StandardOutput);

        return new AndroidScreenshotData(result.StandardOutput);
    }

    private static string BuildFailureMessage(string error, int exitCode)
    {
        var compact = string.Join(' ', error.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length > 200) compact = $"{compact[..199]}…";
        return compact.Length == 0
            ? $"Không thể chụp màn hình Android (exit code {exitCode})."
            : $"Không thể chụp màn hình Android: {compact}";
    }
}
