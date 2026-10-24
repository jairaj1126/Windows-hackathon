
namespace RobotFleetSimulation.Models;

public enum RobotTaskStatus
{
    Pending,
    InProgress,
    Completed
}

public sealed class RobotTask
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DestinationRow { get; set; }

    public int DestinationColumn { get; set; }

    public RobotTaskStatus Status { get; set; } =
        RobotTaskStatus.Pending;

    public string StatusText => Status.ToString();

    public string DestinationText =>
        $"Row {DestinationRow}, Col {DestinationColumn}";
}
