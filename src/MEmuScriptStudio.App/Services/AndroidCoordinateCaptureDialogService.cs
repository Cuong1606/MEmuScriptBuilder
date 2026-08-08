using System.Windows;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Services;

public enum AndroidCoordinateCaptureMode
{
    Tap,
    Hold,
    Swipe
}

public sealed record AndroidCoordinateCaptureResult(CapturedTap? Tap = null, CapturedSwipe? Swipe = null);

public interface IAndroidCoordinateCaptureDialogService
{
    Task<AndroidCoordinateCaptureResult?> CaptureAsync(
        string adbPath,
        AndroidAdbDevice device,
        AndroidCoordinateCaptureMode mode,
        CancellationToken cancellationToken);
}

public sealed class AndroidCoordinateCaptureDialogService(
    IAndroidScreenshotCaptureService screenshotCaptureService) : IAndroidCoordinateCaptureDialogService
{
    public Task<AndroidCoordinateCaptureResult?> CaptureAsync(
        string adbPath,
        AndroidAdbDevice device,
        AndroidCoordinateCaptureMode mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new AndroidCoordinateCaptureWindow(
            screenshotCaptureService,
            adbPath,
            device,
            mode)
        {
            Owner = Application.Current?.MainWindow
        };
        using var registration = cancellationToken.Register(() =>
            _ = window.Dispatcher.BeginInvoke(window.Close));
        var accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(accepted ? window.CaptureResult : null);
    }
}
