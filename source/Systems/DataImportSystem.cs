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
        private readonly ComponentQuery<IsDataRequest> requestQuery;
        private readonly ComponentQuery<IsDataSource> fileQuery;
        private readonly Dictionary<Entity, uint> dataVersions;
        private readonly List<Operation> operations;

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            Update(world);
            PerformInstructions(world);
        }

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                CleanUp();
            }
        }

        public DataImportSystem()
        {
            requestQuery = new();
            fileQuery = new();
            dataVersions = new();
            operations = new();
        }

        private readonly void CleanUp()
        {
            while (operations.Count > 0)
            {
                Operation operation = operations.RemoveAt(0);
                operation.Dispose();
            }

            operations.Dispose();
            dataVersions.Dispose();
            fileQuery.Dispose();
            requestQuery.Dispose();
        }

        /// <summary>
        /// Iterates over all entities with the <see cref="IsDataRequest"/> component and attempts
        /// to import the data at its address.
        /// </summary>
        private void Update(World world)
        {
            requestQuery.Update(world);
            foreach (var x in requestQuery)
            {
                IsDataRequest request = x.Component1;
                Entity entity = new(world, x.entity);
                bool sourceChanged;
                if (!dataVersions.ContainsKey(entity))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = dataVersions[entity] != request.version;
                }

                if (sourceChanged)
                {
                    Trace.WriteLine($"Searching for data at `{request.address}` for `{x.entity}`");
                    if (TryLoadDataOntoEntity((entity, request.address)))
                    {
                        dataVersions.AddOrSet(entity, request.version);
                    }
                    else
                    {
                        Trace.WriteLine($"Data request for `{entity}` with address `{request.address}` failed, data not found");
                    }
                }
            }
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

        private readonly unsafe bool TryLoadDataOntoEntity((Entity entity, FixedString address) input)
        {
            Entity entity = input.entity;
            World world = entity.GetWorld();
            FixedString address = input.address;
            USpan<char> buffer = stackalloc char[(int)FixedString.Capacity];
            uint length = address.CopyTo(buffer);
            buffer = buffer.Slice(0, length);
            if (TryImport(world, buffer, out BinaryReader reader))
            {
                Operation operation = new();
                Operation.SelectedEntity selectedEntity = operation.SelectEntity(entity);

                //load the bytes onto the entity
                USpan<BinaryData> readData = reader.GetBytes().As<BinaryData>();
                if (!entity.ContainsArray<BinaryData>())
                {
                    selectedEntity.CreateArray(readData);
                }
                else
                {
                    selectedEntity.ResizeArray<BinaryData>(readData.Length);
                    selectedEntity.SetArrayElements(0, readData);
                }

                reader.Dispose();

                //increment data version
                if (entity.TryGetComponent(out IsData data))
                {
                    data.version++;
                    selectedEntity.SetComponent(data);
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
        private readonly bool TryImport(World world, USpan<char> address, out BinaryReader newReader)
        {
            //search embedded resources
            foreach (EmbeddedAddress embeddedResource in EmbeddedAddress.All)
            {
                if (embeddedResource.address.Matches(address))
                {
                    string resourcePath = $"{embeddedResource.assembly.GetName().Name}.{embeddedResource.address.ToString().Replace('/', '.')}";
                    System.IO.Stream stream = embeddedResource.assembly.GetManifestResourceStream(resourcePath) ?? throw new Exception($"Embedded resource at `{resourcePath}` could not be found");
                    stream.Position = 0;
                    newReader = new(stream);
                    Trace.WriteLine($"Loaded data from embedded resource at `{embeddedResource.address}`");
                    return true;
                }
            }

            //search world
            fileQuery.Update(world);
            foreach (var result in fileQuery)
            {
                IsDataSource file = result.Component1;
                if (new Address(file.address).Matches(address))
                {
                    USpan<byte> fileData = world.GetArray<BinaryData>(result.entity).As<byte>();
                    newReader = new(fileData);
                    Trace.WriteLine($"Loaded data from entity `{result.entity}` for address `{file.address}`");
                    return true;
                }
            }

            return TryLoadFromFileSystem(address, out newReader);
        }

        private static bool TryLoadFromFileSystem(USpan<char> address, out BinaryReader reader)
        {
            string addressStr = address.ToString();
            if (System.IO.File.Exists(addressStr))
            {
                using System.IO.FileStream fileStream = new(addressStr, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                reader = new(fileStream);
                Trace.WriteLine($"Loaded data from file system at `{addressStr}`");
                return true;
            }
            else
            {
                reader = default;
                return false;
            }
        }
    }
}
