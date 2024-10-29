namespace Data.Tests
{
    public class ColorTests
    {
        [Test]
        public void CheckColorConversion()
        {
            Color red = Color.FromHSV(0, 1, 1);
            Assert.That(red, Is.EqualTo(new Color(1, 0, 0)));

            Color green = Color.FromHSV(120f / 360f, 1, 1);
            Assert.That(green, Is.EqualTo(new Color(0, 1, 0)));

            Color blue = Color.FromHSV(240f / 360f, 1, 1);
            Assert.That(blue, Is.EqualTo(new Color(0, 0, 1)));

            Color white = Color.FromHSV(0, 0, 1);
            Assert.That(white, Is.EqualTo(new Color(1, 1, 1)));

            Color doorhinge = new(0, 1f, 1f);
            Assert.That(doorhinge.H, Is.EqualTo(0.5f));
        }
    }
}
