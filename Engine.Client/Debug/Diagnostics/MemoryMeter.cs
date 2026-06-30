using Engine.Client;
using Engine.Client.Assets.Atlas;
using System;
using System.Diagnostics;

namespace Engine.Client.Debug.Diagnostics;

public static class MemoryMeter
{
    public static string GetInfo()
    {
        var process = Process.GetCurrentProcess();

        long managed = GC.GetTotalMemory(false);
        long workingSet = process.WorkingSet64;

        var client = GameClient.Instance;
        int gen0 = client.GCMeter.gen0;
        int gen1 = client.GCMeter.gen1;
        int gen2 = client.GCMeter.gen2;
        var all = FormatMB(client.GCMeter.allocatedBytes);

        var atlasPages = GameClient.Assets.GetAllAtlasses();

        long estimatedTextureMemory = 0;
        var id = 1;
        string infoPerAtlas = string.Empty;
        foreach (var atlas in atlasPages)
        {
            var tex = atlas.Texture;
            estimatedTextureMemory += tex.Width * tex.Height * 4;
            if (infoPerAtlas == "")
                infoPerAtlas = GetAtlasInfo(atlas, id);
            else
                infoPerAtlas += $"\n{GetAtlasInfo(atlas, id)}";

            id++;
        }

        return
            $"Managed Memory: {FormatMB(managed)} Mb\n" + // project memory usage
            $"Process Memory: {FormatMB(workingSet)} Mb\n" + // entire .net/process memory usage
            $"Estimated VRAM (textures): {FormatMB(estimatedTextureMemory)} Mb\n" +
            $"Atlas Pages: ({atlasPages.Count})\n" +
            $"{infoPerAtlas}\n" +
            $"GC Gen0: {gen0} | Gen1: {gen1} | Gen2: {gen2} | Allocated: {all}Mb"; // how many time GC has runned since the executable start    
    }

    private static string FormatMB(long bytes)
        => (bytes / 1024f / 1024f).ToString("0.00");

    private static string GetAtlasInfo(AtlasPage page, int id)
    {
        var tex = page.Texture;
        var memory = FormatMB(tex.Width * tex.Height * 4);

        return $"   > {id} | Memory: {memory}Mb | Sprite count: {page.Regions.Count}";
    }
}

public struct GCMeter
{
    public int gen0 { get; set; }
    public int gen1 { get; set; }
    public int gen2 { get; set; }
    public long allocatedBytes { get; set; }
}
