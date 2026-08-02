namespace MEmuScriptStudio.Core.MEmu;

public sealed record MemuCommand(string ExecutablePath, IReadOnlyList<string> Arguments)
{
    public string Preview => string.Join(' ', new[] { Quote(ExecutablePath) }.Concat(Arguments.Select(Quote)));

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character != '"'))
        {
            return value;
        }

        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

public sealed class MemuCommandBuilder
{
    public MemuCommand BuildListVms(string memucPath)
    {
        ValidatePath(memucPath);
        return new MemuCommand(memucPath, ["listvms"]);
    }

    public MemuCommand BuildAndroidShell(string memucPath, int instanceIndex, string command)
    {
        ValidatePath(memucPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));

        return new MemuCommand(memucPath, ["-i", instanceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), "execcmd", command]);
    }

    private static void ValidatePath(string memucPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memucPath);
        if (!string.Equals(Path.GetFileName(memucPath), "memuc.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Đường dẫn phải trỏ tới memuc.exe.", nameof(memucPath));
        }
    }
}
