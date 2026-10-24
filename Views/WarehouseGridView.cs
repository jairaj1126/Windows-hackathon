
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;

namespace RobotFleetSimulation.Views;

public enum EditorMode
{
    Wall,
    RobotStart,
    Destination,
    Erase
}

public sealed class WarehouseGridView : Canvas
{
    private const int Rows = 20;
    private const int Columns = 30;

    private readonly DispatcherTimer _movementTimer = new();
    private readonly Ellipse _robotShape = new();
    private readonly PathfindingService _pathfindingService = new();
    private readonly RobotFleetService? _robotFleet;

    private readonly List<(int Row, int Col)> _route = new();
    private readonly HashSet<(int Row, int Col)> _path = new();

    private readonly GridCell[,] _cells = new GridCell[Rows, Columns];
    private readonly Rectangle[,] _rectangles = new Rectangle[Rows, Columns];

    private readonly TextBlock _selectionLabel;

    private double _cellWidth;
    private double _cellHeight;

    private (int Row, int Col)? _robotStart;
    private (int Row, int Col)? _destination;
    private (int Row, int Col)? _robotPosition;

    private int _routeIndex;
    private int _stepsTaken;

    public EditorMode CurrentMode { get; set; } = EditorMode.Wall;
    public int SelectedRobotId { get; set; } = 1;

    public event Action<int, string, int, int, int, int>? RobotUpdated;

    public event Action<int, int, CellType>? CellChanged;
    public event Action<string, int, int>? RobotStatusChanged;
    public event Action<int, int>? StatisticsChanged;
    public event Action? TaskCompleted;

    public WarehouseGridView(
        TextBlock selectionLabel,
        RobotFleetService? robotFleet = null)
    {
        _selectionLabel = selectionLabel;
        _robotFleet = robotFleet;

        Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
        ClipToBounds = true;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                _cells[row, col] = new GridCell(row, col);

                var rect = new Rectangle
                {
                    Fill = new SolidColorBrush(
                        Color.FromRgb(31, 41, 55)),
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(55, 65, 81)),
                    StrokeThickness = 0.7,
                    Tag = (row, col)
                };

                rect.MouseLeftButtonDown += OnCellClicked;

