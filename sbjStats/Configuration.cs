using System;
using Dalamud.Configuration;

namespace sbjStats;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string Endpoint { get; set; } = "https://serahill.net/api/admin/games/import";
    public string ApiKey { get; set; } = string.Empty;
    public bool EnableUpload { get; set; } = true;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}