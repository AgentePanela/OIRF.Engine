using Engine.Shared.Assets;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Content.Pipeline.Builder;

namespace Engine.ResourcesBuilder;

public static class ShaderBuilder
{
    public static void Build(TargetPlatform platform = TargetPlatform.DesktopGL, GraphicsProfile profile = GraphicsProfile.Reach, string[]? resourceFolders = default)
    {
        if (resourceFolders is null)
        {
            var resourcesRoot = SharedResourceManager.GetMainResourcesFolder();
            var engineResourcesRoot = Path.Combine(resourcesRoot, "..", "Engine", "Engine.Shared", "EngineResources");
            resourceFolders = [resourcesRoot, engineResourcesRoot];
        }

        foreach (var root in resourceFolders)
        {
            // Sprite shaders get lighting support injected in memory (source
            // files under Shaders/ are never touched) - see
            // ShaderLightingInjector for the rename+wrap mechanism.
            var generatedRoot = ShaderLightingInjector.GenerateRoot(root);

            // A fresh ContentBuilder per root
            var builder = new ShaderContentBuilder();
            var parameters = new ContentBuilderParams
            {
                WorkingDirectory = AppContext.BaseDirectory,
                SourceDirectory = generatedRoot,
                OutputDirectory = "",
                IntermediateDirectory = "obj/ContentBuilder",
                Platform = platform,
                GraphicsProfile = profile,
                Mode = ContentBuilderMode.Builder,
                SkipClean = true,
            };
            builder.Run(parameters);
        }
    }
}

internal sealed class ShaderContentBuilder : ContentBuilder
{
    public override IContentCollection GetContentCollection()
    {
        var content = new ContentCollection();
        content.Include<WildcardRule>("Shaders/*.fx");
        return content;
    }
}
