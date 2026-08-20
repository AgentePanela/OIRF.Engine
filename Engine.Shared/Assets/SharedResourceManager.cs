using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engine.Shared.Storage;
using Engine.Shared.IoC;

namespace Engine.Shared.Assets;

public sealed class SharedResourceManager
{
    private List<string> _resourcesFolders = new();

    public SharedResourceManager()
        => Init();

    internal void Init()
    {
        // todo: new types of file systems
        FileSystem.Current = new DesktopFileSystem();

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
            if (!FileSystem.DirectoryExists(dir))
                continue;

            var rfiles = FileSystem.GetFiles(dir, $"**.{fileType}");
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
            if (!FileSystem.DirectoryExists(dir))
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

    /// <summary>
    /// Returns a copy array of the resources folders locations
    /// </summary>
    public string[] GetResourcesFolders()
        => _resourcesFolders.ToArray();

    public static string NormalizeKey(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return Path.ChangeExtension(relative, null)
                   .Replace('\\', '/');
    }

    public static string GetMainResourcesFolder()
    {
        if (TryGetResDirArg(out var resDir))
            return resDir;

        // if we are running from the root (dotnet run), Resources/ is right here
        if (FileSystem.DirectoryExists("Resources"))
            return "Resources";

#if DEBUG
        return Path.Combine("..", "..", "Resources");
#else
        return Path.Combine("..", "Resources");
#endif
    }

    /// <summary>
    /// Looks for a "--resDir" command line argument (e.g. --resDir "../Resources")
    /// that overrides the default resources folder location.
    /// </summary>
    private static bool TryGetResDirArg(out string resDir)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            const string prefix = "--resDir";

            if (args[i].StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
            {
                resDir = args[i][(prefix.Length + 1)..];
                return true;
            }

            if (string.Equals(args[i], prefix, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                resDir = args[i + 1];
                return true;
            }
        }

        resDir = string.Empty;
        return false;
    }
}