using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public enum MemuInstanceHealthStatus
{
    Healthy,
    Unavailable,
    Unknown
}

public sealed record MemuInstanceCoreIdentity(
    int ProcessId,
    long CreationTimeUtcFileTime,
    string VerifiedInstanceIdentity);

public sealed record MemuInstanceHealthResult(
    MemuInstanceHealthStatus Status,
    string? Diagnostic = null,
    MemuInstanceCoreIdentity? CoreIdentity = null)
{
    public int? CoreProcessId => CoreIdentity?.ProcessId;

    public static MemuInstanceHealthResult HealthyFor(MemuInstanceCoreIdentity coreIdentity) =>
        new(MemuInstanceHealthStatus.Healthy, CoreIdentity: coreIdentity);
    public static MemuInstanceHealthResult HealthyFor(
        int coreProcessId,
        long creationTimeUtcFileTime,
        string verifiedInstanceIdentity) =>
        HealthyFor(new MemuInstanceCoreIdentity(
            coreProcessId,
            creationTimeUtcFileTime,
            verifiedInstanceIdentity));
    public static MemuInstanceHealthResult HealthyFor(int coreProcessId, long creationTimeUtcFileTime) =>
        HealthyFor(coreProcessId, creationTimeUtcFileTime, string.Empty);
    public static MemuInstanceHealthResult Unavailable(string? diagnostic = null) =>
        new(MemuInstanceHealthStatus.Unavailable, diagnostic);
    public static MemuInstanceHealthResult Unknown(string? diagnostic = null) =>
        new(MemuInstanceHealthStatus.Unknown, diagnostic);
}

public interface IMemuCoreIdentityResolver
{
    Task<MemuInstanceHealthResult> ResolveAsync(
        MemuInstance instance,
        CancellationToken cancellationToken);
}

public interface IPinnedMemuCoreHealthCheck
{
    Task<MemuInstanceHealthResult> CheckAsync(
        MemuInstance instance,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken);
}

public interface IMemuInstanceHealthProbe : IMemuCoreIdentityResolver, IPinnedMemuCoreHealthCheck
{
    Task<MemuInstanceHealthResult> CheckAsync(
        MemuInstance instance,
        MemuInstanceCoreIdentity? expectedCoreIdentity,
        CancellationToken cancellationToken);

    Task<MemuInstanceHealthResult> IMemuCoreIdentityResolver.ResolveAsync(
        MemuInstance instance,
        CancellationToken cancellationToken) =>
        CheckAsync(instance, null, cancellationToken);

    Task<MemuInstanceHealthResult> IPinnedMemuCoreHealthCheck.CheckAsync(
        MemuInstance instance,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken) =>
        CheckAsync(instance, expectedCoreIdentity, cancellationToken);
}

public sealed record MemuHealthDiagnostic(
    DateTimeOffset Timestamp,
    string Checkpoint,
    int InstanceIndex,
    string InstanceName,
    int? HostProcessId,
    int CandidateCoreCount,
    int? MatchedCoreProcessId,
    long? CoreCreationTimeUtcFileTime,
    string ResolverSource,
    MemuInstanceHealthStatus Result,
    string ReasonCode,
    string? Detail = null);

public interface IMemuHealthDiagnosticLogger
{
    void Write(MemuHealthDiagnostic diagnostic);
}

internal sealed class AssumeHealthyMemuCoreIdentityResolver : IMemuCoreIdentityResolver
{
    public static AssumeHealthyMemuCoreIdentityResolver Instance { get; } = new();

    public Task<MemuInstanceHealthResult> ResolveAsync(
        MemuInstance instance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MemuInstanceHealthResult.HealthyFor(
            instance.ProcessId ?? int.MaxValue,
            0,
            instance.Name));
    }
}

internal sealed class AssumeHealthyPinnedMemuCoreHealthCheck : IPinnedMemuCoreHealthCheck
{
    public static AssumeHealthyPinnedMemuCoreHealthCheck Instance { get; } = new();

    public Task<MemuInstanceHealthResult> CheckAsync(
        MemuInstance instance,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MemuInstanceHealthResult.HealthyFor(expectedCoreIdentity));
    }
}

internal static class MemuInstanceHealthChecks
{
    public const string UnavailableMessage = "Core MEmu không còn hoạt động.";
    public const string UnknownMessage = "Không thể xác minh trạng thái Core MEmu.";

    public static async Task<MemuInstanceHealthResult> ResolveSafelyAsync(
        IMemuCoreIdentityResolver resolver,
        MemuInstance instance,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resolver.ResolveAsync(instance, cancellationToken).ConfigureAwait(false)
                ?? MemuInstanceHealthResult.Unknown("Resolver trả về kết quả rỗng.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return MemuInstanceHealthResult.Unknown(exception.Message);
        }
    }

    public static async Task<MemuInstanceHealthResult> CheckPinnedSafelyAsync(
        IPinnedMemuCoreHealthCheck healthCheck,
        MemuInstance instance,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            return await healthCheck
                .CheckAsync(instance, expectedCoreIdentity, checkpoint, cancellationToken)
                .ConfigureAwait(false)
                ?? MemuInstanceHealthResult.Unknown("Pinned health check trả về kết quả rỗng.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return MemuInstanceHealthResult.Unknown(exception.Message);
        }
    }
}

internal sealed class MemuInstanceUnavailableException : Exception
{
    public MemuInstanceUnavailableException(string? diagnostic)
        : base(MemuInstanceHealthChecks.UnavailableMessage)
    {
        Diagnostic = diagnostic;
    }

    public string? Diagnostic { get; }
}
