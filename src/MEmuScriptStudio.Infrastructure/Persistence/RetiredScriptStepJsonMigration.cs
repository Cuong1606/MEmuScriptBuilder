using System.Text.Json;
using System.Text.Json.Nodes;

namespace MEmuScriptStudio.Infrastructure.Persistence;

internal static class RetiredScriptStepJsonMigration
{
    private static readonly HashSet<string> RetiredDiscriminators =
        ["clearRecentApps", "clearAppCache"];

    public static async Task<T?> DeserializeAsync<T>(
        Stream stream,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var root = await JsonNode.ParseAsync(
            stream,
            documentOptions: default,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (root is null) return default;
        Migrate(root);
        return root.Deserialize<T>(options);
    }

    private static void Migrate(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("$type", out var discriminatorNode) &&
                discriminatorNode is JsonValue discriminatorValue &&
                discriminatorValue.TryGetValue<string>(out var discriminator) &&
                RetiredDiscriminators.Contains(discriminator))
            {
                jsonObject["$type"] = "note";
                SetCaseInsensitive(jsonObject, "IsEnabled", JsonValue.Create(false));
                SetCaseInsensitive(
                    jsonObject,
                    "Text",
                    JsonValue.Create("Bước cũ đã bị loại bỏ và được chuyển thành ghi chú tắt khi nạp dữ liệu."));
            }

            foreach (var child in jsonObject.Select(pair => pair.Value).Where(value => value is not null).ToList())
                Migrate(child!);
            return;
        }

        if (node is JsonArray array)
        foreach (var child in array.Where(value => value is not null).ToList())
            Migrate(child!);
    }

    private static void SetCaseInsensitive(JsonObject jsonObject, string name, JsonNode? value)
    {
        var existingName = jsonObject.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        jsonObject[existingName ?? name] = value;
    }
}
