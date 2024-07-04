using System;
using Unmanaged;
using Unmanaged.Collections;

namespace Data
{
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

        public readonly bool Equals(ReadOnlySpan<char> value)
        {
            if (value.Length != path.Length)
            {
                return false;
            }

            int extensionIndex = RawPath.LastIndexOf('.');
            for (uint i = 0; i < value.Length; i++)
            {
                char c = path[i];
                char valueC = value[(int)i];
                if (c != valueC)
                {
                    if (valueC == ' ' && (c == '_' || c == '.'))
                    {
                        continue;
                    }
                    else if (valueC == '/' && c == '.' && i != extensionIndex)
                    {
                        continue;
                    }

                    return false;
                }
            }

            return true;
        }
    }
}