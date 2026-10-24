
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotFleetSimulation.Models;
using RobotFleetSimulation.Services;
using RobotFleetSimulation.Views;

namespace RobotFleetSimulation;

public partial class MainWindow : Window
{
    private readonly WarehouseGridView _warehouseGrid;
    private readonly TaskQueueService _taskQueue = new();
    private readonly RobotFleetService _robotFleet = new();

    private readonly Dictionary<int, int> _lastRobotSteps = new();

    private RobotTask? _activeTask;

    private double _batteryLevel = 100;

    private const double BatteryUsagePerStep = 1.0;
    private const double LowBatteryThreshold = 20.0;

    private int _lastProcessedSteps;
    private int _nextRobotNumber = 4;

    public MainWindow()
    {
        InitializeComponent();

        // Place robots in separate initial cells.
        _robotFleet.AddRobot("Robot-01", 0, 0);
        _robotFleet.AddRobot("Robot-02", 19, 27);
        _robotFleet.AddRobot("Robot-03", 19, 28);

        _warehouseGrid = new WarehouseGridView(
            SelectionLabel,
            _robotFleet);

        WarehouseHost.Children.Add(_warehouseGrid);

        _warehouseGrid.RobotStatusChanged +=
            HandleRobotStatusChanged;

        _warehouseGrid.StatisticsChanged +=
            HandleStatisticsChanged;

        _warehouseGrid.RobotUpdated +=
            HandleFleetRobotUpdated;

        _warehouseGrid.TaskCompleted +=
            HandleTaskCompleted;

        _warehouseGrid.SetSimulationSpeed(
            SimulationSpeedSlider.Value);

        RefreshTaskQueue();
        RefreshRobotFleet();
    }

    private void HandleRobotStatusChanged(
        string status,
        int row,
        int col)
    {
        Robot? primaryRobot = _robotFleet.GetRobotById(1);

        if (primaryRobot is not null)
        {
            _robotFleet.UpdateRobotStatus(
                primaryRobot.Id,
                ConvertStatus(status));

            if (row >= 0 && col >= 0)
            {
                _robotFleet.UpdateRobotPosition(
                    primaryRobot.Id,
                    row,
                    col);
            }
        }

        ActiveRobotsValue.Text =
            _robotFleet.ActiveRobots.ToString();

        UpdateSelectedRobotDetails();
    }

    private void HandleFleetRobotUpdated(
        int robotId,
        string status,
        int row,
        int col,
        int steps,
        int routeLength)
    {
        Robot? robot = _robotFleet.GetRobotById(robotId);

        if (robot is null)
            return;

        _robotFleet.UpdateRobotPosition(robotId, row, col);
        _robotFleet.UpdateRobotStatus(
            robotId,
            ConvertStatus(status));

        // Robot-01's battery is handled by the statistics event.
        // Other robots consume battery independently.
        if (robotId != 1)
        {
            int previousSteps = _lastRobotSteps.GetValueOrDefault(
                robotId, 0);

            if (steps > previousSteps)
            {
                double newBattery = robot.BatteryLevel -
                    (steps - previousSteps) * BatteryUsagePerStep;

                _robotFleet.UpdateBattery(robotId, newBattery);
            }

            _lastRobotSteps[robotId] = steps;
        }

        RefreshRobotFleet();
    }

