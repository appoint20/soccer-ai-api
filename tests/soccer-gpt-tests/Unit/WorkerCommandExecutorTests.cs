using Microsoft.Extensions.Logging;
using Moq;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_worker.Worker;

namespace soccer_gpt_tests.Unit;

public class WorkerCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Standings_InvokesStandingsJob()
    {
        var runner = new Mock<ISyncJobRunner>();
        runner.Setup(r => r.RunStandingsAsync(2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult());

        var logger = new Mock<ILogger<WorkerCommandExecutor>>();
        var sut = new WorkerCommandExecutor(runner.Object, logger.Object);

        var code = await sut.ExecuteAsync(new WorkerCommand(WorkerJob.Standings, 2025), CancellationToken.None);

        Assert.Equal(0, code);
        runner.Verify(r => r.RunStandingsAsync(2025, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Ml_WhenTrainingFails_ReturnsOne()
    {
        var runner = new Mock<ISyncJobRunner>();
        runner.Setup(r => r.RunMlTrainingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var logger = new Mock<ILogger<WorkerCommandExecutor>>();
        var sut = new WorkerCommandExecutor(runner.Object, logger.Object);

        var code = await sut.ExecuteAsync(new WorkerCommand(WorkerJob.Ml, null), CancellationToken.None);

        Assert.Equal(1, code);
        runner.Verify(r => r.RunMlTrainingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
