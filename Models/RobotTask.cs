
namespace RobotFleetSimulation.Models;

public enum RobotTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Cancelled,
    Failed
}

public enum RobotTaskType { Move, Pickup, Delivery, Charge, MultiStop }

public sealed class TaskStop
{
    public int Row { get; set; }
    public int Column { get; set; }
    public string Action { get; set; } = "Visit";
}

public sealed class RobotTask
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DestinationRow { get; set; }

    public int DestinationColumn { get; set; }

    public int Priority { get; set; } = 1;

    public int? AssignedRobotId { get; set; }
    public DateTime? DeadlineUtc { get; set; }

    public RobotTaskType Type { get; set; } = RobotTaskType.Delivery;

    public double PayloadWeightKg { get; set; }

    public List<TaskStop> Stops { get; set; } = [];

    public RobotTaskStatus Status { get; set; } =
        RobotTaskStatus.Pending;

    public string StatusText => Status.ToString();

    public string DestinationText =>
        $"Row {DestinationRow}, Col {DestinationColumn}";
}
