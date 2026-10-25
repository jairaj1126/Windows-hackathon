
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
        int destinationColumn,
        int priority = 1,
        DateTime? deadlineUtc = null,
        RobotTaskType type = RobotTaskType.Delivery,
        double payloadWeightKg = 0,
        IEnumerable<TaskStop>? stops = null)
    {
        var task = new RobotTask
        {
            Id = _nextId++,
            Name = name,
            DestinationRow = destinationRow,
            DestinationColumn = destinationColumn,
            Priority = Math.Clamp(priority, 1, 5),
            DeadlineUtc = deadlineUtc,
            Type = type,
            PayloadWeightKg = Math.Max(0, payloadWeightKg),
            Stops = stops?.ToList() ?? [],
            Status = RobotTaskStatus.Pending
        };

        _tasks.Add(task);
        return task;
    }

    public void ReplaceAll(IEnumerable<RobotTask> tasks)
    {
        _tasks.Clear();
        _tasks.AddRange(tasks);
        _nextId = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
    }

    public RobotTask? GetNextPendingTask()
    {
        return _tasks.Where(task => task.Status == RobotTaskStatus.Pending)
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.Id)
            .FirstOrDefault();
    }

    public IEnumerable<RobotTask> GetPendingTasks() =>
        _tasks.Where(task => task.Status == RobotTaskStatus.Pending)
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.Id)
            .ToList();

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

    public bool Cancel(int taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Status is RobotTaskStatus.Completed or RobotTaskStatus.Cancelled or RobotTaskStatus.Failed)
            return false;
        task.Status = RobotTaskStatus.Cancelled;
        return true;
    }

    public bool MarkFailed(int taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Status is RobotTaskStatus.Completed or RobotTaskStatus.Cancelled or RobotTaskStatus.Failed)
            return false;
        task.Status = RobotTaskStatus.Failed;
        return true;
    }

    public bool Requeue(int taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.Status != RobotTaskStatus.InProgress) return false;
        task.Status = RobotTaskStatus.Pending;
        task.AssignedRobotId = null;
        return true;
    }
}
