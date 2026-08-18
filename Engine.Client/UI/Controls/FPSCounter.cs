using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public class FPSCounter : Label
{
    public FPSCounter()
    {
        
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        Text = $"{GameClient.GameTime.Fps}";

        switch (GameClient.GameTime.Fps)
        {
            case <= 10:
                Color = Color.Red;
                break;

            case <= 30:
                Color = Color.Yellow;
                break;
            
            case <= 60:
                Color = Color.Orange;
                break;
                
            default:
                Color = Color.White;
                break;
        }

        base.DrawSelf(sb, fontManager, dt);
    }
}