using System.IO;

namespace Engine.Shared.Storage;

/// <summary>
/// Abstracts reading/writing of resource and user-data files, so the engine isn't hardwired to
/// on-disk access. The active implementation is selected once, in <see cref="Engine.Shared.Assets.SharedResourceManager"/>.
/// </summary>
public static class FileSystem
{
    /// <summary>
    /// The current IFileSystem being used.
    /// </summary>
    public static IFileSystem Current { get; internal set; } = new DesktopFileSystem();

    public static void CreateDirectory(string path)
        => Current.CreateDirectory(path);

    public static void DeleteFile(string path)
        => Current.DeleteFile(path);

    public static bool DirectoryExists(string? path)
        => Current.DirectoryExists(path);

    public static bool FileExists(string? path)
        => Current.FileExists(path);

    /// <inheritdoc cref="IFileSystem.GetFiles(string, string)"/>
    public static string[] GetFiles(string directory, string searchPattern)
        => Current.GetFiles(directory, searchPattern);

    public static Stream OpenRead(string path)
        => Current.OpenRead(path);

    public static byte[] ReadAllBytes(string path)
        => Current.ReadAllBytes(path);

    public static string ReadAllText(string path)
        => Current.ReadAllText(path);

    public static void WriteAllBytes(string path, byte[] data)
        => Current.WriteAllBytes(path, data);

    public static void WriteAllText(string path, string content)
        => Current.WriteAllText(path, content);

}
