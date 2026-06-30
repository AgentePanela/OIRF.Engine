using Engine.Shared.Configuration;

namespace Engine.Server.CVars;

public static class ServerCVars
{
    public static CVarDef<int> Port = CVarDef.Create("server.port", 1212);
    public static CVarDef<string> ServerName = CVarDef.Create("server.name", "MyServer");
}