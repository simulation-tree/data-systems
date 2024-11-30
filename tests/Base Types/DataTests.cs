using Data.Components;
using Data.Systems;
using Simulation;
using Simulation.Components;
using System;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged.Tests;
using Worlds;

namespace Data.Tests
{
    public abstract class DataTests : UnmanagedTests
    {
        private World world;
        private Simulator simulator;

        public World World => world;
        public Simulator Simulator => simulator;

        protected override void SetUp()
        {
            base.SetUp();

            ComponentType.Register<IsDataRequest>();
            ComponentType.Register<IsDataSource>();
            ComponentType.Register<IsData>();
            ComponentType.Register<IsProgram>();
            ComponentType.Register<ProgramAllocation>();
            ArrayType.Register<BinaryData>();

            world = new();
            simulator = new(world);
            simulator.AddSystem<DataImportSystem>();
        }

        protected override void TearDown()
        {
            simulator.Dispose();
            world.Dispose();
            base.TearDown();
        }

        protected async Task Simulate(World world, CancellationToken cancellation)
        {
            TimeSpan delta = Simulator.Update();
            await Task.Delay(delta, cancellation).ConfigureAwait(false);
        }
    }
}
