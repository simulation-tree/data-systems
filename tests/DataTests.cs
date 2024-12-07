using Data.Components;
using Simulation;
using Simulation.Components;
using Simulation.Tests;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataTests : SimulationTests
    {
        protected override void SetUp()
        {
            base.SetUp();
            ComponentType.Register<IsDataRequest>();
            ComponentType.Register<IsDataSource>();
            ComponentType.Register<IsData>();
            ComponentType.Register<IsProgram>();
            ArrayType.Register<BinaryData>();
            Simulator.AddSystem<DataImportSystem>();
        }
    }
}
