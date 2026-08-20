using System;
using System.IO;
using Engine.Shared.Storage;

namespace Engine.Shared.Storage;

/// <summary>
/// Manages user storage, like saving cvars files, saves, cache, etc...
/// </summary>
public sealed class UserStorageManager
{
    public string DataPath { get; private set; }
    public UserStorageManager(string path, bool useAppdata)
    {
        //IoCManager.ResolveDependencies(this);
        SetPath(path, useAppdata);
    }

    private void SetPath(string path, bool useAppdata)
    {
        string npath = string.Empty;
        if (useAppdata)
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            npath = Path.Combine(appdata, path);
        }
        else
        {
            var baseDir = AppContext.BaseDirectory;
            npath = Path.Combine(baseDir, path);
        }
        
        DataPath = npath;
    }

    public string GetFullPath(string relativePath)
    {
        return Path.Combine(DataPath, relativePath);
    }

    public void WriteText(string relativePath, string content)
    {
        var full = GetFullPath(relativePath);

        var dir = Path.GetDirectoryName(full);
        if (!FileSystem.DirectoryExists(dir))
            FileSystem.CreateDirectory(dir ?? throw new NullReferenceException("No directory found."));

        FileSystem.WriteAllText(full, content);
    }

    public string? ReadText(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (!FileSystem.FileExists(full))
            return null;

        return FileSystem.ReadAllText(full);
    }

    public void WriteBinary(string relativePath, byte[] data)
    {
        var full = GetFullPath(relativePath);

        var dir = Path.GetDirectoryName(full);
        if (!FileSystem.DirectoryExists(dir))
            FileSystem.CreateDirectory(dir ?? throw new NullReferenceException("No directory found."));

        FileSystem.WriteAllBytes(full, data);
    }

    public byte[]? ReadBinary(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (!FileSystem.FileExists(full))
            return null;

        return FileSystem.ReadAllBytes(full);
    }

    public bool FileExists(string relativePath)
    {
        return FileSystem.FileExists(GetFullPath(relativePath));
    }

    public void DeleteFile(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (FileSystem.FileExists(full))
            FileSystem.DeleteFile(full);
    }
}
