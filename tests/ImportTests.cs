using Data.Events;
using Data.Systems;
using Simulation;
using System;
using System.Threading;
using System.Threading.Tasks;
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

        private void Simulate(World world)
        {
            world.Submit(new DataUpdate());
            world.Poll();
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

        [Test, CancelAfter(1200)]
        public async Task ReadFromStaticFileSystem(CancellationToken cancellation)
        {
            const string fileName = "test.txt";
            using World world = new();
            using DataImportSystem imports = new(world);
            Simulate(world);

            DataSource file = new(world, fileName, "Hello, World!");
            DataRequest request = new(world, fileName);
            Simulate(world);

            await request.UntilLoaded(cancellation);
            using BinaryReader reader = new(request.Data);
            using UnmanagedArray<char> buffer = new(reader.Length);
            Span<char> span = buffer.AsSpan();
            int length = reader.ReadUTF8Span(span);
            ReadOnlySpan<char> fileText = span[..length];
            Assert.That(fileText.ToString(), Is.EqualTo("Hello, World!"));
        }

        [Test, CancelAfter(1000)]
        public async Task FindEntityFile(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem imports = new(world);
            Simulate(world);

            string randomStr = Guid.NewGuid().ToString();
            DataSource file = new(world, "tomato", randomStr);
            DataRequest readTomato = new(world, "tomato");
            Simulate(world);

            await readTomato.UntilLoaded(cancellation);
            Assert.That(readTomato.Status, Is.EqualTo(DataRequest.DataStatus.Loaded));
            using BinaryReader reader = new(readTomato.Data);
            Span<char> buffer = stackalloc char[128];
            int length = reader.ReadUTF8Span(buffer);
            ReadOnlySpan<char> text = buffer[..length];
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test, CancelAfter(4000)]
        public async Task DontFindThis(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem imports = new(world);
            Simulate(world);

            DataRequest readTomato = new(world, "tomato");
            CancellationTokenSource cts = new(400);
            Simulate(world);

            await Task.Run(async () =>
            {
                try
                {
                    await readTomato.UntilLoaded(cts.Token);
                    Assert.Fail("Should not have found the file.");
                }
                catch (Exception ex)
                {
                    Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
                }
            }, cancellation);

            Assert.That(readTomato.Status, Is.EqualTo(DataRequest.DataStatus.None));
        }

        [Test]
        public void UseDataReference()
        {
            using World world = new();
            DataRequest defaultMaterial = new(world, Address.Get<DefaultMaterial>());
            Assert.That(defaultMaterial.Address.ToString(), Is.EqualTo("Assets/Materials/unlit.mat"));
        }

        public readonly struct DefaultMaterial : IDataReference
        {
            FixedString IDataReference.Value => "Assets/Materials/unlit.mat";
        }

        [Test, CancelAfter(5000)]
        public async Task FindFileWithWildcard(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem imports = new(world);
            Simulate(world);

            DataSource sourceMat = new(world, "Assets/Materials/unlit.mat", "material");
            DataSource sourceJson = new(world, "Assets/Materials/unlit.json", "json");
            DataSource sourceShader = new(world, "Assets/Materials/unlit.shader", "shader");
            DataSource sourceTxt = new(world, "Assets/Materials/unlit.txt", "text");

            DataRequest matRequest = new(world, "*/unlit.mat");
            DataRequest anyShaderRequest = new(world, "*.shader");
            Simulate(world);

            await matRequest.UntilLoaded(cancellation);
            await anyShaderRequest.UntilLoaded(cancellation);

            using BinaryReader matReader = new(matRequest.Data);
            using BinaryReader shaderReader = new(anyShaderRequest.Data);
            Span<char> buffer = stackalloc char[128];
            int length = matReader.ReadUTF8Span(buffer);
            Assert.That(buffer[..length].ToString(), Is.EqualTo("material"));

            length = shaderReader.ReadUTF8Span(buffer);
            Assert.That(buffer[..length].ToString(), Is.EqualTo("shader"));
        }

        [Test, CancelAfter(1000)]
        public async Task FindEmbeddedResource(CancellationToken cancellation)
        {
            using World world = new();
            using DataImportSystem imports = new(world);
            Simulate(world);

            DataRequest testRequest = new(world, "*/Assets/TestData.txt");
            Simulate(world);

            await testRequest.UntilLoaded(cancellation);
            using BinaryReader reader = new(testRequest.Data);
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
