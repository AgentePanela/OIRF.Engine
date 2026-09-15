using Engine.Shared.Configuration;
using Engine.Shared.IoC;

namespace Engine.Shared.Console.Commands;

public sealed class SaveConfigCommand : IConsoleCommand
{
    public string Name => "save_config";
    public string Description => "Saves every cvar that differs from its default to config.toml.";

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public SaveConfigCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        _cfg.SaveConfig();
        shell.WriteLine("Config saved.");
    }
}

public sealed class LoadConfigCommand : IConsoleCommand
{
    public string Name => "load_config";
    public string Description => "Reloads cvars from config.toml.";

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public LoadConfigCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        _cfg.LoadConfig();
        shell.WriteLine("Config loaded.");
    }
}