    private static RobotStatus ConvertStatus(string status)
    {
        if (status.Contains(
                "NoRoute",
                StringComparison.OrdinalIgnoreCase) ||
            status.Contains(
                "No Route",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.NoRoute;
        }

        if (status.Contains(
                "Moving",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.Moving;
        }

        if (status.Contains(
                "Paused",
                StringComparison.OrdinalIgnoreCase) ||
            status.Contains(
                "Blocked",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.Paused;
        }

        if (status.Contains(
                "Charging",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.Charging;
        }

        if (status.Contains(
                "Completed",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.Completed;
        }

        if (status.Contains(
                "Ready",
                StringComparison.OrdinalIgnoreCase))
        {
            return RobotStatus.Ready;
        }

        return RobotStatus.Idle;
    }

    private void RobotFleetList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        int index = RobotFleetList.SelectedIndex;

        if (index < 0 || index >= _robotFleet.Robots.Count)
            return;

        Robot robot = _robotFleet.Robots[index];

        _warehouseGrid.SelectedRobotId = robot.Id;

        RobotFleetMessageText.Text =
            $"Selected {robot.Name} (ID: {robot.Id}).";

        UpdateSelectedRobotDetails();
    }

    private void UpdateSelectedRobotDetails()
    {
        int index = RobotFleetList.SelectedIndex;

        if (index < 0 || index >= _robotFleet.Robots.Count)
            return;

        Robot robot = _robotFleet.Robots[index];

        RobotPositionText.Text = robot.PositionText;
        RobotStatusText.Text = robot.StatusText;

        BatteryProgressBar.Value = robot.BatteryLevel;
        BatteryPercentageText.Text = robot.BatteryText;

        BatteryPercentageText.Foreground =
            robot.BatteryLevel <= LowBatteryThreshold
                ? Brushes.OrangeRed
                : Brushes.LightGreen;
    }

    private void RefreshRobotFleet()
    {
        int selectedIndex = RobotFleetList.SelectedIndex;

        RobotFleetList.ItemsSource = null;

        RobotFleetList.ItemsSource = _robotFleet.Robots
            .Select(robot =>
                $"{robot.Name} | {robot.PositionText} | " +
                $"{robot.StatusText} | Battery: {robot.BatteryText}")
            .ToList();

        TotalRobotsValue.Text =
            _robotFleet.TotalRobots.ToString();

        if (_robotFleet.Robots.Count > 0)
        {
            if (selectedIndex < 0 ||
                selectedIndex >= _robotFleet.Robots.Count)
            {
                selectedIndex = 0;
            }

            RobotFleetList.SelectedIndex = selectedIndex;

            Robot? selectedRobot =
                _robotFleet.Robots[selectedIndex];

            if (selectedRobot is not null)
                _warehouseGrid.SelectedRobotId = selectedRobot.Id;
        }

        ActiveRobotsValue.Text =
            _robotFleet.ActiveRobots.ToString();

        UpdateSelectedRobotDetails();
    }

    private void AddRobot_Click(
        object sender,
        RoutedEventArgs e)
    {
        int robotNumber = _nextRobotNumber++;

        int row = (robotNumber - 1) / 30;
        int column = (robotNumber - 1) % 30;

        if (row >= 20)
        {
            RobotFleetMessageText.Text =
                "The warehouse has no more initial cells available.";

            return;
        }

        try
        {
            Robot robot = _robotFleet.AddRobot(
                $"Robot-{robotNumber:00}",
                row,
                column);

            RefreshRobotFleet();

            RobotFleetMessageText.Text =
                $"{robot.Name} added at row {row}, column {column}.";
        }
        catch (InvalidOperationException)
        {
            RobotFleetMessageText.Text =
                $"Cannot add a robot at ({row}, {column}): the cell is occupied.";
        }
    }

    private void StartSimulation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_activeTask is not null)
        {
            _warehouseGrid.StartSimulation();
            return;
        }

        if (_taskQueue.PendingCount > 0)
        {
            StartNextPendingTask();
            return;
        }

        _warehouseGrid.StartSimulation();
    }

    private bool StartNextPendingTask()
    {
        RobotTask? task = _taskQueue.GetNextPendingTask();

        if (task is null)
            return false;

        if (!_warehouseGrid.IsValidTaskDestination(
                task.DestinationRow,
                task.DestinationColumn))
        {
            TaskMessageText.Text =
                $"Task #{task.Id} has an invalid or occupied destination.";

            return false;
        }

        if (!_warehouseGrid.SetTaskDestination(
                task.DestinationRow,
                task.DestinationColumn))
        {
            TaskMessageText.Text =
                $"No route exists for task #{task.Id}. Check the walls and destination.";

            return false;
        }

        if (!_taskQueue.MarkInProgress(task.Id))
            return false;

        _activeTask = task;

        RefreshTaskQueue();

        TaskMessageText.Text =
            $"Executing task #{task.Id}: {task.Name}";

        _warehouseGrid.StartSimulation();

        return true;
    }

    private void HandleTaskCompleted()
    {
        if (_activeTask is null)
            return;

        RobotTask completedTask = _activeTask;

        _taskQueue.MarkCompleted(completedTask.Id);
        _activeTask = null;

        RefreshTaskQueue();

        TaskMessageText.Text =
            $"Task #{completedTask.Id} completed!";

        if (_taskQueue.PendingCount > 0)
            StartNextPendingTask();
    }

    private void HandleStatisticsChanged(
        int steps,
        int routeLength)
    {
        StepsTakenText.Text = $"Steps Taken: {steps}";
        RouteLengthText.Text = $"Route Length: {routeLength}";

        if (steps > _lastProcessedSteps)
        {
            int newSteps = steps - _lastProcessedSteps;

            _batteryLevel = Math.Max(
                0,
                _batteryLevel -
                newSteps * BatteryUsagePerStep);
        }

        _lastProcessedSteps = steps;

        Robot? primaryRobot = _robotFleet.GetRobotById(1);

        if (primaryRobot is not null)
        {
            _robotFleet.UpdateBattery(
                primaryRobot.Id,
                _batteryLevel);
        }

        RefreshRobotFleet();

        if (_batteryLevel <= LowBatteryThreshold &&
            _activeTask is not null)
        {
            _warehouseGrid.PauseSimulation();

            TaskMessageText.Text =
                "Low battery! Simulation paused. Recharge before continuing.";
        }
    }

    private void PauseSimulation_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.PauseSimulation();
    }

    private void ResetSimulation_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.ResetSimulation();

        _lastProcessedSteps = 0;

        foreach (int robotId in _lastRobotSteps.Keys.ToList())
            _lastRobotSteps[robotId] = 0;

        if (_activeTask is not null)
        {
            TaskMessageText.Text =
                $"Task #{_activeTask.Id} remains in progress. Press Start to resume.";
        }
    }

