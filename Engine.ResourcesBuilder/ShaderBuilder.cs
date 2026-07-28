using Engine.Shared.Assets;
using Engine.Shared.Storage;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Content.Pipeline.Builder;

namespace Engine.ResourcesBuilder;

public static class ShaderBuilder
{
    /// <summary>
    /// Whether a usable pre-built shader set already exists for <paramref name="platform"/>, so
    /// <see cref="Build"/> can be skipped entirely and that directory used as-is instead.
    /// </summary>
    public static bool HasPrecompiled(TargetPlatform platform)
    {
        var dir = GetPrecompiledDirectory(platform);
        return FileSystem.DirectoryExists(dir) && FileSystem.GetFiles(dir, "*.xnb").Length > 0;
    }

    public static void Build(TargetPlatform platform = TargetPlatform.DesktopGL, GraphicsProfile profile = GraphicsProfile.Reach, string[]? resourceFolders = default, string saveDir = "obj/ContentBuilder")
    {
        if (resourceFolders is null)
        {
            var resourcesRoot = SharedResourceManager.GetMainResourcesFolder();
            #if DEBUG
            var engineResourcesRoot = Path.Combine(resourcesRoot, "..", "Engine", "Engine.Shared", "EngineResources");
            #else
            var engineResourcesRoot = Path.Combine(resourcesRoot, "..", "EngineResources");
            #endif
            resourceFolders = [resourcesRoot, engineResourcesRoot];
        }

        foreach (var root in resourceFolders)
        {
            // Sprite shaders get lighting support injected in memory (source
            // files under Shaders/ are never touched) - see
            // ShaderLightingInjector for the rename+wrap mechanism.
            var generatedRoot = ShaderLightingInjector.GenerateRoot(root, saveDir);

            // A fresh ContentBuilder per root
            var builder = new ShaderContentBuilder();
            var parameters = new ContentBuilderParams
            {
                WorkingDirectory = AppContext.BaseDirectory,
                SourceDirectory = generatedRoot,
                OutputDirectory = saveDir,
                IntermediateDirectory = saveDir,
                Platform = platform,
                GraphicsProfile = profile,
                Mode = ContentBuilderMode.Builder,
                SkipClean = true,
                #if DEBUG
                LogLevel = LogLevel.Info,
                #else
                LogLevel = LogLevel.Warning,
                #endif
            };

            try
            {
                builder.Run(parameters);
            }
            finally
            {
                // generatedRoot is a short-lived temp folder unless there was no
                // Shaders/ to process at all, in which case GenerateRoot handed
                // back `root` itself - never delete that.
                // TODO: re-enable, disabled temporarily for debugging.
                // if (generatedRoot != root && Directory.Exists(generatedRoot))
                //     Directory.Delete(generatedRoot, recursive: true);
            }
        }
    }

    public static string GetPrecompiledDirectory(TargetPlatform platform)
        => Path.Combine(AppContext.BaseDirectory, "PrecompiledShaders", platform.ToString());

}

internal sealed class ShaderContentBuilder : ContentBuilder
{
    public override IContentCollection GetContentCollection()
    {
        var content = new ContentCollection();
        content.SetContentRoot("");
        content.Include<WildcardRule>("*.fx");
        return content;
    }
}
