using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;

namespace RobotFleetSimulation.Views;

public enum EditorMode { Wall, RobotStart, Destination, Erase, Shelf, ChargingStation, SlowZone, LoadingDock, PackingStation, Elevator, RestrictedZone, OneWayNorth, OneWayEast, OneWaySouth, OneWayWest }

public sealed class WarehouseGridView : Canvas
{
    public const int FixedRows = 60;
    public const int FixedColumns = 80;
    public int Rows { get; private set; } = FixedRows;
    public int Columns { get; private set; } = FixedColumns;
    private readonly DispatcherTimer _timer = new();
    private readonly RobotFleetService _fleet;
    private readonly TextBlock _label;
    private ScrollViewer? _viewport;
    private GridCell[,] _cells = new GridCell[FixedRows, FixedColumns];
    private readonly PathfindingService _pathfinder = new();
    private readonly Dictionary<int, List<(int Row, int Col)>> _routes = [];
    private readonly Dictionary<int, int> _routeIndexes = [];
    private readonly Dictionary<int, int> _taskIds = [];
    private readonly Dictionary<int, List<TaskStop>> _taskStops = [];
    private readonly Dictionary<int, List<int>> _taskStopIndexes = [];
    private readonly Dictionary<int, RobotTaskType> _taskTypes = [];
    private readonly Dictionary<int, double> _taskPayloads = [];
    private readonly HashSet<int> _charging = [];
    private readonly Dictionary<int, double> _speedAccumulators = [];
    private readonly Dictionary<int, int> _waitFor = [];
    private readonly int[,] _congestionHeat = new int[FixedRows, FixedColumns];
    private readonly Dictionary<(int Row, int Col), int> _routeOverlay = [];
    private readonly List<SimulationFrame> _history = [];
    private SimulationFrame? _replayFrame;
    private readonly Stack<(int Row, int Col, CellType Before, CellType After)> _undo = [];
    private readonly Stack<(int Row, int Col, CellType Before, CellType After)> _redo = [];
    private readonly Dictionary<int, (int Row, int Col)> _chargeReturnTargets = [];
    private readonly Dictionary<int, List<TaskStop>> _chargeReturnStops = [];
    private double _tileWidth, _tileHeight;
    private double _zoom = 1;
    private double _speed = 300;
    private bool _running;
    private bool _singleStep;
    private int _totalSteps;
    private int _tick;
    public int CongestionWaits { get; private set; }
    public long IdleRobotTicks { get; private set; }
    public double BatteryConsumed { get; private set; }
    public double IdleTimeSeconds { get; private set; }
    private static readonly Brush[] RouteBrushes =
    [Brushes.Cyan, Brushes.Magenta, Brushes.Gold, Brushes.DeepSkyBlue, Brushes.Orange];
    private static readonly Brush[] RouteOverlayBrushes =
    [new SolidColorBrush(Color.FromArgb(95, 34, 211, 238)), new SolidColorBrush(Color.FromArgb(95, 232, 121, 249)),
     new SolidColorBrush(Color.FromArgb(95, 250, 204, 21)), new SolidColorBrush(Color.FromArgb(95, 56, 189, 248)),
     new SolidColorBrush(Color.FromArgb(95, 251, 146, 60))];
    private static readonly Brush[] CellBrushes =
    [Brushes.DarkSlateGray, Brushes.SlateGray, Brushes.Transparent, Brushes.Transparent,
     FrozenBrush(120, 74, 42), FrozenBrush(16, 130, 115), FrozenBrush(121, 91, 35),
     FrozenBrush(37, 99, 235), FrozenBrush(219, 112, 42), FrozenBrush(139, 92, 246),
     FrozenBrush(15, 23, 42), FrozenBrush(22, 112, 137), FrozenBrush(22, 112, 137),
     FrozenBrush(22, 112, 137), FrozenBrush(22, 112, 137), FrozenBrush(90, 109, 57)];
    private static readonly Brush[] HeatBrushes = Enumerable.Range(0, 18)
        .Select(i => FrozenBrush(239, 68, 68, (byte)Math.Min(170, 45 + i * 10))).ToArray();

    public EditorMode CurrentMode { get; set; } = EditorMode.Wall;
    public int SelectedRobotId { get; set; } = 1;
    public event Action<string, int, int>? RobotStatusChanged;
    public event Action<int, int>? StatisticsChanged;
    public event Action<int, int>? RobotTaskCompleted;
    public event Action? FleetChanged;
    public event Action<int, int, CellType>? CellChanged;
    public event Action<int>? HistoryChanged;
    public int HistoryFrameCount => _history.Count;
    public IReadOnlyList<SimulationFrame> History => _history;

    public WarehouseGridView(TextBlock selectionLabel, RobotFleetService fleet)
    {
        _label = selectionLabel;
        _fleet = fleet;
        Background = new SolidColorBrush(Color.FromRgb(10, 17, 27));
        ClipToBounds = true;
        MouseLeftButtonDown += CellClicked;
        InitializeCells();
        UpdateCanvasExtent();
        _timer.Interval = TimeSpan.FromMilliseconds(_speed);
        _timer.Tick += (_, _) => TickFleet();
        SizeChanged += (_, _) => Render();
        Render();
        RecordFrame("Simulation initialized");
    }

    public void SetSimulationSpeed(double milliseconds)
    {
        _speed = Math.Clamp(milliseconds, 50, 1000);
        _timer.Interval = TimeSpan.FromMilliseconds(_speed);
    }

