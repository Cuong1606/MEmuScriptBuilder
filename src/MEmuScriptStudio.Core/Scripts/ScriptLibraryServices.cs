using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Scripts;

public static class ScriptLibraryValidator
{
    public static void Validate(IReadOnlyCollection<ScriptDefinition> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        var byId = scripts.GroupBy(script => script.Id).ToDictionary(group => group.Key, group => group.ToList());
        if (byId.Any(pair => pair.Key == Guid.Empty || pair.Value.Count != 1))
            throw new InvalidDataException("Thư viện chứa ID kịch bản rỗng hoặc trùng nhau.");

        foreach (var script in scripts)
        {
            if (script.Kind == ScriptKind.Regular)
            {
                if (script.CompositeItems.Count != 0)
                    throw new InvalidDataException($"Kịch bản thường '{script.Name}' không được chứa mục gộp.");
                ValidateIds(script.Steps.Select(step => step.Id), $"bước trong '{script.Name}'");
                if (script.Steps.OfType<DelayStep>().Any(delay => delay.DurationMilliseconds < 0))
                    throw new InvalidDataException($"Thời gian chờ trong '{script.Name}' không được âm.");
                continue;
            }

            if (script.Steps.Count != 0)
                throw new InvalidDataException($"Kịch bản gộp '{script.Name}' không được chứa bước thường.");
            ValidateIds(script.CompositeItems.Select(item => item.Id), $"mục gộp trong '{script.Name}'");
            foreach (var item in script.CompositeItems)
            {
                switch (item)
                {
                    case CompositeDelayItem { DurationMilliseconds: < 0 }:
                        throw new InvalidDataException($"Thời gian chờ trong '{script.Name}' không được âm.");
                    case ScriptReferenceItem reference when !byId.TryGetValue(reference.ScriptId, out var matches) || matches.Count != 1:
                        throw new InvalidDataException($"Kịch bản gộp '{script.Name}' tham chiếu ScriptId không tồn tại.");
                    case ScriptReferenceItem reference when byId[reference.ScriptId][0].Kind != ScriptKind.Regular:
                        throw new InvalidDataException($"Kịch bản gộp '{script.Name}' chỉ được tham chiếu kịch bản thường.");
                }
            }
        }
    }

    public static IReadOnlyList<ScriptDefinition> BuildExportClosure(
        IReadOnlyCollection<ScriptDefinition> selected,
        IReadOnlyCollection<ScriptDefinition> library)
    {
        ArgumentNullException.ThrowIfNull(selected);
        Validate(library);
        var byId = library.ToDictionary(script => script.Id);
        var result = new Dictionary<Guid, ScriptDefinition>();
        foreach (var script in selected)
        {
            result[script.Id] = script;
            if (script.Kind != ScriptKind.Composite) continue;
            foreach (var reference in script.CompositeItems.OfType<ScriptReferenceItem>())
                result[reference.ScriptId] = byId[reference.ScriptId];
        }
        return result.Values.ToList();
    }

    private static void ValidateIds(IEnumerable<Guid> ids, string description)
    {
        var values = ids.ToList();
        if (values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Count)
            throw new InvalidDataException($"ID {description} bị rỗng hoặc trùng nhau.");
    }
}

public static class ScriptBundleCloner
{
    public static IReadOnlyList<ScriptDefinition> CloneWithRemappedIds(IReadOnlyCollection<ScriptDefinition> scripts)
    {
        ScriptLibraryValidator.Validate(scripts);
        var pairs = scripts.Select(script => (Source: script, Clone: ScriptCloner.Clone(script))).ToList();
        var scriptIds = pairs.ToDictionary(pair => pair.Source.Id, pair => pair.Clone.Id);
        return pairs.Select(pair =>
        {
            var clone = pair.Clone;
            foreach (var reference in clone.CompositeItems.OfType<ScriptReferenceItem>())
                reference.ScriptId = scriptIds[reference.ScriptId];
            return clone;
        }).ToList();
    }
}
