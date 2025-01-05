namespace Data.Systems.Tests
{
    public class AddressTests
    {
        [Test]
        public void CheckDataReferenceEquality()
        {
            Address defaultMaterialAddress = Address.Get<DefaultMaterial>();
            Assert.That(defaultMaterialAddress.ToString(), Is.EqualTo("Assets/Materials/unlit.mat"));
        }

        [Test]
        public void AddressEquality()
        {
            Address a = new("abacus");
            Assert.That(a.Matches("*/abacus"), Is.True);
        }

        public readonly struct DefaultMaterial : IDataReference
        {
            readonly Address IDataReference.Value => "Assets/Materials/unlit.mat";
        }
    }
}