    public void AttachViewport(ScrollViewer viewport)
    {
        if (_viewport is not null) _viewport.ScrollChanged -= Viewport_ScrollChanged;
        _viewport = viewport;
        _viewport.ScrollChanged += Viewport_ScrollChanged;
        Render();
    }

    private void Viewport_ScrollChanged(object sender, ScrollChangedEventArgs e) => Render();

    public void SetZoom(double zoom)
    {
        double centerColumn = _viewport is null ? Columns / 2d :
            (_viewport.HorizontalOffset + _viewport.ViewportWidth / 2) / Math.Max(1, _tileWidth);
        double centerRow = _viewport is null ? Rows / 2d :
            (_viewport.VerticalOffset + _viewport.ViewportHeight / 2) / Math.Max(1, _tileHeight);
        _zoom = Math.Clamp(zoom, .1, 2.0);
        UpdateCanvasExtent();
        if (_viewport is not null)
        {
            double cellSize = 24 * _zoom;
            _viewport.Dispatcher.BeginInvoke(() =>
            {
                _viewport.ScrollToHorizontalOffset(centerColumn * cellSize - _viewport.ViewportWidth / 2);
                _viewport.ScrollToVerticalOffset(centerRow * cellSize - _viewport.ViewportHeight / 2);
            }, DispatcherPriority.Loaded);
        }
    }

    public void ShowReplayFrame(int index)
    {
        _timer.Stop(); _running = false;
        if (_history.Count == 0) return;
        index = Math.Clamp(index, 0, _history.Count - 1);
        _replayFrame = index == _history.Count - 1 ? null : _history[index];
        _label.Text = _replayFrame is null ? "Live view" : $"Replay · tick {_replayFrame.Tick} · {_replayFrame.Event}";
        Render();
    }

    public void SetLayoutCell(int row, int col, CellType type)
    {
        if (Inside(row, col) && !_fleet.IsCellOccupied(row, col)) _cells[row, col].Type = type;
    }

    public int GetRouteLength(int robotId, int row, int col)
    {
        var robot = _fleet.GetRobotById(robotId);
        if (robot is null || !Inside(row, col)) return int.MaxValue;
        var route = FindRoute(robot, row, col);
        return route.Count == 0 ? int.MaxValue : route.Count - 1;
    }

    public int GetRouteLength(int robotId, RobotTask task)
    {
        var robot = _fleet.GetRobotById(robotId);
        if (robot is null) return int.MaxValue;
        var stops = task.Stops.Concat([new TaskStop { Row = task.DestinationRow, Column = task.DestinationColumn }]);
        var position = (Row: robot.Row, Col: robot.Column);
        int total = 0;
        foreach (var stop in stops)
        {
            var route = FindRouteFrom(robot, position, (stop.Row, stop.Column));
            if (route.Count == 0) return int.MaxValue;
            total += Math.Max(0, route.Count - 1);
            position = (stop.Row, stop.Column);
        }
        return total;
    }

    public void CancelAssignment(int robotId)
    {
        _routes.Remove(robotId); _routeIndexes.Remove(robotId); _taskIds.Remove(robotId); _taskStops.Remove(robotId); _taskStopIndexes.Remove(robotId); _speedAccumulators.Remove(robotId);
        _charging.Remove(robotId); _chargeReturnTargets.Remove(robotId); _chargeReturnStops.Remove(robotId);
        _taskTypes.Remove(robotId); _taskPayloads.Remove(robotId);
        var robot = _fleet.GetRobotById(robotId);
        if (robot is not null) { robot.AssignedTaskId = null; robot.Status = RobotStatus.Idle; }
        Render(); FleetChanged?.Invoke(); NotifyStats();
    }

    public void RefreshView() { Render(); RecordFrame("Warehouse updated"); }

    public void PauseRobot(int robotId)
    {
        var robot = _fleet.GetRobotById(robotId);
        if (robot is null || robot.Status is not (RobotStatus.Moving or RobotStatus.Charging)) return;
        robot.Status = RobotStatus.Paused;
        FleetChanged?.Invoke();
    }

    public void ResumeRobot(int robotId)
    {
        var robot = _fleet.GetRobotById(robotId);
        if (robot is null || !_routes.ContainsKey(robotId)) return;
        robot.Status = RobotStatus.Moving;
        _running = true;
        _timer.Start();
        FleetChanged?.Invoke();
    }

    public bool UndoMapEdit()
    {
        if (!_undo.TryPeek(out var edit) ||
            (!new GridCell(edit.Row, edit.Col) { Type = edit.Before }.IsWalkable && _fleet.IsCellOccupied(edit.Row, edit.Col))) return false;
        _undo.Pop();
        _cells[edit.Row, edit.Col].Type = edit.Before;
        _redo.Push(edit);
        Render(); NotifyStats();
        return true;
    }

    public bool RedoMapEdit()
    {
        if (!_redo.TryPeek(out var edit) ||
            (!new GridCell(edit.Row, edit.Col) { Type = edit.After }.IsWalkable && _fleet.IsCellOccupied(edit.Row, edit.Col))) return false;
        _redo.Pop();
        _cells[edit.Row, edit.Col].Type = edit.After;
        _undo.Push(edit);
        Render(); NotifyStats();
        return true;
    }

    public bool IsValidTaskDestination(int row, int col) =>
        Inside(row, col) && _cells[row, col].IsWalkable &&
        !_fleet.Robots.Any(r => r.Row == row && r.Column == col);

    public bool HasActiveRoute(int robotId) => _routes.ContainsKey(robotId);

