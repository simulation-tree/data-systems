using Collections.Generic;
using Data.Components;
using Data.Messages;
using Simulation;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unmanaged;
using Worlds;

namespace Data.Systems
{
    public readonly partial struct DataImportSystem : ISystem
    {
        private readonly Dictionary<Entity, LoadingTask> tasks;
        private readonly Stack<Operation> operations;

        private DataImportSystem(Dictionary<Entity, LoadingTask> tasks, Stack<Operation> operations)
        {
            this.tasks = tasks;
            this.operations = operations;
        }

        unsafe readonly uint ISystem.GetMessageHandlers(USpan<MessageHandler> handlers)
        {
            handlers[0] = MessageHandler.Create<LoadData>(new(&HandleDataRequest));
            return 1;
        }

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                Dictionary<Entity, LoadingTask> tasks = new();
                Stack<Operation> operations = new();
                systemContainer.Write(new DataImportSystem(tasks, operations));
            }
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            ComponentType dataComponent = world.Schema.GetComponentType<IsDataRequest>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(dataComponent))
                {
                    USpan<uint> entities = chunk.Entities;
                    USpan<IsDataRequest> components = chunk.GetComponents<IsDataRequest>(dataComponent);
                    for (uint i = 0; i < entities.Length; i++)
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

                            if (TryLoad(entity, request.address, out Operation operation))
                            {
                                operations.Push(operation);
                                request.status = RequestStatus.Loaded;
                            }
                            else
                            {
                                task.duration += delta;
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

        void ISystem.Finish(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                while (operations.TryPop(out Operation operation))
                {
                    operation.Dispose();
                }

                operations.Dispose();
                tasks.Dispose();
            }
        }

        private readonly void PerformInstructions(World world)
        {
            while (operations.TryPop(out Operation operation))
            {
                operation.Perform(world);
                operation.Dispose();
            }
        }

        private static bool TryLoad(Entity entity, Address address, out Operation operation)
        {
            World world = entity.world;
            if (TryLoad(world, address, out ByteReader newReader))
            {
                USpan<byte> readData = newReader.GetBytes();
                operation = new();
                operation.SelectEntity(entity);
                operation.CreateOrSetArray(readData.As<BinaryData>());
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
        private static bool TryLoad(World world, Address address, out ByteReader newReader)
        {
            if (EmbeddedResourceRegistry.TryGet(address, out EmbeddedResource embeddedResource))
            {
                Trace.WriteLine($"Loaded data from embedded resource at `{address}`");
                newReader = embeddedResource.CreateBinaryReader();
                return true;
            }

            if (TryLoadFromWorld(world, address, out newReader))
            {
                return true;
            }

            return TryLoadFromFileSystem(address, out newReader);
        }

        private static bool TryLoadFromWorld(World world, Address address, out ByteReader newReader)
        {
            ComponentType sourceType = world.Schema.GetComponentType<IsDataSource>();
            foreach (Chunk chunk in world.Chunks)
            {
                if (chunk.Definition.ContainsComponent(sourceType))
                {
                    USpan<uint> entities = chunk.Entities;
                    USpan<IsDataSource> components = chunk.GetComponents<IsDataSource>(sourceType);
                    for (uint i = 0; i < entities.Length; i++)
                    {
                        ref IsDataSource source = ref components[i];
                        if (source.address.Matches(address))
                        {
                            uint entity = entities[i];
                            USpan<byte> fileData = world.GetArray<BinaryData>(entity).AsSpan<byte>();
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

        [UnmanagedCallersOnly]
        private static StatusCode HandleDataRequest(SystemContainer container, World world, MemoryAddress messageAllocation)
        {
            ref LoadData message = ref messageAllocation.Read<LoadData>();
            if (!message.IsLoaded)
            {
                if (TryLoad(message.world, message.address, out ByteReader newReader))
                {
                    message = message.BecomeLoaded(newReader);
                    return StatusCode.Success(0);
                }
                else
                {
                    Trace.TraceError($"Failed to load data from address `{message.address}`, data not found");
                    return StatusCode.Failure(0);
                }
            }

            return StatusCode.Continue;
        }
    }
}