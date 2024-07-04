using Simulation;
using Data.Components;
using Data.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Systems
{
    public class DataImportSystem : SystemBase
    {
        private readonly Query<IsData> fileQuery;
        private readonly UnmanagedList<EmbeddedResource> embeddedResources;

        public DataImportSystem(World world) : base(world)
        {
            Subscribe<DataUpdate>(Update);
            fileQuery = new(world);
            embeddedResources = new();
            Dictionary<int, Assembly> sourceAssemblies = [];
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string resourcePath in assembly.GetManifestResourceNames())
                {
                    int resourcePathHash = Djb2Hash.GetDjb2HashCode(resourcePath);
                    if (sourceAssemblies.TryGetValue(resourcePathHash, out Assembly? existing))
                    {
                        Debug.WriteLine($"Duplicate resource with same addressa at `{resourcePath}` from `{assembly.GetName()}` was ignored, data from `{existing.GetName()}` is used instead");
                    }
                    else
                    {
                        System.IO.Stream stream = assembly.GetManifestResourceStream(resourcePath) ?? throw new Exception($"Resource `{resourcePath}` from `{assembly.GetName()}` was not found");
                        stream.Position = 0;
                        BinaryReader reader = new(stream);
                        EmbeddedResource resource = new(reader, resourcePath.AsSpan());
                        embeddedResources.Add(resource);
                        sourceAssemblies.Add(resourcePathHash, assembly);
                        Debug.WriteLine($"Resource {resourcePath} from {assembly.GetName()} was imported");
                    }
                }
            }
        }

        public override void Dispose()
        {
            foreach (EmbeddedResource resource in embeddedResources)
            {
                resource.Dispose();
            }

            embeddedResources.Dispose();
            fileQuery.Dispose();
            base.Dispose();
        }

        private void Update(DataUpdate e)
        {
            Update();
        }

        /// <summary>
        /// Iterates over all entities with the <see cref="IsDataRequest"/> component and attempts
        /// to import the data at its address.
        /// </summary>
        private void Update()
        {
            foreach (EntityID entity in world.GetAll<IsDataRequest>())
            {
                ref IsDataRequest import = ref world.GetComponentRef<IsDataRequest>(entity);
                if (import.changed)
                {
                    import.changed = false;
                    if (!TryImport(world, entity, import.address))
                    {
                        throw new NullReferenceException($"No data found to import from {import.address}");
                    }

                    Debug.WriteLine($"Data imported from {import.address} onto {entity}");
                }
            }
        }

        private bool TryImport(World world, EntityID entity, FixedString address)
        {
            Span<char> buffer = stackalloc char[address.Length];
            address.CopyTo(buffer);
            if (TryImport(buffer, out BinaryReader reader))
            {
                if (!world.ContainsCollection<byte>(entity))
                {
                    world.CreateCollection<byte>(entity);
                }

                UnmanagedList<byte> data = world.GetCollection<byte>(entity);
                data.Clear();
                data.AddRange(reader.AsSpan());
                reader.Dispose();
                return true;
            }
            else return false;
        }

        public bool TryImport(FixedString address, out BinaryReader newReader)
        {
            Span<char> buffer = stackalloc char[address.Length];
            address.CopyTo(buffer);
            return TryImport(buffer, out newReader);
        }

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// </summary>
        public bool TryImport(ReadOnlySpan<char> address, out BinaryReader newReader)
        {
            //search embedded resources
            for (uint i = 0; i < embeddedResources.Count; i++)
            {
                EmbeddedResource resource = embeddedResources[i];
                if (resource.Equals(address))
                {
                    newReader = new(resource.reader);
                    return true;
                }
            }

            //search world
            fileQuery.Fill();
            foreach (Query<IsData>.Result result in fileQuery)
            {
                IsData file = result.Component1;
                if (file.name.Equals(address))
                {
                    UnmanagedList<byte> fileData = world.GetCollection<byte>(result.entity);
                    newReader = new(fileData.AsSpan());
                    return true;
                }
            }

            //search system
            string addressString = address.ToString();
            if (!System.IO.File.Exists(addressString))
            {
                newReader = default;
                return false;
            }

            using System.IO.FileStream fileStream = new(addressString, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            newReader = new(fileStream);
            return true;
        }
    }
}
