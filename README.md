# Data Systems

Implements the `data` project with a system that loads data from various sources.

### Loading from an entity

```cs
DataSource source = new(world, "fileA");
source.WriteUTF8("Some data here!");

DataRequest request = new(world, "fileA");

simulator.Update();

using ByteReader reader = new(request.CreateByteReader());
Span<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an entity {loadedData}");
```

### Loading from file on disk

```cs
DataRequest request = new(world, "C:/fileB.txt");

simulator.Update();

using ByteReader reader = new(request.CreateByteReader());
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
DataRequest request = new(world, "Assets/test.txt");

simulator.Update();

using ByteReader reader = new(request.CreateByteReader());
Span<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an embedded resource {loadedData}");
```

### Loading through a message

Data can be loaded by another system through the `LoadData` message:
```cs
LoadData message = new("Assets/test.txt");
simulator.Broadcast(ref message);
if (message.TryConsume(out ByteReader data))
{
    Span<char> dataBuffer = stackalloc char[32];
    uint textLength = data.ReadUTF8Span(dataBuffer);
    string loadedData = dataBuffer.Slice(0, textLength).ToString();
    Console.WriteLine($"Loaded data through a message {loadedData}");
    data.Dispose();
}
else
{
    Console.WriteLine("Failed to load data through a message");
}
```