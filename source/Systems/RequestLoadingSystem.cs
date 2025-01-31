using Collections;
using Data;
using Data.Components;
using Requests.Components;
using Simulation;
using System;
using System.Diagnostics;
using System.Reflection;
using Unmanaged;
using Worlds;

namespace Requests.Systems
{
    public readonly partial struct RequestLoadingSystem : ISystem
    {
        private readonly Dictionary<Entity, uint> dataVersions;
        private readonly Stack<Operation> operations;

        private RequestLoadingSystem(Dictionary<Entity, uint> dataVersions, Stack<Operation> operations)
        {
            this.dataVersions = dataVersions;
            this.operations = operations;
        }

        void ISystem.Start(in SystemContainer systemContainer, in World world)
        {
            if (systemContainer.World == world)
            {
                Dictionary<Entity, uint> dataVersions = new();
                Stack<Operation> operations = new();
                systemContainer.Write(new RequestLoadingSystem(dataVersions, operations));
            }
        }

        void ISystem.Update(in SystemContainer systemContainer, in World world, in TimeSpan delta)
        {
            ComponentQuery<RequestComponent> requestQuery = new(world);
            requestQuery.ExcludeDisabled(true);
            foreach (var r in requestQuery)
            {
                ref RequestComponent component = ref r.component1;
                if (component.status == RequestStatus.Pending)
                {
                    bool versionChanged;
                    Entity entity = new(world, r.entity);
                    if (!dataVersions.ContainsKey(entity))
                    {
                        versionChanged = true;
                    }
                    else
                    {
                        versionChanged = dataVersions[entity] != component.version;
                    }

                    if (versionChanged)
                    {
                        Trace.WriteLine($"Searching for data at `{component.address}` for `{entity}`");
                        if (TryLoad(entity, component.address, component.version))
                        {
                            dataVersions.AddOrSet(entity, component.version);
                            component.status = RequestStatus.Loaded;
                        }
                        else
                        {
                            Trace.WriteLine($"Data request for `{entity}` with address `{component.address}` failed, data not found");
                            component.status = RequestStatus.NotFound;
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
                dataVersions.Dispose();
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

        private readonly unsafe bool TryLoad(Entity entity, Address address, uint version)
        {
            World world = entity.GetWorld();
            if (TryLoad(world, address, out BinaryReader newReader))
            {
                Schema schema = world.Schema;
                Operation operation = new();
                Operation.SelectedEntity selectedEntity = operation.SelectEntity(entity);

                //load the bytes onto the entity
                USpan<BinaryData> readData = newReader.GetBytes().As<BinaryData>();
                if (!entity.ContainsArray<BinaryData>())
                {
                    selectedEntity.CreateArray(readData, schema);
                }
                else
                {
                    selectedEntity.ResizeArray<BinaryData>(readData.Length, schema);
                    selectedEntity.SetArrayElements(0, readData, schema);
                }

                newReader.Dispose();

                //increment data version
                ref DatumComponent data = ref entity.TryGetComponent<DatumComponent>(out bool contains);
                if (contains)
                {
                    selectedEntity.SetComponent(new DatumComponent(version, address), schema);
                }
                else
                {
                    selectedEntity.AddComponent(new DatumComponent(version, address), schema);
                }

                operations.Push(operation);
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

        private static bool TryLoadFromEmbeddedResources(Address address, out BinaryReader newReader)
        {
            foreach ((Assembly assembly, Address address) embeddedResource in EmbeddedResourceRegistry.All)
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

        private static bool TryLoadFromWorld(World world, Address address, out BinaryReader newReader)
        {
            ComponentQuery<DatumComponent> sourceQuery = new(world);
            sourceQuery.ExcludeDisabled(true);
            foreach (var r in sourceQuery)
            {
                ref DatumComponent source = ref r.component1;
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
    }
}
