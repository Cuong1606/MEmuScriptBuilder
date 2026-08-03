using System.Windows;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Services;

public interface ITapCaptureOverlayService
{
    ITapCaptureOverlaySession Show();
}

public interface ITapCaptureOverlaySession : IProgress<TapCaptureUpdate>, IDisposable;

public sealed class TapCaptureOverlayService : ITapCaptureOverlayService
{
    public ITapCaptureOverlaySession Show()
    {
        var window = new SwipeCaptureOverlayWindow
        {
            Owner = Application.Current?.MainWindow
        };
        window.ConfigureTapCapture();
        window.Show();
        return new Session(window);
    }

    private sealed class Session(SwipeCaptureOverlayWindow window) : ITapCaptureOverlaySession
    {
        private int disposed;

        public void Report(TapCaptureUpdate value)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                if (Volatile.Read(ref disposed) == 0) window.UpdateCapture(value);
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (window.Dispatcher.CheckAccess()) window.Close();
            else _ = window.Dispatcher.BeginInvoke(window.Close);
        }
    }
}
