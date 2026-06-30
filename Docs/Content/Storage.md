# Storage

The storage system in ORIF Engine is managed by `UserStorageManager`. It provides a clean, platform-independent API for reading and writing text and binary files, such as game saves, configuration profiles, caches, and custom data.

---

## Overview

| Class | Role |
|-------|------|
| `UserStorageManager` | Manages read, write, existence, and deletion operations for files in the user storage path. |

The storage path is determined during the client/server initialization using `Options.DataPath`. Depending on the platform and configuration:
- **Client**: Typically configured to use the application data directory (`%APPDATA%` on Windows, or equivalent on other platforms) under the specified sub-path.
- **Server**: Typically configured to use a local subdirectory in the executable's base path.

---

## Accessing the Storage Manager

`UserStorageManager` is registered as a dependency in the IoC container on both the client and server. You can access it by injecting it using the `[Dependency]` attribute or resolving it manually.

### Dependency Injection

```csharp
using Engine.Shared.IoC;
using Engine.Shared.Storage;

public class MyGameSystem : EntitySystem
{
    [Dependency] private readonly UserStorageManager _storage = default!;
    
    // ...
}
```

### Manual Resolution

```csharp
using Engine.Shared.IoC;
using Engine.Shared.Storage;

var storage = IoCManager.Resolve<UserStorageManager>();
```

---

## Path Configuration & Details

You can query the base path and convert relative paths to absolute system paths.

### Base Directory Path
The property `DataPath` returns the absolute directory path where files are read and written.

```csharp
string rootDir = _storage.DataPath;
// Example: C:\Users\<Name>\AppData\Roaming\MyGame
```

### Get Full Path
To get the absolute path for a relative file or subdirectory:

```csharp
string fullPath = _storage.GetFullPath("saves/save_01.dat");
```

---

## Working with Text Files

`UserStorageManager` provides simple methods to write and read string contents.

### Writing Text
`WriteText` writes the content string to the specified relative path. If the parent directories do not exist, they are automatically created.

```csharp
string configJson = "{\"volume\": 80, \"fullscreen\": true}";
_storage.WriteText("config/settings.json", configJson);
```

### Reading Text
`ReadText` reads the file content and returns it as a string. If the file does not exist, it returns `null`.

```csharp
string? configJson = _storage.ReadText("config/settings.json");
if (configJson != null)
{
    // Parse settings
}
```

---

## Working with Binary Files

For save games, images, or raw structured data, you can read and write raw bytes.

### Writing Binary Data
`WriteBinary` writes a byte array to the specified relative path. Parent directories are automatically created if they don't exist.

```csharp
byte[] saveData = new byte[] { 0x4F, 0x52, 0x49, 0x46, 0x01, 0x00 };
_storage.WriteBinary("saves/save_001.bin", saveData);
```

### Reading Binary Data
`ReadBinary` reads the file content as a byte array. If the file does not exist, it returns `null`.

```csharp
byte[]? saveData = _storage.ReadBinary("saves/save_001.bin");
if (saveData != null)
{
    // Load state from bytes
}
```

---

## Checking and Deleting Files

### Checking File Existence
You can verify if a file exists before attempting to read it.

```csharp
if (_storage.FileExists("saves/save_001.bin"))
{
    Console.WriteLine("Save file found!");
}
```

### Deleting Files
Use `DeleteFile` to remove a file. If the file does not exist, the operation completes silently without throwing an exception.

```csharp
_storage.DeleteFile("saves/save_001.bin");
```
