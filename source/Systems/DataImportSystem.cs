using Data.Components;
using Data.Events;
using Simulation;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Systems
{
    public class DataImportSystem : SystemBase
    {
        private readonly Query<IsData> fileQuery;
        private UnmanagedList<BinaryReader> embeddedResources;
        private UnmanagedList<Address> embeddedAddresses;

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        public DataImportSystem(World world) : base(world)
        {
            Subscribe<DataUpdate>(Update);
            fileQuery = new(world);
        }

        public override void Dispose()
        {
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
            base.Dispose();
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void Update(DataUpdate e)
        {
            Update();
        }

        /// <summary>
        /// Iterates over all entities with the <see cref="IsDataRequest"/> component and attempts
        /// to import the data at its address.
        /// </summary>
        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void Update()
        {
            foreach (eint entity in world.GetAll<IsDataRequest>())
            {
                ref IsDataRequest import = ref world.GetComponentRef<IsDataRequest>(entity);
                if (import.changed)
                {
                    import.changed = false;
                    if (!TryImport(world, entity, import.address))
                    {
                        throw new RequestedDataNotFoundException($"Could not find data to import from `{import.address}`");
                    }
                }
            }
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void FindAllEmbeddedResources()
        {
            //todo: efficiency: skip loading all embedded resources, its very taxing on startup time
            embeddedResources = new();
            embeddedAddresses = new();
            Dictionary<int, Assembly> sourceAssemblies = [];
            Dictionary<string, Assembly> allAssemblies = new();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                allAssemblies.TryAdd(assemblyName, assembly);
                foreach (AssemblyName referencedAssemblyName in assembly.GetReferencedAssemblies())
                {
                    assemblyName = referencedAssemblyName.Name ?? string.Empty;
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

                    //ignore FxResources
                    if (resourcePath.StartsWith("FxResources"))
                    {
                        continue;
                    }

                    FixedString resourcePathText = new(resourcePath);
                    int resourcePathHash = resourcePathText.GetHashCode();
                    if (sourceAssemblies.TryGetValue(resourcePathHash, out Assembly? existing))
                    {
                        Console.WriteLine($"Duplicate resource with same address at `{resourcePathText}` from `{assemblyName}` was ignored, data from `{existing.GetName()}` is used instead");
                    }
                    else
                    {
                        System.IO.Stream stream = assembly.GetManifestResourceStream(resourcePath) ?? throw new Exception("Impossible");
                        stream.Position = 0;
                        BinaryReader reader = new(stream);
                        embeddedResources.Add(reader);
                        embeddedAddresses.Add(new Address(resourcePathText));
                        sourceAssemblies.Add(resourcePathHash, assembly);
                        Console.WriteLine($"Registered embedded resource at `{resourcePathText}` from `{assemblyName}`");
                    }
                }
            }
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private bool TryImport(World world, eint entity, FixedString address)
        {
            Span<char> buffer = stackalloc char[address.Length];
            address.CopyTo(buffer);
            if (TryImport(buffer, out BinaryReader reader))
            {
                if (!world.ContainsList<byte>(entity))
                {
                    world.CreateList<byte>(entity);
                }

                UnmanagedList<byte> data = world.GetList<byte>(entity);
                data.Clear();
                data.AddRange(reader.AsSpan());
                reader.Dispose();
                return true;
            }
            else return false;
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        public bool TryImport(FixedString address, out BinaryReader newReader)
        {
            Span<char> buffer = stackalloc char[address.Length];
            address.CopyTo(buffer);
            return TryImport(buffer, out newReader);
        }

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// </summary>
        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        public bool TryImport(ReadOnlySpan<char> address, out BinaryReader newReader)
        {
            if (embeddedResources == default)
            {
                FindAllEmbeddedResources();
            }

            //search embedded resources
            for (uint i = 0; i < embeddedAddresses.Count; i++)
            {
                Address embeddedAddress = embeddedAddresses[i];
                if (embeddedAddress.Matches(address))
                {
                    newReader = new(embeddedResources[i]);
                    return true;
                }
            }

            //search world
            fileQuery.Update();
            foreach (Query<IsData>.Result result in fileQuery)
            {
                IsData file = result.Component1;
                if (new Address(file.address).Matches(address))
                {
                    UnmanagedList<byte> fileData = world.GetList<byte>(result.entity);
                    newReader = new(fileData.AsSpan());
                    return true;
                }
            }

            //search file system
            string addressString = address.ToString();
            if (!System.IO.File.Exists(addressString))
            {
                newReader = default;
                return false;
            }

            using System.IO.FileStream fileStream = new(addressString, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            newReader = new(fileStream);
            return true;
        }
    }
}
