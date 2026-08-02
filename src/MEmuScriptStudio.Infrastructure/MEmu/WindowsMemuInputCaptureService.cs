using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class WindowsMemuInputCaptureService(
    IProcessRunner processRunner,
    MemuCommandBuilder commandBuilder) : IMemuInputCaptureService
{
    private readonly SemaphoreSlim captureGate = new(1, 1);

    public async Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, CancellationToken cancellationToken) =>
        (CapturedTap)await CaptureAsync(memucPath, instance, CaptureKind.Tap, null, cancellationToken).ConfigureAwait(false);

    public async Task<CapturedSwipe> CaptureSwipeAsync(
        string memucPath,
        MemuInstance instance,
        IProgress<SwipeCaptureUpdate>? progress,
        CancellationToken cancellationToken) =>
        (CapturedSwipe)await CaptureAsync(memucPath, instance, CaptureKind.Swipe, progress, cancellationToken).ConfigureAwait(false);

    private async Task<object> CaptureAsync(
        string memucPath,
        MemuInstance instance,
        CaptureKind kind,
        IProgress<SwipeCaptureUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (!instance.IsRunning || instance.WindowHandle is null or <= 0)
            throw new InvalidOperationException("Instance phải đang chạy và có window handle hợp lệ.");

        await captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sizeCommand = commandBuilder.BuildAndroidShell(memucPath, instance.Index, "wm size");
            var sizeResult = await processRunner.RunAsync(
                new ProcessRequest(sizeCommand.ExecutablePath, sizeCommand.Arguments, TimeSpan.FromSeconds(10)),
                cancellationToken).ConfigureAwait(false);
            if (sizeResult.ExitCode != 0)
                throw new InvalidOperationException($"Không đọc được độ phân giải Android: {sizeResult.StandardError.Trim()}");

            var (guestWidth, guestHeight) = AndroidScreenSizeParser.Parse(sizeResult.StandardOutput);
            var windowHandle = new nint(instance.WindowHandle.Value);
            if (!NativeMethods.IsWindow(windowHandle))
                throw new InvalidOperationException("Cửa sổ MEmu không còn tồn tại. Hãy làm mới danh sách instance.");
            NativeMethods.GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (instance.ProcessId is null || windowProcessId != (uint)instance.ProcessId.Value)
                throw new InvalidOperationException("Window handle không còn thuộc instance đã chọn. Hãy làm mới danh sách instance.");

            var session = new MouseCaptureSession(
                () => ResolveViewport(windowHandle, guestWidth, guestHeight),
                guestWidth,
                guestHeight,
                kind,
                progress,
                cancellationToken);
            return await session.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            captureGate.Release();
        }
    }

    private static ScreenRectangle ResolveViewport(nint rootWindow, int guestWidth, int guestHeight)
    {
        if (!NativeMethods.IsWindow(rootWindow))
            throw new InvalidOperationException("Cửa sổ MEmu đã đóng trong lúc ghi tọa độ.");

        var rootRectangle = TryGetScreenClientRectangle(rootWindow)
            ?? throw new InvalidOperationException("Không đọc được vùng cửa sổ MEmu.");
        var windows = new List<nint>();
        NativeMethods.EnumChildWindows(rootWindow, (child, _) =>
        {
            if (NativeMethods.IsWindowVisible(child)) windows.Add(child);
            return true;
        }, nint.Zero);

        var candidates = windows.Select(TryGetScreenClientRectangle).Where(value => value is not null).Cast<ScreenRectangle>().ToList();
        return MemuViewportSelector.Select(rootRectangle, candidates, guestWidth, guestHeight);
    }

    private static ScreenRectangle? TryGetScreenClientRectangle(nint window)
    {
        if (!NativeMethods.GetClientRect(window, out var client)) return null;
        var origin = new NativeMethods.Point(client.Left, client.Top);
        if (!NativeMethods.ClientToScreen(window, ref origin)) return null;
        return new ScreenRectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    private enum CaptureKind { Tap, Swipe }

    private sealed class MouseCaptureSession(
        Func<ScreenRectangle> viewportProvider,
        int guestWidth,
        int guestHeight,
        CaptureKind kind,
        IProgress<SwipeCaptureUpdate>? progress,
        CancellationToken cancellationToken)
    {
        private readonly TaskCompletionSource<object> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim quitSignal = new(false);
        private NativeMethods.HookProc? mouseCallback;
        private NativeMethods.HookProc? keyboardCallback;
        private bool leftPointerDown;
        private bool rightPointerDown;
        private ScreenPoint startGuest;
        private readonly SwipePointSelection swipeSelection = new();
        private readonly InputCaptureKeyLatch keyLatch = new();
        private long lastProgressTimestamp;
        private Timer? pendingKeyFallback;
        private readonly object outcomeGate = new();
        private object? capturedResult;
        private Exception? capturedException;
        private bool wasCancelled;

        public Task<object> RunAsync()
        {
            var thread = new Thread(RunMessageLoop) { IsBackground = true, Name = "MEmu input capture" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private void RunMessageLoop()
        {
            mouseCallback = MouseHook;
            keyboardCallback = KeyboardHook;
            var module = NativeMethods.GetModuleHandle(null);
            var mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, mouseCallback, module, 0);
            var keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, keyboardCallback, module, 0);
            if (mouseHook == nint.Zero || keyboardHook == nint.Zero)
            {
                if (mouseHook != nint.Zero && !NativeMethods.UnhookWindowsHookEx(mouseHook))
                    completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Không thể dọn mouse hook sau lỗi khởi tạo."));
                if (keyboardHook != nint.Zero && !NativeMethods.UnhookWindowsHookEx(keyboardHook))
                    completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Không thể dọn keyboard hook sau lỗi khởi tạo."));
                completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error(), "Không thể bắt thao tác chuột/bàn phím."));
                return;
            }

            using var registration = cancellationToken.Register(() => RequestCancellation());

            try
            {
                if (kind == CaptureKind.Swipe) ReportProgress(viewportProvider());
                while (!quitSignal.IsSet)
                {
                    while (NativeMethods.PeekMessage(out var message, nint.Zero, 0, 0, NativeMethods.PmRemove))
                    {
                        NativeMethods.TranslateMessage(ref message);
                        NativeMethods.DispatchMessage(ref message);
                    }
                    if (!quitSignal.IsSet) quitSignal.Wait(TimeSpan.FromMilliseconds(10));
                }
            }
            finally
            {
                Interlocked.Exchange(ref pendingKeyFallback, null)?.Dispose();
                Exception? cleanupException = null;
                if (!NativeMethods.UnhookWindowsHookEx(mouseHook))
                    cleanupException = new Win32Exception(Marshal.GetLastWin32Error(), "Không thể dọn mouse hook.");
                if (!NativeMethods.UnhookWindowsHookEx(keyboardHook) && cleanupException is null)
                    cleanupException = new Win32Exception(Marshal.GetLastWin32Error(), "Không thể dọn keyboard hook.");
                CompleteAfterTeardown(cleanupException);
            }
        }

        private nint MouseHook(int code, nint message, nint data)
        {
            if (code < 0) return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
            try
            {
                var mouse = Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(data);
                var screenPoint = new ScreenPoint(mouse.Point.X, mouse.Point.Y);
                if (kind == CaptureKind.Swipe)
                    return HandleSwipeMouse(code, message, data, screenPoint);

                if (!leftPointerDown)
                {
                    if ((int)message != NativeMethods.WmLButtonDown)
                        return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
                    var viewport = viewportProvider();
                    if (!viewport.Contains(screenPoint))
                        return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
                    leftPointerDown = true;
                    startGuest = MemuCoordinateMapper.ToGuest(screenPoint, viewport, guestWidth, guestHeight);
                    return 1;
                }

                if ((int)message == NativeMethods.WmLButtonUp)
                {
                    leftPointerDown = false;
                    RequestResult(new CapturedTap(startGuest.X, startGuest.Y));
                }
                return 1;
            }
            catch (Exception exception)
            {
                RequestException(exception);
                return 1;
            }
        }

        private nint HandleSwipeMouse(int code, nint message, nint data, ScreenPoint screenPoint)
        {
            var messageValue = (int)message;
            if (messageValue == NativeMethods.WmMouseMove)
            {
                var now = Stopwatch.GetTimestamp();
                if (lastProgressTimestamp == 0 || Stopwatch.GetElapsedTime(lastProgressTimestamp, now) >= TimeSpan.FromMilliseconds(33))
                {
                    lastProgressTimestamp = now;
                    ReportProgress(viewportProvider());
                }
                return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
            }

            if (messageValue == NativeMethods.WmLButtonDown)
            {
                var viewport = viewportProvider();
                if (!viewport.Contains(screenPoint)) return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
                leftPointerDown = true;
                swipeSelection.SelectStart(MemuCoordinateMapper.ToGuest(screenPoint, viewport, guestWidth, guestHeight));
                ReportProgress(viewport);
                return 1;
            }

            if (messageValue == NativeMethods.WmLButtonUp && leftPointerDown)
            {
                leftPointerDown = false;
                ReportProgress(viewportProvider());
                return 1;
            }

            if (messageValue == NativeMethods.WmRButtonDown)
            {
                var viewport = viewportProvider();
                if (!viewport.Contains(screenPoint)) return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
                rightPointerDown = true;
                swipeSelection.SelectEnd(MemuCoordinateMapper.ToGuest(screenPoint, viewport, guestWidth, guestHeight));
                ReportProgress(viewport);
                return 1;
            }

            if (messageValue == NativeMethods.WmRButtonUp && rightPointerDown)
            {
                rightPointerDown = false;
                ReportProgress(viewportProvider());
                return 1;
            }

            return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
        }

        private void ReportProgress(ScreenRectangle viewport) =>
            progress?.Report(new SwipeCaptureUpdate(
                viewport,
                guestWidth,
                guestHeight,
                swipeSelection.StartPoint,
                swipeSelection.EndPoint));

        private nint KeyboardHook(int code, nint message, nint data)
        {
            if (code >= 0)
            {
                var keyboard = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardData>(data);
                var key = keyboard.VirtualKey switch
                {
                    NativeMethods.VkEscape => InputCaptureKey.Escape,
                    NativeMethods.VkReturn => InputCaptureKey.Enter,
                    _ => InputCaptureKey.Other
                };
                var isKeyDown = (int)message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var action = InputCaptureKeyPolicy.Resolve(kind == CaptureKind.Swipe, key, isKeyDown, swipeSelection.CanConfirm);
                switch (action)
                {
                    case InputCaptureKeyAction.Cancel:
                        PrepareKeyboardCancellation(key);
                        return 1;
                    case InputCaptureKeyAction.Confirm:
                        PrepareKeyboardResult(swipeSelection.Confirm(), key);
                        return 1;
                    case InputCaptureKeyAction.Suppress:
                        if (!isKeyDown && keyLatch.Release(key))
                        {
                            Interlocked.Exchange(ref pendingKeyFallback, null)?.Dispose();
                            RequestQuit();
                        }
                        return 1;
                }
            }
            return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
        }

        private void PrepareKeyboardResult(object result, InputCaptureKey key)
        {
            lock (outcomeGate)
            {
                if (capturedResult is not null || capturedException is not null || wasCancelled) return;
                capturedResult = result;
                keyLatch.Begin(key);
                ArmPendingKeyFallback();
            }
        }

        private void PrepareKeyboardCancellation(InputCaptureKey key)
        {
            lock (outcomeGate)
            {
                if (capturedResult is not null || capturedException is not null || wasCancelled) return;
                wasCancelled = true;
                keyLatch.Begin(key);
                ArmPendingKeyFallback();
            }
        }

        private void ArmPendingKeyFallback()
        {
            Interlocked.Exchange(
                ref pendingKeyFallback,
                new Timer(_ => RequestQuit(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan))?.Dispose();
        }

        private void RequestResult(object result)
        {
            lock (outcomeGate)
            {
                if (capturedResult is not null || capturedException is not null || wasCancelled) return;
                capturedResult = result;
            }
            RequestQuit();
        }

        private void RequestException(Exception exception)
        {
            lock (outcomeGate)
            {
                if (capturedResult is not null || capturedException is not null || wasCancelled) return;
                capturedException = exception;
            }
            RequestQuit();
        }

        private void RequestCancellation()
        {
            lock (outcomeGate)
            {
                if (capturedResult is not null || capturedException is not null || wasCancelled) return;
                wasCancelled = true;
            }
            RequestQuit();
        }

        private void RequestQuit()
        {
            quitSignal.Set();
        }

        private void CompleteAfterTeardown(Exception? cleanupException)
        {
            lock (outcomeGate)
            {
                if (capturedException is not null) completion.TrySetException(capturedException);
                else if (cleanupException is not null) completion.TrySetException(cleanupException);
                else if (wasCancelled) completion.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
                else if (capturedResult is not null) completion.TrySetResult(capturedResult);
                else completion.TrySetException(new InvalidOperationException("Phiên ghi thao tác kết thúc nhưng không có kết quả."));
            }
        }
    }

    private static class NativeMethods
    {
        internal const int WhKeyboardLl = 13;
        internal const int WhMouseLl = 14;
        internal const int WmKeyDown = 0x0100;
        internal const int WmKeyUp = 0x0101;
        internal const int WmSysKeyDown = 0x0104;
        internal const int WmSysKeyUp = 0x0105;
        internal const int WmMouseMove = 0x0200;
        internal const int WmLButtonDown = 0x0201;
        internal const int WmLButtonUp = 0x0202;
        internal const int WmRButtonDown = 0x0204;
        internal const int WmRButtonUp = 0x0205;
        internal const uint PmRemove = 0x0001;
        internal const uint VkEscape = 0x1B;
        internal const uint VkReturn = 0x0D;

        internal delegate nint HookProc(int code, nint wParam, nint lParam);
        internal delegate bool EnumWindowsProc(nint window, nint parameter);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;
            internal Point(int x, int y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Message { internal nint Window; internal uint Value; internal nint WParam, LParam; internal uint Time; internal Point Point; }
        [StructLayout(LayoutKind.Sequential)] internal struct LowLevelMouseData { internal Point Point; internal uint MouseData, Flags, Time; internal nuint ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct LowLevelKeyboardData { internal uint VirtualKey, ScanCode, Flags, Time; internal nuint ExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint GetModuleHandle(string? moduleName);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rectangle rectangle);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint window, ref Point point);
    }
}
