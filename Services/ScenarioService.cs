using System.IO;
using System.Text.Json;
using RobotFleetSimulation.Models;

namespace RobotFleetSimulation.Services;

public sealed class ScenarioService
{
    public const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string path, WarehouseScenario scenario)
    {
        scenario.SchemaVersion = CurrentSchemaVersion;
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(scenario, Options));
        File.Move(tempPath, path, true);
    }

    public WarehouseScenario Load(string path)
    {
        var scenario = JsonSerializer.Deserialize<WarehouseScenario>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The scenario file is empty or invalid.");
        if (scenario.SchemaVersion > CurrentSchemaVersion || scenario.SchemaVersion < 1)
            throw new InvalidDataException($"Scenario schema {scenario.SchemaVersion} is not supported.");
        if (scenario.Rows is < 10 or > 200 || scenario.Columns is < 10 or > 300)
            throw new InvalidDataException("Scenario dimensions are outside supported limits.");
        return scenario;
    }
}
