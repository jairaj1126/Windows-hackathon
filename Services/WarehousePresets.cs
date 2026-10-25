using RobotFleetSimulation.Models;

namespace RobotFleetSimulation.Services;

public static class WarehousePresets
{
    public static readonly string[] Names = ["Starter Warehouse", "Cross-Dock Hub", "Compact Lab"];

    public static WarehouseScenario Create(string name)
    {
        var scenario = new WarehouseScenario { SchemaVersion = 2, Rows = 60, Columns = 80 };
        scenario.Cells = Enumerable.Range(0, scenario.Rows)
            .Select(_ => Enumerable.Repeat(CellType.Empty, scenario.Columns).ToArray()).ToArray();
        for (int r = 2; r < scenario.Rows - 2; r++)
        {
            for (int c = 4; c < scenario.Columns - 3; c++)
                if (c % 5 is 0 or 1) scenario.Cells[r][c] = CellType.Shelf;
            if (r % 5 == 0) scenario.Cells[r][scenario.Columns / 2] = CellType.Crossing;
        }
        scenario.Cells[scenario.Rows - 2][1] = CellType.ChargingStation;
        scenario.Cells[1][scenario.Columns - 2] = CellType.LoadingDock;
        scenario.Cells[scenario.Rows / 2][scenario.Columns - 2] = CellType.PackingStation;
        scenario.Cells[scenario.Rows / 2][scenario.Columns / 2] = CellType.Elevator;
        int aisleA = Math.Min(2, scenario.Columns - 2);
        int aisleB = Math.Min(3, scenario.Columns - 2);
        for (int r = 3; r < scenario.Rows - 2; r++)
        {
            scenario.Cells[r][aisleA] = CellType.OneWayNorth;
            scenario.Cells[r][aisleB] = CellType.OneWaySouth;
        }
        if (scenario.Rows > 12 && scenario.Columns > 12)
            scenario.Cells[2][scenario.Columns / 2] = CellType.RestrictedZone;
        scenario.Robots =
        [
            new Robot { Id = 1, Name = "Robot-01", Row = 0, Column = 0, SpeedCellsPerTick = 1, PayloadCapacityKg = 30 },
            new Robot { Id = 2, Name = "Robot-02", Row = scenario.Rows - 1, Column = scenario.Columns - 2, SpeedCellsPerTick = 1.25, PayloadCapacityKg = 60 },
            new Robot { Id = 3, Name = "Robot-03", Row = scenario.Rows - 1, Column = scenario.Columns - 1, SpeedCellsPerTick = .75, PayloadCapacityKg = 15 }
        ];
        return scenario;
    }
}
