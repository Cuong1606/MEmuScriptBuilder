using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.MEmu;

internal sealed record WindowsProcessSnapshotEntry(
    int ProcessId,
    int ParentProcessId,
    string ExecutableName,
    long? CreationTimeUtcFileTime = null,
    string? CreationTimeFailureReason = null);

internal interface IWindowsProcessSnapshotProvider
{
    IReadOnlyList<WindowsProcessSnapshotEntry> Capture();
}

internal sealed record ProcessCommandLineMetadata(
    string? CommandLine,
    string Source,
    string ReasonCode,
    string? Detail = null)
{
    public bool Succeeded => CommandLine is not null;
}

internal interface IWindowsProcessCommandLineMetadataProvider
{
    ProcessCommandLineMetadata Read(int processId);
}

public sealed class WindowsMemuCoreIdentityResolver : IMemuCoreIdentityResolver
{
    private static readonly TimeSpan DefaultResolutionTimeout = TimeSpan.FromSeconds(3);
    private readonly IWindowsProcessSnapshotProvider processSnapshotProvider;
    private readonly IWindowsProcessCommandLineMetadataProvider commandLineMetadataProvider;
    private readonly IMemuHealthDiagnosticLogger? diagnosticLogger;
    private readonly TimeSpan resolutionTimeout;
    private readonly SemaphoreSlim resolutionGate = new(1, 1);

    public WindowsMemuCoreIdentityResolver(IMemuHealthDiagnosticLogger? diagnosticLogger = null)
        : this(
            new ToolHelpProcessSnapshotProvider(),
            new FallbackWindowsProcessCommandLineMetadataProvider(),
            diagnosticLogger,
            DefaultResolutionTimeout)
    {
    }

    internal WindowsMemuCoreIdentityResolver(
        IWindowsProcessSnapshotProvider processSnapshotProvider,
        IWindowsProcessCommandLineMetadataProvider commandLineMetadataProvider,
        IMemuHealthDiagnosticLogger? diagnosticLogger = null,
        TimeSpan? resolutionTimeout = null)
    {
        this.processSnapshotProvider = processSnapshotProvider;
        this.commandLineMetadataProvider = commandLineMetadataProvider;
        this.diagnosticLogger = diagnosticLogger;
        this.resolutionTimeout = resolutionTimeout ?? DefaultResolutionTimeout;
        if (this.resolutionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(resolutionTimeout));
    }

