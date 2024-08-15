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

            DataSource file = new(world, fileName, "Hello, World!");
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
            DataSource file = new(world, "tomato", randomStr);
            DataRequest readTomato = new(world, "tomato");
            using BinaryReader reader = new(readTomato.GetBytes());
            Span<char> buffer = stackalloc char[128];
            int length = reader.ReadUTF8Span(buffer);
            ReadOnlySpan<char> text = buffer[..length];
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test]
        public void DontFindThis()
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);
            Assert.Throws<RequestedDataNotFoundException>(() =>
            {
                DataRequest readTomato = new(world, "tomato");
            });
        }

        [Test]
        public void UseDataReference()
        {
            using World world = new();
            DataRequest defaultMaterial = new(world, Address.Get<DefaultMaterial>());
            Assert.That(defaultMaterial.GetAddress().ToString(), Is.EqualTo("Assets/Materials/unlit.mat"));
        }

        public readonly struct DefaultMaterial : IDataReference
        {
            FixedString IDataReference.Value => "Assets/Materials/unlit.mat";
        }

        [Test]
        public void FindFileWithWildcard()
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);
            DataSource sourceMat = new(world, "Assets/Materials/unlit.mat", "material");
            DataSource sourceJson = new(world, "Assets/Materials/unlit.json", "json");
            DataSource sourceShader = new(world, "Assets/Materials/unlit.shader", "shader");
            DataSource sourceTxt = new(world, "Assets/Materials/unlit.txt", "text");

            DataRequest matRequest = new(world, "*/unlit.mat");
            DataRequest anyShaderRequest = new(world, "*.shader");

            using BinaryReader matReader = new(matRequest.GetBytes());
            using BinaryReader shaderReader = new(anyShaderRequest.GetBytes());
            Span<char> buffer = stackalloc char[128];
            int length = matReader.ReadUTF8Span(buffer);
            Assert.That(buffer[..length].ToString(), Is.EqualTo("material"));

            length = shaderReader.ReadUTF8Span(buffer);
            Assert.That(buffer[..length].ToString(), Is.EqualTo("shader"));
        }

        [Test]
        public void FindEmbeddedResource()
        {
            using World world = new();
            using DataImportSystem dataImports = new(world);

            DataRequest testData = new(world, "*/Assets/TestData.txt");
            using BinaryReader reader = new(testData.GetBytes());
            Span<char> buffer = stackalloc char[128];
            int length = reader.ReadUTF8Span(buffer);
            Assert.That(buffer[..length].ToString(), Contains.Substring("abacus"));
        }

        [Test]
        public void AddressEquality()
        {
            Address a = new("abacus");
            Assert.That(a.Matches("*/abacus"), Is.True);
        }
    }
}
