using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace sbjStats.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string endpoint;
    private string apiKey;
    private bool enableUpload;

    public ConfigWindow(Plugin plugin) : base("SBJ Stats Config###sbjStatsConfig")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 180),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        endpoint = plugin.Configuration.Endpoint;
        apiKey = plugin.Configuration.ApiKey;
        enableUpload = plugin.Configuration.EnableUpload;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.InputText("Endpoint", ref endpoint, 512);
        ImGui.InputText("API Key", ref apiKey, 512);
        ImGui.Checkbox("Enable Upload", ref enableUpload);

        if (ImGui.Button("Save"))
        {
            plugin.Configuration.Endpoint = endpoint.Trim();
            plugin.Configuration.ApiKey = apiKey.Trim();
            plugin.Configuration.EnableUpload = enableUpload;
            plugin.Configuration.Save();
        }

        if (ImGui.CollapsingHeader("Debug"))
        {
            ImGui.Text($"Endpoint set: {!string.IsNullOrWhiteSpace(endpoint)}");
            ImGui.Text($"API key set: {!string.IsNullOrWhiteSpace(apiKey)}");
        }
    }
}