using Collections.Generic;
using Data.Components;
using Data.Messages;
using Simulation;
using System;
using System.Diagnostics;
using Unmanaged;
using Worlds;

namespace Data.Systems
{
    public partial class DataImportSystem : ISystem, IDisposable, IListener<LoadData>
    {
        private readonly Dictionary<Entity, LoadingTask> tasks;
        private readonly Stack<Operation> operations;

        public DataImportSystem()
        {
            tasks = new(4);
            operations = new(4);
        }

        public void Dispose()
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Dispose();
            }

            operations.Dispose();
            tasks.Dispose();
        }

        void ISystem.Update(Simulator simulator, double deltaTime)
        {
            World world = simulator.world;
            Schema schema = world.Schema;
            int dataComponent = schema.GetComponentType<IsDataRequest>();
            int sourceType = schema.GetComponentType<IsDataSource>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(dataComponent))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsDataRequest> components = chunk.GetComponents<IsDataRequest>(dataComponent);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsDataRequest request = ref components[i];
                        Entity entity = new(world, entities[i]);
                        if (request.status == RequestStatus.Submitted)
                        {
                            request.status = RequestStatus.Loading;
                            Trace.WriteLine($"Started fetching data at `{request.address}` for `{entity}`");
                        }

                        if (request.status == RequestStatus.Loading)
                        {
                            ref LoadingTask task = ref tasks.TryGetValue(entity, out bool contains);
                            if (!contains)
                            {
                                task = ref tasks.Add(entity);
                                task = new(DateTime.UtcNow);
                            }

                            if (TryLoad(entity, request.address, sourceType, out Operation operation))
                            {
                                operations.Push(operation);
                                request.status = RequestStatus.Loaded;
                            }
                            else
                            {
                                task.duration += deltaTime;
                                if (task.duration >= request.timeout)
                                {
                                    request.status = RequestStatus.NotFound;
                                    Trace.WriteLine($"Data request for `{entity}` with address `{request.address}` failed, data not found");
                                }
                                else
                                {
                                    //keep waiting
                                }
                            }
                        }
                    }
                }
            }

            PerformInstructions(world);
        }

        void IListener<LoadData>.Receive(ref LoadData request)
        {
            if (request.status == RequestStatus.Uninitialized)
            {
                request.status = RequestStatus.Loading;
            }

            if (request.status == RequestStatus.Loading)
            {
                int sourceType = request.world.Schema.GetComponentType<IsDataSource>();
                if (TryLoad(request.world, request.address, sourceType, out ByteReader newReader))
                {
                    request.Load(newReader);
                }
                else
                {
                    request.NotFound();
                }
            }
        }

        private void PerformInstructions(World world)
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        private static bool TryLoad(Entity entity, Address address, int sourceType, out Operation operation)
        {
            World world = entity.world;
            if (TryLoad(world, address, sourceType, out ByteReader newReader))
            {
                Span<byte> readData = newReader.GetBytes();
                operation = new();
                operation.SelectEntity(entity);
                operation.CreateOrSetArray(readData.As<byte, DataByte>());
                newReader.Dispose();
                return true;
            }
            else
            {
                operation = default;
                return false;
            }
        }

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// <para>
        /// The output <paramref name="newReader"/> must be disposed after completing its use.
        /// </para>
        /// </summary>
        private static bool TryLoad(World world, Address address, int sourceType, out ByteReader newReader)
        {
            if (EmbeddedResourceRegistry.TryGet(address, out EmbeddedResource embeddedResource))
            {
                Trace.WriteLine($"Loaded data from embedded resource at `{address}`");
                newReader = embeddedResource.CreateByteReader();
                return true;
            }

            if (TryLoadFromWorld(world, address, sourceType, out newReader))
            {
                return true;
            }

            return TryLoadFromFileSystem(address, out newReader);
        }

        private static bool TryLoadFromWorld(World world, Address address, int sourceType, out ByteReader newReader)
        {
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(sourceType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsDataSource> components = chunk.GetComponents<IsDataSource>(sourceType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsDataSource source = ref components[i];
                        if (source.address.Matches(address))
                        {
                            uint entity = entities[i];
                            Span<byte> fileData = world.GetArray<DataByte>(entity).AsSpan<byte>();
                            newReader = new(fileData);
                            Trace.WriteLine($"Loaded data from entity `{entity}` for address `{source.address}`");
                            return true;
                        }
                    }
                }
            }

            newReader = default;
            return false;
        }

        private static bool TryLoadFromFileSystem(Address address, out ByteReader newReader)
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