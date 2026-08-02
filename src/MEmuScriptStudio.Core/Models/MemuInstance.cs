namespace MEmuScriptStudio.Core.Models;

public sealed record MemuInstance(int Index, string Name, bool IsRunning, int? ProcessId);
