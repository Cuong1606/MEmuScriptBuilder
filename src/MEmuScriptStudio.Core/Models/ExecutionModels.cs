using System.Text.Json.Serialization;
using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Core.Models;

public enum StepExecutionStatus
{
    NotRun,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public sealed class ExecutionRequest
{
    public required ScriptDefinition Script { get; init; }
    public IReadOnlyDictionary<Guid, ScriptDefinition> ScriptLibrary { get; init; } =
        new Dictionary<Guid, ScriptDefinition>();
    public required string MemucPath { get; init; }
    public required int InstanceIndex { get; init; }
    public required MemuInstance Target { get; init; }
    public MemuInstanceCoreIdentity? ExpectedCoreIdentity { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed class ExecutionResult
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public bool WasCancelled { get; init; }
    public IReadOnlyList<StepExecutionResult> Steps { get; init; } = [];
}

public sealed class StepExecutionResult
{
    public required Guid StepId { get; init; }
    public StepExecutionStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public int? ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string CommandPreview { get; init; } = string.Empty;
    public CompositeExecutionContext? CompositeContext { get; init; }
}

public sealed record CompositeExecutionContext(
    Guid CompositeScriptId,
    string CompositeScriptName,
    Guid CompositeItemId,
    Guid OccurrenceId,
    Guid? ChildScriptId,
    string? ChildScriptName,
    Guid? ChildStepId,
    string? ChildStepName)
{
    public string DisplayName => ChildScriptName is null
        ? $"{CompositeScriptName} → {ChildStepName ?? "Chờ"}"
        : $"{CompositeScriptName} → {ChildScriptName}";
    public string FullDisplayName => ChildScriptName is null || ChildStepName is null
        ? DisplayName
        : $"{DisplayName} → {ChildStepName}";
}

public sealed record StepExecutionUpdate(
    Guid StepId,
    StepExecutionStatus Status,
    StepExecutionResult? Result = null,
    CompositeExecutionContext? CompositeContext = null);

public sealed class ApplicationSettings
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? MemucPath { get; set; }
    public Dictionary<string, string> ApplicationDisplayNames { get; init; } = [];
    public MultiInstanceRunSettings MultiInstanceRun { get; init; } = new();
    public ControlCenterLayoutSettings ControlCenterLayout { get; set; } = new();
}

public sealed class ControlCenterLayoutSettings
{
    public const double DefaultWindowWidth = 1180;
    public const double DefaultWindowHeight = 680;
    public const double DefaultSetupPanelWidth = 640;
    public const double DefaultSetupPanelRatio = 4d / 7d;
    public const double DefaultRecentListRatio = 0.38;
    public const double MinimumWindowWidth = 760;
    public const double MinimumWindowHeight = 420;
    public const double MinimumSetupPanelWidth = 360;
    public const double MinimumRuntimePanelWidth = 300;
    public const double MinimumRecentListHeight = 140;
    public const double MinimumRecentDetailHeight = 160;
    public const double SplitterWidth = 8;

