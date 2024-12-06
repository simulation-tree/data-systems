using Collections;
using Data.Components;
using Simulation;
using System;
using System.Diagnostics;
using Unmanaged;
using Worlds;

namespace Data.Systems
{
    public readonly partial struct DataImportSystem : ISystem
    {
        private readonly Dictionary<Entity, uint> dataVersions;
        private readonly List<Operation> operations;

        public DataImportSystem()
        {
            dataVersions = new();
            operations = new();
        }

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            ComponentQuery<IsDataRequest> requestQuery = new(world);
            foreach (var r in requestQuery)
            {
                bool sourceChanged;
                ref IsDataRequest component = ref r.component1;
                Entity entity = new(world, r.entity);
                if (!dataVersions.ContainsKey(entity))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = dataVersions[entity] != component.version;
                }

                if (sourceChanged)
                {
                    Trace.WriteLine($"Searching for data at `{component.address}` for `{entity}`");
                    if (TryLoadDataOntoEntity(entity, component.address))
                    {
                        dataVersions.AddOrSet(entity, component.version);
                    }
                    else
                    {
                        Trace.WriteLine($"Data request for `{entity}` with address `{component.address}` failed, data not found");
                    }
                }
            }

            PerformInstructions(world);
        }

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
        }

        void IDisposable.Dispose()
        {
            while (operations.Count > 0)
            {
                Operation operation = operations.RemoveAt(0);
                operation.Dispose();
            }

            operations.Dispose();
            dataVersions.Dispose();
        }

        private readonly void PerformInstructions(World world)
        {
            while (operations.Count > 0)
            {
                Operation operation = operations.RemoveAt(0);
                world.Perform(operation);
                operation.Dispose();
            }
        }

        private readonly unsafe bool TryLoadDataOntoEntity(Entity entity, FixedString address)
        {
            World world = entity.GetWorld();
            if (TryLoad(world, address, out BinaryReader newReader))
            {
                Operation operation = new();
                Operation.SelectedEntity selectedEntity = operation.SelectEntity(entity);

                //load the bytes onto the entity
                USpan<BinaryData> readData = newReader.GetBytes().As<BinaryData>();
                if (!entity.ContainsArray<BinaryData>())
                {
                    selectedEntity.CreateArray(readData);
                }
                else
                {
                    selectedEntity.ResizeArray<BinaryData>(readData.Length);
                    selectedEntity.SetArrayElements(0, readData);
                }

                newReader.Dispose();

                //increment data version
                ref IsData data = ref entity.TryGetComponent<IsData>(out bool contains);
                if (contains)
                {
                    selectedEntity.SetComponent(new IsData(data.version + 1));
                }
                else
                {
                    selectedEntity.AddComponent(new IsData());
                }

                operations.Add(operation);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// <para>
        /// The output <paramref name="newReader"/> must be disposed after completing its use.
        /// </para>
        /// </summary>
        private static bool TryLoad(World world, FixedString address, out BinaryReader newReader)
        {
            if (TryLoadFromEmbeddedResources(address, out newReader))
            {
                return true;
            }

            if (TryLoadFromWorld(world, address, out newReader))
            {
                return true;
            }

            return TryLoadFromFileSystem(address, out newReader);
        }

        private static bool TryLoadFromEmbeddedResources(FixedString address, out BinaryReader newReader)
        {
            foreach (EmbeddedAddress embeddedResource in EmbeddedAddress.All)
            {
                if (embeddedResource.address.Matches(address))
                {
                    string[] names = embeddedResource.assembly.GetManifestResourceNames();
                    string resourcePath = $"{embeddedResource.assembly.GetName().Name}.{embeddedResource.address.ToString().Replace('/', '.')}";
                    System.IO.Stream stream = embeddedResource.assembly.GetManifestResourceStream(resourcePath) ?? throw new Exception($"Embedded resource at `{resourcePath}` could not be found");
                    stream.Position = 0;
                    newReader = new(stream);
                    Trace.WriteLine($"Loaded data from embedded resource at `{embeddedResource.address}`");
                    return true;
                }
            }

            newReader = default;
            return false;
        }

        private static bool TryLoadFromWorld(World world, FixedString address, out BinaryReader newReader)
        {
            ComponentQuery<IsDataSource> sourceQuery = new(world);
            foreach (var r in sourceQuery)
            {
                ref IsDataSource source = ref r.component1;
                if (source.address.Matches(address))
                {
                    USpan<byte> fileData = world.GetArray<BinaryData>(r.entity).As<byte>();
                    newReader = new(fileData);
                    Trace.WriteLine($"Loaded data from entity `{r.entity}` for address `{source.address}`");
                    return true;
                }
            }

            newReader = default;
            return false;
        }

        private static bool TryLoadFromFileSystem(FixedString address, out BinaryReader newReader)
        {
            string addressStr = address.ToString();
            if (System.IO.File.Exists(addressStr))
            {
                using System.IO.FileStream fileStream = new(addressStr, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                newReader = new(fileStream);
                Trace.WriteLine($"Loaded data from file system at `{addressStr}`");
                return true;
            }
            else
            {
                newReader = default;
                return false;
            }
        }
    }
}
