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
            if (!TryParseCsvLine(line, out var fields) ||
                fields.Count != 5 ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var windowHandle) ||
                windowHandle < 0 ||
                !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var status) ||
                status is not (0 or 1) ||
                !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPid) ||
                parsedPid < 0)
            {
                continue;
            }

            instances.Add(new MemuInstance(
                index,
                fields[1],
                status == 1,
                parsedPid == 0 ? null : parsedPid,
                windowHandle == 0 ? null : windowHandle));
        }

        return instances;
    }

    private static bool TryParseCsvLine(string line, out IReadOnlyList<string> fields)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var fieldWasQuoted = false;
        var quoteWasClosed = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                    quoteWasClosed = true;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == ',')
            {
                values.Add(fieldWasQuoted ? current.ToString() : current.ToString().Trim());
                current.Clear();
                fieldWasQuoted = false;
                quoteWasClosed = false;
            }
            else if (character == '"')
            {
                if (quoteWasClosed || current.ToString().Trim().Length != 0)
                {
                    fields = [];
                    return false;
                }

                current.Clear();
                quoted = true;
                fieldWasQuoted = true;
            }
            else if (quoteWasClosed)
            {
                if (!char.IsWhiteSpace(character))
                {
                    fields = [];
                    return false;
                }
            }
            else
            {
                current.Append(character);
            }
        }

        values.Add(fieldWasQuoted ? current.ToString() : current.ToString().Trim());
        fields = values;
        return !quoted;
    }
}
