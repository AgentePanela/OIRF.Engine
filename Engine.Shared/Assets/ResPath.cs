using System.Collections.Generic;
using System.IO;
using Engine.Shared.Assets;
using Engine.Shared.IoC;

namespace Engine.Shared.Assets;

/// <summary>
/// A resource path, used to get files in a resources file, e.g: ResPath("Prototypes").GetFiles("yml"); </p>
/// This make possible have multiple resources folders.
/// </summary>
public readonly struct ResPath
{
    private static readonly Dictionary<string, ResPath> _registry = new();

    /// <summary>
    /// Every distinct ResPath root ever constructed, e.g. "Prototypes", "Locale", "Textures".
    /// </summary>
    public static IReadOnlyCollection<ResPath> Registered => _registry.Values;

    public readonly string Directory;
    public ResPath(string path)
    {
        Directory = path;
        _registry.TryAdd(path, this);
    }

    public ResFile[] GetFiles(string fileType)
        => IoCManager.Resolve<SharedResourceManager>().GetResPathFiles(this, fileType);

    public string[] GetFolders()
        => IoCManager.Resolve<SharedResourceManager>().GetResPathFolders(this);
}

public readonly struct ResFile
{
    public readonly string FilePath;
    public readonly string Relative;
    public readonly ResPath ResPath;

    public ResFile(string path, string relative, ResPath resPath)
    {
        FilePath = path;
        Relative = relative;
        ResPath = resPath;
    }
}