    public bool AssignTask(int robotId, int row, int col, int taskId)
        => AssignTask(robotId, row, col, taskId, [], 0, RobotTaskType.Delivery);

    public bool AssignTask(int robotId, int row, int col, int taskId, IReadOnlyList<TaskStop> stops,
        double payloadKg, RobotTaskType taskType = RobotTaskType.Delivery)
    {
        var robot = _fleet.GetRobotById(robotId);
        if (robot is null || !Inside(row, col) || !_cells[row, col].IsWalkable ||
            _fleet.IsCellOccupied(row, col, robotId)) return false;
        if (payloadKg > robot.PayloadCapacityKg || payloadKg < 0) return false;
        var targets = stops.Where(stop => stop.Row != row || stop.Column != col)
            .Append(new TaskStop { Row = row, Column = col }).ToList();
        var route = new List<(int Row, int Col)>();
        var stopIndexes = new List<int>();
        var position = (Row: robot.Row, Col: robot.Column);
        foreach (var stop in targets)
        {
            if (!Inside(stop.Row, stop.Column) || !_cells[stop.Row, stop.Column].IsWalkable) return false;
            var segment = FindRouteFrom(robot, position, (stop.Row, stop.Column));
            if (segment.Count == 0) return false;
            route.AddRange(route.Count == 0 ? segment : segment.Skip(1));
            stopIndexes.Add(route.Count - 1);
            position = (stop.Row, stop.Column);
        }
        if (route.Count == 0) route.Add((robot.Row, robot.Column));
        _routes[robotId] = route;
        _routeIndexes[robotId] = 0;
        _taskIds[robotId] = taskId;
        _taskStops[robotId] = targets;
        _taskStopIndexes[robotId] = stopIndexes;
        _taskTypes[robotId] = taskType;
        _taskPayloads[robotId] = payloadKg;
        _speedAccumulators[robotId] = 0;
        robot.PayloadKg = taskType == RobotTaskType.Pickup ? 0 : payloadKg;
        robot.AssignedTaskId = taskId > 0 ? taskId : null;
        robot.Status = route.Count == 1 ? RobotStatus.Completed : RobotStatus.Ready;
        Render();
        NotifyStats();
        return true;
    }

    public void StartSimulation()
    {
        var runnable = _routes.Keys.Select(_fleet.GetRobotById).Any(robot => robot is not null &&
            _routes[robot.Id].Count > 1 &&
            (robot.Status is RobotStatus.Ready or RobotStatus.Paused or RobotStatus.Moving or RobotStatus.Charging) &&
            (robot.BatteryLevel > 0 || _cells.Cast<GridCell>().Any(cell => cell.Type == CellType.ChargingStation)));
        if (!runnable) { _label.Text = "Assign a reachable task to a robot first."; return; }
        _replayFrame = null;
        _running = true;
        foreach (int id in _routes.Keys)
        {
            var robot = _fleet.GetRobotById(id);
            if (robot is not null && robot.Status is RobotStatus.Ready or RobotStatus.Paused)
                robot.Status = RobotStatus.Moving;
        }
        _timer.Start();
        FleetChanged?.Invoke();
        _label.Text = "Fleet simulation running — collision checks and route replanning are active.";
    }

    public void PauseSimulation()
    {
        _running = false;
        _timer.Stop();
        foreach (int id in _routes.Keys)
        {
            var robot = _fleet.GetRobotById(id);
            if (robot?.Status == RobotStatus.Moving) robot.Status = RobotStatus.Paused;
        }
        FleetChanged?.Invoke();
        _label.Text = "Fleet simulation paused.";
    }

    public void StepOnce()
    {
        _replayFrame = null;
        bool wasRunning = _running;
        _timer.Stop();
        _running = false;
        foreach (int id in _routes.Keys)
        {
            var robot = _fleet.GetRobotById(id);
            if (robot?.Status == RobotStatus.Ready) robot.Status = RobotStatus.Paused;
        }
        _singleStep = true;
        TickFleet();
        _singleStep = false;
        if (wasRunning) { _running = true; _timer.Start(); }
    }

    public void ResetSimulation()
    {
        PauseSimulation();
        foreach (int id in _routes.Keys.ToArray())
        {
            var robot = _fleet.GetRobotById(id);
            if (robot is null) continue;
            if (_charging.Contains(id))
            {
                robot.Status = RobotStatus.Ready;
                continue;
            }
            if (!_routes.TryGetValue(id, out var assignedRoute) || assignedRoute.Count == 0)
            { robot.Status = RobotStatus.Idle; continue; }
            robot.Row = assignedRoute[0].Row;
            robot.Column = assignedRoute[0].Col;
            robot.Status = RobotStatus.Paused;
            _routeIndexes[id] = 0;
        }
        _totalSteps = 0;
        CongestionWaits = 0; IdleRobotTicks = 0; IdleTimeSeconds = 0; BatteryConsumed = 0;
        foreach (var robot in _fleet.Robots) robot.TotalSteps = 0;
        Render();
        NotifyStats();
        RecordFrame("Simulation reset");
    }

