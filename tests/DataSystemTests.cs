using Simulation.Tests;
using Types;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataSystemTests : SimulationTests
    {
        static DataSystemTests()
        {
            MetadataRegistry.Load<DataMetadataBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.Add(new DataImportSystem());
        }

        protected override void TearDown()
        {
            simulator.Remove<DataImportSystem>();
            base.TearDown();
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<DataSchemaBank>();
            return schema;
        }
    }
}