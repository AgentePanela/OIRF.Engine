using System;
using Microsoft.Xna.Framework.Content.Pipeline;
using MonoGame.Framework.Utilities;

namespace Engine.Client.Extensions;

public static class TargetPlatformExtension
{
    public static TargetPlatform ToTargetPlatform(this MonoGamePlatform platform)
    {
        return platform switch
        {
            MonoGamePlatform.Android         => TargetPlatform.Android,
            MonoGamePlatform.iOS             => TargetPlatform.iOS,
            MonoGamePlatform.DesktopGL       => TargetPlatform.DesktopGL,
            MonoGamePlatform.Windows         => TargetPlatform.Windows,
            MonoGamePlatform.WindowsDX12     => TargetPlatform.WindowsDX12,
            MonoGamePlatform.XboxOne         => TargetPlatform.XboxOne,
            MonoGamePlatform.XboxSeries      => TargetPlatform.XboxSeries,
            MonoGamePlatform.PlayStation4    => TargetPlatform.PlayStation4,
            MonoGamePlatform.PlayStation5    => TargetPlatform.PlayStation5,
            MonoGamePlatform.NintendoSwitch  => TargetPlatform.Switch,
            MonoGamePlatform.DesktopVK       => TargetPlatform.DesktopVK,
            MonoGamePlatform.WebGL           => TargetPlatform.Web,

            // no equivalent
            MonoGamePlatform.tvOS => throw new NotSupportedException(
                "tvOS is not supported by ResourcesBuilder"),

            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }
}