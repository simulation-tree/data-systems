using System;
using Unmanaged;
using Unmanaged.Collections;

namespace Data
{
    //todo: this looks like it represents generic addressable data, perhaps its name should be updated and moved to `data`
    public readonly struct EmbeddedResource : IDisposable
    {
        public readonly BinaryReader reader;
        private readonly UnmanagedArray<char> path;

        public readonly ReadOnlySpan<char> RawPath => path.AsSpan();

        public EmbeddedResource(BinaryReader reader, ReadOnlySpan<char> path)
        {
            this.reader = reader;
            this.path = new(path);
        }

        public readonly override string ToString()
        {
            return $"EmbeddedResource: {RawPath.ToString()}";
        }

        public readonly void Dispose()
        {
            path.Dispose();
            reader.Dispose();
        }
    }
}