    public WarehouseScenario CreateScenario(IEnumerable<RobotTask> tasks) => new()
    {
        SchemaVersion = 2,
        Rows = Rows,
        Columns = Columns,
        Cells = Enumerable.Range(0, Rows).Select(r => Enumerable.Range(0, Columns)
            .Select(c => _cells[r, c].Type).ToArray()).ToArray(),
        Robots = _fleet.Robots.Select(r => new Robot { Id = r.Id, Name = r.Name, Row = r.Row,
            Column = r.Column, BatteryLevel = r.BatteryLevel, BatteryCapacity = r.BatteryCapacity,
            SpeedCellsPerTick = r.SpeedCellsPerTick, PayloadCapacityKg = r.PayloadCapacityKg, PayloadKg = r.PayloadKg,
            Status = r.Status, TotalSteps = r.TotalSteps }).ToList(),
        Tasks = tasks.Select(t => new RobotTask { Id = t.Id, Name = t.Name,
            DestinationRow = t.DestinationRow, DestinationColumn = t.DestinationColumn,
            Priority = t.Priority, Status = t.Status, AssignedRobotId = t.AssignedRobotId,
            DeadlineUtc = t.DeadlineUtc, Type = t.Type, PayloadWeightKg = t.PayloadWeightKg,
            Stops = t.Stops.Select(stop => new TaskStop { Row = stop.Row, Column = stop.Column, Action = stop.Action }).ToList() }).ToList()
    };

    public void LoadScenario(WarehouseScenario scenario)
    {
        PauseSimulation();
        if (scenario.SchemaVersion is < 1 or > 2) throw new InvalidDataException($"Unsupported scenario schema version {scenario.SchemaVersion}.");
        Rows = FixedRows;
        Columns = FixedColumns;
        _cells = new GridCell[Rows, Columns];
        for (int r = 0; r < Rows; r++) for (int c = 0; c < Columns; c++)
        {
            var type = r < scenario.Rows && c < scenario.Columns && r < scenario.Cells.Length && scenario.Cells[r] is { Length: > 0 } line && c < line.Length
                ? line[c] : CellType.Empty;
            _cells[r, c] = new GridCell(r, c) { Type = type };
        }
        _fleet.ReplaceAll(scenario.Robots);
        foreach (var robot in _fleet.Robots)
        {
            if (Inside(robot.Row, robot.Column) && _cells[robot.Row, robot.Column].IsWalkable &&
                !_fleet.Robots.Any(other => other.Id < robot.Id && other.Row == robot.Row && other.Column == robot.Column)) continue;
            var open = Enumerable.Range(0, Rows).SelectMany(row => Enumerable.Range(0, Columns)
                    .Select(column => (Row: row, Col: column)))
                .FirstOrDefault(cell => _cells[cell.Row, cell.Col].IsWalkable &&
                    !_fleet.Robots.Any(other => other.Id != robot.Id && other.Row == cell.Row && other.Column == cell.Col));
            robot.Row = open.Row; robot.Column = open.Col; robot.Status = RobotStatus.Idle; robot.AssignedTaskId = null;
        }
        _routes.Clear(); _routeIndexes.Clear(); _taskIds.Clear(); _taskStops.Clear(); _taskStopIndexes.Clear(); _taskTypes.Clear(); _taskPayloads.Clear(); _speedAccumulators.Clear(); _charging.Clear();
        _chargeReturnTargets.Clear(); _chargeReturnStops.Clear();
        _replayFrame = null; _history.Clear(); _tick = 0;
        UpdateCanvasExtent();
        Render(); FleetChanged?.Invoke(); NotifyStats(); RecordFrame("Scenario loaded");
    }

    public bool IsWalkable(int row, int col) => Inside(row, col) && _cells[row, col].IsWalkable;
    public CellType GetCellType(int row, int col) => _cells[row, col].Type;

    private void CellClicked(object sender, MouseButtonEventArgs e)
    {
        if (_tileWidth <= 0 || _tileHeight <= 0) return;
        Point position = e.GetPosition(this);
        int r = (int)(position.Y / _tileHeight), c = (int)(position.X / _tileWidth);
        if (!Inside(r, c)) return;
        CellType old = _cells[r, c].Type;
        CellType next = CurrentMode switch
        {
            EditorMode.Wall => old == CellType.Wall ? CellType.Empty : CellType.Wall,
            EditorMode.Shelf => old == CellType.Shelf ? CellType.Empty : CellType.Shelf,
            EditorMode.ChargingStation => old == CellType.ChargingStation ? CellType.Empty : CellType.ChargingStation,
            EditorMode.SlowZone => old == CellType.SlowZone ? CellType.Empty : CellType.SlowZone,
            EditorMode.LoadingDock => old == CellType.LoadingDock ? CellType.Empty : CellType.LoadingDock,
            EditorMode.PackingStation => old == CellType.PackingStation ? CellType.Empty : CellType.PackingStation,
            EditorMode.Elevator => old == CellType.Elevator ? CellType.Empty : CellType.Elevator,
            EditorMode.RestrictedZone => old == CellType.RestrictedZone ? CellType.Empty : CellType.RestrictedZone,
            EditorMode.OneWayNorth => CellType.OneWayNorth,
            EditorMode.OneWayEast => CellType.OneWayEast,
            EditorMode.OneWaySouth => CellType.OneWaySouth,
            EditorMode.OneWayWest => CellType.OneWayWest,
            EditorMode.Erase => CellType.Empty,
            _ => old
        };
        if (CurrentMode == EditorMode.RobotStart)
        {
            var robot = _fleet.GetRobotById(SelectedRobotId);
            if (robot is null || robot.AssignedTaskId is not null || !_cells[r, c].IsWalkable || _fleet.IsCellOccupied(r, c, robot.Id))
            { _label.Text = "Choose an open cell that is not occupied."; return; }
            robot.Row = r; robot.Column = c; robot.Status = RobotStatus.Idle;
            _routes.Remove(robot.Id); FleetChanged?.Invoke();
            _label.Text = $"{robot.Name} positioned at ({r}, {c}).";
        }
        else if (CurrentMode == EditorMode.Destination)
        {
            var robot = _fleet.GetRobotById(SelectedRobotId);
            if (robot is null || !AssignTask(robot.Id, r, c, -1))
            { _label.Text = "No route to that destination."; return; }
            _label.Text = $"Route assigned to {robot.Name}. Press Start to run.";
        }
        else
        {
            if (!new GridCell(r, c) { Type = next }.IsWalkable && _fleet.IsCellOccupied(r, c))
            { _label.Text = "Cannot block a cell occupied by a robot."; return; }
            _cells[r, c].Type = next;
            _undo.Push((r, c, old, next));
            _redo.Clear();
            foreach (int id in _routes.Keys.ToArray())
            {
                var robot = _fleet.GetRobotById(id);
                if (robot is not null && _taskIds.GetValueOrDefault(id, -1) < 0)
                {
                    var oldRoute = _routes[id];
                    if (oldRoute.Skip(_routeIndexes.GetValueOrDefault(id)).Contains((r, c)))
                        _routes.Remove(id);
                }
            }
            CellChanged?.Invoke(r, c, next);
            RecordFrame($"Map cell ({r},{c}) set to {next}");
            _label.Text = $"Cell ({r}, {c}) changed to {next}.";
        }
        Render(); NotifyStats();
    }

