using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;
using RobotFleetSimulation.Views;

namespace RobotFleetSimulation;

public partial class MainWindow : Window
{
    private readonly RobotFleetService _robotFleet = new();
    private readonly TaskQueueService _taskQueue = new();
    private readonly ScenarioService _scenarioService = new();
    private readonly WarehouseGridView _warehouseGrid;
    private int _nextRobotNumber = 4;
    private bool _simulationStarted;
    private int _completedTasks;
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private LocalApiService? _apiService;
    private bool _updatingReplaySlider;
    private bool _refreshingFleet;

    public MainWindow()
    {
        InitializeComponent();
        _robotFleet.AddRobot("Robot-01", 0, 0);
        var robot2 = _robotFleet.AddRobot("Robot-02", 19, 27);
        robot2.SpeedCellsPerTick = 1.25; robot2.PayloadCapacityKg = 45; robot2.BatteryCapacity = 120; robot2.BatteryLevel = 120;
        var robot3 = _robotFleet.AddRobot("Robot-03", 19, 28);
        robot3.SpeedCellsPerTick = .75; robot3.PayloadCapacityKg = 15; robot3.BatteryCapacity = 80; robot3.BatteryLevel = 80;
        _warehouseGrid = new WarehouseGridView(SelectionLabel, _robotFleet);
        WarehouseHost.Children.Add(_warehouseGrid);
        _warehouseGrid.AttachViewport(MapScroller);
        BuildDefaultWarehouse();
        _warehouseGrid.FleetChanged += RefreshRobotFleet;
        _warehouseGrid.StatisticsChanged += HandleStatisticsChanged;
        _warehouseGrid.RobotTaskCompleted += HandleTaskCompleted;
        _warehouseGrid.HistoryChanged += UpdateReplaySlider;
        _warehouseGrid.SetSimulationSpeed(SimulationSpeedSlider.Value);
        PresetPicker.ItemsSource = WarehousePresets.Names;
        DispatchStrategyPicker.ItemsSource = Enum.GetNames<DispatchStrategy>().Select(name => name switch
        {
            nameof(DispatchStrategy.PriorityAndNearest) => "Priority + nearest",
            nameof(DispatchStrategy.NearestTask) => "Nearest task",
            nameof(DispatchStrategy.HighestBattery) => "Highest battery",
            _ => "Balanced workload"
        }).ToList();
        MapScroller.ScrollToHorizontalOffset(0);
        _autosaveTimer.Tick += (_, _) => SaveAutosave();
        _autosaveTimer.Start();
        Closing += (_, _) => { _autosaveTimer.Stop(); SaveAutosave(); _apiService?.Dispose(); };
        try { _apiService = new LocalApiService(HandleApiRequest); _apiService.Start(); ApiStatusText.Text = "API online · 127.0.0.1:5084"; }
        catch (HttpListenerException) { ApiStatusText.Text = "API unavailable · port 5084 is in use"; }
        RefreshTaskQueue();
        RefreshRobotFleet();
        LoadSelectedRobotInputs();
        LoadAutosaveIfPresent();
    }

    private void BuildDefaultWarehouse()
    {
        // A recognizable rack-and-aisle layout with marked charging bays.
        for (int row = 3; row < _warehouseGrid.Rows - 3; row++)
        {
            for (int col = 4; col < _warehouseGrid.Columns - 3; col++)
                if (col % 5 is 4 or 0) _warehouseGrid.SetLayoutCell(row, col, CellType.Shelf);
            for (int col = 8; col < _warehouseGrid.Columns - 1; col += 5)
                _warehouseGrid.SetLayoutCell(row, col, CellType.SlowZone);
        }
        _warehouseGrid.SetLayoutCell(_warehouseGrid.Rows - 2, 1, CellType.ChargingStation);
        _warehouseGrid.SetLayoutCell(_warehouseGrid.Rows - 2, _warehouseGrid.Columns - 2, CellType.ChargingStation);
        _warehouseGrid.SetLayoutCell(1, _warehouseGrid.Columns - 2, CellType.LoadingDock);
        _warehouseGrid.SetLayoutCell(_warehouseGrid.Rows / 2, _warehouseGrid.Columns - 2, CellType.PackingStation);
        _warehouseGrid.SetLayoutCell(_warehouseGrid.Rows / 2, _warehouseGrid.Columns / 2, CellType.Elevator);
        _warehouseGrid.RefreshView();
    }

