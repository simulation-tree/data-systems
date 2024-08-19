using Data.Components;
using Data.Events;
using Simulation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading;
using Unmanaged;
using Unmanaged.Collections;

namespace Data.Systems
{
    public class DataImportSystem : SystemBase
    {
        private readonly Query<IsDataRequest> requestQuery;
        private readonly UnmanagedList<eint> loadingEntities;
        private readonly ConcurrentQueue<UnmanagedArray<Instruction>> operations;

        private UnmanagedList<BinaryReader> embeddedResources;
        private UnmanagedList<Address> embeddedAddresses;

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        public DataImportSystem(World world) : base(world)
        {
            requestQuery = new(world);
            loadingEntities = new();
            operations = new();
            Subscribe<DataUpdate>(Update);
        }

        public unsafe override void Dispose()
        {
            while (operations.TryDequeue(out UnmanagedArray<Instruction> operation))
            {
                foreach (Instruction instruction in operation)
                {
                    instruction.Dispose();
                }

                operation.Dispose();
            }

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

            requestQuery.Dispose();
            base.Dispose();
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void Update(DataUpdate e)
        {
            Update();
            PerformInstructions();
        }

        /// <summary>
        /// Iterates over all entities with the <see cref="IsDataRequest"/> component and attempts
        /// to import the data at its address.
        /// </summary>
        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void Update()
        {
            requestQuery.Update();
            foreach (var x in requestQuery)
            {
                eint entity = x.entity;
                ref IsDataRequest import = ref x.Component1;
                if (import.status == DataRequest.DataStatus.Unknown)
                {
                    if (embeddedResources == default)
                    {
                        FindAllEmbeddedResources();
                    }

                    if (loadingEntities.TryAdd(entity))
                    {
                        import.status = DataRequest.DataStatus.Loading;
                        //ThreadPool.QueueUserWorkItem(LoadDataOntoEntity, entity, false);
                        LoadDataOntoEntity(entity);
                    }
                    else
                    {
                        //entity become unknown when already loading
                        throw new InvalidOperationException($"Entity `{entity}` is already loading data");
                    }
                }
            }
        }

        private unsafe void PerformInstructions()
        {
            while (operations.TryDequeue(out UnmanagedArray<Instruction> operation))
            {
                Console.WriteLine($"Performing operation with {operation.Length} instructions");
                world.Perform(operation);
                operation.Dispose();
            }
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private unsafe void LoadDataOntoEntity(eint entity)
        {
            IsDataRequest import = world.GetComponentRef<IsDataRequest>(entity);
            FixedString address = import.address;
            Span<char> buffer = stackalloc char[FixedString.MaxLength];
            int length = address.CopyTo(buffer);
            buffer = buffer[..length];

            Console.WriteLine($"Loading data from `{buffer}` onto entity `{entity}` ({import.status})");
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (TryImport(buffer, out BinaryReader reader))
            {
                Console.WriteLine("import");
                if (!world.ContainsList<byte>(entity))
                {
                    world.CreateList<byte>(entity);
                }

                UnmanagedList<byte> data = world.GetList<byte>(entity);
                data.Clear();
                data.AddRange(reader.AsSpan());
                reader.Dispose();
                import.status = DataRequest.DataStatus.Loaded;
                Console.WriteLine("loaded");
            }
            else
            {
                import.status = DataRequest.DataStatus.None;
                Console.WriteLine("no");
            }

            stopwatch.Stop();
            Console.WriteLine($"Finished loading data at `{address}` onto entity `{entity}` in {stopwatch.ElapsedMilliseconds}ms");
            UnmanagedArray<Instruction> instructions = new(2);
            instructions[0] = Instruction.SelectEntity(entity);
            instructions[1] = Instruction.SetComponent(import);
            operations.Enqueue(instructions);
            Console.WriteLine($"Enqueued operation with {instructions.Length} instructions");
        }

        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private void FindAllEmbeddedResources()
        {
            //todo: efficiency: skip loading all embedded resources? its very taxing on startup time
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

        /// <summary>
        /// Attempts to import data from the given address into a new reader.
        /// </summary>
        [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
        private bool TryImport(ReadOnlySpan<char> address, out BinaryReader newReader)
        {
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
            using Query<IsData> fileQuery = new(world);
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

            return TryLoadFromFileSystem(address, out newReader);
        }

        private bool TryLoadFromFileSystem(ReadOnlySpan<char> address, out BinaryReader reader)
        {
            try
            {
                using System.IO.FileStream fileStream = new(address.ToString(), System.IO.FileMode.Open, System.IO.FileAccess.Read);
                reader = new(fileStream);
                return true;
            }
            catch
            {
                reader = default;
                return false;
            }
        }
    }
}
