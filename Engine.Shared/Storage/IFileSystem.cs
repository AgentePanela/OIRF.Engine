using System.IO;

namespace Engine.Shared.Storage;

/// <summary>
/// Abstracts reading/writing of resource and user-data files, so the engine isn't hardwired to
/// on-disk access. The active implementation is selected once, in <see cref="Engine.Shared.Assets.SharedResourceManager"/>.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string? path);

    bool DirectoryExists(string? path);

    void CreateDirectory(string path);

    /// <summary>
    /// Returns every file under <paramref name="directory"/> (recursive) matching <paramref name="searchPattern"/>.
    /// </summary>
    string[] GetFiles(string directory, string searchPattern);

    byte[] ReadAllBytes(string path);

    string ReadAllText(string path);

    Stream OpenRead(string path);

    void WriteAllText(string path, string content);

    void WriteAllBytes(string path, byte[] data);

    void DeleteFile(string path);
}
