using System;
using System.IO;
using System.Reflection;

namespace Engine.Server;

/// <summary>
/// Configuration options for the headless game server.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Game server name for recognition.
    /// </summary>
    public string ServerName = "MyServer";

    /// <summary>
    /// Game version string.
    /// </summary>
    public string Version = "1.0.0";

    /// <summary>
    /// Server tick rate in ticks per second.
    /// Default: 60 TPS (~16.6ms per tick).
    /// </summary>
    [Obsolete("Use net.tickrate cvar instead!")]
    public int? TickRate;

    /// <summary>
    /// Network port to listen on.
    /// </summary>
    [Obsolete("Use net.port cvar instead!")]
    public int? Port;

    public string DataPath = "data";

    /// <summary>
    /// Whether to save the current CVar config on shutdown.
    /// </summary>
    public bool SaveConfigOnExit = false;

    /// <summary>
    /// Content assemblies to scan for EntitySystems, Components, Prototypes, etc.
    /// Should include the game's Content.Server assembly.
    /// </summary>
    public Assembly[] Assemblies = Array.Empty<Assembly>();
}
