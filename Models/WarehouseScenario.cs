namespace RobotFleetSimulation.Models;

public sealed class WarehouseScenario
{
    public int SchemaVersion { get; set; } = 2;
    public int Rows { get; set; } = 20;
    public int Columns { get; set; } = 30;
    public CellType[][] Cells { get; set; } = [];
    public List<Robot> Robots { get; set; } = [];
    public List<RobotTask> Tasks { get; set; } = [];
}
