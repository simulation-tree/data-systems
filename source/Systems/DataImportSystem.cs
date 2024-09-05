using Data.Components;
using Data.Events;
using Simulation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Systems
{
    public class DataImportSystem : SystemBase
    {
        private readonly ComponentQuery<IsDataRequest> requestQuery;
        private readonly ComponentQuery<IsDataSource> fileQuery;
        private readonly UnmanagedList<uint> loadingEntities;
        private readonly UnmanagedDictionary<uint, uint> dataVersions;
        private readonly ConcurrentQueue<Operation> operations;

        private UnmanagedList<BinaryReader> embeddedResources;
        private UnmanagedList<Address> embeddedAddresses;

        public DataImportSystem(World world) : base(world)
        {
            requestQuery = new();
            fileQuery = new();
            loadingEntities = new();
            dataVersions = new();
            operations = new();
            Subscribe<DataUpdate>(Update);
        }

        public unsafe override void Dispose()
        {
            while (operations.TryDequeue(out Operation operation))
            {
                operation.Dispose();
            }

            dataVersions.Dispose();
            loadingEntities.Dispose();

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
            base.Dispose();
        }

        private void Update(DataUpdate e)
        {
            Update();
            PerformInstructions();
        }

        /// <summary>
        /// Iterates over all entities with the <see cref="IsDataRequest"/> component and attempts
        /// to import the data at its address.
        /// </summary>
        private void Update()
        {
            requestQuery.Update(world);
            foreach (var x in requestQuery)
            {
                IsDataRequest request = x.Component1;
                uint entity = x.entity;
                bool sourceChanged = false;
                uint requestEntity = x.entity;
                if (!dataVersions.ContainsKey(requestEntity))
                {
                    sourceChanged = true;
                }
                else
                {
                    sourceChanged = dataVersions[requestEntity] != request.version;
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
                        dataVersions.AddOrSet(requestEntity, request.version);
                    }
                    else
                    {
                        Debug.WriteLine($"Data request for `{requestEntity}` with address `{request.address}` failed, data not found");
                    }
                }
            }
        }

        private unsafe void PerformInstructions()
        {
            while (operations.TryDequeue(out Operation operation))
            {
                world.Perform(operation);
                operation.Dispose();
            }
        }

        private unsafe bool TryLoadDataOntoEntity((uint entity, FixedString address) input)
        {
            uint entity = input.entity;
            FixedString address = input.address;
            USpan<char> buffer = stackalloc char[(int)FixedString.MaxLength];
            uint length = address.CopyTo(buffer);
            buffer = buffer.Slice(0, length);
            if (TryImport(buffer, out BinaryReader reader))
            {
                Operation operation = new();
                operation.SelectEntity(entity);

                //load the bytes onto the entity
                if (!world.ContainsArray<byte>(entity))
                {
                    operation.CreateArray<byte>(reader.GetBytes());
                }
                else
                {
                    USpan<byte> readData = reader.GetBytes();
                    operation.ResizeArray<byte>(readData.length);
                    operation.SetArrayElement(0, readData);
                }

                reader.Dispose();

                //increment data version
                if (world.TryGetComponent(entity, out IsData data))
                {
                    data.version++;
                    operation.SetComponent(data);
                }
                else
                {
                    operation.AddComponent(new IsData());
                }

                operations.Enqueue(operation);
                return true;
            }
            else
            {
                return false;
            }
        }

        //[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        [UnconditionalSuppressMessage("Aot", "IL2026")]
        private void FindAllEmbeddedResources()
        {
            //todo: efficiency: skip loading all embedded resources? its very taxing on startup time and it stores the data
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
        private bool TryImport(USpan<char> address, out BinaryReader newReader)
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
