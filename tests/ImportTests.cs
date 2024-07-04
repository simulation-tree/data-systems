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
