using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MEmuScriptStudio.App.Services;

internal sealed record SingleInstanceNames(string MutexName, string PipeName)
{
    public static SingleInstanceNames ForCurrentUserSession()
    {
        string identity;
        try
        {
            identity = WindowsIdentity.GetCurrent().User?.Value
                       ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
        catch (Exception)
        {
            identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        var identityHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))).Substring(0, 16);
        var sessionId = Process.GetCurrentProcess().SessionId;
        return new SingleInstanceNames(
            $@"Local\MEmuScriptStudio.SingleInstance.{identityHash}.{sessionId}",
            $"MEmuScriptStudio.Activation.{identityHash}.{sessionId}");
    }
}

internal readonly record struct SingleInstanceStartupResult(bool IsPrimary, bool ActivationSent)
{
    public bool ShouldContinueStartup => IsPrimary;
}

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string ActivateMainWindowRequest = "ActivateMainWindow";
    private readonly SingleInstanceNames names;
    private readonly Action<Exception> reportError;
    private readonly int connectTimeoutMilliseconds;
    private readonly object sync = new();
    private Mutex? ownershipMutex;
    private CancellationTokenSource? listenerCancellation;
    private NamedPipeServerStream? activeServer;
    private Task? listenerTask;
    private bool ownsMutex;
    private bool started;
    private bool disposed;

    public SingleInstanceCoordinator(
        SingleInstanceNames names,
        Action<Exception>? reportError = null,
        int connectTimeoutMilliseconds = 1500)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (string.IsNullOrWhiteSpace(names.MutexName))
            throw new ArgumentException("Mutex name is required.", nameof(names));
        if (string.IsNullOrWhiteSpace(names.PipeName))
            throw new ArgumentException("Pipe name is required.", nameof(names));
        if (connectTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectTimeoutMilliseconds));
        this.names = names;
        this.reportError = reportError ?? (_ => { });
        this.connectTimeoutMilliseconds = connectTimeoutMilliseconds;
    }

    public SingleInstanceStartupResult Start(Action activateMainWindow)
    {
        ArgumentNullException.ThrowIfNull(activateMainWindow);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("Single-instance coordination has already started.");
            started = true;
        }

        var mutex = new Mutex(initiallyOwned: true, names.MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return new SingleInstanceStartupResult(false, TrySendActivationRequest());
        }

        lock (sync)
        {
            ownershipMutex = mutex;
            ownsMutex = true;
            listenerCancellation = new CancellationTokenSource();
            listenerTask = ListenAsync(activateMainWindow, listenerCancellation.Token);
        }
        return new SingleInstanceStartupResult(true, false);
    }

    private bool TrySendActivationRequest()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", names.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(connectTimeoutMilliseconds);
            var payload = Encoding.UTF8.GetBytes($"{ActivateMainWindowRequest}\n");
            client.Write(payload);
            client.Flush();
            return true;
        }
        catch (Exception exception)
        {
            ReportSafely(exception);
            return false;
        }
    }

    private async Task ListenAsync(Action activateMainWindow, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var retryAfterFailure = false;
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    names.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    128,
                    128);
                lock (sync)
                {
                    if (disposed)
                    {
                        server.Dispose();
                        return;
                    }
                    activeServer = server;
                }

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCancellation.CancelAfter(TimeSpan.FromSeconds(2));
                var request = await ReadRequestAsync(server, requestCancellation.Token).ConfigureAwait(false);
                if (!string.Equals(request, ActivateMainWindowRequest, StringComparison.Ordinal)) continue;
                try
                {
                    activateMainWindow();
                }
                catch (Exception exception)
                {
                    ReportSafely(exception);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                retryAfterFailure = true;
                ReportSafely(exception);
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(activeServer, server)) activeServer = null;
                }
                server?.Dispose();
            }

            if (!retryAfterFailure) continue;
            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task<string> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[64];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
            if (buffer.AsSpan(0, length).Contains((byte)'\n')) break;
        }
        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    private void ReportSafely(Exception exception)
    {
        try { reportError(exception); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        Mutex? mutex;
        CancellationTokenSource? cancellation;
        NamedPipeServerStream? server;
        Task? task;
        bool releaseMutex;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            mutex = ownershipMutex;
            ownershipMutex = null;
            releaseMutex = ownsMutex;
            ownsMutex = false;
            cancellation = listenerCancellation;
            listenerCancellation = null;
            server = activeServer;
            activeServer = null;
            task = listenerTask;
            listenerTask = null;
        }

        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        server?.Dispose();
        if (task is not null)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        if (releaseMutex && mutex is not null)
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException exception) { ReportSafely(exception); }
        }
        mutex?.Dispose();
        cancellation?.Dispose();
    }
}
