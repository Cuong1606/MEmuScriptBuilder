using System.Windows;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Services;

public interface ISwipeCaptureOverlayService
{
    ISwipeCaptureOverlaySession Show();
}

public interface ISwipeCaptureOverlaySession : IProgress<SwipeCaptureUpdate>, IDisposable;

public sealed class SwipeCaptureOverlayService : ISwipeCaptureOverlayService
{
    public ISwipeCaptureOverlaySession Show()
    {
        var window = new SwipeCaptureOverlayWindow
        {
            Owner = Application.Current?.MainWindow
        };
        window.Show();
        return new Session(window);
    }

    private sealed class Session(SwipeCaptureOverlayWindow window) : ISwipeCaptureOverlaySession
    {
        private int disposed;

        public void Report(SwipeCaptureUpdate value)
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
