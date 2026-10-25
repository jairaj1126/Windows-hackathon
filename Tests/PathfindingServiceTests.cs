using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;
using Xunit;

namespace RobotFleetSimulation.Tests;

public sealed class PathfindingServiceTests
{
    [Fact]
    public void FindsShortestRouteAroundShelves()
    {
        var map = new CellType[5, 5];
        map[1, 0] = CellType.Shelf;
        map[1, 1] = CellType.Shelf;
        map[1, 2] = CellType.Shelf;

        var route = new PathfindingService().FindPath(map, (0, 0), (2, 2));

        Assert.NotEmpty(route);
        Assert.Equal((0, 0), route[0]);
        Assert.Equal((2, 2), route[^1]);
        Assert.Equal(6, route.Count - 1);
    }

    [Fact]
    public void ReturnsEmptyWhenDestinationIsSealed()
    {
        var map = new CellType[3, 3];
        map[0, 1] = CellType.Wall;
        map[1, 0] = CellType.Wall;
        map[1, 1] = CellType.Wall;

        Assert.Empty(new PathfindingService().FindPath(map, (0, 0), (2, 2)));
    }
}