    private void TickFleet()
    {
        _waitFor.Clear();
        long idleCount = _fleet.Robots.LongCount(r => r.Status is RobotStatus.Idle or RobotStatus.Ready);
        IdleRobotTicks += idleCount;
        IdleTimeSeconds += idleCount * _speed / 1000d;
        var reservedCells = new HashSet<(int Row, int Col)>();
        foreach (var robot in _fleet.Robots.OrderBy(r => (r.Id + _tick) % Math.Max(1, _fleet.TotalRobots)).ToArray())
        {
            if (_charging.Contains(robot.Id))
            {
                var chargeRoute = _routes.GetValueOrDefault(robot.Id);
                var chargeIndex = _routeIndexes.GetValueOrDefault(robot.Id);
                if (chargeRoute is not null && chargeIndex >= chargeRoute.Count - 1)
                {
                    if (robot.BatteryLevel >= robot.BatteryCapacity)
                    {
                        _charging.Remove(robot.Id);
                        var target = _chargeReturnTargets.GetValueOrDefault(
                            robot.Id,
                            (Row: robot.Row, Col: robot.Column));
                        _chargeReturnTargets.Remove(robot.Id);
                        var stops = _chargeReturnStops.GetValueOrDefault(robot.Id, []);
                        List<int> resumedStopIndexes = [];
                        var resumed = stops.Count > 0
                            ? BuildRoute(robot, stops, out resumedStopIndexes)
                            : FindRoute(robot, target.Row, target.Col);
                        _routes[robot.Id] = resumed;
                        _routeIndexes[robot.Id] = 0;
                        if (stops.Count > 0) _taskStopIndexes[robot.Id] = resumedStopIndexes;
                        robot.Status = resumed.Count > 1 && _running ? RobotStatus.Moving : RobotStatus.Ready;
                        _taskStops[robot.Id] = stops;
                        _chargeReturnStops.Remove(robot.Id);
                    }
                    else { robot.BatteryLevel = Math.Min(robot.BatteryCapacity, robot.BatteryLevel + robot.BatteryCapacity * .08); robot.Status = RobotStatus.Charging; }
                    continue;
                }
            }
            if (!_routes.TryGetValue(robot.Id, out var route) ||
                (robot.Status != RobotStatus.Moving && !(_singleStep && robot.Status == RobotStatus.Paused))) continue;
            if (robot.BatteryCapacity > 0 && robot.BatteryLevel / robot.BatteryCapacity <= .2 && !_charging.Contains(robot.Id))
            {
                var charger = FindNearestCharger(robot);
                if (charger is null)
                {
                    if (robot.BatteryLevel <= 1) { robot.Status = RobotStatus.Paused; _label.Text = $"{robot.Name} battery depleted; place a charging station."; continue; }
                }
                else if (robot.BatteryLevel <= 1 || Math.Abs(charger.Value.Row - robot.Row) + Math.Abs(charger.Value.Col - robot.Column) <= robot.BatteryLevel - 1)
                {
                    var chargeRoute = FindRoute(robot, charger.Value.Row, charger.Value.Col);
                    if (chargeRoute.Count > 0)
                    {
                        _chargeReturnTargets[robot.Id] = route[^1];
                        int currentIndex = _routeIndexes.GetValueOrDefault(robot.Id);
                        _chargeReturnStops[robot.Id] = _taskStops.GetValueOrDefault(robot.Id, [])
                            .Zip(_taskStopIndexes.GetValueOrDefault(robot.Id, []))
                            .Where(pair => pair.Second > currentIndex)
                            .Select(pair => pair.First).ToList();
                        _routes[robot.Id] = chargeRoute; _routeIndexes[robot.Id] = 0; _charging.Add(robot.Id); robot.Status = RobotStatus.Moving;
                        route = chargeRoute;
                    }
                    else if (robot.BatteryLevel <= 1) { robot.Status = RobotStatus.Paused; continue; }
                }
            }
            double speedBudget = _speedAccumulators.GetValueOrDefault(robot.Id) + Math.Clamp(robot.SpeedCellsPerTick, .25, 3);
            int moveCount = Math.Clamp((int)speedBudget, 0, 3);
            _speedAccumulators[robot.Id] = speedBudget - moveCount;
            for (int move = 0; move < moveCount; move++)
            {
                if (!_routes.TryGetValue(robot.Id, out route)) break;
                int index = _routeIndexes.GetValueOrDefault(robot.Id);
                if (index >= route.Count - 1) { CompleteRobotRoute(robot); break; }
                var next = route[index + 1];
                int deltaRow = next.Row - robot.Row, deltaCol = next.Col - robot.Column;
                if (!_cells[next.Row, next.Col].IsWalkable || !CanExit(_cells[robot.Row, robot.Column].Type, deltaRow, deltaCol) ||
                    !CanEnter(_cells[next.Row, next.Col].Type, deltaRow, deltaCol))
                {
                    var detour = FindRoute(robot, route[^1].Row, route[^1].Col);
                    if (detour.Count > 1) { _routes[robot.Id] = detour; _routeIndexes[robot.Id] = 0; }
                    else robot.Status = RobotStatus.Paused;
                    break;
                }
                if (_fleet.IsCellOccupied(next.Row, next.Col, robot.Id) || reservedCells.Contains(next))
                {
                    _congestionHeat[next.Row, next.Col]++;
                    var blocker = _fleet.GetRobotAt(next.Row, next.Col);
                    if (blocker is not null)
                    {
                        _waitFor[robot.Id] = blocker.Id;
                        if (TryResolveDeadlock(robot.Id, reservedCells)) break;
                    }
                    var detour = FindRoute(robot, route[^1].Row, route[^1].Col);
                    if (detour.Count > 1) { _routes[robot.Id] = detour; _routeIndexes[robot.Id] = 0; }
                    CongestionWaits++;
                    break;
                }
                reservedCells.Add(next);
                robot.Row = next.Row; robot.Column = next.Col; robot.TotalSteps++;
                _waitFor.Remove(robot.Id);
                robot.BatteryLevel = Math.Max(0, robot.BatteryLevel - 1); BatteryConsumed++;
                _routeIndexes[robot.Id] = index + 1; _totalSteps++;
                if (_cells[next.Row, next.Col].Type == CellType.SlowZone) { robot.BatteryLevel = Math.Max(0, robot.BatteryLevel - 0.25); BatteryConsumed += 0.25; }
                if (index + 1 >= route.Count - 1) { CompleteRobotRoute(robot); break; }
            }
        }
        if (_fleet.Robots.All(r => r.Status != RobotStatus.Moving) && _charging.Count == 0) { _timer.Stop(); _running = false; }
        _tick++;
        Render(); NotifyStats(); FleetChanged?.Invoke();
        RecordFrame("Simulation tick");
    }

