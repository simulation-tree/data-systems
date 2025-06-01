using Data.Messages;
using Simulation.Tests;
using Types;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataSystemTests : SimulationTests
    {
        public World world;

        static DataSystemTests()
        {
            MetadataRegistry.Load<DataMetadataBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            Schema schema = new();
            schema.Load<DataSchemaBank>();
            world = new(schema);
            Simulator.Add(new DataImportSystem(Simulator, world));
        }

        protected override void TearDown()
        {
            Simulator.Remove<DataImportSystem>();
            world.Dispose();
            base.TearDown();
        }

        protected override void Update(double deltaTime)
        {
            Simulator.Broadcast(new DataUpdate());
        }
    }
}