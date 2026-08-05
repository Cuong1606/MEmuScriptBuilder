using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Scripts;

public interface IScriptStore
{
    Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken);
}

public static class ScriptTemplateFactory
{
    public static ScriptDefinition CreateRestartChrome() => new()
    {
        Name = "Khởi động lại Chrome",
        Steps =
        [
            new ForceStopStep { Name = "Dừng Chrome", PackageName = "com.android.chrome" },
            new DelayStep { Name = "Chờ 2 giây", DurationMilliseconds = 2000 },
            new OpenAppStep
            {
                Name = "Mở Chrome",
                PackageName = "com.android.chrome",
                ActivityName = "com.google.android.apps.chrome.Main"
            }
        ]
    };
}

public static class ScriptCloner
{
    public static ScriptDefinition Clone(ScriptDefinition source, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ScriptDefinition
        {
            Name = name ?? $"{source.Name} — Bản sao",
            Kind = source.Kind,
            DefaultInstanceIndex = source.DefaultInstanceIndex,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variables = source.Variables.Select(variable => new ScriptVariable
            {
                Name = variable.Name,
                Value = variable.Value,
                IsSecret = variable.IsSecret
            }).ToList(),
            Steps = source.Steps.Select(CloneStep).ToList(),
            CompositeItems = source.CompositeItems.Select(CloneCompositeItem).ToList()
        };
    }

    public static ScriptDefinition ClonePreservingIds(ScriptDefinition source) => CloneWithId(source, source.Id);

    public static ScriptDefinition CloneWithId(ScriptDefinition source, Guid id)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ScriptDefinition
        {
            SchemaVersion = source.SchemaVersion,
            Id = id,
            Name = source.Name,
            Kind = source.Kind,
            DefaultInstanceIndex = source.DefaultInstanceIndex,
            UpdatedAt = source.UpdatedAt,
            Variables = source.Variables.Select(variable => new ScriptVariable
            {
                Name = variable.Name,
                Value = variable.Value,
                IsSecret = variable.IsSecret
            }).ToList(),
            Steps = source.Steps.Select(CloneStepPreservingId).ToList(),
            CompositeItems = source.CompositeItems.Select(CloneCompositeItemPreservingId).ToList()
        };
    }

    public static ScriptStep CloneStep(ScriptStep step) => CloneStepCore(step, null);

    public static ScriptStep CloneStepPreservingId(ScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return CloneStepCore(step, step.Id);
    }

    public static CompositeScriptItem CloneCompositeItem(CompositeScriptItem item) =>
        CloneCompositeItemCore(item, Guid.NewGuid());

    public static CompositeScriptItem CloneCompositeItemPreservingId(CompositeScriptItem item) =>
        CloneCompositeItemCore(item, item.Id);

    private static CompositeScriptItem CloneCompositeItemCore(CompositeScriptItem item, Guid id)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item switch
        {
            ScriptReferenceItem value => new ScriptReferenceItem
            {
                Id = id,
                IsEnabled = value.IsEnabled,
                ScriptId = value.ScriptId,
                ContinueOnFailure = value.ContinueOnFailure
            },
            CompositeDelayItem value => new CompositeDelayItem
            {
                Id = id,
                IsEnabled = value.IsEnabled,
                DurationMilliseconds = value.DurationMilliseconds
            },
            _ => throw new NotSupportedException($"Không thể nhân bản {item.GetType().Name}.")
        };
    }

    private static ScriptStep CloneStepCore(ScriptStep step, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step switch
        {
            AndroidShellStep value => CopyCommon(value, new AndroidShellStep { Id = id ?? Guid.NewGuid(), Name = value.Name, Command = value.Command }),
            ForceStopStep value => CopyCommon(value, new ForceStopStep { Id = id ?? Guid.NewGuid(), Name = value.Name, PackageName = value.PackageName }),
            OpenAppStep value => CopyCommon(value, new OpenAppStep { Id = id ?? Guid.NewGuid(), Name = value.Name, PackageName = value.PackageName, ActivityName = value.ActivityName }),
            DelayStep value => CopyCommon(value, new DelayStep { Id = id ?? Guid.NewGuid(), Name = value.Name, DurationMilliseconds = value.DurationMilliseconds }),
            TapStep value => CopyCommon(value, new TapStep { Id = id ?? Guid.NewGuid(), Name = value.Name, X = value.X, Y = value.Y }),
            HoldStep value => CopyCommon(value, new HoldStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = value.Name,
                X = value.X,
                Y = value.Y,
                DurationMilliseconds = value.DurationMilliseconds
            }),
            SwipeStep value => CopyCommon(value, new SwipeStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = value.Name,
                X1 = value.X1,
                Y1 = value.Y1,
                X2 = value.X2,
                Y2 = value.Y2,
                DurationMilliseconds = value.DurationMilliseconds
            }),
            InputTextStep value => CopyCommon(value, new InputTextStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = value.Name,
                Text = value.Text,
                PressEnterAfterInput = value.PressEnterAfterInput
            }),
            AndroidClipboardPasteStep value => CopyCommon(value, new AndroidClipboardPasteStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = value.Name,
                PressEnterAfterPaste = value.PressEnterAfterPaste
            }),
            KeyEventStep value => CopyCommon(value, new KeyEventStep { Id = id ?? Guid.NewGuid(), Name = value.Name, Key = value.Key }),
            NoteStep value => CopyCommon(value, new NoteStep { Id = id ?? Guid.NewGuid(), Name = value.Name, Text = value.Text }),
            CloseChromeTabsStep value => CopyCommon(value, new CloseChromeTabsStep { Id = id ?? Guid.NewGuid(), Name = value.Name }),
            _ => throw new NotSupportedException($"Không thể nhân bản {step.GetType().Name}.")
        };
    }

    private static T CopyCommon<T>(ScriptStep source, T target) where T : ScriptStep
    {
        target.IsEnabled = source.IsEnabled;
        target.ContinueOnError = source.ContinueOnError;
        target.TimeoutSeconds = source.TimeoutSeconds;
        return target;
    }
}
