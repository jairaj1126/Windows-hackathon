# Robot Fleet Simulation

A Windows desktop warehouse simulator built with C# and WPF on .NET 10. It provides a 20 × 30 grid, a simple robot fleet dashboard, a task queue, and animated grid movement using four-direction A* pathfinding.

## Features

- Edit the warehouse grid by placing/removing walls, setting Robot-01's start, choosing destinations, and erasing cells.
- Find a shortest route around walls with Manhattan-distance A* pathfinding.
- Start, pause, and reset the simulation; adjust the movement interval.
- Add robots, view their positions/status/battery, and create destination tasks processed in queue order.
- Track task counts, route length, steps, and battery level. Robot-01's battery can be recharged from the dashboard.

## Requirements

- Windows (the application uses WPF).
- .NET 10 SDK, matching the `net10.0-windows` target in `RobotFleetSimulation.csproj`.

## Run

From this folder, run:

```powershell
dotnet run --project .\RobotFleetSimulation.csproj
```

Or build it with:

```powershell
dotnet build .\RobotFleetSimulation.csproj
```

## How to use

1. Use **Robot Start** mode and click a cell to set Robot-01's start position.
2. Use **Wall** mode to toggle obstacles. The start and destination markers must be erased before placing a wall on those cells.
3. Use **Destination** mode to click a destination, or enter a task name and row/column and select **Add Task**.
4. Select **Start** to run the current route or begin the next pending task. Use **Pause**, **Reset**, and **Recharge Battery** as needed.
5. Select a robot in the fleet list to view its details. The current simulation movement engine is for Robot-01.

Grid coordinates are zero-based: rows 0–19 and columns 0–29. Movement is orthogonal (up, down, left, right). A cell occupied by a different robot is treated as blocked.

## Project layout

- `MainWindow.xaml` / `MainWindow.xaml.cs` — dashboard layout, UI events, and orchestration.
- `Views/WarehouseGridView.cs` — grid editing, rendering, movement timer, and route execution.
- `Models/` — robot, task, and grid cell data types.
- `Services/PathfindingService..cs` — A* route search.
- `Services/RobotFleetService.cs` — in-memory robot collection and state updates.
- `Services/TaskQueueService.cs` — in-memory FIFO task queue and status transitions.

## Current limitations and missing pieces

- **Only Robot-01 is simulated on the grid.** Additional robots can be added and shown in the fleet list, but they do not have independent routes or movement. Tasks are not assigned across the fleet; queued tasks run through the single grid movement engine.
- **No multi-robot scheduling or collision avoidance.** Other robots are treated as fixed obstacles; the simulator does not plan around moving robots, reserve cells, or coordinate routes.
- **No persistence.** Robots, warehouse layout, tasks, and simulation state are lost when the application closes.
- **Charging is manual and only wired for Robot-01.** There are no charging stations, automatic low-battery recovery, or per-robot charging behavior.
- **No automated test project or documented test suite is present.**
- **No solution file, release packaging, or installer is present.** The project can be launched directly with its `.csproj`.