    public async Task<MemuInstanceHealthResult> ResolveAsync(
        MemuInstance instance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        cancellationToken.ThrowIfCancellationRequested();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(resolutionTimeout);
        var gateEntered = false;
        Task<Resolution>? resolutionTask = null;
        Resolution resolution;
        try
        {
            await resolutionGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            gateEntered = true;
            resolutionTask = Task.Run(() => Resolve(instance, deadline.Token), CancellationToken.None);
            resolution = await resolutionTask.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            resolution = Result(
                instance,
                MemuInstanceHealthResult.Unknown($"Core resolver vượt quá {resolutionTimeout.TotalSeconds:F0} giây."),
                0,
                null,
                null,
                "NATIVE+WMI",
                "RESOLVER_TIMEOUT");
        }
        catch (Exception exception)
        {
            resolution = Result(
                instance,
                MemuInstanceHealthResult.Unknown(exception.Message),
                0,
                null,
                null,
                "NATIVE+WMI",
                "RESOLVER_EXCEPTION",
                exception.GetType().Name);
        }
        finally
        {
            if (gateEntered)
            {
                if (resolutionTask is null || resolutionTask.IsCompleted)
                {
                    resolutionGate.Release();
                }
                else
                {
                    _ = resolutionTask.ContinueWith(
                        static (_, state) => ((SemaphoreSlim)state!).Release(),
                        resolutionGate,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }

        diagnosticLogger?.Write(resolution.Diagnostic);
        return resolution.Result;
    }

    private Resolution Resolve(MemuInstance instance, CancellationToken cancellationToken)
    {
        if (!instance.IsRunning)
            return Result(instance, MemuInstanceHealthResult.Unavailable("Instance không còn ở trạng thái running."),
                0, null, null, "TOOLHELP", "INSTANCE_NOT_RUNNING");
        if (instance.ProcessId is not > 0)
            return Result(instance, MemuInstanceHealthResult.Unknown("listvms không cung cấp PID cho instance."),
                0, null, null, "TOOLHELP", "HOST_PID_UNAVAILABLE");

        IReadOnlyList<WindowsProcessSnapshotEntry> snapshot;
        try
        {
            snapshot = processSnapshotProvider.Capture();
        }
        catch (Exception exception)
        {
            return Result(instance, MemuInstanceHealthResult.Unknown(exception.Message),
                0, null, null, "TOOLHELP", "PROCESS_ENUMERATION_FAILED", exception.GetType().Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var root = snapshot.FirstOrDefault(process => process.ProcessId == instance.ProcessId.Value);
        var candidates = snapshot.Where(process => IsHeadless(process.ExecutableName)).ToList();
        if (root is null)
            return Result(instance, MemuInstanceHealthResult.Unavailable($"Instance PID {instance.ProcessId} đã thoát."),
                candidates.Count, null, null, "TOOLHELP", "HOST_PROCESS_EXITED");
        if (!IsMemuHost(root.ExecutableName) && !IsHeadless(root.ExecutableName))
            return Result(instance, MemuInstanceHealthResult.Unknown($"PID {instance.ProcessId} không phải host MEmu đã biết."),
                candidates.Count, null, null, "TOOLHELP", "HOST_PROCESS_NAME_MISMATCH");

        var hostMetadata = commandLineMetadataProvider.Read(root.ProcessId);
        if (!hostMetadata.Succeeded)
            return Result(instance,
                MemuInstanceHealthResult.Unknown(
                    $"Không đọc được command line của MEmu host PID {root.ProcessId}: {hostMetadata.Detail ?? hostMetadata.ReasonCode}"),
                candidates.Count, null, null, hostMetadata.Source, "HOST_COMMAND_LINE_METADATA_FAILED",
                $"HostReason={hostMetadata.ReasonCode};{hostMetadata.Detail}");

        var verifiedInstanceIdentity = IsHeadless(root.ExecutableName)
            ? TryGetHeadlessInstanceIdentity(hostMetadata.CommandLine)
            : TryGetMemuHostInstanceIdentity(hostMetadata.CommandLine);
        if (verifiedInstanceIdentity is null)
            return Result(instance,
                MemuInstanceHealthResult.Unknown(
                    $"Không tách được VM identity từ command line của MEmu host PID {root.ProcessId}."),
                candidates.Count, null, null, hostMetadata.Source, "HOST_IDENTITY_PARSE_FAILED",
                $"HostReason={hostMetadata.ReasonCode}");

        var matches = new List<(WindowsProcessSnapshotEntry Process, ProcessCommandLineMetadata Metadata)>();
        var resolvedCandidates = new List<(int ProcessId, string Identity, ProcessCommandLineMetadata Metadata)>();
        var unreadable = new List<(int ProcessId, ProcessCommandLineMetadata Metadata)>();
        var unparseable = new List<int>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = commandLineMetadataProvider.Read(candidate.ProcessId);
            if (!metadata.Succeeded)
            {
                unreadable.Add((candidate.ProcessId, metadata));
                continue;
            }

            var candidateIdentity = TryGetHeadlessInstanceIdentity(metadata.CommandLine);
            if (candidateIdentity is null)
            {
                unparseable.Add(candidate.ProcessId);
                continue;
            }

            resolvedCandidates.Add((candidate.ProcessId, candidateIdentity, metadata));

            if (string.Equals(
                    candidateIdentity,
                    verifiedInstanceIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((candidate, metadata));
            }
        }

        if (unreadable.Count > 0)
        {
            var first = unreadable[0];
            return Result(instance,
                MemuInstanceHealthResult.Unknown(
                    $"Không đọc được command line của MEmuHeadless PID {first.ProcessId}: {first.Metadata.Detail ?? first.Metadata.ReasonCode}"),
                candidates.Count, null, null, first.Metadata.Source, first.Metadata.ReasonCode,
                $"Pid={first.ProcessId};UnreadableCount={unreadable.Count};Source={first.Metadata.Source};Reason={first.Metadata.ReasonCode};{first.Metadata.Detail}");
        }

        if (unparseable.Count > 0)
            return Result(instance,
                MemuInstanceHealthResult.Unknown(
                    $"Command line MEmuHeadless không có duy nhất một --comment identity hợp lệ."),
                candidates.Count, null, null, "NATIVE+WMI", "HEADLESS_IDENTITY_PARSE_FAILED",
                string.Join(',', unparseable));

        if (matches.Count == 0)
            return Result(instance,
                MemuInstanceHealthResult.Unknown($"Không có MEmuHeadless khớp --comment {verifiedInstanceIdentity}."),
                candidates.Count, null, null, CombineSources(hostMetadata, resolvedCandidates), "NO_MATCHING_CORE",
                CreateIdentityMappingDetail(hostMetadata, verifiedInstanceIdentity, resolvedCandidates));
        if (matches.Count > 1)
            return Result(instance,
                MemuInstanceHealthResult.Unknown($"VM identity {verifiedInstanceIdentity} ánh xạ tới nhiều core."),
                candidates.Count, null, null, CombineSources(hostMetadata, resolvedCandidates), "MULTIPLE_MATCHING_CORES",
                CreateIdentityMappingDetail(hostMetadata, verifiedInstanceIdentity, resolvedCandidates));

        var match = matches[0];
        if (match.Process.CreationTimeUtcFileTime is not long creationTime)
            return Result(instance,
                MemuInstanceHealthResult.Unknown(
                    $"Không đọc được creation time của MEmuHeadless PID {match.Process.ProcessId}."),
                candidates.Count, match.Process.ProcessId, null, match.Metadata.Source,
                "CREATION_TIME_UNAVAILABLE", match.Process.CreationTimeFailureReason);

        var identity = new MemuInstanceCoreIdentity(match.Process.ProcessId, creationTime, verifiedInstanceIdentity);
        var reasonCode = hostMetadata.Source == FallbackWindowsProcessCommandLineMetadataProvider.FallbackSource ||
            match.Metadata.Source == FallbackWindowsProcessCommandLineMetadataProvider.FallbackSource
            ? "COMMAND_LINE_FALLBACK_USED"
            : "CORE_RESOLVED";
        return Result(instance, MemuInstanceHealthResult.HealthyFor(identity),
            candidates.Count, match.Process.ProcessId, creationTime,
            $"HOST:{hostMetadata.Source};CORE:{match.Metadata.Source}", reasonCode,
            $"VerifiedIdentity={verifiedInstanceIdentity};HostReason={hostMetadata.ReasonCode};CoreReason={match.Metadata.ReasonCode};{hostMetadata.Detail};{match.Metadata.Detail}");
    }

    private static Resolution Result(
        MemuInstance instance,
        MemuInstanceHealthResult result,
        int candidateCount,
        int? matchedPid,
        long? creationTime,
        string source,
        string reasonCode,
        string? detail = null) => new(
            result,
            new MemuHealthDiagnostic(
                DateTimeOffset.UtcNow,
                "PreflightResolve",
                instance.Index,
                instance.Name,
                instance.ProcessId,
                candidateCount,
                matchedPid,
                creationTime,
                source,
                result.Status,
                reasonCode,
                detail));

    internal static string? TryGetHeadlessInstanceIdentity(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var arguments = ParseCommandLine(commandLine);
        if (arguments is null) return null;

        string? identity = null;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!string.Equals(arguments[index], "--comment", StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = arguments[index + 1].Trim();
            if (candidate.Length == 0 || identity is not null) return null;
            identity = candidate;
        }

        return identity;
    }

    internal static string? TryGetMemuHostInstanceIdentity(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var arguments = ParseCommandLine(commandLine);
        if (arguments is null || arguments.Count < 2) return null;
        var identity = arguments[1].Trim();
        return identity.Length == 0 || identity.StartsWith('-') ? null : identity;
    }

    private static string CombineSources(
        ProcessCommandLineMetadata hostMetadata,
        IReadOnlyList<(int ProcessId, string Identity, ProcessCommandLineMetadata Metadata)> candidates) =>
        $"HOST:{hostMetadata.Source};CORE:{string.Join(',', candidates.Select(candidate => candidate.Metadata.Source).Distinct())}";

    private static string CreateIdentityMappingDetail(
        ProcessCommandLineMetadata hostMetadata,
        string verifiedIdentity,
        IReadOnlyList<(int ProcessId, string Identity, ProcessCommandLineMetadata Metadata)> candidates) =>
        $"VerifiedIdentity={verifiedIdentity};HostSource={hostMetadata.Source};HostReason={hostMetadata.ReasonCode};HostDetail={hostMetadata.Detail};Candidates=[{string.Join('|', candidates.Select(candidate =>
            $"Pid={candidate.ProcessId},Identity={candidate.Identity},Source={candidate.Metadata.Source},Reason={candidate.Metadata.ReasonCode},Detail={candidate.Metadata.Detail}"))}]";

    private static IReadOnlyList<string>? ParseCommandLine(string commandLine)
    {
        var argumentVector = NativeMethods.CommandLineToArgvW(commandLine, out var argumentCount);
        if (argumentVector == nint.Zero || argumentCount <= 0) return null;

        try
        {
            var arguments = new string[argumentCount];
            for (var index = 0; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argumentVector, index * nint.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            _ = NativeMethods.LocalFree(argumentVector);
        }
    }

    private static bool IsHeadless(string executableName) =>
        string.Equals(Path.GetFileName(executableName), "MEmuHeadless.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsMemuHost(string executableName) =>
        string.Equals(Path.GetFileName(executableName), "MEmu.exe", StringComparison.OrdinalIgnoreCase);

    private sealed record Resolution(MemuInstanceHealthResult Result, MemuHealthDiagnostic Diagnostic);

    private static class NativeMethods
    {
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);

        [DllImport("kernel32.dll")]
        internal static extern nint LocalFree(nint memory);
    }
}

public sealed class WindowsPinnedMemuCoreHealthCheck : IPinnedMemuCoreHealthCheck
{
    private readonly IWindowsProcessSnapshotProvider processSnapshotProvider;
    private readonly IMemuHealthDiagnosticLogger? diagnosticLogger;

    public WindowsPinnedMemuCoreHealthCheck(IMemuHealthDiagnosticLogger? diagnosticLogger = null)
        : this(new ToolHelpProcessSnapshotProvider(), diagnosticLogger)
    {
    }

    internal WindowsPinnedMemuCoreHealthCheck(
        IWindowsProcessSnapshotProvider processSnapshotProvider,
        IMemuHealthDiagnosticLogger? diagnosticLogger = null)
    {
        this.processSnapshotProvider = processSnapshotProvider;
        this.diagnosticLogger = diagnosticLogger;
    }

    public Task<MemuInstanceHealthResult> CheckAsync(
        MemuInstance instance,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(expectedCoreIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        MemuInstanceHealthResult result;
        string reasonCode;
        string? detail = null;
        try
        {
            var snapshot = processSnapshotProvider.Capture();
            cancellationToken.ThrowIfCancellationRequested();
            var process = snapshot.FirstOrDefault(item => item.ProcessId == expectedCoreIdentity.ProcessId);
            if (process is null)
            {
                result = MemuInstanceHealthResult.Unavailable($"Pinned Core PID {expectedCoreIdentity.ProcessId} đã thoát.");
                reasonCode = "PINNED_CORE_EXITED";
            }
            else if (!string.Equals(Path.GetFileName(process.ExecutableName), "MEmuHeadless.exe", StringComparison.OrdinalIgnoreCase))
            {
                result = MemuInstanceHealthResult.Unavailable($"PID {expectedCoreIdentity.ProcessId} không còn là MEmuHeadless.exe.");
                reasonCode = "PROCESS_NAME_MISMATCH";
            }
            else if (process.CreationTimeUtcFileTime is null)
            {
                result = MemuInstanceHealthResult.Unknown($"Không xác minh được generation của pinned Core PID {expectedCoreIdentity.ProcessId}.");
                reasonCode = "CREATION_TIME_UNAVAILABLE";
                detail = process.CreationTimeFailureReason;
            }
            else if (process.CreationTimeUtcFileTime != expectedCoreIdentity.CreationTimeUtcFileTime)
            {
                result = MemuInstanceHealthResult.Unavailable($"Pinned Core PID {expectedCoreIdentity.ProcessId} đã bị tái sử dụng.");
                reasonCode = "PID_REUSED";
            }
            else
            {
                result = MemuInstanceHealthResult.HealthyFor(expectedCoreIdentity);
                reasonCode = "PINNED_CORE_HEALTHY";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = MemuInstanceHealthResult.Unknown(exception.Message);
            reasonCode = "PROCESS_ENUMERATION_FAILED";
            detail = exception.GetType().Name;
        }

        diagnosticLogger?.Write(new MemuHealthDiagnostic(
            DateTimeOffset.UtcNow,
            checkpoint,
            instance.Index,
            instance.Name,
            instance.ProcessId,
            0,
            expectedCoreIdentity.ProcessId,
            expectedCoreIdentity.CreationTimeUtcFileTime,
            "PINNED_IDENTITY",
            result.Status,
            reasonCode,
            detail));
        return Task.FromResult(result);
    }
}

internal sealed class FallbackWindowsProcessCommandLineMetadataProvider : IWindowsProcessCommandLineMetadataProvider
{
    internal const string FallbackSource = "WMI_WIN32_PROCESS";

    private readonly IWindowsProcessCommandLineReader primary;
    private readonly IWindowsProcessCommandLineReader fallback;

    public FallbackWindowsProcessCommandLineMetadataProvider()
        : this(new NativeWindowsProcessCommandLineReader(), new WmiWindowsProcessCommandLineReader())
    {
    }

    internal FallbackWindowsProcessCommandLineMetadataProvider(
        IWindowsProcessCommandLineReader primary,
        IWindowsProcessCommandLineReader fallback)
    {
        this.primary = primary;
        this.fallback = fallback;
    }

    public ProcessCommandLineMetadata Read(int processId)
    {
        var primaryResult = primary.Read(processId);
        if (primaryResult.Succeeded) return primaryResult;

        var fallbackResult = fallback.Read(processId);
        if (fallbackResult.Succeeded)
            return fallbackResult with
            {
                Source = FallbackSource,
                ReasonCode = "COMMAND_LINE_FALLBACK_USED",
                Detail = $"Primary={primaryResult.ReasonCode}:{primaryResult.Detail}"
            };

        return fallbackResult with
        {
            Source = "NATIVE+WMI",
            ReasonCode = "COMMAND_LINE_METADATA_FAILED",
            Detail = $"Primary={primaryResult.ReasonCode}:{primaryResult.Detail};Fallback={fallbackResult.ReasonCode}:{fallbackResult.Detail}"
        };
    }
}

internal interface IWindowsProcessCommandLineReader
{
    ProcessCommandLineMetadata Read(int processId);
}

internal sealed class NativeWindowsProcessCommandLineReader : IWindowsProcessCommandLineReader
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const int ProcessCommandLineInformation = 60;
    private const int MaximumCommandLineBytes = 64 * 1024;

    public ProcessCommandLineMetadata Read(int processId)
    {
        using var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            return Failure("OPEN_PROCESS_FAILED", $"Win32={Marshal.GetLastWin32Error()}");

        var firstStatus = NativeMethods.NtQueryInformationProcess(
            process,
            ProcessCommandLineInformation,
            nint.Zero,
            0,
            out var requiredLength);
        if (requiredLength <= Marshal.SizeOf<NativeMethods.UnicodeString>() ||
            requiredLength > MaximumCommandLineBytes)
        {
            return Failure("COMMAND_LINE_LENGTH_INVALID",
                $"NtStatus=0x{unchecked((uint)firstStatus):X8};RequiredLength={requiredLength}");
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            var secondStatus = NativeMethods.NtQueryInformationProcess(
                process,
                ProcessCommandLineInformation,
                buffer,
                requiredLength,
                out var returnedLength);
            if (secondStatus < 0)
                return Failure("NT_QUERY_COMMAND_LINE_FAILED",
                    $"NtStatus=0x{unchecked((uint)secondStatus):X8};ReturnedLength={returnedLength}");

            var commandLine = Marshal.PtrToStructure<NativeMethods.UnicodeString>(buffer);
            if (commandLine.Length == 0)
                return new ProcessCommandLineMetadata(string.Empty, "NT_QUERY_INFORMATION_PROCESS", "COMMAND_LINE_PRIMARY_SUCCESS");
            if (commandLine.Buffer == nint.Zero || commandLine.Length % sizeof(char) != 0)
                return Failure("UNICODE_STRING_INVALID", $"Length={commandLine.Length};Buffer={commandLine.Buffer}");

            var commandLineOffset = commandLine.Buffer.ToInt64() - buffer.ToInt64();
            if (commandLineOffset < 0 || commandLineOffset + commandLine.Length > requiredLength)
                return Failure("COMMAND_LINE_BUFFER_OUT_OF_RANGE",
                    $"Offset={commandLineOffset};Length={commandLine.Length};RequiredLength={requiredLength}");

            var value = Marshal.PtrToStringUni(commandLine.Buffer, commandLine.Length / sizeof(char));
            return value is null
                ? Failure("UNICODE_DECODE_FAILED", $"Length={commandLine.Length}")
                : new ProcessCommandLineMetadata(value, "NT_QUERY_INFORMATION_PROCESS", "COMMAND_LINE_PRIMARY_SUCCESS");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessCommandLineMetadata Failure(string reasonCode, string detail) =>
        new(null, "NT_QUERY_INFORMATION_PROCESS", reasonCode, detail);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("ntdll.dll")]
        internal static extern int NtQueryInformationProcess(
            SafeProcessHandle process,
            int processInformationClass,
            nint processInformation,
            int processInformationLength,
            out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        internal struct UnicodeString
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal nint Buffer;
        }
    }
}

internal sealed class WmiWindowsProcessCommandLineReader : IWindowsProcessCommandLineReader
{
    public ProcessCommandLineMetadata Read(int processId)
    {
        object? locator = null;
        object? services = null;
        object? results = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator", throwOnError: false);
            if (locatorType is null)
                return Failure("WMI_PROVIDER_UNAVAILABLE", "SWbemLocator is not registered.");

            locator = Activator.CreateInstance(locatorType);
            if (locator is null) return Failure("WMI_PROVIDER_UNAVAILABLE", "Cannot create SWbemLocator.");
            dynamic dynamicLocator = locator;
            services = dynamicLocator.ConnectServer(".", "root\\cimv2");
            dynamic dynamicServices = services;
            results = dynamicServices.ExecQuery(
                $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {processId}",
                "WQL",
                0x30);

            foreach (var item in (IEnumerable)results)
            {
                try
                {
                    dynamic process = item;
                    string? commandLine = process.CommandLine as string;
                    return commandLine is null
                        ? Failure("WMI_COMMAND_LINE_NULL", $"Pid={processId}")
                        : new ProcessCommandLineMetadata(commandLine, "WMI_WIN32_PROCESS", "COMMAND_LINE_FALLBACK_SUCCESS");
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }

            return Failure("WMI_PROCESS_NOT_FOUND", $"Pid={processId}");
        }
        catch (Exception exception)
        {
            return Failure("WMI_QUERY_FAILED", $"{exception.GetType().Name}:{exception.Message}");
        }
        finally
        {
            ReleaseComObject(results);
            ReleaseComObject(services);
            ReleaseComObject(locator);
        }
    }

    private static ProcessCommandLineMetadata Failure(string reasonCode, string detail) =>
        new(null, "WMI_WIN32_PROCESS", reasonCode, detail);

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            _ = Marshal.FinalReleaseComObject(value);
    }
}

internal sealed class ToolHelpProcessSnapshotProvider : IWindowsProcessSnapshotProvider
{
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x00001000;

    public IReadOnlyList<WindowsProcessSnapshotEntry> Capture()
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Không thể chụp process snapshot.");

        var entry = new NativeMethods.ProcessEntry32
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>()
        };
        if (!NativeMethods.Process32First(snapshot, ref entry))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ErrorNoMoreFiles) return [];
            throw new Win32Exception(error, "Không thể đọc process snapshot.");
        }

        var processes = new List<WindowsProcessSnapshotEntry>();
        do
        {
            if (entry.ProcessId <= int.MaxValue && entry.ParentProcessId <= int.MaxValue)
            {
                var creation = IsHeadless(entry.ExecutableName)
                    ? ReadCreationTime((int)entry.ProcessId)
                    : default;
                processes.Add(new WindowsProcessSnapshotEntry(
                    (int)entry.ProcessId,
                    (int)entry.ParentProcessId,
                    entry.ExecutableName,
                    creation.CreationTimeUtcFileTime,
                    creation.FailureReason));
            }

            entry.Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>();
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));

        var lastError = Marshal.GetLastWin32Error();
        if (lastError != NativeMethods.ErrorNoMoreFiles)
            throw new Win32Exception(lastError, "Process snapshot kết thúc bất thường.");
        return processes;
    }

    internal static (long? CreationTimeUtcFileTime, string? FailureReason) ReadCreationTime(int processId)
    {
        using var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            return (null, $"OPEN_PROCESS_FAILED:Win32={Marshal.GetLastWin32Error()}");
        if (!NativeMethods.GetProcessTimes(process, out var creationTime, out _, out _, out _))
            return (null, $"GET_PROCESS_TIMES_FAILED:Win32={Marshal.GetLastWin32Error()}");
        return (((long)creationTime.HighDateTime << 32) | creationTime.LowDateTime, null);
    }

    private static bool IsHeadless(string executableName) =>
        string.Equals(Path.GetFileName(executableName), "MEmuHeadless.exe", StringComparison.OrdinalIgnoreCase);

    private static class NativeMethods
    {
        internal const int ErrorNoMoreFiles = 18;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry32
        {
            internal uint Size;
            internal uint Usage;
            internal uint ProcessId;
            internal nint DefaultHeapId;
            internal uint ModuleId;
            internal uint Threads;
            internal uint ParentProcessId;
            internal int BasePriority;
            internal uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string ExecutableName;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(
            SafeProcessHandle process,
            out FileTime creationTime,
            out FileTime exitTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            internal uint LowDateTime;
            internal uint HighDateTime;
        }
    }
}
