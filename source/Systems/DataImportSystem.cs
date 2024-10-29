using Data.Components;
using Simulation;
using Simulation.Functions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Systems
{
    public struct DataImportSystem : ISystem
    {
        private readonly ComponentQuery<IsDataRequest> requestQuery;
        private readonly ComponentQuery<IsDataSource> fileQuery;
        private readonly UnmanagedDictionary<Entity, uint> dataVersions;
        private readonly UnmanagedList<Operation> operations;

        private UnmanagedList<BinaryReader> embeddedResources;
        private UnmanagedList<Address> embeddedAddresses;

        readonly unsafe InitializeFunction ISystem.Initialize => new(&Initialize);
        readonly unsafe IterateFunction ISystem.Update => new(&Update);
        readonly unsafe FinalizeFunction ISystem.Finalize => new(&Finalize);

        [UnmanagedCallersOnly]
        private static void Initialize(SystemContainer container, World world)
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
        private static void Finalize(SystemContainer container, World world)
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

        private void CleanUp()
        {
            while (operations.Count > 0)
            {
                Operation operation = operations.RemoveAt(0);
                operation.Dispose();
            }

            operations.Dispose();
            dataVersions.Dispose();

            if (embeddedResources != default)
            {
                foreach (BinaryReader resource in embeddedResources)
                {
                    resource.Dispose();
                }

                embeddedResources.Dispose();
                embeddedAddresses.Dispose();
            }

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
                bool sourceChanged = false;
                Entity entity = new(world, x.entity);
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
                    if (embeddedResources == default)
                    {
                        FindAllEmbeddedResources();
                    }

                    //ThreadPool.QueueUserWorkItem(UpdateMeshReferencesOnModelEntity, modelEntity, false);
                    if (TryLoadDataOntoEntity((entity, request.address)))
                    {
                        dataVersions.AddOrSet(entity, request.version);
                    }
                    else
                    {
                        Debug.WriteLine($"Data request for `{entity}` with address `{request.address}` failed, data not found");
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
            USpan<char> buffer = stackalloc char[(int)FixedString.MaxLength];
            uint length = address.CopyTo(buffer);
            buffer = buffer.Slice(0, length);
            if (TryImport(world, buffer, out BinaryReader reader))
            {
                Operation operation = new();
                operation.SelectEntity(entity);

                //load the bytes onto the entity
                if (!entity.ContainsArray<byte>())
                {
                    operation.CreateArray<byte>(reader.GetBytes());
                }
                else
                {
                    USpan<byte> readData = reader.GetBytes();
                    operation.ResizeArray<byte>(readData.Length);
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

        [UnconditionalSuppressMessage("Aot", "IL2026")]
        private void FindAllEmbeddedResources()
        {
            embeddedResources = new();
            embeddedAddresses = new();
            Dictionary<int, Assembly> sourceAssemblies = [];
            Dictionary<string, Assembly> allAssemblies = new();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (IgnoredAssembly(assemblyName)) continue;

                allAssemblies.TryAdd(assemblyName, assembly);
                foreach (AssemblyName referencedAssemblyName in assembly.GetReferencedAssemblies())
                {
                    assemblyName = referencedAssemblyName.Name ?? string.Empty;
                    if (IgnoredAssembly(assemblyName)) continue;

                    if (allAssemblies.ContainsKey(assemblyName))
                    {
                        continue;
                    }

                    try
                    {
                        Assembly referencedAssembly = Assembly.Load(referencedAssemblyName);
                        allAssemblies.Add(assemblyName, referencedAssembly);
                    }
                    catch (Exception)
                    {
                        //ignore
                    }
                }
            }

            static bool IgnoredAssembly(string assemblyName)
            {
                if (assemblyName.StartsWith("TestCentric"))
                {
                    return true;
                }

                if (assemblyName == "System.Private.Xml")
                {
                    return true;
                }

                return false;
            }

            foreach (KeyValuePair<string, Assembly> pair in allAssemblies)
            {
                Assembly assembly = pair.Value;
                string assemblyName = pair.Key;
                string[] resources = assembly.GetManifestResourceNames();
                foreach (string resourcePath in resources)
                {
                    if (resourcePath.StartsWith("ILLink"))
                    {
                        continue;
                    }

                    if (resourcePath.StartsWith("Microsoft.VisualStudio.TestPlatform"))
                    {
                        continue;
                    }

                    if (resourcePath.StartsWith("FxResources"))
                    {
                        continue;
                    }

                    FixedString resourcePathText = resourcePath;
                    int resourcePathHash = resourcePathText.GetHashCode();
                    if (sourceAssemblies.TryGetValue(resourcePathHash, out Assembly? existing))
                    {
                        Debug.WriteLine($"Duplicate resource with same address at `{resourcePathText}` from `{assemblyName}` was ignored, data from `{existing.GetName()}` is used instead");
                    }
                    else
                    {
                        System.IO.Stream stream = assembly.GetManifestResourceStream(resourcePath) ?? throw new Exception("Impossible");
                        stream.Position = 0;
                        BinaryReader reader = new(stream);
                        embeddedResources.Add(reader);
                        embeddedAddresses.Add(new Address(resourcePathText));
                        sourceAssemblies.Add(resourcePathHash, assembly);
                        Debug.WriteLine($"Registered embedded resource at `{resourcePathText}` from `{assemblyName}`");
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// </summary>
        private readonly bool TryImport(World world, USpan<char> address, out BinaryReader newReader)
        {
            //search embedded resources
            for (uint i = 0; i < embeddedAddresses.Count; i++)
            {
                Address embeddedAddress = embeddedAddresses[i];
                if (embeddedAddress.Matches(address))
                {
                    newReader = new(embeddedResources[i]);
                    Debug.WriteLine($"Loaded data from embedded resource at `{embeddedAddress.ToString()}`");
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
                    USpan<byte> fileData = world.GetArray<byte>(result.entity);
                    newReader = new(fileData);
                    Debug.WriteLine($"Loaded data from entity at `{file.address.ToString()}`");
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
                Debug.WriteLine($"Loaded data from file system at `{addressStr}`");
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
