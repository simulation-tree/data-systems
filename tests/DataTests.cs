using Data.Components;
using Simulation.Tests;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataTests : SimulationTests
    {
        static DataTests()
        {
            TypeLayout.Register<IsDataRequest>("IsDataRequest");
            TypeLayout.Register<IsDataSource>("IsDataSource");
            TypeLayout.Register<IsData>("IsData");
            TypeLayout.Register<BinaryData>("BinaryData");
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
