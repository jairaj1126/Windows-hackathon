namespace RobotFleetSimulation.Models;

public enum DispatchStrategy
{
    PriorityAndNearest,
    NearestTask,
    HighestBattery,
    BalancedWorkload
}