    private void CompleteRobotRoute(Robot robot)
    {
        int taskId = _taskIds.GetValueOrDefault(robot.Id, -1);
        if (_charging.Contains(robot.Id)) { robot.Status = RobotStatus.Charging; return; }
        robot.Status = RobotStatus.Completed;
        robot.AssignedTaskId = null;
        robot.PayloadKg = 0;
        _taskIds.Remove(robot.Id); _taskStops.Remove(robot.Id); _taskStopIndexes.Remove(robot.Id);
        _routes.Remove(robot.Id); _routeIndexes.Remove(robot.Id); _speedAccumulators.Remove(robot.Id);
        RobotStatusChanged?.Invoke("Completed", robot.Row, robot.Column);
        RecordFrame($"{robot.Name} completed its route");
        RobotTaskType taskType = _taskTypes.GetValueOrDefault(robot.Id, RobotTaskType.Delivery);
        if (taskType == RobotTaskType.Pickup) robot.PayloadKg = _taskPayloads.GetValueOrDefault(robot.Id);
        else if (taskType == RobotTaskType.Delivery) robot.PayloadKg = 0;
        if (taskType == RobotTaskType.Charge) robot.BatteryLevel = robot.BatteryCapacity;
        _taskTypes.Remove(robot.Id); _taskPayloads.Remove(robot.Id);
        if (taskId > 0) RobotTaskCompleted?.Invoke(robot.Id, taskId);
    }

