using Data.Components;
using Simulation.Tests;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataTests : SimulationTests
    {
        static DataTests()
        {
            TypeLayout.Register<IsDataRequest>();
            TypeLayout.Register<IsDataSource>();
            TypeLayout.Register<IsData>();
            TypeLayout.Register<BinaryData>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            world.Schema.RegisterComponent<IsDataRequest>();
            world.Schema.RegisterComponent<IsDataSource>();
            world.Schema.RegisterComponent<IsData>();
            world.Schema.RegisterArrayElement<BinaryData>();
            simulator.AddSystem<DataImportSystem>();
        }
    }
}
