
namespace RobotFleetSimulation.Models;

public enum CellType
{
    Empty,
    Wall,
    RobotStart,
    Destination
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

    public bool IsWalkable => Type != CellType.Wall;
}
