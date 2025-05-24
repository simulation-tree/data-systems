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
        private readonly Dictionary<uint, LoadingTask> tasks;
        private readonly Operation operation;
        private readonly int dataComponent;
        private readonly int sourceType;
        private double time;

        public DataImportSystem(Simulator simulator)
        {
            tasks = new(4);
            operation = new();

            World world = simulator.world;
            Schema schema = world.Schema;
            dataComponent = schema.GetComponentType<IsDataRequest>();
            sourceType = schema.GetComponentType<IsDataSource>();
        }

        public void Dispose()
        {
            operation.Dispose();
            tasks.Dispose();
        }

        void ISystem.Update(Simulator simulator, double deltaTime)
        {
            time += deltaTime;
            World world = simulator.world;
            Schema schema = world.Schema;
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(dataComponent))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsDataRequest> components = chunk.GetComponents<IsDataRequest>(dataComponent);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsDataRequest request = ref components[i];
                        uint entity = entities[i];
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
                                task = new(time);
                            }

                            if (TryLoad(world, entity, request.address, sourceType, operation))
                            {
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

            if (operation.Count > 0)
            {
                operation.Perform(world);
                operation.Reset();
            }
        }

        void IListener<LoadData>.Receive(ref LoadData request)
        {
            if (TryLoad(request.world, request.address, sourceType, out ByteReader newReader))
            {
                request.Found(newReader);
            }
            else
            {
                request.NotFound();
            }
        }

        private static bool TryLoad(World world, uint entity, Address address, int sourceType, Operation operation)
        {
            if (TryLoad(world, address, sourceType, out ByteReader newReader))
            {
                Span<byte> readData = newReader.GetBytes();
                operation.SetSelectedEntity(entity);
                operation.CreateOrSetArray(readData.As<byte, DataByte>());
                newReader.Dispose();
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