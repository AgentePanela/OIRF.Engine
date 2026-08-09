using System;
using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;

namespace Engine.Client.UI;

/// <summary>
/// The basic node in the GUI system.
/// </summary>
public partial class Control : IDisposable
{
    internal virtual void Draw(ShapeBatch sb, IFontManager fontManager)
    {
        foreach (var child in Children)
            child.Draw(sb, fontManager);
        

    }
}