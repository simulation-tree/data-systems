using Simulation.Tests;
using Types;
using Worlds;

namespace Data.Systems.Tests
{
    public abstract class DataSystemsTests : SimulationTests
    {
        static DataSystemsTests()
        {
            TypeRegistry.Load<Data.TypeBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.AddSystem<DataImportSystem>();
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<Data.SchemaBank>();
            return schema;
        }
    }
}