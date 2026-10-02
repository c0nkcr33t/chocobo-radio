using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChocoboRadio;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    public MainWindow(Plugin plugin) : base("Chocobo Radio###ChocoboRadio")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 320), MaximumSize = new Vector2(900, 800) };
    }

    public override void Draw()
    {
        var config = plugin.Config;
        var selected = config.SelectedStation;
        var valid = selected >= 0 && selected < config.Stations.Count;
        if (ImGui.BeginCombo("Station", valid ? config.Stations[selected].Name : "Select station"))
        {
            for (var i = 0; i < config.Stations.Count; i++)
            {
                if (!ImGui.Selectable($"{config.Stations[i].Name}##{i}", i == selected)) continue;
                config.SelectedStation = i;
                config.Save();
                if (plugin.Player.IsRunning) plugin.Player.Play(config);
            }
            ImGui.EndCombo();
        }
        if (ImGui.Button("Play / Reconnect")) plugin.Player.Play(config);
        ImGui.SameLine();
        if (ImGui.Button("Stop")) plugin.Player.Stop();
        var volume = config.Volume;
        if (ImGui.SliderInt("Volume", ref volume, 0, 100)) config.Volume = volume;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }
        ImGui.TextWrapped(plugin.Player.Status);
        ImGui.Separator();
        var popup = config.OpenOnMount;
        if (ImGui.Checkbox("Open panel when mounting", ref popup)) { config.OpenOnMount = popup; config.Save(); }
        var autoplay = config.AutoPlay;
        if (ImGui.Checkbox("Play selected station when mounting", ref autoplay)) { config.AutoPlay = autoplay; config.Save(); }
        var muteMusic = config.MuteGameMusic;
        if (ImGui.Checkbox("Mute game music while radio plays", ref muteMusic))
        {
            config.MuteGameMusic = muteMusic;
            config.Save();
        }
        if (ImGui.CollapsingHeader("Stations"))
        {
            ImGui.TextWrapped("Add a direct MP3 audio stream URL. Playback follows FFXIV master and BGM volume/mute settings.");
            selected = config.SelectedStation;
            if (selected >= 0 && selected < config.Stations.Count)
            {
                var station = config.Stations[selected];
                var name = station.Name;
                var url = station.Url;
                if (ImGui.InputText("Name", ref name, 256)) { station.Name = name; config.Save(); }
                if (ImGui.InputText("Stream URL", ref url, 2048)) { station.Url = url; config.Save(); }
                if (ImGui.Button("Remove selected station"))
                {
                    plugin.Player.Stop();
                    config.Stations.RemoveAt(selected);
                    config.SelectedStation = 0;
                    config.Save();
                }
            }
            if (ImGui.Button("Add station"))
            {
                config.Stations.Add(new Station());
                config.SelectedStation = config.Stations.Count - 1;
                config.Save();
            }
            ImGui.TextWrapped("Dismounting stops playback. Volume changes apply live. Radio is heard locally; passengers do not hear it automatically.");
        }
    }
}