    private bool TryResolveDeadlock(int blockedRobotId, HashSet<(int Row, int Col)> reservations)
    {
        var chain = new List<int>();
        int current = blockedRobotId;
        while (_waitFor.TryGetValue(current, out int next))
        {
            int cycleIndex = chain.IndexOf(current);
            if (cycleIndex >= 0)
            {
                var cycle = chain.Skip(cycleIndex).ToList();
                int yieldId = cycle.Max();
                var yielder = _fleet.GetRobotById(yieldId);
                if (yielder is null || !_routes.TryGetValue(yieldId, out var route)) return false;
                int index = _routeIndexes.GetValueOrDefault(yieldId);
                var backtrackCandidate = route.Take(Math.Max(0, index)).Reverse()
                    .Where(cell => _cells[cell.Row, cell.Col].IsWalkable &&
                        !_fleet.IsCellOccupied(cell.Row, cell.Col, yieldId) && !reservations.Contains(cell))
                    .Select(cell => ((int Row, int Col)?)cell).FirstOrDefault();
                backtrackCandidate ??= DirectionsForYield(yielder)
                    .Select(direction => (Row: yielder.Row + direction.Row, Col: yielder.Column + direction.Col))
                    .Where(cell => Inside(cell.Row, cell.Col) && _cells[cell.Row, cell.Col].IsWalkable &&
                        !_fleet.IsCellOccupied(cell.Row, cell.Col, yieldId) && !reservations.Contains(cell))
                    .Select(cell => ((int Row, int Col)?)cell).FirstOrDefault();
                if (backtrackCandidate is not { } backtrack) return false;
                _fleet.TryMoveRobot(yieldId, backtrack.Row, backtrack.Col);
                yielder.TotalSteps++; yielder.BatteryLevel = Math.Max(0, yielder.BatteryLevel - 1);
                BatteryConsumed++; _totalSteps++;
                var remaining = _taskStops.GetValueOrDefault(yieldId, [])
                    .Zip(_taskStopIndexes.GetValueOrDefault(yieldId, []))
                    .Where(pair => pair.Second > index).Select(pair => pair.First).ToList();
                if (remaining.Count > 0)
                {
                    _routes[yieldId] = BuildRoute(yielder, remaining, out var stopIndexes);
                    _taskStops[yieldId] = remaining; _taskStopIndexes[yieldId] = stopIndexes; _routeIndexes[yieldId] = 0;
                }
                else
                {
                    var target = route[^1];
                    _routes[yieldId] = FindRoute(yielder, target.Row, target.Col);
                    _routeIndexes[yieldId] = 0;
                }
                foreach (int id in cycle) _waitFor.Remove(id);
                _label.Text = $"Traffic deadlock detected; {yielder.Name} backed up to clear the aisle.";
                RecordFrame($"Deadlock resolved by yielding {yielder.Name}");
                return true;
            }
            chain.Add(current);
            current = next;
        }
        return false;
    }

    private static readonly (int Row, int Col)[] YieldDirections = [(-1, 0), (0, 1), (1, 0), (0, -1)];
    private static IEnumerable<(int Row, int Col)> DirectionsForYield(Robot robot) => YieldDirections;

    private (int Row, int Col)? FindNearestCharger(Robot robot)
    {
        var chargers = Enumerable.Range(0, Rows).SelectMany(r => Enumerable.Range(0, Columns)
            .Where(c => _cells[r, c].Type == CellType.ChargingStation).Select(c => (Row: r, Col: c)));
        return chargers.Select(cell => (Cell: cell, Route: FindRoute(robot, cell.Row, cell.Col)))
            .Where(item => item.Route.Count > 0)
            .OrderBy(item => item.Route.Count)
            .Select(item => ((int Row, int Col)?)item.Cell)
            .FirstOrDefault();
    }

    private List<(int Row, int Col)> FindRoute(Robot robot, int row, int col) =>
        FindRouteFrom(robot, (robot.Row, robot.Column), (row, col));

    private List<(int Row, int Col)> BuildRoute(Robot robot, IReadOnlyList<TaskStop> stops, out List<int> stopIndexes)
    {
        stopIndexes = [];
        var route = new List<(int Row, int Col)>();
        var position = (Row: robot.Row, Col: robot.Column);
        foreach (var stop in stops)
        {
            var segment = FindRouteFrom(robot, position, (stop.Row, stop.Column));
            if (segment.Count == 0) return [];
            route.AddRange(route.Count == 0 ? segment : segment.Skip(1));
            stopIndexes.Add(route.Count - 1);
            position = (stop.Row, stop.Column);
        }
        return route.Count == 0 ? [(robot.Row, robot.Column)] : route;
    }

    private List<(int Row, int Col)> FindRouteFrom(Robot robot, (int Row, int Col) start, (int Row, int Col) destination)
    {
        var map = BuildMap();
        foreach (var other in _fleet.Robots.Where(r => r.Id != robot.Id))
            if (Inside(other.Row, other.Column)) map[other.Row, other.Column] = CellType.Wall;
        return _pathfinder.FindPath(map, start, destination);
    }

    private CellType[,] BuildMap()
    {
        var map = new CellType[Rows, Columns];
        for (int r = 0; r < Rows; r++) for (int c = 0; c < Columns; c++) map[r, c] = _cells[r, c].Type;
        return map;
    }

    private static bool CanExit(CellType type, int deltaRow, int deltaCol) => type switch
    {
        CellType.OneWayNorth => deltaRow == -1,
        CellType.OneWayEast => deltaCol == 1,
        CellType.OneWaySouth => deltaRow == 1,
        CellType.OneWayWest => deltaCol == -1,
        _ => true
    };

    private static bool CanEnter(CellType type, int deltaRow, int deltaCol) => type switch
    {
        CellType.OneWayNorth => deltaRow == -1,
        CellType.OneWayEast => deltaCol == 1,
        CellType.OneWaySouth => deltaRow == 1,
        CellType.OneWayWest => deltaCol == -1,
        _ => true
    };

    private void UpdateCanvasExtent()
    {
        const double baseTileSize = 24;
        Width = Columns * baseTileSize * _zoom;
        Height = Rows * baseTileSize * _zoom;
        Render();
    }

    private void InitializeCells()
    {
        _cells = new GridCell[Rows, Columns];
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
            _cells[row, column] = new GridCell(row, column);
    }