    private void RefreshRobotFleet()
    {
        if (RobotFleetList is null) return;
        int selected = RobotFleetList.SelectedIndex;
        _refreshingFleet = true;
        RobotFleetList.ItemsSource = null;
        RobotFleetList.ItemsSource = _robotFleet.Robots.Select(robot =>
            $"{robot.Name} | ({robot.Row},{robot.Column}) | {robot.StatusText} | {robot.BatteryText}").ToList();
        TotalRobotsValue.Text = _robotFleet.TotalRobots.ToString();
        ActiveRobotsValue.Text = _robotFleet.ActiveRobots.ToString();
        if (_robotFleet.Robots.Count > 0)
        {
            RobotFleetList.SelectedIndex = Math.Clamp(selected, 0, _robotFleet.Robots.Count - 1);
            _warehouseGrid.SelectedRobotId = _robotFleet.Robots[RobotFleetList.SelectedIndex].Id;
            UpdateSelectedRobotDetails();
        }
        _refreshingFleet = false;
    }

    private void UpdateSelectedRobotDetails()
    {
        if (RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        _warehouseGrid.SelectedRobotId = robot.Id;
        RobotPositionText.Text = robot.PositionText;
        SelectedRobotNameText.Text = robot.Name;
        RobotStatusText.Text = robot.StatusText;
        RobotStatusText.Foreground = robot.Status switch
        {
            RobotStatus.Moving => Brushes.DeepSkyBlue,
            RobotStatus.Paused => Brushes.Orange,
            RobotStatus.Charging => Brushes.MediumPurple,
            RobotStatus.Completed => Brushes.LightGreen,
            RobotStatus.NoRoute => Brushes.OrangeRed,
            _ => Brushes.LightGray
        };
        RobotCapabilityText.Text = $"Speed {robot.SpeedCellsPerTick:0.##} cells/tick · Payload {robot.PayloadKg:0.##}/{robot.PayloadCapacityKg:0.##} kg";
        BatteryProgressBar.Maximum = robot.BatteryCapacity;
        BatteryProgressBar.Value = robot.BatteryLevel;
        BatteryPercentageText.Text = robot.BatteryText;
        BatteryPercentageText.Foreground = robot.BatteryLevel / Math.Max(1, robot.BatteryCapacity) <= .2 ? Brushes.OrangeRed : Brushes.LightGreen;
        UpdateBatteryChart();
    }

    private void RobotFleetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingFleet) return;
        if (RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        _warehouseGrid.SelectedRobotId = robot.Id;
        RobotFleetMessageText.Text = $"Selected {robot.Name} (ID {robot.Id}).";
        UpdateSelectedRobotDetails();
        LoadSelectedRobotInputs();
    }