    private void WallMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.CurrentMode = EditorMode.Wall;

        SelectionLabel.Text =
            "Mode: Wall — click a cell to toggle a wall.";
    }

    private void RobotStartMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.CurrentMode = EditorMode.RobotStart;

        SelectionLabel.Text =
            "Mode: Robot Start — select Robot-01's starting cell.";
    }

    private void DestinationMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.CurrentMode = EditorMode.Destination;

        SelectionLabel.Text =
            $"Mode: Destination — select a destination for Robot-{_warehouseGrid.SelectedRobotId:00}.";
    }

    private void EraseMode_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.CurrentMode = EditorMode.Erase;

        SelectionLabel.Text =
            "Mode: Erase — click a cell to clear it.";
    }

    private void SimulationSpeedSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_warehouseGrid is null ||
            SimulationSpeedText is null)
        {
            return;
        }

        _warehouseGrid.SetSimulationSpeed(e.NewValue);

        SimulationSpeedText.Text =
            $"{e.NewValue:0} ms per step";
    }

    private void AddTask_Click(
        object sender,
        RoutedEventArgs e)
    {
        string name = TaskNameInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            TaskMessageText.Text =
                "Please enter a task name.";

            return;
        }

        if (!int.TryParse(TaskRowInput.Text, out int row) ||
            !int.TryParse(TaskColumnInput.Text, out int column))
        {
            TaskMessageText.Text =
                "Row and column must be whole numbers.";

            return;
        }

        if (row < 0 || row >= 20 ||
            column < 0 || column >= 30)
        {
            TaskMessageText.Text =
                "Destination must be within rows 0–19 and columns 0–29.";

            return;
        }

        if (!_warehouseGrid.IsValidTaskDestination(row, column))
        {
            TaskMessageText.Text =
                "That destination is blocked by a wall or another robot.";

            return;
        }

        RobotTask task = _taskQueue.AddTask(
            name, row, column);

        RefreshTaskQueue();

        TaskMessageText.Text =
            $"Task #{task.Id} added for destination ({row}, {column}).";
    }

    private void RefreshTaskQueue()
    {
        TaskList.ItemsSource = null;

        TaskList.ItemsSource = _taskQueue.Tasks
            .Select(task =>
                $"#{task.Id} | {task.Name} | " +
                $"Destination: ({task.DestinationRow}, {task.DestinationColumn}) | " +
                $"Status: {task.StatusText}")
            .ToList();

        PendingTasksValue.Text =
            _taskQueue.PendingCount.ToString();

        InProgressTasksValue.Text =
            _taskQueue.InProgressCount.ToString();

        CompletedTasksValue.Text =
            _taskQueue.CompletedCount.ToString();

        RefreshRobotFleet();
    }

    private void RechargeBattery_Click(
        object sender,
        RoutedEventArgs e)
    {
        _warehouseGrid.PauseSimulation();

        _batteryLevel = 100;
        _lastProcessedSteps = 0;

        Robot? primaryRobot = _robotFleet.GetRobotById(1);

        if (primaryRobot is not null)
            _robotFleet.UpdateBattery(primaryRobot.Id, 100);

        RefreshRobotFleet();

        TaskMessageText.Text =
            "Robot-01 battery recharged to 100%. Press Start to continue.";
    }
}
