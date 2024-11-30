using Collections;
using Data.Components;
using Simulation;
using Simulation.Functions;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unmanaged;
using Worlds;

namespace Data.Systems
{
    public readonly struct DataImportSystem : ISystem
    {
        private readonly ComponentQuery<IsDataRequest> requestQuery;
        private readonly ComponentQuery<IsDataSource> fileQuery;
        private readonly Dictionary<Entity, uint> dataVersions;
        private readonly List<Operation> operations;

        readonly unsafe StartSystem ISystem.Start => new(&Start);
        readonly unsafe UpdateSystem ISystem.Update => new(&Update);
        readonly unsafe FinishSystem ISystem.Finish => new(&Finish);

        [UnmanagedCallersOnly]
        private static void Start(SystemContainer container, World world)
        {
        }

        [UnmanagedCallersOnly]
        private static void Update(SystemContainer container, World world, TimeSpan delta)
        {
            ref DataImportSystem system = ref container.Read<DataImportSystem>();
            system.Update(world);
            system.PerformInstructions(world);
        }

        [UnmanagedCallersOnly]
        private static void Finish(SystemContainer container, World world)
        {
            if (container.World == world)
            {
                ref DataImportSystem system = ref container.Read<DataImportSystem>();
                system.CleanUp();
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
                operation.SelectEntity(entity);

                //load the bytes onto the entity
                USpan<BinaryData> readData = reader.GetBytes().As<BinaryData>();
                if (!entity.ContainsArray<BinaryData>())
                {
                    operation.CreateArray(readData);
                }
                else
                {
                    operation.ResizeArray<BinaryData>(readData.Length);
                    operation.SetArrayElements(0, readData);
                }

                reader.Dispose();

                //increment data version
                if (entity.TryGetComponent(out IsData data))
                {
                    data.version++;
                    operation.SetComponent(data);
                }
                else
                {
                    operation.AddComponent(new IsData());
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
        /// </summary>
        private readonly bool TryImport(World world, USpan<char> address, out BinaryReader newReader)
        {
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
