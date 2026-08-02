using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.MEmu;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MemuInstanceServiceTests
{
    [TestMethod]
    public async Task GetInstancesAsync_ParsesSuccessfulOutputWithoutRunningRealMemu()
    {
        var runner = new StubProcessRunner(new ProcessResult(0, "3,Test VM,445566,1,700", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new MemuInstanceService(runner, new MemuCommandBuilder(), new MemuListVmsParser());

        var instances = await service.GetInstancesAsync(@"C:\MEmu\memuc.exe", CancellationToken.None);

        Assert.AreEqual(1, instances.Count);
        Assert.AreEqual(3, instances[0].Index);
        Assert.AreEqual("listvms", runner.LastRequest?.Arguments.Single());
    }

    [TestMethod]
    public async Task GetInstancesAsync_RejectsNonZeroExitCode()
    {
        var runner = new StubProcessRunner(new ProcessResult(1, string.Empty, "failure", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new MemuInstanceService(runner, new MemuCommandBuilder(), new MemuListVmsParser());

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.GetInstancesAsync(@"C:\MEmu\memuc.exe", CancellationToken.None));

        StringAssert.Contains(exception.Message, "exit code 1");
    }

    private sealed class StubProcessRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
