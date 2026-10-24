
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

    public string PositionText => $"Row: {Row}, Col: {Column}";

    public string BatteryText => $"{BatteryLevel:0}%";

    public string StatusText => Status.ToString();
}
