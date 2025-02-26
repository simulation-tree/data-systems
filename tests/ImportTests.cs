using Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using Unmanaged;
using Worlds;

namespace Data.Systems.Tests
{
    public class ImportTests : DataSystemTests
    {
        [Test, CancelAfter(1200)]
        public async Task ReadFromStaticFileSystem(CancellationToken cancellation)
        {
            const string FileName = "test.txt";
            DataSource file = new(world, FileName);
            file.WriteUTF8("Hello, World!");

            DataRequest request = new(world, FileName);

            await request.UntilCompliant(Simulate, cancellation);

            using ByteReader reader = request.CreateBinaryReader();
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
            DataSource file = new(world, FileName);
            file.WriteUTF8(randomStr);

            DataRequest readTomato = new(world, FileName);

            await readTomato.UntilCompliant(Simulate, cancellation);

            Assert.That(readTomato.IsCompliant, Is.True);
            using ByteReader reader = readTomato.CreateBinaryReader();
            USpan<char> buffer = stackalloc char[128];
            uint length = reader.ReadUTF8(buffer);
            USpan<char> text = buffer.Slice(0, length);
            Assert.That(text.ToString(), Is.EqualTo(randomStr));
        }

        [Test, CancelAfter(4000)]
        public async Task DontFindThis()
        {
            const string FileName = "tomato";
            DataRequest readTomato = new(world, FileName);
            CancellationTokenSource cts = new(800);
            try
            {
                await readTomato.UntilCompliant(Simulate, cts.Token);
                Assert.Fail("Should not have found the file");
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
            DataSource sourceMat = new(world, "Assets/Materials/unlit.mat");
            sourceMat.WriteUTF8("material");
            DataSource sourceJson = new(world, "Assets/Materials/unlit.json");
            sourceJson.WriteUTF8("json");
            DataSource sourceShader = new(world, "Assets/Materials/unlit.shader");
            sourceShader.WriteUTF8("shader");
            DataSource sourceTxt = new(world, "Assets/Materials/unlit.txt");
            sourceTxt.WriteUTF8("text");

            DataRequest matRequest = new(world, "*/unlit.mat");
            DataRequest anyShaderRequest = new(world, "*.shader");

            await matRequest.UntilCompliant(Simulate, cancellation);
            await anyShaderRequest.UntilCompliant(Simulate, cancellation);

            using ByteReader matReader = matRequest.CreateBinaryReader();
            using ByteReader shaderReader = anyShaderRequest.CreateBinaryReader();
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
            DataRequest request = new(world, FileName);

            await request.UntilCompliant(Simulate, cancellation);

            using ByteReader reader = request.CreateBinaryReader();
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
            DataRequest request = new(world, FileName);

            await request.UntilCompliant(Simulate, cancellation);

            using ByteReader reader = request.CreateBinaryReader();
            using Array<char> buffer = new(reader.Length);
            USpan<char> span = buffer.AsSpan();
            uint length = reader.ReadUTF8(span);
            USpan<char> fileText = span.Slice(0, length);
            Assert.That(fileText.ToString(), Contains.Substring("i am an embedded resource"));
        }
    }
}
