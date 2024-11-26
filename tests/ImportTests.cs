using Collections;
using Data.Systems;
using Simulation;
using Simulation.Tests;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged;

namespace Data.Tests
{
    public class ImportTests : SimulatorTests
    {
        protected override void SetUp()
        {
            base.SetUp();
            Simulator.AddSystem<DataImportSystem>();
            RuntimeHelpers.RunClassConstructor(typeof(TypeTable).TypeHandle);
        }

        [Test, CancelAfter(1200)]
        public async Task ReadFromStaticFileSystem(CancellationToken cancellation)
        {
            const string fileName = "test.txt";
            DataSource file = new(World, fileName, "Hello, World!");
            DataRequest request = new(World, fileName);

            await request.UntilCompliant(Simulate, cancellation);

            using BinaryReader reader = new(request.Data);
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8Span(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Is.EqualTo("Hello, World!"));
        }

        [Test, CancelAfter(1000)]
        public async Task FindEntityFile(CancellationToken cancellation)
        {
            string randomStr = Guid.NewGuid().ToString();
            DataSource file = new(World, "tomato", randomStr);
            DataRequest readTomato = new(World, "tomato");

            await readTomato.UntilCompliant(Simulate, cancellation);

            Assert.That(readTomato.IsCompliant(), Is.True);
            using BinaryReader reader = new(readTomato.Data);
            USpan<char> buffer = stackalloc char[128];
            uint length = reader.ReadUTF8Span(buffer);
            USpan<char> text = buffer.Slice(0, length);
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test, CancelAfter(4000)]
        public async Task DontFindThis(CancellationToken cancellation)
        {
            DataRequest readTomato = new(World, "tomato");
            CancellationTokenSource cts = new(800);
            try
            {
                await readTomato.UntilCompliant(Simulate, cts.Token);
                Assert.Fail("Should not have found the file.");
            }
            catch (Exception ex)
            {
                Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
            }

            Assert.That(readTomato.IsCompliant(), Is.False);
        }

        [Test, CancelAfter(5000)]
        public async Task FindFileWithWildcard(CancellationToken cancellation)
        {
            DataSource sourceMat = new(World, "Assets/Materials/unlit.mat", "material");
            DataSource sourceJson = new(World, "Assets/Materials/unlit.json", "json");
            DataSource sourceShader = new(World, "Assets/Materials/unlit.shader", "shader");
            DataSource sourceTxt = new(World, "Assets/Materials/unlit.txt", "text");

            DataRequest matRequest = new(World, "*/unlit.mat");
            DataRequest anyShaderRequest = new(World, "*.shader");

            await matRequest.UntilCompliant(Simulate, cancellation);
            await anyShaderRequest.UntilCompliant(Simulate, cancellation);

            using BinaryReader matReader = new(matRequest.Data);
            using BinaryReader shaderReader = new(anyShaderRequest.Data);
            USpan<char> buffer = stackalloc char[128];
            uint length = matReader.ReadUTF8Span(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("material"));

            length = shaderReader.ReadUTF8Span(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("shader"));
        }

        [Test, CancelAfter(1000)]
        public async Task FindEmbeddedResource(CancellationToken cancellation)
        {
            DataRequest testRequest = new(World, "*/Assets/TestData.txt");

            await testRequest.UntilCompliant(Simulate, cancellation);

            using BinaryReader reader = new(testRequest.Data);
            USpan<char> buffer = stackalloc char[128];
            uint length = reader.ReadUTF8Span(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Contains.Substring("abacus"));
        }
    }
}