    private void Render() => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _tileWidth = ActualWidth / Columns;
        _tileHeight = ActualHeight / Rows;
        _routeOverlay.Clear();
        foreach (var (id, route) in _routes)
            foreach (var cell in route) _routeOverlay[cell] = Math.Abs(id - 1) % RouteOverlayBrushes.Length;

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(39, 55, 70)), Math.Clamp(Math.Min(_tileWidth, _tileHeight) * .035, .45, 1.2));
        GetVisibleCellBounds(out int firstRow, out int lastRow, out int firstColumn, out int lastColumn);
        for (int r = firstRow; r <= lastRow; r++)
        for (int c = firstColumn; c <= lastColumn; c++)
        {
            var rect = new Rect(c * _tileWidth, r * _tileHeight, _tileWidth, _tileHeight);
            drawingContext.DrawRectangle(TileBrush(r, c), gridPen, rect);
            DrawCellSymbol(drawingContext, r, c, rect);
        }

        foreach (var robot in _fleet.Robots)
        {
            var replayRobot = _replayFrame?.Robots.FirstOrDefault(item => item.Id == robot.Id);
            int renderRow = replayRobot?.Row ?? robot.Row;
            int renderCol = replayRobot?.Column ?? robot.Column;
            if (!Inside(renderRow, renderCol)) continue;
            int color = Math.Abs(robot.Id - 1) % RouteBrushes.Length;
            double size = Math.Max(8, Math.Min(_tileWidth, _tileHeight) * 0.7);
            var center = new Point(renderCol * _tileWidth + _tileWidth / 2, renderRow * _tileHeight + _tileHeight / 2);
            drawingContext.DrawEllipse(RouteBrushes[color], new Pen(Brushes.White, Math.Max(1, size * .06)), center, size / 2, size / 2);
            DrawLabel(drawingContext, robot.Id.ToString(), center, Math.Max(7, size * .34), Brushes.Black, FontWeights.Bold);
            if (robot.Id == SelectedRobotId)
                drawingContext.DrawEllipse(null, new Pen(Brushes.White, Math.Max(1, size * .055)), center, size * .61, size * .61);
        }
        foreach (var (id, route) in _routes)
        {
            int idx = _routeIndexes.GetValueOrDefault(id);
            if (idx >= route.Count) continue;
            var dest = route[^1];
            var center = new Point(dest.Col * _tileWidth + _tileWidth / 2, dest.Row * _tileHeight + _tileHeight / 2);
            drawingContext.DrawEllipse(RouteBrushes[Math.Abs(id - 1) % RouteBrushes.Length], new Pen(Brushes.White, 1), center, Math.Max(3, _tileWidth * .16), Math.Max(3, _tileHeight * .16));
        }
    }

    private void DrawCellSymbol(DrawingContext drawingContext, int row, int col, Rect rect)
    {
        if (Math.Min(rect.Width, rect.Height) < 14) return;
        string? symbol = _cells[row, col].Type switch
        {
            CellType.ChargingStation => "⚡",
            CellType.LoadingDock => "D",
            CellType.PackingStation => "P",
            CellType.Elevator => "⇅",
            CellType.RestrictedZone => "×",
            CellType.OneWayNorth => "↑",
            CellType.OneWayEast => "→",
            CellType.OneWaySouth => "↓",
            CellType.OneWayWest => "←",
            CellType.Crossing => "+",
            _ => null
        };
        if (symbol is null) return;
        DrawLabel(drawingContext, symbol, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
            Math.Min(rect.Width, rect.Height) * .48, Brushes.White, FontWeights.SemiBold);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, Point center, double fontSize, Brush brush, FontWeight weight)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }

    private void GetVisibleCellBounds(out int firstRow, out int lastRow, out int firstColumn, out int lastColumn)
    {
        if (_viewport is null || _viewport.ViewportWidth <= 0 || _viewport.ViewportHeight <= 0)
        { firstRow = 0; lastRow = Rows - 1; firstColumn = 0; lastColumn = Columns - 1; return; }
        firstColumn = Math.Clamp((int)(_viewport.HorizontalOffset / _tileWidth), 0, Columns - 1);
        firstRow = Math.Clamp((int)(_viewport.VerticalOffset / _tileHeight), 0, Rows - 1);
        lastColumn = Math.Clamp((int)Math.Ceiling((_viewport.HorizontalOffset + _viewport.ViewportWidth) / _tileWidth), 0, Columns - 1);
        lastRow = Math.Clamp((int)Math.Ceiling((_viewport.VerticalOffset + _viewport.ViewportHeight) / _tileHeight), 0, Rows - 1);
    }

    private Brush TileBrush(int row, int col)
    {
        if (_congestionHeat[row, col] > 0)
        {
            int bucket = Math.Clamp(_congestionHeat[row, col] / 2, 0, HeatBrushes.Length - 1);
            return HeatBrushes[bucket];
        }
        if (_routeOverlay.TryGetValue((row, col), out int routeColor)) return RouteOverlayBrushes[routeColor];
        return CellBrushes[(int)_cells[row, col].Type];
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void RecordFrame(string eventText)
    {
        _history.Add(new SimulationFrame
        {
            Tick = _tick,
            TimestampUtc = DateTime.UtcNow,
            Event = eventText,
            Robots = _fleet.Robots.Select(robot => new RobotFrame
            {
                Id = robot.Id, Row = robot.Row, Column = robot.Column,
                BatteryLevel = robot.BatteryLevel, Status = robot.Status
            }).ToList()
        });
        if (_history.Count > 5000) _history.RemoveAt(0);
        HistoryChanged?.Invoke(_history.Count);
    }

    private void NotifyStats()
    {
        var selectedRouteLength = _routes.TryGetValue(SelectedRobotId, out var route) ? Math.Max(0, route.Count - 1) : 0;
        StatisticsChanged?.Invoke(_totalSteps, selectedRouteLength);
    }

    private bool Inside(int row, int col) => row >= 0 && row < Rows && col >= 0 && col < Columns;
}