    public double WindowWidth { get; set; } = DefaultWindowWidth;
    public double WindowHeight { get; set; } = DefaultWindowHeight;
    public bool IsMaximized { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SetupPanelRatio { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RecentListRatio { get; set; }

    // Read-only migration input for schema 6 and earlier. New saves use SetupPanelRatio.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SetupPanelWidth { get; set; }

    public static ControlCenterLayoutSettings Normalize(
        ControlCenterLayoutSettings? value,
        double availableWidth = 7680,
        double availableHeight = 4320)
    {
        value ??= new ControlCenterLayoutSettings();
        var maximumWidth = IsFinitePositive(availableWidth)
            ? Math.Max(MinimumWindowWidth, availableWidth)
            : 7680;
        var maximumHeight = IsFinitePositive(availableHeight)
            ? Math.Max(MinimumWindowHeight, availableHeight)
            : 4320;
        var width = NormalizeDimension(value.WindowWidth, MinimumWindowWidth, maximumWidth, DefaultWindowWidth);
        var height = NormalizeDimension(value.WindowHeight, MinimumWindowHeight, maximumHeight, DefaultWindowHeight);
        double? setupRatio = value.SetupPanelRatio.HasValue
            ? NormalizeSetupPanelRatio(value.SetupPanelRatio.Value)
            : null;
        double? recentListRatio = value.RecentListRatio.HasValue
            ? NormalizeRecentListRatio(value.RecentListRatio.Value)
            : null;
        var legacySetupWidth = setupRatio.HasValue
            ? null
            : NormalizeLegacySetupPanelWidth(value.SetupPanelWidth, width);

        return new ControlCenterLayoutSettings
        {
            WindowWidth = width,
            WindowHeight = height,
            IsMaximized = value.IsMaximized,
            SetupPanelRatio = setupRatio,
            RecentListRatio = recentListRatio,
            SetupPanelWidth = legacySetupWidth
        };
    }

    public static double ResolveSetupPanelRatio(ControlCenterLayoutSettings settings, double panelWidth)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ratio = settings.SetupPanelRatio.HasValue
            ? NormalizeSetupPanelRatio(settings.SetupPanelRatio.Value)
            : settings.SetupPanelWidth.HasValue && double.IsFinite(settings.SetupPanelWidth.Value) && panelWidth > 0
                ? settings.SetupPanelWidth.Value / panelWidth
                : DefaultSetupPanelRatio;
        return ClampSplitRatio(
            ratio,
            panelWidth,
            MinimumSetupPanelWidth,
            MinimumRuntimePanelWidth,
            DefaultSetupPanelRatio);
    }

    public static double ResolveRecentListRatio(ControlCenterLayoutSettings settings, double panelHeight) =>
        ClampSplitRatio(
            settings.RecentListRatio.HasValue
                ? NormalizeRecentListRatio(settings.RecentListRatio.Value)
                : DefaultRecentListRatio,
            panelHeight,
            MinimumRecentListHeight,
            MinimumRecentDetailHeight,
            DefaultRecentListRatio);

    public static double CaptureSplitRatio(double firstActual, double secondActual, double fallback)
    {
        var total = firstActual + secondActual;
        return IsFinitePositive(firstActual) && IsFinitePositive(secondActual) && IsFinitePositive(total)
            ? Math.Clamp(firstActual / total, 0d, 1d)
            : Math.Clamp(fallback, 0d, 1d);
    }

    public static double NormalizeSetupPanelRatio(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : DefaultSetupPanelRatio;

    public static double NormalizeRecentListRatio(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : DefaultRecentListRatio;

    public static double ClampSplitRatio(
        double value,
        double available,
        double minimumFirst,
        double minimumSecond,
        double fallback)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : Math.Clamp(fallback, 0d, 1d);
        if (!IsFinitePositive(available) || available < minimumFirst + minimumSecond) return normalized;
        return Math.Clamp(normalized, minimumFirst / available, 1d - (minimumSecond / available));
    }

    private static double? NormalizeLegacySetupPanelWidth(double? value, double windowWidth)
    {
        if (!value.HasValue) return null;
        var maximum = Math.Max(MinimumSetupPanelWidth, windowWidth - SplitterWidth - MinimumRuntimePanelWidth);
        return NormalizeDimension(value.Value, MinimumSetupPanelWidth, maximum, DefaultSetupPanelWidth);
    }

    private static double NormalizeDimension(double value, double minimum, double maximum, double fallback)
    {
        var safeFallback = Math.Clamp(fallback, minimum, maximum);
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : safeFallback;
    }

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}

public enum ScriptAssignmentMode
{
    OneScriptForAll,
    PerInstance
}

public enum LaunchSpacingMode
{
    Fixed,
    Random
}

public sealed class MultiInstanceRunSettings
{
    public LaunchSpacingMode LaunchSpacingMode { get; set; } = LaunchSpacingMode.Fixed;
    public int FixedSpacingMilliseconds { get; set; }
    public int RandomMinimumSpacingMilliseconds { get; set; }
    public int RandomMaximumSpacingMilliseconds { get; set; }
    public bool StopAllOnInvalidTarget { get; set; }
    public ScriptAssignmentMode ScriptAssignmentMode { get; set; } = ScriptAssignmentMode.OneScriptForAll;
    public Guid? CommonScriptId { get; set; }
    public Dictionary<int, Guid> ScriptAssignments { get; init; } = [];
}
