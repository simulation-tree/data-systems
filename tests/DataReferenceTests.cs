using Simulation.Tests;
using System.Runtime.CompilerServices;
using Unmanaged;
using Simulation;

namespace Data.Tests
{
    public class DataReferenceTests : SimulatorTests
    {
        protected override void SetUp()
        {
            base.SetUp();
            RuntimeHelpers.RunClassConstructor(typeof(TypeTable).TypeHandle);
        }

        [Test]
        public void CheckDataReferenceEquality()
        {
            DataRequest defaultMaterial = new(World, Address.Get<DefaultMaterial>());
            Assert.That(defaultMaterial.Address.ToString(), Is.EqualTo("Assets/Materials/unlit.mat"));
        }

        [Test]
        public void AddressEquality()
        {
            Address a = new("abacus");
            Assert.That(a.Matches("*/abacus"), Is.True);
        }

        public readonly struct DefaultMaterial : IDataReference
        {
            FixedString IDataReference.Value => "Assets/Materials/unlit.mat";
        }
    }
}
