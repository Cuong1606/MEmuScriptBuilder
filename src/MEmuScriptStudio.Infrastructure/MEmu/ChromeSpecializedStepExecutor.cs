using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class ChromeSpecializedStepExecutor(IChromeTabService chromeTabService) : ISpecializedStepExecutor
{
    public string BuildPreview(ScriptStep step, string memucPath, int instanceIndex) => step switch
    {
        CloseChromeTabsStep =>
            $"[Instance {instanceIndex}] CDP qua ADB forward: ưu tiên browser WebSocket/Target domain; " +
            "chỉ khi không tương thích protocol mới dùng Legacy /json/list + /json/close; xác minh còn 0 page và luôn gỡ forward.",
        _ => throw new NotSupportedException($"{step.GetType().Name} không phải bước Chrome chuyên biệt.")
    };

    public async Task<SpecializedStepExecutionResult> ExecuteAsync(
        ScriptStep step,
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step is not CloseChromeTabsStep)
            throw new NotSupportedException($"{step.GetType().Name} không phải bước Chrome chuyên biệt.");
        if (step.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        var startedAt = DateTimeOffset.UtcNow;
        var preview = BuildPreview(step, memucPath, instanceIndex);
        var outcome = await chromeTabService.CloseAllTabsAsync(
            memucPath,
            instanceIndex,
            TimeSpan.FromSeconds(step.TimeoutSeconds),
            cancellationToken).ConfigureAwait(false);
        return new SpecializedStepExecutionResult(
            outcome.Succeeded,
            outcome.Succeeded ? 0 : null,
            outcome.Succeeded ? outcome.Message : string.Empty,
            outcome.Succeeded ? string.Empty : outcome.Message,
            preview,
            startedAt,
            DateTimeOffset.UtcNow);
    }
}