                _rectangles[row, col] = rect;
                Children.Add(rect);
            }
        }

        _robotShape.Fill = Brushes.White;
        _robotShape.Stroke = Brushes.Black;
        _robotShape.StrokeThickness = 2;
        _robotShape.IsHitTestVisible = false;

        Children.Add(_robotShape);

        _movementTimer.Interval = TimeSpan.FromMilliseconds(300);
        _movementTimer.Tick += (_, _) => MoveRobotOneStep();

        SizeChanged += (_, _) => DrawGrid();

        DrawGrid();
        NotifyStatistics();
        NotifyRobotStatus("Idle");
    }

    public void SetSimulationSpeed(double milliseconds)
    {
        milliseconds = Math.Clamp(milliseconds, 50, 1000);
        _movementTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
    }

    public bool IsValidTaskDestination(int row, int col)
    {
        return row >= 0 && row < Rows &&
               col >= 0 && col < Columns &&
               _cells[row, col].Type != CellType.Wall &&
               !IsOccupiedByAnotherRobot(row, col);
    }

    public bool SetTaskDestination(int row, int col)
    {
        if (!IsValidTaskDestination(row, col))
            return false;

        _movementTimer.Stop();

        if (_destination is { } oldDestination &&
            _cells[oldDestination.Row, oldDestination.Col].Type
                == CellType.Destination)
        {
            _cells[oldDestination.Row, oldDestination.Col].Type =
                CellType.Empty;
        }

        _destination = (row, col);

        if (_cells[row, col].Type != CellType.RobotStart)
            _cells[row, col].Type = CellType.Destination;

        RecalculatePath();

        if (_route.Count == 0)
        {
            _selectionLabel.Text =
                $"No route to task destination ({row}, {col}).";

            return false;
        }

        _selectionLabel.Text =
            $"Task destination set to Row {row}, Column {col}.";

        return true;
    }

    private bool IsOccupiedByAnotherRobot(int row, int col)
    {
        if (_robotFleet is null)
            return false;

        foreach (Robot robot in _robotFleet.Robots)
        {
            // Robot 1 is represented by the current grid movement engine.
            if (robot.Id == 1)
                continue;

            if (robot.Row == row && robot.Column == col)
                return true;
        }

        return false;
    }

    private void DrawGrid()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        _cellWidth = ActualWidth / Columns;
        _cellHeight = ActualHeight / Rows;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                Rectangle rect = _rectangles[row, col];

                rect.Width = _cellWidth;
                rect.Height = _cellHeight;

                SetLeft(rect, col * _cellWidth);
                SetTop(rect, row * _cellHeight);

                PaintCell(row, col);
            }
        }

        DrawRobot();
    }

    private void OnCellClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect ||
            rect.Tag is not ValueTuple<int, int> position)
        {
            return;
        }

        int row = position.Item1;
        int col = position.Item2;

        if (CurrentMode == EditorMode.RobotStart &&
            IsOccupiedByAnotherRobot(row, col))
        {
            _selectionLabel.Text =
                "Another robot already occupies this cell.";

            return;
        }

        CellType oldType = _cells[row, col].Type;

        CellType newType = CurrentMode switch
        {
            EditorMode.Wall => oldType == CellType.Wall
                ? CellType.Empty
                : CellType.Wall,

            EditorMode.RobotStart => CellType.RobotStart,
            EditorMode.Destination => CellType.Destination,
            EditorMode.Erase => CellType.Empty,
            _ => CellType.Empty
        };

        if (newType == CellType.Wall &&
            (oldType == CellType.RobotStart ||
             oldType == CellType.Destination))
        {
            _selectionLabel.Text =
                "Select Erase before placing a wall on a marker.";

            return;
        }

        if (newType == CellType.Wall &&
            _robotPosition == (row, col))
        {
            _selectionLabel.Text =
                "Cannot place a wall under the moving robot.";

            return;
        }

        _movementTimer.Stop();

        if (newType == CellType.RobotStart)
            ClearType(CellType.RobotStart);

        if (newType == CellType.Destination)
            ClearType(CellType.Destination);

        if (_robotStart == (row, col))
            _robotStart = null;

        if (_destination == (row, col))
            _destination = null;

        _cells[row, col].Type = newType;

        if (newType == CellType.RobotStart)
        {
            _robotStart = (row, col);
            _robotPosition = (row, col);
        }

        if (newType == CellType.Destination)
            _destination = (row, col);

        if (CurrentMode == EditorMode.Erase &&
            _robotPosition == (row, col))
        {
            _robotPosition = _robotStart;
        }

        RecalculatePath();
        PaintAllCells();

        _selectionLabel.Text =
            $"Selected cell: Row {row}, Column {col} — {newType}";

        CellChanged?.Invoke(row, col, newType);
    }

    private void RecalculatePath()
    {
        _movementTimer.Stop();

        _path.Clear();
        _route.Clear();

        _routeIndex = 0;
        _stepsTaken = 0;

        if (_robotPosition is null)
            _robotPosition = _robotStart;

        if (_robotPosition is null || _destination is null)
        {
            PaintAllCells();
            NotifyStatistics();
            NotifyRobotStatus("Idle");
            return;
        }

        var map = new CellType[Rows, Columns];

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                map[row, col] = _cells[row, col].Type;
            }
        }

        // Treat other robots as temporary obstacles during pathfinding.
        if (_robotFleet is not null)
        {
            foreach (Robot robot in _robotFleet.Robots)
            {
                if (robot.Id == 1)
                    continue;

                if (robot.Row >= 0 && robot.Row < Rows &&
                    robot.Column >= 0 && robot.Column < Columns)
                {
                    map[robot.Row, robot.Column] = CellType.Wall;
                }
            }
        }

        _route.AddRange(_pathfindingService.FindPath(
            map,
            _robotPosition.Value,
            _destination.Value));

        foreach (var cell in _route)
            _path.Add(cell);

        PaintAllCells();
        NotifyStatistics();

        NotifyRobotStatus(_route.Count > 0 ? "Ready" : "No route");
    }

    public void StartSimulation()
    {
        if (_robotFleet is not null)
{
    Robot? robot = _robotFleet.GetRobotById(SelectedRobotId);

    if (robot is null)
    {
        _selectionLabel.Text = "Selected robot was not found.";
        return;
    }

    if (robot.BatteryLevel <= 0)
    {
        _selectionLabel.Text =
            "Battery empty. Recharge the robot before starting.";

        NotifyRobotStatus("Charging");
        return;
    }
}
        if (_route.Count == 0)
        {
            _selectionLabel.Text =
                "No valid route. Select a start and destination.";

            NotifyRobotStatus("No route");
            return;
        }

        if (_route.Count == 1)
        {
            _robotPosition = _route[0];
            PaintAllCells();

            _selectionLabel.Text = "Destination reached!";
            NotifyRobotStatus("Completed");
            TaskCompleted?.Invoke();

            return;
        }

        if (_routeIndex >= _route.Count - 1)
        {
            _routeIndex = 0;
            _stepsTaken = 0;
            _robotPosition = _route[0];

            PaintAllCells();
            NotifyStatistics();
        }

        _movementTimer.Start();
        _selectionLabel.Text = "Simulation running...";
        NotifyRobotStatus("Moving");
    }

    public void PauseSimulation()
    {
        _movementTimer.Stop();

        _selectionLabel.Text = "Simulation paused.";
        NotifyRobotStatus("Paused");
    }

    public void ResetSimulation()
    {
        _movementTimer.Stop();

        _routeIndex = 0;
        _stepsTaken = 0;
        _robotPosition = _robotStart;

        RecalculatePath();

        PaintAllCells();
        NotifyStatistics();

        _selectionLabel.Text = "Simulation reset.";

        NotifyRobotStatus(
            _robotStart is null
                ? "Idle"
                : _route.Count == 0
                    ? "No route"
                    : "Ready");
    }

    private void MoveRobotOneStep()
    {
        // Stop if the selected robot has run out of battery.
if (_robotFleet is not null)
{
    Robot? robot = _robotFleet.GetRobotById(SelectedRobotId);

    if (robot is null || robot.BatteryLevel <= 0)
    {
        _movementTimer.Stop();
        NotifyRobotStatus("Charging");
        _selectionLabel.Text =
            "Battery empty. Recharge the robot before continuing.";
        return;
    }
}
        if (_route.Count == 0)
        {
            _movementTimer.Stop();
            NotifyRobotStatus("No route");
            return;
        }

        if (_routeIndex >= _route.Count - 1)
        {
            CompleteRoute();
            return;
        }

        var nextPosition = _route[_routeIndex + 1];

        // Check again immediately before movement.
        if (IsOccupiedByAnotherRobot(
                nextPosition.Row,
                nextPosition.Col))
        {
            _movementTimer.Stop();

            _selectionLabel.Text =
                "Movement blocked: another robot occupies the next cell.";

            NotifyRobotStatus("Blocked");
            return;
        }

        _routeIndex++;
_robotPosition = _route[_routeIndex];
_stepsTaken++;

// Drain 1% battery for each successful movement step.
_robotFleet?.DrainBattery(SelectedRobotId, 1);

        PaintAllCells();
        NotifyStatistics();

        if (_routeIndex >= _route.Count - 1)
            CompleteRoute();
        else
            NotifyRobotStatus("Moving");
    }

    private void CompleteRoute()
    {
        _movementTimer.Stop();

        _selectionLabel.Text = "Destination reached!";
        NotifyRobotStatus("Completed");

        TaskCompleted?.Invoke();
    }

    private void NotifyRobotStatus(string status)
    {
        if (_robotPosition is null)
        {
            RobotStatusChanged?.Invoke(status, -1, -1);
            return;
        }

        var position = _robotPosition.Value;

        RobotStatusChanged?.Invoke(
            status,
            position.Row,
            position.Col);
    }

    private void NotifyStatistics()
    {
        int routeLength = Math.Max(0, _route.Count - 1);

        StatisticsChanged?.Invoke(_stepsTaken, routeLength);
    }

    private void DrawRobot()
    {
        if (_robotPosition is null ||
            _cellWidth <= 0 ||
            _cellHeight <= 0)
        {
            _robotShape.Visibility = Visibility.Collapsed;
            return;
        }

        var position = _robotPosition.Value;

        double diameter = Math.Max(
            4,
            Math.Min(_cellWidth, _cellHeight) * 0.65);

        _robotShape.Width = diameter;
        _robotShape.Height = diameter;
        _robotShape.Visibility = Visibility.Visible;

        SetLeft(
            _robotShape,
            position.Col * _cellWidth +
            (_cellWidth - diameter) / 2);

        SetTop(
            _robotShape,
            position.Row * _cellHeight +
            (_cellHeight - diameter) / 2);
    }

    private void ClearType(CellType type)
    {
        foreach (GridCell cell in _cells)
        {
            if (cell.Type == type)
                cell.Type = CellType.Empty;
        }

        if (type == CellType.RobotStart)
        {
            _robotStart = null;
            _robotPosition = null;
        }

        if (type == CellType.Destination)
            _destination = null;
    }

    private void PaintAllCells()
    {
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
                PaintCell(row, col);
        }

        DrawRobot();
    }

    private void PaintCell(int row, int col)
    {
        Rectangle rect = _rectangles[row, col];

        rect.Fill = _cells[row, col].Type switch
        {
            CellType.Wall => new SolidColorBrush(
                Color.FromRgb(239, 68, 68)),

            CellType.RobotStart => new SolidColorBrush(
                Color.FromRgb(59, 130, 246)),

            CellType.Destination => new SolidColorBrush(
                Color.FromRgb(34, 197, 94)),

            _ when _path.Contains((row, col)) =>
                new SolidColorBrush(Color.FromRgb(250, 204, 21)),

            _ => new SolidColorBrush(
                Color.FromRgb(31, 41, 55))
        };
    }

    public bool IsWalkable(int row, int col)
    {
        return row >= 0 && row < Rows &&
               col >= 0 && col < Columns &&
               _cells[row, col].IsWalkable &&
               !IsOccupiedByAnotherRobot(row, col);
    }

    public CellType GetCellType(int row, int col)
    {
        return _cells[row, col].Type;
    }
}
