using Collections;
using Simulation;
using System;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged;
using Worlds;

namespace Data.Systems.Tests
{
    public class ImportTests : DataSystemsTests
    {
        [Test, CancelAfter(1200)]
        public async Task ReadFromStaticFileSystem(CancellationToken cancellation)
        {
            Address fileName = "test.txt";
            DataSource file = new(world, fileName, "Hello, World!");
            DataRequest request = new(world, fileName);

            await request.UntilLoaded(Simulate, cancellation);

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
            DataSource file = new(world, "tomato", randomStr);
            DataRequest readTomato = new(world, "tomato");

            await readTomato.UntilLoaded(Simulate, cancellation);

            Assert.That(readTomato.Is(), Is.True);
            using BinaryReader reader = new(readTomato.Data);
            USpan<char> buffer = stackalloc char[128];
            uint length = reader.ReadUTF8Span(buffer);
            USpan<char> text = buffer.Slice(0, length);
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test, CancelAfter(4000)]
        public async Task DontFindThis()
        {
            DataRequest readTomato = new(world, "tomato");
            CancellationTokenSource cts = new(800);
            try
            {
                await readTomato.UntilLoaded(Simulate, cts.Token);
                Assert.Fail("Should not have found the file.");
            }
            catch (Exception ex)
            {
                Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
            }

            Assert.That(readTomato.IsLoaded, Is.False);
        }

        [Test, CancelAfter(5000)]
        public async Task FindFileWithWildcard(CancellationToken cancellation)
        {
            DataSource sourceMat = new(world, "Assets/Materials/unlit.mat", "material");
            DataSource sourceJson = new(world, "Assets/Materials/unlit.json", "json");
            DataSource sourceShader = new(world, "Assets/Materials/unlit.shader", "shader");
            DataSource sourceTxt = new(world, "Assets/Materials/unlit.txt", "text");

            DataRequest matRequest = new(world, "*/unlit.mat");
            DataRequest anyShaderRequest = new(world, "*.shader");

            await matRequest.UntilLoaded(Simulate, cancellation);
            await anyShaderRequest.UntilLoaded(Simulate, cancellation);

            using BinaryReader matReader = new(matRequest.Data);
            using BinaryReader shaderReader = new(anyShaderRequest.Data);
            USpan<char> buffer = stackalloc char[128];
            uint length = matReader.ReadUTF8Span(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("material"));

            length = shaderReader.ReadUTF8Span(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("shader"));
        }

        [Test, CancelAfter(5000)]
        public async Task LoadFromFileSystem(CancellationToken cancellation)
        {
            const string fileName = "Assets/TestData.txt";
            DataRequest request = new(world, fileName);

            await request.UntilLoaded(Simulate, cancellation);

            using BinaryReader reader = new(request.Data);
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8Span(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Contains.Substring("abacus"));
        }

        [Test, CancelAfter(5000)]
        public async Task LoadEmbeddedResource(CancellationToken cancellation)
        {
            EmbeddedAddress.Register(GetType().Assembly, "Assets/EmbeddedTestData.txt");

            const string fileName = "*/EmbeddedTestData.txt";
            DataRequest request = new(world, fileName);

            await request.UntilLoaded(Simulate, cancellation);

            using BinaryReader reader = new(request.Data);
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8Span(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Contains.Substring("i am an embedded resource"));
        }
    }
}
