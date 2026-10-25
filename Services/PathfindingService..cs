
using RobotFleetSimulation.Models;

namespace RobotFleetSimulation.Services;

public sealed class PathfindingService
{
    private static readonly (int Row, int Col)[] Directions =
    {
        (-1, 0), // Up
        (1, 0),  // Down
        (0, -1), // Left
        (0, 1)   // Right
    };

    public List<(int Row, int Col)> FindPath(
        CellType[,] map,
        (int Row, int Col) start,
        (int Row, int Col) destination)
    {
        int rows = map.GetLength(0);
        int columns = map.GetLength(1);

        if (!IsInside(start.Row, start.Col, rows, columns) ||
            !IsInside(destination.Row, destination.Col, rows, columns))
        {
            return new List<(int Row, int Col)>();
        }

        if (!IsWalkable(map[start.Row, start.Col]) ||
            !IsWalkable(map[destination.Row, destination.Col]))
        {
            return new List<(int Row, int Col)>();
        }

        var openSet = new PriorityQueue<
            (int Row, int Col), int>();

        var cameFrom = new Dictionary<
            (int Row, int Col), (int Row, int Col)>();

        var gScore = new Dictionary<
            (int Row, int Col), int>
        {
            [start] = 0
        };

        var closedSet = new HashSet<(int Row, int Col)>();

        openSet.Enqueue(start, Heuristic(start, destination));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == destination)
                return ReconstructPath(cameFrom, current);

            if (!closedSet.Add(current))
                continue;

            foreach (var direction in Directions)
            {
                var neighbor = (
                    Row: current.Row + direction.Row,
                    Col: current.Col + direction.Col
                );

                if (!IsInside(neighbor.Row, neighbor.Col, rows, columns))
                    continue;

                if (!CanExit(map[current.Row, current.Col], direction.Row, direction.Col))
                    continue;

                if (!CanEnter(map[neighbor.Row, neighbor.Col], direction.Row, direction.Col))
                    continue;

                if (!IsWalkable(map[neighbor.Row, neighbor.Col]))
                    continue;

                if (closedSet.Contains(neighbor))
                    continue;

                int tentativeGScore = gScore[current] + 1;

                if (gScore.TryGetValue(neighbor, out int oldScore) &&
                    tentativeGScore >= oldScore)
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeGScore;

                int fScore = tentativeGScore +
                    Heuristic(neighbor, destination);

                openSet.Enqueue(neighbor, fScore);
            }
        }

        // An empty list means no route was found.
        return new List<(int Row, int Col)>();
    }

    private static int Heuristic(
        (int Row, int Col) a,
        (int Row, int Col) b)
    {
        // Manhattan distance: suitable for four-direction movement.
        return Math.Abs(a.Row - b.Row) +
               Math.Abs(a.Col - b.Col);
    }

    private static bool IsInside(
        int row,
        int col,
        int rows,
        int columns)
    {
        return row >= 0 && row < rows &&
               col >= 0 && col < columns;
    }

    private static bool IsWalkable(CellType type) =>
        type is not CellType.Wall and not CellType.Shelf and not CellType.RestrictedZone;

    private static bool CanExit(CellType type, int deltaRow, int deltaColumn) => type switch
    {
        CellType.OneWayNorth => deltaRow == -1,
        CellType.OneWayEast => deltaColumn == 1,
        CellType.OneWaySouth => deltaRow == 1,
        CellType.OneWayWest => deltaColumn == -1,
        _ => true
    };

    private static bool CanEnter(CellType type, int deltaRow, int deltaColumn) => type switch
    {
        CellType.OneWayNorth => deltaRow == -1,
        CellType.OneWayEast => deltaColumn == 1,
        CellType.OneWaySouth => deltaRow == 1,
        CellType.OneWayWest => deltaColumn == -1,
        _ => true
    };

    private static List<(int Row, int Col)> ReconstructPath(
        Dictionary<(int Row, int Col), (int Row, int Col)> cameFrom,
        (int Row, int Col) current)
    {
        var path = new List<(int Row, int Col)> { current };

        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }
}
