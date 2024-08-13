using Simulation;
using Data.Systems;
using System;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Tests
{
    public class ImportTests
    {
        [TearDown]
        public void CleanUp()
        {
            Allocations.ThrowIfAny();
        }

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

        [Test]
        public void ReadFromStaticFileSystem()
        {
            const string fileName = "test.txt";
            using World world = new();
            using DataImportSystem system = new(world);

            Data file = new(world, fileName, "Hello, World!");
            if (system.TryImport(fileName, out BinaryReader reader))
            {
                using UnmanagedArray<char> buffer = new(reader.Length);
                Span<char> span = buffer.AsSpan();
                int length = reader.ReadUTF8Span(span);
                ReadOnlySpan<char> fileText = span[..length];
                Assert.That(fileText.ToString(), Is.EqualTo("Hello, World!"));
                reader.Dispose();
            }
            else
            {
                Assert.Fail("File not found.");
            }
        }

        [Test]
        public void FindEntityFile()
        {
            string randomStr = Guid.NewGuid().ToString();
            using World world = new();
            using DataImportSystem dataImports = new(world);
            Data file = new(world, "tomato", randomStr);
            DataRequest readTomato = new(world, "tomato");
            using BinaryReader reader = new(readTomato.AsSpan());
            Span<char> buffer = stackalloc char[128];
            int length = reader.ReadUTF8Span(buffer);
            ReadOnlySpan<char> text = buffer[..length];
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }
    }
}
