using Engine.Client.Graphics.Fonts;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    private static bool IsEffectUnshaded(Effect? effect) =>
        effect?.CurrentTechnique?.Name == "Unshaded";
}