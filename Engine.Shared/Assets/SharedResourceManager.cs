using System;
using System.Collections.Generic;
using System.IO;
using Engine.Shared.IoC;

namespace Engine.Shared.Assets;

public sealed class SharedResourceManager
{
    private List<string> _resourcesFolders = new();

    public SharedResourceManager()
        => Init();

    internal void Init()
    {
        var engineResources = Path.Combine(AppContext.BaseDirectory, "EngineResources");
        _resourcesFolders.Add(engineResources); // add the engine resources folder first, before the main content resources folder.
        _resourcesFolders.Add(GetMainResourcesFolder());
        IoCManager.ResolveDependencies(this);
    }

    public ResFile[] GetResPathFiles(ResPath path, string fileType)
    {
        List<ResFile> files = new();
        foreach (var resources in _resourcesFolders)
        {
            var dir = Path.Combine(resources, path.Directory);
            if (!Directory.Exists(dir))
                continue;
            
            var rfiles = Directory.GetFiles(dir, $"**.{fileType}", SearchOption.AllDirectories);
            foreach (var file in rfiles)
            {
                var key = NormalizeKey(dir, file);
                var resFile = new ResFile(file, key, path);
                files.Add(resFile);
            }
        }

        return files.Count == 0 ? Array.Empty<ResFile>() : files.ToArray();
    }

    public string[] GetResPathFolders(ResPath path)
    {
        List<string> dirs = new();
        foreach (var resources in _resourcesFolders)
        {
            var dir = Path.Combine(resources, path.Directory);
            if (!Directory.Exists(dir))
                continue;
            
            dirs.Add(dir);
        }

        return dirs.Count == 0 ? Array.Empty<string>() : dirs.ToArray();
    }

    public bool TryGetResFile(string fullPath, ResPath resPath, out ResFile file)
    {
        foreach (var resources in _resourcesFolders)
        {
            var dir = Path.Combine(resources, resPath.Directory);

            if (!Path.GetFullPath(fullPath)
                    .StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
                continue;

            var key = NormalizeKey(dir, fullPath);

            file = new ResFile(fullPath, key, resPath);
            return true;
        }

        file = default;
        return false;
    }

    public void AddResourcesFolder(string path)
        => _resourcesFolders.Add(path);
    
    public void RemoveResourcesFolder(string path)
        => _resourcesFolders.Remove(path);

    public static string NormalizeKey(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return Path.ChangeExtension(relative, null)
                   .Replace('\\', '/');
    }

    public static string GetMainResourcesFolder()
    {
        // if we are running from the root (dotnet run), Resources/ is right here
        if (Directory.Exists("Resources"))
            return "Resources";

#if DEBUG
        return Path.Combine("..", "..", "Resources");
#else
        return Path.Combine("Resources");
#endif
    }
}