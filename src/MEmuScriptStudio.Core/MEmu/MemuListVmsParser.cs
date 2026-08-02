using System.Globalization;
using System.Text;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public sealed class MemuListVmsParser
{
    public IReadOnlyList<MemuInstance> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        var instances = new List<MemuInstance>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = ParseCsvLine(line);
            if (fields.Count < 3 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            var isRunning = IsRunning(fields[2]);
            int? processId = null;
            if (fields.Count > 3 && int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPid) && parsedPid > 0)
            {
                processId = parsedPid;
            }

            instances.Add(new MemuInstance(index, fields[1], isRunning, processId));
        }

        return instances;
    }

    private static bool IsRunning(string value) =>
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("running", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("started", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        values.Add(current.ToString().Trim());
        return values;
    }
}
