using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class MemuInstanceService(
    IProcessRunner processRunner,
    MemuCommandBuilder commandBuilder,
    MemuListVmsParser parser) : IMemuInstanceService
{
    public async Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken)
    {
        var command = commandBuilder.BuildListVms(memucPath);
        var result = await processRunner.RunAsync(
            new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(30)),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"memuc listvms thất bại (exit code {result.ExitCode}): {result.StandardError.Trim()}");
        }

        return parser.Parse(result.StandardOutput);
    }
}
