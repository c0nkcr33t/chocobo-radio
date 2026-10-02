using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChocoboRadio;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private bool initialSize = true;
    private string scrollingText = "";
    private double scrollStarted;
    private int editIndex = -1;
    private Station? editedStation;
    private string editName = "";
    private string editUrl = "";
    private string editMessage = "";
    private static readonly Vector4 Amber = new(1f, 0.72f, 0.30f, 1);
    private static readonly Vector4 Muted = new(0.55f, 0.64f, 0.68f, 1);

    public MainWindow(Plugin plugin) : base("Chocobo Radio###ChocoboRadio")
    {
        this.plugin = plugin;
        Size = new Vector2(400, 140);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        var style = ImGui.GetStyle();
        // Size from actual rows, rather than reserving space for the removed full view.
        var height = style.WindowPadding.Y * 2 + ImGui.GetFrameHeight() * 2
            + ImGui.GetTextLineHeightWithSpacing() * 2 + 12 + style.ItemSpacing.Y * 3;
        var headerWidth = ImGui.CalcTextSize("Chocobo Radio").X + ImGui.CalcTextSize("StationsSettingsX").X
            + style.FramePadding.X * 6 + style.ItemSpacing.X * 3 + style.WindowPadding.X * 2 + 20;
        var minimumWidth = Math.Max(360, headerWidth);
        if (initialSize)
        {
            Size = new Vector2(Math.Max(400, minimumWidth), height);
            SizeCondition = ImGuiCond.Always;
            initialSize = false;
        }
        else SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(minimumWidth, height),
            MaximumSize = new Vector2(Math.Max(1000, minimumWidth), height),
        };
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.065f, 0.065f, 0.06f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.17f, 0.17f, 0.15f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.29f, 0.27f, 0.21f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.38f, 0.30f, 0.17f, 1));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Amber);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
    }

    public override void Draw()
    {
        // A custom faceplate replaces the normal window title bar. Drag its badge.
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        draw.AddRect(pos + new Vector2(2), pos + size - new Vector2(2),
            ImGui.GetColorU32(new Vector4(0.38f, 0.39f, 0.37f, 1)), 8);
        foreach (var x in new[] { 7f, size.X - 7 })
        {
            var screw = pos + new Vector2(x, size.Y - 8);
            draw.AddCircleFilled(screw, 3, ImGui.GetColorU32(Muted));
            draw.AddLine(screw - new Vector2(2, 0), screw + new Vector2(2, 0), 0xff222222);
        }
        var badge = ImGui.GetCursorScreenPos();
        var controlsWidth = ImGui.CalcTextSize("Stations").X + ImGui.CalcTextSize("Settings").X + ImGui.CalcTextSize("X").X
            + ImGui.GetStyle().FramePadding.X * 6 + ImGui.GetStyle().ItemSpacing.X * 3;
        var badgeSize = new Vector2(Math.Max(100, ImGui.GetContentRegionAvail().X - controlsWidth), ImGui.GetFrameHeight());
        ImGui.InvisibleButton("##DragFaceplate", badgeSize);
        draw.AddText(badge + new Vector2(5, 3), ImGui.GetColorU32(Amber), "Chocobo Radio");
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to move the stereo");
        ImGui.SameLine();
        if (ImGui.Button("Stations")) ImGui.OpenPopup("Stations##Stereo");
        ImGui.SameLine();
        if (ImGui.Button("Settings")) ImGui.OpenPopup("Settings##Stereo");
        ImGui.SameLine();
        if (ImGui.Button("X")) IsOpen = false;
        DrawPlayer();
        DrawPopup("Stations##Stereo", true);
        DrawPopup("Settings##Stereo", false);
    }

    private void DrawPopup(string name, bool stations)
    {
        // Explicit popup and child sizes avoid the auto-size/fill-remaining loop
        // without using a modal that dims and blocks the game.
        var scale = ImGui.GetTextLineHeight() / 17f;
        var available = ImGui.GetMainViewport().WorkSize - new Vector2(24);
        var popupSize = Vector2.Min(new Vector2(440, stations ? 360 : 320) * Math.Max(1, scale), available);
        ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);
        if (!ImGui.BeginPopup(name, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar)) return;
        if (ImGui.SmallButton("Close")) ImGui.CloseCurrentPopup();
        ImGui.Separator();
        // Scroll the editor independently; the stereo itself always remains small.
        var contentSize = new Vector2(
            Math.Max(1, popupSize.X - ImGui.GetStyle().WindowPadding.X * 2),
            Math.Max(1, popupSize.Y - ImGui.GetStyle().WindowPadding.Y * 2
                - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y * 2 - 1));
        if (ImGui.BeginChild(name + "Content", contentSize))
        {
            if (stations) DrawStations();
            else
            {
                DrawSettings();
                ImGui.Separator();
                ImGui.TextWrapped(plugin.Player.Status);
                if (plugin.Player.IsRunning && ImGui.Button("Reconnect")) plugin.Player.Play(plugin.Config);
            }
        }
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private void DrawPlayer()
    {
        var config = plugin.Config;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var sliderWidth = ImGui.CalcTextSize("100").X + ImGui.GetStyle().FramePadding.X * 2;
        var displayHeight = ImGui.GetTextLineHeightWithSpacing() * 2 + 12;
        var displayWidth = Math.Max(100, ImGui.GetContentRegionAvail().X - sliderWidth - spacing.X);
        ImGui.BeginGroup();
        DrawDisplay(displayWidth, displayHeight);
        var hasStations = config.Stations.Count > 0;
        ImGui.BeginDisabled(!hasStations);
        var buttonWidth = (displayWidth - spacing.X * 2) / 3;
        if (ImGui.Button("<<", new Vector2(buttonWidth, 0))) ChangeStation(-1);
        ImGui.SameLine();
        if (ImGui.Button(plugin.Player.IsRunning ? "Stop" : "Play", new Vector2(buttonWidth, 0)))
        {
            if (plugin.Player.IsRunning) plugin.Player.Stop();
            else plugin.Player.Play(config);
        }
        ImGui.SameLine();
        if (ImGui.Button(">>", new Vector2(buttonWidth, 0))) ChangeStation(1);
        ImGui.EndDisabled();
        ImGui.EndGroup();
        ImGui.SameLine();
        var volume = config.Volume;
        var sliderHeight = displayHeight + spacing.Y + ImGui.GetFrameHeight();
        if (ImGui.VSliderInt("##Volume", new Vector2(sliderWidth, sliderHeight), ref volume, 0, 100, "%d"))
            config.Volume = volume;
        if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetTooltip($"Volume: {volume}%");
        if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
    }

    private void DrawDisplay(float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.02f, 0.035f, 0.035f, 1)), 6);
        draw.AddRect(origin, origin + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.2f, 0.29f, 0.27f, 1)), 6);
        var station = plugin.Player.PlayingStation;
        if (station.Length == 0 && plugin.Config.SelectedStation >= 0 && plugin.Config.SelectedStation < plugin.Config.Stations.Count)
            station = plugin.Config.Stations[plugin.Config.SelectedStation].Name;
        if (station.Length == 0) station = "NO STATION SELECTED";
        var track = plugin.Player.Track;
        var title = track.Display.Length > 0 ? track.Display : plugin.Player.IsRunning ? "Waiting for station track information…" : "Ready when you are";
        var left = origin + new Vector2(10, 6);
        var right = origin + new Vector2(width - 10, height - 4);
        draw.PushClipRect(left, right, true);
        draw.AddText(left, ImGui.GetColorU32(Muted), station);
        var line = left + new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
        if (scrollingText != title) { scrollingText = title; scrollStarted = ImGui.GetTime(); }
        var textWidth = ImGui.CalcTextSize(title).X;
        var available = Math.Max(1, width - 20);
        var offset = 0f;
        if (plugin.Config.ScrollTrackText && textWidth > available)
        {
            var travel = textWidth - available;
            var time = (ImGui.GetTime() - scrollStarted) % (travel / 28 + 4);
            offset = (float)Math.Clamp((time - 2) * 28, 0, travel);
        }
        draw.AddText(line - new Vector2(offset, 0), ImGui.GetColorU32(Amber), title);
        draw.PopClipRect();
        ImGui.Dummy(new Vector2(width, height));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(station + "\n" + track.Display + "\n" + plugin.Player.Status);
    }

    private void Tune(int index)
    {
        plugin.Config.SelectedStation = index;
        plugin.Config.Save();
        if (plugin.Player.IsRunning) plugin.Player.Play(plugin.Config);
    }

    private void ChangeStation(int direction)
    {
        var count = plugin.Config.Stations.Count;
        if (count == 0) return;
        var current = Math.Clamp(plugin.Config.SelectedStation, 0, count - 1);
        Tune((current + direction + count) % count);
    }

    private void DrawStations()
    {
        var config = plugin.Config;
        ImGui.TextColored(Amber, "SAVED STATIONS");
        if (ImGui.Button("Add station"))
        {
            editedStation = null;
            editIndex = -1;
            editName = "New station";
            editUrl = "";
            editMessage = "";
        }
        if (ImGui.BeginListBox("##StationLibrary", new Vector2(-1, 110)))
        {
            for (var i = 0; i < config.Stations.Count; i++)
            {
                if (!ImGui.Selectable($"{config.Stations[i].Name}##saved{i}", editedStation == config.Stations[i])) continue;
                    editIndex = i;
                editedStation = config.Stations[i];
                editName = editedStation.Name;
                editUrl = editedStation.Url;
                editMessage = "";
            }
            ImGui.EndListBox();
        }
        ImGui.InputText("Name", ref editName, 256);
        ImGui.InputText("MP3 stream URL", ref editUrl, 2048);
        if (ImGui.Button(editedStation == null ? "Save new station" : "Save changes"))
        {
            if (string.IsNullOrWhiteSpace(editName) || !Uri.TryCreate(editUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) editMessage = "Enter a name and a direct HTTP(S) MP3 stream URL.";
            else
            {
                    if (editedStation == null)
                {
                    editedStation = new Station();
                    config.Stations.Add(editedStation);
                    editIndex = config.Stations.Count - 1;
                }
                var urlChanged = editedStation.Url != uri.AbsoluteUri;
                editedStation.Name = editName.Trim();
                editedStation.Url = uri.AbsoluteUri;
                config.Save();
                if (urlChanged && config.SelectedStation == editIndex && plugin.Player.IsRunning) plugin.Player.Play(config);
                editMessage = "Saved.";
            }
        }
        if (editedStation != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Tune in")) { Tune(editIndex); if (!plugin.Player.IsRunning) plugin.Player.Play(config); }
            ImGui.SameLine();
            if (ImGui.Button("Delete")) ImGui.OpenPopup("Delete station?");
        }
        if (ImGui.BeginPopup("Delete station?"))
        {
            ImGui.TextUnformatted("Delete this saved station?");
            if (ImGui.Button("Delete station") && editedStation != null)
            {
                if (config.SelectedStation == editIndex) plugin.Player.Stop();
                    config.Stations.RemoveAt(editIndex);
                if (config.SelectedStation > editIndex) config.SelectedStation--;
                config.SelectedStation = Math.Clamp(config.SelectedStation, 0, Math.Max(0, config.Stations.Count - 1));
                config.Save();
                editedStation = null;
                editIndex = -1;
                editName = editUrl = editMessage = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.TextWrapped(editMessage);
    }

    private void DrawSettings()
    {
        var config = plugin.Config;
        ImGui.TextColored(Amber, "PLAYBACK");
        var fullTime = config.FullTimePlayback;
        if (ImGui.Checkbox("Keep playing off mount", ref fullTime)) { config.FullTimePlayback = fullTime; config.Save(); }
        ImGui.TextWrapped(fullTime ? "Play throughout your session. Logout still stops radio." : "Dismounting stops the radio. You can also start it manually.");
        var autoplay = config.AutoPlay;
        if (ImGui.Checkbox(fullTime ? "Autoplay on login / enabling full-time mode" : "Autoplay when mounting", ref autoplay)) { config.AutoPlay = autoplay; config.Save(); }
        var mute = config.MuteGameMusic;
        if (ImGui.Checkbox("Mute game music while radio plays", ref mute)) { config.MuteGameMusic = mute; config.Save(); }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Amber, "DISPLAY");
        var popup = config.OpenOnMount;
        if (ImGui.Checkbox("Open player when mounting", ref popup)) { config.OpenOnMount = popup; config.Save(); }
        var scroll = config.ScrollTrackText;
        if (ImGui.Checkbox("Scroll long track names", ref scroll)) { config.ScrollTrackText = scroll; config.Save(); }
        ImGui.TextWrapped("Track information comes from the station. Some stations do not send titles, and artist/title formatting may vary.");
    }
}
