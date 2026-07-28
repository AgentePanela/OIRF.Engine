using System.IO;

namespace Engine.Shared.Storage;

internal sealed class DesktopFileSystem : IFileSystem
{
    public bool FileExists(string? path)
        => File.Exists(path);

    public bool DirectoryExists(string? path)
        => Directory.Exists(path);

    public void CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    public string[] GetFiles(string directory, string searchPattern)
        => Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories);

    public byte[] ReadAllBytes(string path)
        => File.ReadAllBytes(path);

    public string ReadAllText(string path)
        => File.ReadAllText(path);

    public Stream OpenRead(string path)
        => File.OpenRead(path);

    public void WriteAllText(string path, string content)
        => File.WriteAllText(path, content);

    public void WriteAllBytes(string path, byte[] data)
        => File.WriteAllBytes(path, data);

    public void DeleteFile(string path)
        => File.Delete(path);
}
