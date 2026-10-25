using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;
using Xunit;

namespace RobotFleetSimulation.Tests;

public sealed class TaskQueueServiceTests
{
    [Fact]
    public void ReturnsHigherPriorityTaskFirst()
    {
        var queue = new TaskQueueService();
        queue.AddTask("Routine", 1, 1, 1);
        var urgent = queue.AddTask("Urgent", 2, 2, 5);

        Assert.Same(urgent, queue.GetNextPendingTask());
    }

    [Fact]
    public void SupportsCancellationAndFailureStates()
    {
        var queue = new TaskQueueService();
        var cancelled = queue.AddTask("Cancel me", 1, 1);
        var failed = queue.AddTask("Missed", 2, 2);

        Assert.True(queue.Cancel(cancelled.Id));
        Assert.True(queue.MarkFailed(failed.Id));
        Assert.Equal(RobotTaskStatus.Cancelled, cancelled.Status);
        Assert.Equal(RobotTaskStatus.Failed, failed.Status);
    }
}
