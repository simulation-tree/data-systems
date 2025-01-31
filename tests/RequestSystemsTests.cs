using Simulation.Tests;
using Types;
using Worlds;
using Data;

namespace Requests.Systems.Tests
{
    public abstract class RequestSystemsTests : SimulationTests
    {
        static RequestSystemsTests()
        {
            TypeRegistry.Load<DataTypeBank>();
            TypeRegistry.Load<RequestsTypeBank>();
        }

        protected override void SetUp()
        {
            base.SetUp();
            simulator.AddSystem<RequestLoadingSystem>();
        }

        protected override Schema CreateSchema()
        {
            Schema schema = base.CreateSchema();
            schema.Load<DataSchemaBank>();
            schema.Load<RequestsSchemaBank>();
            return schema;
        }
    }
}