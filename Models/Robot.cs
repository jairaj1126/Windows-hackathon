
namespace RobotFleetSimulation.Models;

public enum RobotStatus
{
    Idle,
    Ready,
    Moving,
    Paused,
    Charging,
    Completed,
    NoRoute
}

public sealed class Robot
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    public double BatteryLevel { get; set; } = 100;

    public RobotStatus Status { get; set; } = RobotStatus.Idle;

    public int TotalSteps { get; set; }

    public int? AssignedTaskId { get; set; }

    public double SpeedCellsPerTick { get; set; } = 1;

    public double PayloadCapacityKg { get; set; } = 25;

    public double BatteryCapacity { get; set; } = 100;

    public double PayloadKg { get; set; }

    public string PositionText => $"Row: {Row}, Col: {Column}";

    public string BatteryText => $"{(BatteryCapacity <= 0 ? 0 : BatteryLevel / BatteryCapacity * 100):0}%";

    public string StatusText => Status.ToString();
}
