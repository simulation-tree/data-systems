# Data Systems

Implements `data` with a system that imports data from various sources.

### Loading from an entity

```cs
DataSource source = new(world, "fileA", "Some data is here");
DataRequest request = new(world, "fileA");

simulator.Update();

using BinaryReader reader = new(request.Data);
USpan<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an entity {loadedData}");
```

### Loading from file on disk

```cs
DataRequest request = new(world, "C:/fileB.txt");

simulator.Update();

using BinaryReader reader = new(request.Data);
USpan<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from a file {loadedData}");
```

### Loading from an embedded resource

Assuming there is a file `SomeDataFile.txt` in the `Assets` folder of the project,
marked as an embedded resource:
```cs
public readonly struct SomeDataFile : IDataReference
{
    static SomeDataFile()
    {
        EmbeddedAddress.Register<SomeDataFile>();
    }

    readonly Address IDataReference.Value => "Assets/SomeDataFile.txt";
}

DataRequest request = new(world, Address.Get<SomeDataFile>());

simulator.Update();

using BinaryReader reader = new(request.Data);
USpan<char> dataBuffer = stackalloc char[32];
uint textLength = reader.ReadUTF8Span(dataBuffer);
string loadedData = dataBuffer.Slice(0, textLength).ToString();
Console.WriteLine($"Loaded data from an embedded resource {loadedData}");
```