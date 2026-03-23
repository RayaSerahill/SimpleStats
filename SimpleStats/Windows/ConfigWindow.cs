using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;

namespace sbjStats.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
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

        apiKey = plugin.Configuration.ApiKey;
        enableUpload = plugin.Configuration.EnableUpload;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (ImGui.InputText("API Key", ref apiKey, 512))
            UpdateConfig(() => plugin.Configuration.ApiKey = apiKey.Trim());
        if (ImGui.Checkbox("Enable Live Upload", ref enableUpload))
            UpdateConfig(() => plugin.Configuration.EnableUpload = enableUpload);

        if (!ImGui.BeginTabBar("SimpleGambaTabs"))
            return;

        if (ImGui.BeginTabItem("SimpleBlackjack"))
        {
            ImGui.TextWrapped("If you have existing stats that you want to upload, you can do so by clicking the button below. This will upload all stats that have been recorded so far while avoiding duplicates that are already uploaded.");
            if (ImGui.Button("Upload existing stats###UploadExistingSbj"))
            {
                if (string.IsNullOrEmpty(plugin.Configuration.ApiKey))
                {
                    plugin.ShowToast("Please enter a valid API key.", NotificationType.Error);
                }
                else
                {
                    _ = plugin.UploadExistingStatsSbjAsync();
                }
            }

            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("SimpleScratch"))
        {
            ImGui.TextWrapped("SimpleScratch now has its own upload pipeline shell. Live events and archive retrieval are wired up, but the actual transport format still needs to be written in ScratchUploadHandler.");
            if (ImGui.Button("Upload current archive snapshot###UploadExistingScratch"))
            {
                if (string.IsNullOrEmpty(plugin.Configuration.ApiKey))
                {
                    plugin.ShowToast("Please enter a valid API key.", NotificationType.Error);
                }
                else
                {
                    _ = plugin.UploadExistingStatsScratchAsync();
                }
            }

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void UpdateConfig(Action applyChanges)
    {
        applyChanges();
        plugin.Configuration.Save();
    }
}
