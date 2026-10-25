
namespace RobotFleetSimulation.Models;

public enum CellType
{
    Empty,
    Wall,
    RobotStart,
    Destination,
    Shelf,
    ChargingStation,
    SlowZone,
    LoadingDock,
    PackingStation,
    Elevator,
    RestrictedZone,
    OneWayNorth,
    OneWayEast,
    OneWaySouth,
    OneWayWest,
    Crossing
}

public sealed class GridCell
{
    public int Row { get; }
    public int Column { get; }
    public CellType Type { get; set; }

    public GridCell(int row, int column)
    {
        Row = row;
        Column = column;
        Type = CellType.Empty;
    }

    public bool IsWalkable => Type is not CellType.Wall and not CellType.Shelf and not CellType.RestrictedZone;
}
