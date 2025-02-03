using Collections;
using Data.Components;
using Data.Messages;
using Simulation;
using Simulation.Functions;
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
            handlers[0] = MessageHandler.Create<HandleDataRequest>(new(&HandleDataRequest));
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
            ComponentQuery<IsDataRequest> requestQuery = new(world);
            requestQuery.ExcludeDisabled(true);
            foreach (var r in requestQuery)
            {
                ref IsDataRequest request = ref r.component1;
                TryLoad(delta, new Entity(world, r.entity), ref request);
            }

            PerformInstructions(world);
        }

        private void TryLoad(TimeSpan delta, Entity entity, ref IsDataRequest request)
        {
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
                    task = ref tasks.Add(entity, new LoadingTask(DateTime.UtcNow));
                }

                if (TryLoad(entity, request.address, true))
                {
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
                world.Perform(operation);
                operation.Dispose();
            }
        }

        private readonly unsafe bool TryLoad(Entity entity, Address address, bool commitOperation)
        {
            World world = entity.world;
            if (TryLoad(world, address, out BinaryReader newReader))
            {
                Schema schema = world.Schema;
                USpan<BinaryData> readData = newReader.GetBytes().As<BinaryData>();
                if (commitOperation)
                {
                    Operation operation = new();
                    Operation.SelectedEntity selectedEntity = operation.SelectEntity(entity);

                    //load the bytes onto the entity
                    if (!entity.ContainsArray<BinaryData>())
                    {
                        selectedEntity.CreateArray(readData, schema);
                    }
                    else
                    {
                        selectedEntity.ResizeArray<BinaryData>(readData.Length, schema);
                        selectedEntity.SetArrayElements(0, readData, schema);
                    }

                    operations.Push(operation);
                }
                else
                {
                    if (entity.ContainsArray<BinaryData>())
                    {
                        USpan<BinaryData> existingArray = entity.ResizeArray<BinaryData>(readData.Length);
                        readData.CopyTo(existingArray);
                    }
                    else
                    {
                        entity.CreateArray(readData);
                    }
                }

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
        private static bool TryLoad(World world, Address address, out BinaryReader newReader)
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

        private static bool TryLoadFromWorld(World world, Address address, out BinaryReader newReader)
        {
            ComponentQuery<IsDataSource> sourceQuery = new(world);
            sourceQuery.ExcludeDisabled(true);
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

        private static bool TryLoadFromFileSystem(Address address, out BinaryReader newReader)
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
        private static HandleMessage.Boolean HandleDataRequest(SystemContainer container, World world, Allocation messageAllocation)
        {
            ref DataImportSystem system = ref container.Read<DataImportSystem>();
            ref HandleDataRequest message = ref messageAllocation.Read<HandleDataRequest>();
            Address address = new(message.address);
            if (system.TryLoad(message.entity, address, false))
            {
                message = message.BecomeLoaded();
            }

            return true;
        }
    }
}