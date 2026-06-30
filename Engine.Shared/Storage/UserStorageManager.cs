using System;
using System.IO;
using Engine.Shared.IoC;

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
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir ?? throw new NullReferenceException("No directory found."));

        File.WriteAllText(full, content);
    }

    public string? ReadText(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (!File.Exists(full))
            return null;

        return File.ReadAllText(full);
    }

    public void WriteBinary(string relativePath, byte[] data)
    {
        var full = GetFullPath(relativePath);

        var dir = Path.GetDirectoryName(full);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir ?? throw new NullReferenceException("No directory found."));

        File.WriteAllBytes(full, data);
    }

    public byte[]? ReadBinary(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (!File.Exists(full))
            return null;

        return File.ReadAllBytes(full);
    }

    public bool FileExists(string relativePath)
    {
        return File.Exists(GetFullPath(relativePath));
    }

    public void DeleteFile(string relativePath)
    {
        var full = GetFullPath(relativePath);

        if (File.Exists(full))
            File.Delete(full);
    }
}
