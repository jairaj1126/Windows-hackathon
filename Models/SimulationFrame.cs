namespace RobotFleetSimulation.Models;

public sealed class RobotFrame
{
    public int Id { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public double BatteryLevel { get; set; }
    public RobotStatus Status { get; set; }
}

public sealed class SimulationFrame
{
    public int Tick { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public List<RobotFrame> Robots { get; set; } = [];
    public string Event { get; set; } = "";
}
