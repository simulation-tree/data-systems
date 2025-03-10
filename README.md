# Request Systems

Implements `requests` with a system that imports data from various sources.

### Loading from an entity

```cs
Datum source = new(world, "fileA");
source.WriteUTF8("Some data here!");

Request request = new(world, "fileA");

simulator.Update();

using BinaryReader reader = new(request.GetBinaryData());
Span<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an entity {loadedData}");
```

### Loading from file on disk

```cs
Request request = new(world, "C:/fileB.txt");

simulator.Update();

using BinaryReader reader = new(request.GetBinaryData());
Span<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from a file {loadedData}");
```

### Loading from an embedded resource

Embedded resources in a project can be loaded if their address is registered:
```cs
public readonly struct MyEmbeddedResources : IEmbeddedResourceBank
{
    void IEmbeddedResourceBank.Load(Register register)
    {
        register.Invoke("Assets/test.txt");
    }
}

EmbeddedResourceRegistry.Load<MyEmbeddedResources>();
Request request = new(world, "Assets/test.txt");

simulator.Update();

using BinaryReader reader = new(request.GetBinaryData());
Span<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an embedded resource {loadedData}");
```