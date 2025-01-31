using Collections;
using Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged;
using Worlds;

namespace Requests.Systems.Tests
{
    public class ImportTests : RequestSystemsTests
    {
        [Test, CancelAfter(1200)]
        public async Task ReadFromStaticFileSystem(CancellationToken cancellation)
        {
            const string FileName = "test.txt";
            Datum file = new(world, FileName);
            file.WriteUTF8("Hello, World!");

            Request request = new(world, FileName);

            await request.UntilLoaded(Simulate, cancellation);

            using BinaryReader reader = new(request.GetBinaryData());
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Is.EqualTo("Hello, World!"));
        }

        [Test, CancelAfter(1000)]
        public async Task FindEntityFile(CancellationToken cancellation)
        {
            const string FileName = "tomato";
            string randomStr = Guid.NewGuid().ToString();
            Datum file = new(world, FileName);
            file.WriteUTF8(randomStr);

            Request readTomato = new(world, FileName);

            await readTomato.UntilLoaded(Simulate, cancellation);

            Assert.That(readTomato.Is(), Is.True);
            using BinaryReader reader = new(readTomato.GetBinaryData());
            USpan<char> buffer = stackalloc char[128];
            uint length = reader.ReadUTF8(buffer);
            USpan<char> text = buffer.Slice(0, length);
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test, CancelAfter(4000)]
        public async Task DontFindThis()
        {
            const string FileName = "tomato";
            Request readTomato = new(world, FileName);
            CancellationTokenSource cts = new(800);
            try
            {
                await readTomato.UntilLoaded(Simulate, cts.Token);
                Assert.Fail("Should not have found the file");
            }
            catch (Exception ex)
            {
                Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
            }

            Assert.That(readTomato.IsLoaded(), Is.False);
        }

        [Test, CancelAfter(5000)]
        public async Task FindFileWithWildcard(CancellationToken cancellation)
        {
            Datum sourceMat = new(world, "Assets/Materials/unlit.mat");
            sourceMat.WriteUTF8("material");
            Datum sourceJson = new(world, "Assets/Materials/unlit.json");
            sourceJson.WriteUTF8("json");
            Datum sourceShader = new(world, "Assets/Materials/unlit.shader");
            sourceShader.WriteUTF8("shader");
            Datum sourceTxt = new(world, "Assets/Materials/unlit.txt");
            sourceTxt.WriteUTF8("text");

            Request matRequest = new(world, "*/unlit.mat");
            Request anyShaderRequest = new(world, "*.shader");

            await matRequest.UntilLoaded(Simulate, cancellation);
            await anyShaderRequest.UntilLoaded(Simulate, cancellation);

            using BinaryReader matReader = new(matRequest.GetBinaryData());
            using BinaryReader shaderReader = new(anyShaderRequest.GetBinaryData());
            USpan<char> buffer = stackalloc char[128];
            uint length = matReader.ReadUTF8(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("material"));

            length = shaderReader.ReadUTF8(buffer);
            Assert.That(buffer.Slice(0, length).ToString(), Is.EqualTo("shader"));
        }

        [Test, CancelAfter(5000)]
        public async Task LoadFromFileSystem(CancellationToken cancellation)
        {
            const string FileName = "Assets/TestData.txt";
            Request request = new(world, FileName);

            await request.UntilLoaded(Simulate, cancellation);

            using BinaryReader reader = new(request.GetBinaryData());
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Contains.Substring("abacus"));
        }

        [Test, CancelAfter(5000)]
        public async Task LoadEmbeddedResource(CancellationToken cancellation)
        {
            EmbeddedResourceRegistry.Register(GetType().Assembly, "Assets/EmbeddedTestData.txt");

            const string FileName = "*/EmbeddedTestData.txt";
            Request request = new(world, FileName);

            await request.UntilLoaded(Simulate, cancellation);

            using BinaryReader reader = new(request.GetBinaryData());
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Contains.Substring("i am an embedded resource"));
        }
    }
}
