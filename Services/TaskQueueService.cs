
using System.Collections.Generic;
using System.Linq;
using RobotFleetSimulation.Models;

namespace RobotFleetSimulation.Services;

public sealed class TaskQueueService
{
    private readonly List<RobotTask> _tasks = [];
    private int _nextId = 1;

    public IReadOnlyList<RobotTask> Tasks => _tasks;

    public int PendingCount =>
        _tasks.Count(t => t.Status == RobotTaskStatus.Pending);

    public int InProgressCount =>
        _tasks.Count(t => t.Status == RobotTaskStatus.InProgress);

    public int CompletedCount =>
        _tasks.Count(t => t.Status == RobotTaskStatus.Completed);

    public RobotTask AddTask(
        string name,
        int destinationRow,
        int destinationColumn)
    {
        var task = new RobotTask
        {
            Id = _nextId++,
            Name = name,
            DestinationRow = destinationRow,
            DestinationColumn = destinationColumn,
            Status = RobotTaskStatus.Pending
        };

        _tasks.Add(task);
        return task;
    }

    public RobotTask? GetNextPendingTask()
    {
        return _tasks.FirstOrDefault(
            task => task.Status == RobotTaskStatus.Pending);
    }

    public bool MarkInProgress(int taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);

        if (task is null ||
            task.Status != RobotTaskStatus.Pending)
        {
            return false;
        }

        task.Status = RobotTaskStatus.InProgress;
        return true;
    }

    public bool MarkCompleted(int taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);

        if (task is null ||
            task.Status != RobotTaskStatus.InProgress)
        {
            return false;
        }

        task.Status = RobotTaskStatus.Completed;
        return true;
    }
}
