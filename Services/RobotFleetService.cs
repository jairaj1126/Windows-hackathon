
using System.Collections.Generic;
using System.Linq;
using RobotFleetSimulation.Models;

namespace RobotFleetSimulation.Services;

public sealed class RobotFleetService
{
    private readonly List<Robot> _robots = [];
    private int _nextRobotId = 1;

    public IReadOnlyList<Robot> Robots => _robots;

    public int TotalRobots => _robots.Count;

    public int ActiveRobots =>
        _robots.Count(robot => robot.Status == RobotStatus.Moving);

    public int IdleRobots =>
        _robots.Count(robot => robot.Status == RobotStatus.Idle);

    public Robot AddRobot(string name, int startRow, int startColumn)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new System.ArgumentException(
                "Robot name cannot be empty.",
                nameof(name));
        }

        if (startRow < 0 || startColumn < 0)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(startRow),
                "Robot coordinates cannot be negative.");
        }

        if (IsCellOccupied(startRow, startColumn))
        {
            throw new System.InvalidOperationException(
                $"Cell ({startRow}, {startColumn}) is already occupied.");
        }

        var robot = new Robot
        {
            Id = _nextRobotId++,
            Name = name,
            Row = startRow,
            Column = startColumn,
            BatteryLevel = 100,
            Status = RobotStatus.Idle
        };

        _robots.Add(robot);
        return robot;
    }

    public void ReplaceAll(IEnumerable<Robot> robots)
    {
        _robots.Clear();
        _robots.AddRange(robots);
        _nextRobotId = _robots.Count == 0 ? 1 : _robots.Max(r => r.Id) + 1;
    }

    public Robot? GetRobotById(int robotId)
    {
        return _robots.FirstOrDefault(robot => robot.Id == robotId);
    }

    public Robot? GetRobotAt(int row, int column)
    {
        return _robots.FirstOrDefault(robot =>
            robot.Row == row && robot.Column == column);
    }

    public bool RemoveRobot(int robotId)
    {
        Robot? robot = GetRobotById(robotId);

        if (robot is null)
        {
            return false;
        }

        _robots.Remove(robot);
        return true;
    }

    // Updates a position directly. Use TryMoveRobot when
    // moving a robot to a different cell during simulation.
    public bool UpdateRobotPosition(int robotId, int row, int column)
    {
        Robot? robot = GetRobotById(robotId);

        if (robot is null || row < 0 || column < 0)
        {
            return false;
        }

        robot.Row = row;
        robot.Column = column;
        return true;
    }

    // Moves a robot only if the destination is not occupied
    // by another robot.
    public bool TryMoveRobot(int robotId, int targetRow, int targetColumn)
    {
        Robot? robot = GetRobotById(robotId);

        if (robot is null || targetRow < 0 || targetColumn < 0)
        {
            return false;
        }

        if (IsCellOccupied(targetRow, targetColumn, robotId))
        {
            return false;
        }

        robot.Row = targetRow;
        robot.Column = targetColumn;

        return true;
    }

    public bool UpdateRobotStatus(int robotId, RobotStatus status)
    {
        Robot? robot = GetRobotById(robotId);

        if (robot is null)
        {
            return false;
        }

        robot.Status = status;
        return true;
    }


    public bool UpdateBattery(int robotId, double batteryLevel)
{
    Robot? robot = GetRobotById(robotId);

    if (robot is null)
    {
        return false;
    }

    robot.BatteryLevel = System.Math.Clamp(batteryLevel, 0, robot.BatteryCapacity);

    return true;
}

public bool DrainBattery(int robotId, double amount)
{
    Robot? robot = GetRobotById(robotId);

    if (robot is null || amount < 0)
    {
        return false;
    }

    robot.BatteryLevel = System.Math.Max(0, robot.BatteryLevel - amount);

    return true;
}

    public bool IsCellOccupied(
        int row,
        int column,
        int excludeRobotId = -1)
    {
        return _robots.Any(robot =>
            robot.Id != excludeRobotId &&
            robot.Row == row &&
            robot.Column == column);
    }
}
