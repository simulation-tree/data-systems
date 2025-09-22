using Collections.Generic;
using Data.Components;
using Data.Messages;
using Simulation;
using System;
using System.Diagnostics;
using System.IO;
using Unmanaged;
using Worlds;

namespace Data.Systems
{
    public sealed partial class DataImportSystem : SystemBase, IListener<DataUpdate>, IListener<LoadData>
    {
        private readonly World world;
        private readonly Dictionary<uint, LoadingTask> tasks;
        private readonly Operation operation;
        private readonly int requestType;
        private readonly int sourceType;
        private readonly int byteArrayType;
        private double time;

        public DataImportSystem(Simulator simulator, World world) : base(simulator)
        {
            this.world = world;
            tasks = new(4);
            operation = new(world);

            Schema schema = world.Schema;
            requestType = schema.GetComponentType<IsDataRequest>();
            sourceType = schema.GetComponentType<IsDataSource>();
            byteArrayType = schema.GetArrayType<DataByte>();
        }

        public override void Dispose()
        {
            operation.Dispose();
            tasks.Dispose();
        }

        void IListener<DataUpdate>.Receive(ref DataUpdate message)
        {
            time += message.deltaTime;
            ReadOnlySpan<Chunk> chunks = world.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                Chunk chunk = chunks[c];
                if (chunk.ComponentTypes.Contains(requestType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsDataRequest> components = chunk.GetComponents<IsDataRequest>(requestType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsDataRequest request = ref components[i];
                        uint entity = entities[i];

                        //start loading this request
                        if (request.status == RequestStatus.Submitted)
                        {
                            request.status = RequestStatus.Loading;
                            Trace.WriteLine($"Started fetching data at `{request.address}` for `{entity}`");
                        }

                        //process the request
                        if (request.status == RequestStatus.Loading)
                        {
                            ref LoadingTask task = ref tasks.TryGetValue(entity, out bool contains);
                            if (!contains)
                            {
                                task = ref tasks.Add(entity);
                                task = new(time);
                            }

                            if (TryLoad(entity, request.address))
                            {
                                //finished loading
                                request.status = RequestStatus.Loaded;
                                task.duration = 0;
                            }
                            else
                            {
                                task.duration += message.deltaTime;
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

            if (operation.TryPerform())
            {
                operation.Reset();
            }
        }

        void IListener<LoadData>.Receive(ref LoadData request)
        {
            if (TryLoad(request.address, out ByteReader newReader))
            {
                request.Found(newReader);
            }
            else
            {
                request.NotFound();
            }
        }

        private bool TryLoad(uint entity, Address address)
        {
            if (TryLoad(address, out ByteReader newReader))
            {
                Span<byte> readData = newReader.GetBytes();
                operation.SetSelectedEntity(entity);
                operation.CreateOrSetArray(readData, byteArrayType);
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
        private bool TryLoad(Address address, out ByteReader newReader)
        {
            if (EmbeddedResourceRegistry.TryGet(address, out EmbeddedResource embeddedResource))
            {
                Trace.WriteLine($"Loaded data from embedded resource at `{address}`");
                newReader = embeddedResource.CreateByteReader();
                return true;
            }

            if (TryLoadFromWorld(address, out newReader))
            {
                return true;
            }

            return TryLoadFromFileSystem(address, out newReader);
        }

        private bool TryLoadFromWorld(Address address, out ByteReader newReader)
        {
            ReadOnlySpan<Chunk> chunks = world.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                Chunk chunk = chunks[c];
                if (chunk.ComponentTypes.Contains(sourceType))
                {
                    ReadOnlySpan<uint> entities = chunk.Entities;
                    ComponentEnumerator<IsDataSource> components = chunk.GetComponents<IsDataSource>(sourceType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        ref IsDataSource source = ref components[i];
                        if (source.address.Matches(address))
                        {
                            uint entity = entities[i];
                            Span<byte> fileData = world.GetArray<byte>(entity, byteArrayType);
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
            if (File.Exists(addressStr))
            {
                using FileStream fileStream = new(addressStr, FileMode.Open, FileAccess.Read);
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