    private void LoadSelectedRobotInputs()
    {
        if (RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        RobotSpeedInput.Text = robot.SpeedCellsPerTick.ToString("0.##");
        RobotPayloadCapacityInput.Text = robot.PayloadCapacityKg.ToString("0.##");
        RobotBatteryCapacityInput.Text = robot.BatteryCapacity.ToString("0.##");
    }

    private void AddRobot_Click(object sender, RoutedEventArgs e)
    {
        for (int n = 0; n < _warehouseGrid.Rows * _warehouseGrid.Columns; n++)
        {
            int index = (_nextRobotNumber - 1 + n) % (_warehouseGrid.Rows * _warehouseGrid.Columns);
            int row = index / _warehouseGrid.Columns, col = index % _warehouseGrid.Columns;
            if (!_warehouseGrid.IsWalkable(row, col) || _robotFleet.IsCellOccupied(row, col)) continue;
            var robot = _robotFleet.AddRobot($"Robot-{_nextRobotNumber++:00}", row, col);
            RefreshRobotFleet();
            RobotFleetMessageText.Text = $"{robot.Name} added at ({row}, {col}).";
            _warehouseGrid.RefreshView();
            return;
        }
        RobotFleetMessageText.Text = "No free walkable cells remain.";
    }

    private void StartSimulation_Click(object sender, RoutedEventArgs e)
    {
        AssignPendingTasks();
        _simulationStarted = true;
        _warehouseGrid.StartSimulation();
    }

    private void PauseSimulation_Click(object sender, RoutedEventArgs e) => _warehouseGrid.PauseSimulation();

    private void ResetSimulation_Click(object sender, RoutedEventArgs e)
    {
        _simulationStarted = false;
        _warehouseGrid.ResetSimulation();
        TaskMessageText.Text = "Simulation reset. Assigned tasks remain available to resume.";
    }

    private void StepSimulation_Click(object sender, RoutedEventArgs e)
    {
        AssignPendingTasks();
        _warehouseGrid.StepOnce();
    }

    private void AssignPendingTasks()
    {
        while (_taskQueue.PendingCount > 0)
        {
            var strategy = (DispatchStrategy)Math.Clamp(DispatchStrategyPicker.SelectedIndex, 0, Enum.GetValues<DispatchStrategy>().Length - 1);
            var available = _robotFleet.Robots
                .Where(r => r.AssignedTaskId is null && r.BatteryLevel / Math.Max(1, r.BatteryCapacity) > .05 &&
                    !_warehouseGrid.HasActiveRoute(r.Id) &&
                    (r.Status is RobotStatus.Idle or RobotStatus.Completed))
                .ToList();
            RobotTask? assignedTask = null;
            Robot? assignedRobot = null;
            var tasks = _taskQueue.GetPendingTasks().Select(task => new
            {
                Task = task,
                Candidates = available.Select(robot => (Robot: robot,
                        Length: robot.PayloadCapacityKg >= task.PayloadWeightKg
                            ? _warehouseGrid.GetRouteLength(robot.Id, task)
                            : int.MaxValue))
                    .Where(item => item.Length < int.MaxValue)
                    .ToList()
            }).Where(item => item.Candidates.Count > 0);
            if (strategy == DispatchStrategy.NearestTask)
                tasks = tasks.OrderBy(item => item.Candidates.Min(candidate => candidate.Length))
                    .ThenByDescending(item => item.Task.Priority);
            foreach (var item in tasks)
            {
                var candidates = item.Candidates.AsEnumerable();
                candidates = strategy switch
                {
                    DispatchStrategy.HighestBattery => candidates.OrderByDescending(candidate => candidate.Robot.BatteryLevel / Math.Max(1, candidate.Robot.BatteryCapacity)),
                    DispatchStrategy.BalancedWorkload => candidates.OrderBy(candidate => candidate.Robot.TotalSteps),
                    _ => candidates.OrderBy(candidate => candidate.Length)
                };
                var candidate = candidates
                    .FirstOrDefault();
                if (candidate.Robot is null) continue;
                assignedTask = item.Task;
                assignedRobot = candidate.Robot;
                break;
            }
            if (assignedTask is null || assignedRobot is null)
            {
                var next = _taskQueue.GetNextPendingTask();
                if (next is not null) TaskMessageText.Text = $"Task #{next.Id} is waiting: no available robot has a reachable route.";
                return;
            }
            if (!_warehouseGrid.AssignTask(assignedRobot.Id, assignedTask.DestinationRow, assignedTask.DestinationColumn,
                    assignedTask.Id, assignedTask.Stops, assignedTask.PayloadWeightKg, assignedTask.Type)) return;
            assignedTask.AssignedRobotId = assignedRobot.Id;
            _taskQueue.MarkInProgress(assignedTask.Id);
        }
        RefreshTaskQueue();
    }

    private void HandleTaskCompleted(int robotId, int taskId)
    {
        _taskQueue.MarkCompleted(taskId);
        _completedTasks++;
        TaskMessageText.Text = $"Robot-{robotId:00} completed task #{taskId}.";
        RefreshTaskQueue();
        if (_simulationStarted) AssignPendingTasks();
        if (_robotFleet.Robots.Any(r => r.Status is RobotStatus.Ready or RobotStatus.Moving or RobotStatus.Charging))
            _warehouseGrid.StartSimulation();
    }

    private void HandleStatisticsChanged(int totalSteps, int selectedRouteLength)
    {
        ExpireOverdueTasks();
        StepsTakenText.Text = $"Fleet steps: {totalSteps}";
        RouteLengthText.Text = $"Selected route: {selectedRouteLength}";
        FleetMetricsText.Text = $"Steps {totalSteps} · Battery used {_warehouseGrid.BatteryConsumed:0.0}% · Congestion waits {_warehouseGrid.CongestionWaits} · Idle ticks {_warehouseGrid.IdleRobotTicks}";
        RefreshRobotFleet();
    }

    private void SetMode(EditorMode mode, string description)
    {
        _warehouseGrid.CurrentMode = mode;
        SelectionLabel.Text = description;
    }

    private void WallMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.Wall, "Wall tool — click to add or remove a barrier.");
    private void RobotStartMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.RobotStart, "Robot placement — select a robot, then click an open cell.");
    private void DestinationMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.Destination, "Route tool — select a robot, then click its destination.");
    private void EraseMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.Erase, "Eraser — click a shelf, charger, slow zone, or wall.");
    private void ShelfMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.Shelf, "Shelf tool — click to place or remove storage racks.");
    private void ChargingMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.ChargingStation, "Charge pad — place charging stations for automatic low-battery recovery.");
    private void SlowZoneMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.SlowZone, "Slow zone — mark cells with extra battery use.");
    private void DockMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.LoadingDock, "Loading dock — mark a pickup/dropoff location.");
    private void PackingMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.PackingStation, "Packing station — mark a processing location.");
    private void ElevatorMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.Elevator, "Elevator — mark a vertical transfer point.");
    private void RestrictedMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.RestrictedZone, "Restricted area — robots cannot enter these cells.");
    private void OneWayNorthMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.OneWayNorth, "One-way aisle — northbound only.");
    private void OneWayEastMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.OneWayEast, "One-way aisle — eastbound only.");
    private void OneWaySouthMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.OneWaySouth, "One-way aisle — southbound only.");
    private void OneWayWestMode_Click(object sender, RoutedEventArgs e) => SetMode(EditorMode.OneWayWest, "One-way aisle — westbound only.");
    private void UndoMap_Click(object sender, RoutedEventArgs e) => SelectionLabel.Text = _warehouseGrid.UndoMapEdit() ? "Undid the last map edit." : "No map edit to undo.";
    private void RedoMap_Click(object sender, RoutedEventArgs e) => SelectionLabel.Text = _warehouseGrid.RedoMapEdit() ? "Redid the map edit." : "No map edit to redo.";

    private void PauseSelectedRobot_Click(object sender, RoutedEventArgs e) => _warehouseGrid.PauseRobot(_warehouseGrid.SelectedRobotId);
    private void ResumeSelectedRobot_Click(object sender, RoutedEventArgs e) => _warehouseGrid.ResumeRobot(_warehouseGrid.SelectedRobotId);

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        string name = PresetPicker.SelectedItem?.ToString() ?? WarehousePresets.Names[0];
        try
        {
            var scenario = WarehousePresets.Create(name);
            _warehouseGrid.LoadScenario(scenario);
            _taskQueue.ReplaceAll([]);
            _simulationStarted = false;
            MapDimensionsText.Text = $"Fixed 60 × 80 · 4,800 cells";
            _nextRobotNumber = scenario.Robots.Count + 1;
            RefreshTaskQueue(); RefreshRobotFleet(); LoadSelectedRobotInputs();
            TaskMessageText.Text = $"Loaded the {name} preset.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Preset failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ApplyRobotCapabilities_Click(object sender, RoutedEventArgs e)
    {
        if (RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        if (!double.TryParse(RobotSpeedInput.Text, out double speed) || speed is < .25 or > 3 ||
            !double.TryParse(RobotPayloadCapacityInput.Text, out double capacity) || capacity <= 0 ||
            !double.TryParse(RobotBatteryCapacityInput.Text, out double batteryCapacity) || batteryCapacity <= 0)
        { RobotFleetMessageText.Text = "Enter speed 0.25–3 and positive payload/battery capacities."; return; }
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        if (robot.AssignedTaskId is not null) { RobotFleetMessageText.Text = "Pause or complete the robot's task before changing capabilities."; return; }
        if (robot.PayloadKg > capacity) { RobotFleetMessageText.Text = "Payload capacity cannot be below the robot's current load."; return; }
        robot.SpeedCellsPerTick = speed; robot.PayloadCapacityKg = capacity;
        robot.BatteryCapacity = batteryCapacity; robot.BatteryLevel = Math.Min(robot.BatteryLevel, batteryCapacity);
        RefreshRobotFleet(); RobotFleetMessageText.Text = $"Updated {robot.Name}: speed {speed:0.##}, payload {capacity:0.##} kg.";
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_warehouseGrid is null) return;
        _warehouseGrid.SetZoom(e.NewValue / 100d);
        if (ZoomPercentText is not null) ZoomPercentText.Text = $"{e.NewValue:0}%";
    }

    private void ReplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_warehouseGrid is null || _updatingReplaySlider || _warehouseGrid.HistoryFrameCount == 0) return;
        _warehouseGrid.ShowReplayFrame((int)e.NewValue);
    }

    private void UpdateReplaySlider(int count)
    {
        _updatingReplaySlider = true;
        ReplaySlider.Maximum = Math.Max(0, count - 1);
        ReplaySlider.Value = ReplaySlider.Maximum;
        _updatingReplaySlider = false;
        EventLogList.ItemsSource = _warehouseGrid.History.TakeLast(60)
            .Select(frame => $"Tick {frame.Tick} · {frame.Event} · {frame.TimestampUtc.ToLocalTime():HH:mm:ss}").Reverse().ToList();
        UpdateBatteryChart();
    }

    private void BatteryChart_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBatteryChart();

    private void UpdateBatteryChart()
    {
        if (_warehouseGrid is null || BatteryChart is null || BatteryChart.ActualWidth <= 1 || BatteryChart.ActualHeight <= 1 ||
            RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        var values = _warehouseGrid.History.TakeLast(120)
            .Select(frame => frame.Robots.FirstOrDefault(item => item.Id == robot.Id)?.BatteryLevel)
            .Where(value => value.HasValue).Select(value => value!.Value).ToList();
        BatteryChart.Children.Clear();
        if (values.Count < 2) return;
        double width = BatteryChart.ActualWidth - 8, height = BatteryChart.ActualHeight - 8;
        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++)
        {
            double ratio = values[i] / Math.Max(1, robot.BatteryCapacity);
            points.Add(new Point(4 + width * i / (values.Count - 1), 4 + height * (1 - ratio)));
        }
        BatteryChart.Children.Add(new Polyline { Points = points, Stroke = Brushes.Cyan, StrokeThickness = 2 });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z) { _warehouseGrid.UndoMapEdit(); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y) { _warehouseGrid.RedoMapEdit(); e.Handled = true; }
        else if (e.Key == Key.F5) { StartSimulation_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { PauseSimulation_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { StepSimulation_Click(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void SimulationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_warehouseGrid is null || SimulationSpeedText is null) return;
        _warehouseGrid.SetSimulationSpeed(e.NewValue);
        SimulationSpeedText.Text = $"{e.NewValue:0} ms per step";
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        string name = TaskNameInput.Text.Trim();
        if (name.Length == 0) { TaskMessageText.Text = "Enter a task name."; return; }
        if (!int.TryParse(TaskRowInput.Text, out int row) || !int.TryParse(TaskColumnInput.Text, out int col) ||
            row < 0 || row >= _warehouseGrid.Rows || col < 0 || col >= _warehouseGrid.Columns)
        { TaskMessageText.Text = $"Enter coordinates inside the {_warehouseGrid.Rows} × {_warehouseGrid.Columns} map."; return; }
        if (!_warehouseGrid.IsValidTaskDestination(row, col)) { TaskMessageText.Text = "That destination is blocked or occupied."; return; }
        int priority = (TaskPriorityInput.SelectedIndex + 1).Clamp(1, 5);
        int deadlineMinutes = int.TryParse(TaskDeadlineInput.Text, out int minutes) ? Math.Max(0, minutes) : 0;
        DateTime? deadline = deadlineMinutes > 0 ? DateTime.UtcNow.AddMinutes(deadlineMinutes) : null;
        if (!double.TryParse(TaskPayloadInput.Text, out double payload) || payload < 0)
        { TaskMessageText.Text = "Payload weight must be zero or a positive number."; return; }
        var stops = new List<TaskStop>();
        if (!string.IsNullOrWhiteSpace(TaskStopsInput.Text))
        {
            foreach (string raw in TaskStopsInput.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] point = raw.Split(',', StringSplitOptions.TrimEntries);
                if (point.Length != 2 || !int.TryParse(point[0], out int stopRow) || !int.TryParse(point[1], out int stopCol) ||
                    !_warehouseGrid.IsValidTaskDestination(stopRow, stopCol))
                { TaskMessageText.Text = $"Invalid intermediate stop '{raw}'. Use available row,column coordinates."; return; }
                stops.Add(new TaskStop { Row = stopRow, Column = stopCol, Action = "Visit" });
            }
        }
        var type = (RobotTaskType)Math.Clamp(TaskTypeInput.SelectedIndex, 0, Enum.GetValues<RobotTaskType>().Length - 1);
        if (type == RobotTaskType.Charge && _warehouseGrid.GetCellType(row, col) != CellType.ChargingStation)
        { TaskMessageText.Text = "Charge tasks must target a charging pad."; return; }
        var task = _taskQueue.AddTask(name, row, col, priority, deadline, type, payload, stops);
        RefreshTaskQueue();
        TaskMessageText.Text = $"Task #{task.Id} added (priority {priority}, {type}, {stops.Count + 1} stop(s)).";
        if (_simulationStarted) AssignPendingTasks();
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        if (TaskList.SelectedIndex < 0 || TaskList.SelectedIndex >= _taskQueue.Tasks.Count) return;
        var task = _taskQueue.Tasks[TaskList.SelectedIndex];
        if (!_taskQueue.Cancel(task.Id)) { TaskMessageText.Text = "Completed task cannot be cancelled."; return; }
        if (task.AssignedRobotId is int robotId) _warehouseGrid.CancelAssignment(robotId);
        TaskMessageText.Text = $"Task #{task.Id} cancelled.";
        RefreshTaskQueue();
        if (_simulationStarted) AssignPendingTasks();
    }

    private void ReassignTask_Click(object sender, RoutedEventArgs e)
    {
        if (TaskList.SelectedIndex < 0 || TaskList.SelectedIndex >= _taskQueue.Tasks.Count) return;
        var task = _taskQueue.Tasks[TaskList.SelectedIndex];
        if (task.Status != RobotTaskStatus.InProgress || task.AssignedRobotId is not int robotId)
        { TaskMessageText.Text = "Select an in-progress task to reassign."; return; }
        _warehouseGrid.CancelAssignment(robotId);
        _taskQueue.Requeue(task.Id);
        RefreshTaskQueue();
        AssignPendingTasks();
    }

    private void ExpireOverdueTasks()
    {
        bool changed = false;
        foreach (var task in _taskQueue.Tasks.Where(t =>
                     (t.Status is RobotTaskStatus.Pending or RobotTaskStatus.InProgress) &&
                     t.DeadlineUtc is DateTime deadline && deadline <= DateTime.UtcNow).ToList())
        {
            _taskQueue.MarkFailed(task.Id);
            if (task.AssignedRobotId is int robotId) _warehouseGrid.CancelAssignment(robotId);
            TaskMessageText.Text = $"Task #{task.Id} missed its deadline and was marked failed.";
            changed = true;
        }
        if (changed) RefreshTaskQueue();
    }

    private void RechargeBattery_Click(object sender, RoutedEventArgs e)
    {
        if (RobotFleetList.SelectedIndex < 0 || RobotFleetList.SelectedIndex >= _robotFleet.Robots.Count) return;
        var robot = _robotFleet.Robots[RobotFleetList.SelectedIndex];
        _robotFleet.UpdateBattery(robot.Id, robot.BatteryCapacity);
        if (robot.Status == RobotStatus.Paused) robot.Status = RobotStatus.Ready;
        RefreshRobotFleet();
        TaskMessageText.Text = $"{robot.Name} recharged to 100%.";
    }

    private void SaveScenario_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Robot fleet scenario (*.json)|*.json", FileName = "warehouse-scenario.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { _scenarioService.Save(dialog.FileName, _warehouseGrid.CreateScenario(_taskQueue.Tasks)); TaskMessageText.Text = "Scenario saved."; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void LoadScenario_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Robot fleet scenario (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var scenario = _scenarioService.Load(dialog.FileName);
            RestoreScenario(scenario);
            _completedTasks = scenario.Tasks.Count(t => t.Status == RobotTaskStatus.Completed);
            TaskMessageText.Text = "Scenario loaded. In-progress tasks are queued to resume.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV report (*.csv)|*.csv", FileName = "fleet-report.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new List<string>
        {
            "Category,Name,Status,Steps,BatteryPercent,Priority,Details"
        };
        string summary = $"Completed tasks: {_taskQueue.CompletedCount}; failed: {_taskQueue.Tasks.Count(t => t.Status == RobotTaskStatus.Failed)}; congestion waits: {_warehouseGrid.CongestionWaits}; idle time: {_warehouseGrid.IdleTimeSeconds:0.0}s; battery consumed: {_warehouseGrid.BatteryConsumed:0.0}%";
        double averageBattery = _robotFleet.Robots.Count == 0 ? 0 : _robotFleet.Robots.Average(r => r.BatteryLevel);
        lines.Add($"Summary,Fleet,,{_robotFleet.Robots.Sum(r => r.TotalSteps)},{averageBattery:0.0},,{Csv(summary)}");
        lines.AddRange(_robotFleet.Robots.Select(r => $"Robot,{Csv(r.Name)},{r.Status},{r.TotalSteps},{r.BatteryLevel:0.0},,{Csv(r.PositionText)}"));
        lines.AddRange(_taskQueue.Tasks.Select(t =>
        {
            string detail = $"{t.Type}; destination ({t.DestinationRow},{t.DestinationColumn}); stops {t.Stops.Count}; payload {t.PayloadWeightKg:0.##} kg";
            return $"Task,{Csv(t.Name)},{t.Status},,,{t.Priority},{Csv(detail)}";
        }));
        lines.AddRange(_warehouseGrid.History.Select(frame => $"History,{Csv("Tick " + frame.Tick)},{Csv(frame.Event)},{frame.Tick},,,{Csv(frame.TimestampUtc.ToString("O"))}"));
        try { File.WriteAllLines(dialog.FileName, lines); TaskMessageText.Text = "Fleet metrics report exported."; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private string AutosavePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RobotFleetSimulation", "autosave.json");

    private void SaveAutosave()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AutosavePath)!);
            _scenarioService.Save(AutosavePath, _warehouseGrid.CreateScenario(_taskQueue.Tasks));
        }
        catch { /* Autosave must not interrupt the running simulation. */ }
    }

    private void LoadAutosaveIfPresent()
    {
        if (!File.Exists(AutosavePath)) return;
        try
        {
            var scenario = _scenarioService.Load(AutosavePath);
            RestoreScenario(scenario);
            TaskMessageText.Text = "Restored the latest autosaved warehouse.";
        }
        catch (Exception ex) { TaskMessageText.Text = $"Autosave could not be restored: {ex.Message}"; }
    }

    private void RestoreScenario(WarehouseScenario scenario)
    {
        _warehouseGrid.LoadScenario(scenario);
        foreach (var task in scenario.Tasks)
        {
            if (task.Status == RobotTaskStatus.InProgress)
            { task.Status = RobotTaskStatus.Pending; task.AssignedRobotId = null; }
        }
        foreach (var robot in scenario.Robots)
        {
            robot.AssignedTaskId = null;
            if (robot.Status is RobotStatus.Moving or RobotStatus.Paused or RobotStatus.Charging) robot.Status = RobotStatus.Idle;
        }
        _taskQueue.ReplaceAll(scenario.Tasks);
        MapDimensionsText.Text = $"Fixed 60 × 80 · 4,800 cells";
        _nextRobotNumber = Math.Max(1, scenario.Robots.Count + 1);
        _simulationStarted = false;
        RefreshTaskQueue(); RefreshRobotFleet();
        LoadSelectedRobotInputs();
    }

    private string HandleApiRequest(string method, string path, string body) => Dispatcher.Invoke(() =>
    {
        try
        {
            if (method == "GET" && path == "/api/status")
                return JsonSerializer.Serialize(new
                {
                    rows = _warehouseGrid.Rows, columns = _warehouseGrid.Columns,
                    robots = _robotFleet.Robots, tasks = _taskQueue.Tasks,
                    metrics = new { steps = _robotFleet.Robots.Sum(r => r.TotalSteps), congestionWaits = _warehouseGrid.CongestionWaits,
                        batteryConsumed = _warehouseGrid.BatteryConsumed, completedTasks = _taskQueue.CompletedCount },
                    historyFrames = _warehouseGrid.HistoryFrameCount
                });

            if (method == "POST" && path == "/api/tasks")
            {
                var request = JsonSerializer.Deserialize<ApiTaskRequest>(body, new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                });
                if (request is null || !_warehouseGrid.IsValidTaskDestination(request.Row, request.Column) ||
                    !Enum.IsDefined(request.Type) || request.Priority is < 1 or > 5 ||
                    request.PayloadWeightKg < 0 || double.IsNaN(request.PayloadWeightKg) || double.IsInfinity(request.PayloadWeightKg) ||
                    request.DeadlineMinutes < 0 || request.DeadlineMinutes > 525600)
                    return JsonSerializer.Serialize(new { ok = false, error = "Invalid or blocked destination." });
                if (request.Type == RobotTaskType.Charge && _warehouseGrid.GetCellType(request.Row, request.Column) != CellType.ChargingStation)
                    return JsonSerializer.Serialize(new { ok = false, error = "Charge tasks must target a charging station." });
                var task = _taskQueue.AddTask(request.Name ?? "API task", request.Row, request.Column,
                    request.Priority, request.DeadlineMinutes > 0 ? DateTime.UtcNow.AddMinutes(request.DeadlineMinutes) : null,
                    request.Type, request.PayloadWeightKg);
                if (_simulationStarted) AssignPendingTasks();
                RefreshTaskQueue();
                return JsonSerializer.Serialize(new { ok = true, taskId = task.Id });
            }

            if (method == "POST" && path == "/api/commands")
            {
                var request = JsonSerializer.Deserialize<ApiCommandRequest>(body);
                switch (request?.Command?.ToLowerInvariant())
                {
                    case "start": StartSimulation_Click(this, new RoutedEventArgs()); break;
                    case "pause": PauseSimulation_Click(this, new RoutedEventArgs()); break;
                    case "step": StepSimulation_Click(this, new RoutedEventArgs()); break;
                    default: return JsonSerializer.Serialize(new { ok = false, error = "Use start, pause, or step." });
                }
                return JsonSerializer.Serialize(new { ok = true });
            }

            if (method == "POST" && path.StartsWith("/api/robots/", StringComparison.Ordinal))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var request = JsonSerializer.Deserialize<ApiCommandRequest>(body);
                if (parts.Length == 4 && int.TryParse(parts[2], out int robotId))
                {
                    string command = request?.Command?.ToLowerInvariant() ?? "";
                    if (command == "pause") _warehouseGrid.PauseRobot(robotId);
                    else if (command == "resume") _warehouseGrid.ResumeRobot(robotId);
                    else if (command == "recharge" && _robotFleet.GetRobotById(robotId) is Robot robot)
                    { _robotFleet.UpdateBattery(robotId, robot.BatteryCapacity); RefreshRobotFleet(); }
                    else return JsonSerializer.Serialize(new { ok = false, error = "Use pause, resume, or recharge." });
                    return JsonSerializer.Serialize(new { ok = true });
                }
            }
            return JsonSerializer.Serialize(new { ok = false, error = "Unknown endpoint." });
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { ok = false, error = ex.Message }); }
    });

    private sealed class ApiCommandRequest { public string? Command { get; set; } }
    private sealed class ApiTaskRequest
    {
        public string? Name { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int Priority { get; set; } = 1;
        public int DeadlineMinutes { get; set; }
        public double PayloadWeightKg { get; set; }
        public RobotTaskType Type { get; set; } = RobotTaskType.Delivery;
    }

    private void RefreshTaskQueue()
    {
        TaskList.ItemsSource = null;
        TaskList.ItemsSource = _taskQueue.Tasks.Select(task =>
            $"#{task.Id} | P{task.Priority} | {task.Type} | {task.Name} | ({task.DestinationRow},{task.DestinationColumn}) | {task.StatusText}" +
            (task.AssignedRobotId is int id ? $" | Robot-{id:00}" : "") +
            (task.DeadlineUtc is DateTime deadline ? $" | Due {deadline.ToLocalTime():HH:mm}" : "")).ToList();
        PendingTasksValue.Text = _taskQueue.PendingCount.ToString();
        InProgressTasksValue.Text = _taskQueue.InProgressCount.ToString();
        CompletedTasksValue.Text = _taskQueue.CompletedCount.ToString();
    }
}

internal static class NumericExtensions
{
    public static int Clamp(this int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}
