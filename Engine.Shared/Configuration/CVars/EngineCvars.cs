namespace Engine.Shared.Configuration.CVars;

[CVarDefs]
public sealed class EngineCvars
{
    public static readonly CVarDef<string> EngineVersion =
        CVarDef.Create("engine.version", "1.0.0");

    public static readonly CVarDef<int> SystemProfillerTop =
        CVarDef.Create("engine.system-profiller-top", 10);
}
