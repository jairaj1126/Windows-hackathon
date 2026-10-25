# Robot Fleet Simulation

A Windows desktop warehouse and robot fleet simulator built with C#, WPF, and .NET 10. It demonstrates map authoring, robot dispatch, route planning, traffic handling, battery use, task execution, scenario persistence, and local integration through HTTP.

## Features

### Warehouse maps

- A fixed 60 × 80 warehouse map (4,800 cells), rendered on a single WPF drawing surface.
- Three built-in layouts: Starter Warehouse, Cross-Dock Hub, and Compact Lab.
- Editable walls, shelves, slow zones, charging stations, loading docks, packing stations, elevators, restricted zones, and directional one-way lanes.
- Color-coded tiles, robot routes, congestion heat, zoom, and scrollable map viewing.
- Undo and redo for map edits.
- JSON scenario save/load with schema versioning and atomic file replacement.
- Automatic scenario recovery from the per-user Local Application Data folder.

### Fleet simulation

- Multiple robots with individual speed, payload capacity/load, battery capacity/charge, position, and status.
- A* orthogonal pathfinding that respects obstacles, restricted areas, and one-way movement rules.
- Task types for move, pickup, delivery, charging, and multi-stop work; tasks support priorities, deadlines, payload weights, and intermediate stops.
- Dispatch strategies for priority/nearest, nearest task, highest battery, and balanced workload.
- Per-tick cell reservations, collision checks, congestion tracking, route replanning, and a basic wait-cycle deadlock detector that makes a robot yield where possible.
- Automatic low-battery detours to a reachable charger, followed by resumption of the remaining task stops.
- Fleet and individual robot pause/resume, single-step simulation, adjustable speed, reset, and keyboard shortcuts.

### Monitoring and integration

- Bounded event/history recording with replay slider and per-robot battery trend chart.
- CSV report export with fleet summary, robot/task details, and recorded history events.
- Local REST API bound to `127.0.0.1:5084` for status, task creation, simulation commands, and robot pause/resume/recharge commands.
- A small xUnit test project for pathfinding and task queue behavior.

## Requirements

- Windows (the user interface uses WPF).
- .NET 10 SDK, matching the `net10.0-windows` target.

## Run

```powershell
dotnet run --project .\RobotFleetSimulation.csproj
```

## Tests

```powershell
dotnet test .\Tests\RobotFleetSimulation.Tests.csproj
```

The test project covers representative pathfinding and task queue behavior. Tests have not been run as part of the current feature update.

## Publish

The included script creates a self-contained single-file Windows build. It defaults to `win-x64`; `win-arm64` is also supported.

```powershell
.\publish.ps1
.\publish.ps1 -Runtime win-arm64
```

Published output is written under `artifacts\publish\`.

## Using the simulator

1. Choose a preset or resize the map while robots have no active assignments. Select a map tool and click cells to edit the warehouse.
2. Select a robot to inspect its status and capabilities. Set its start position with **Robot Start** or assign it a direct **Destination**.
3. Create a task by selecting a task type, priority, destination, optional payload/deadline, and optional intermediate stops. Intermediate stops use `row,column` pairs separated by semicolons, for example `4,8; 9,12`.
4. Choose a dispatch strategy and start the fleet. Use the dashboard controls to pause, step, or reset; individual robots can also be paused and resumed.
5. Use the replay slider and event list to inspect recorded states. Save/load scenarios or export a CSV report as needed.

Coordinates are zero-based. Movement is orthogonal. The warehouse is locked at 60 rows × 80 columns. Older or larger saved scenario maps are loaded into this fixed area; cells outside it are cropped and smaller maps are padded with empty cells.

## REST API

The API listens on loopback only at `http://127.0.0.1:5084/` while the desktop application is running.

| Method and path | Purpose |
| --- | --- |
| `GET /api/status` | Current map dimensions, robots, tasks, aggregate metrics, and history frame count. |
| `POST /api/tasks` | Add a task. JSON fields: `name`, `row`, `column`, `priority` (1–5), `deadlineMinutes`, `payloadWeightKg`, and `type` (enum name or numeric enum value). |
| `POST /api/commands` | Send `{ "command": "start" }`, `pause`, or `step`. |
| `POST /api/robots/{id}` | Send `{ "command": "pause" }`, `resume`, or `recharge`. |

Example task request:

```json
{
  "name": "Restock aisle 4",
  "row": 8,
  "column": 16,
  "priority": 3,
  "payloadWeightKg": 12,
  "type": "Delivery"
}
```

The API is unauthenticated and intended for local demonstrations and trusted local tools; it is not designed to be exposed to a network.

## Project structure

- `MainWindow.xaml` and `MainWindow.xaml.cs` — dashboard, controls, dispatch, autosave, reporting, and API integration.
- `Views/WarehouseGridView.cs` — map editor, rendering, route execution, battery behavior, traffic coordination, and replay history.
- `Models/` — robot, task, map, dispatch, history, and scenario data.
- `Services/PathfindingService..cs` — A* path search and one-way movement constraints.
- `Services/RobotFleetService.cs` — fleet collection and robot state operations.
- `Services/TaskQueueService.cs` — task lifecycle and priority ordering.
- `Services/WarehousePresets.cs` — built-in warehouse layouts.
- `Services/ScenarioService.cs` — versioned JSON persistence.
- `Services/LocalApiService.cs` — loopback HTTP listener.
- `Tests/` — xUnit tests for core behaviors.
- `publish.ps1` — self-contained Windows publish helper.

## Current boundaries

- Elevator tiles are facility markers; the simulator currently models one 2D floor and does not route between levels.
- Robot movement, workload, battery use, and deadlock yielding are simplified simulation rules. Yielding is best-effort and does not guarantee deadlock-free traffic flow for every map.
- History is kept in memory and capped at 5,000 frames; only the latest 60 events are shown in the dashboard list. Scenario saves preserve map, robot capabilities/state, and task data, but do not save an exact in-progress route/tick for deterministic continuation.
- The REST API provides local HTTP integration. MQTT, physical robot adapters, distributed simulation, and authentication are not included.
- Deadlines use wall-clock UTC time and are evaluated while simulation ticks occur.
