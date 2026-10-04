using System;
using System.Numerics;
using System.Linq;
using Dalamud.Interface;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.IO;
using Dalamud.Interface.ManagedFontAtlas;

namespace ChocoboRadio;

internal sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool initialSize = true;
    private enum EditorPanel { Stations, Settings }
    private EditorPanel editorPanel;
    private bool editorOpen;
    private float editorProgress;
    private float editorHeight;
    private string scrollingText = "";
    private double scrollStarted;
    private int editIndex = -1;
    private Station? editedStation;
    private string editName = "";
    private string editUrl = "";
    private StationStreamType editStreamType;
    private string editMessage = "";
    private CancellationTokenSource? lookupCancellation;
    private Task<StationProbeResult>? stationProbe;
    private string probeUrl = "";
    private string inspectedUrl = "";
    private bool saveAfterProbe;
    private readonly IFontHandle? displayFont;
    private readonly IFontHandle? titleFont;
    private readonly IFontHandle? stationFont;
    private readonly StreamHealthMeter streamHealth = new();
    private const string FaceplateTitle = "CHOCOBO RADIO";
    private float headerHeight;
    private float stationHeight;
    private float displayHeight;
    private volatile string fontWarning = "";
    private Vector3 DisplayAccent => plugin.Config.PoweredOn ? plugin.Config.AccentColor : new Vector3(0.6f);
    private Vector4 Accent => new(DisplayAccent, 1);
    private static readonly Vector4 Muted = new(0.55f, 0.64f, 0.68f, 1);

    public MainWindow(Plugin plugin) : base("Chocobo Radio###ChocoboRadio")
    {
        this.plugin = plugin;
        displayFont = CreateFont("IBMPlexMono-Medium.ttf", 18);
        titleFont = CreateFont("Rajdhani-SemiBold.ttf", 24);
        stationFont = CreateFont("Rajdhani-SemiBold.ttf", 16);
        Size = new Vector2(400, 140);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private IFontHandle? CreateFont(string filename, float size)
    {
        try
        {
            return Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(step => step.OnPreBuild(toolkit =>
            {
                try
                {
                    using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("ChocoboRadio.Fonts." + filename)
                        ?? throw new InvalidDataException($"Embedded font resource is missing: {filename}");
                    toolkit.Font = toolkit.AddFontFromStream(stream, new SafeFontConfig { SizePx = size }, true, filename);
                }
                catch (Exception ex)
                {
                    fontWarning = "A custom font could not load; using the default font. See /xllog for details.";
                    Plugin.Log.Error(ex, "Chocobo Radio: could not build font {Font}; using default", filename);
                    toolkit.Font = toolkit.AddDalamudDefaultFont(size);
                }
            }));
        }
        catch (Exception ex)
        {
            fontWarning = "Custom font setup failed; using the default font. See /xllog for details.";
            Plugin.Log.Error(ex, "Chocobo Radio: could not register font {Font}; using default", filename);
            return null;
        }
    }

    public override void PreDraw()
    {
        var style = ImGui.GetStyle();
        // Size from actual rows, rather than reserving space for the removed full view.
        var defaultHeight = ImGui.GetTextLineHeight();
        float titleWidth;
        using (titleFont?.Push())
        {
            titleWidth = ImGui.CalcTextSize(FaceplateTitle).X;
            headerHeight = Math.Max(ImGui.GetTextLineHeight(), defaultHeight + style.FramePadding.Y * 2);
        }
        using (stationFont?.Push()) stationHeight = Math.Max(defaultHeight, ImGui.GetTextLineHeight());
        using (displayFont?.Push()) displayHeight = stationHeight + 3 + Math.Max(defaultHeight, ImGui.GetTextLineHeight()) + 12;
        var height = style.WindowPadding.Y * 2 + headerHeight + displayHeight
            + ImGui.GetFrameHeight() + style.ItemSpacing.Y * 2;
        // Reveal the editor with a short, reversible ease-in/ease-out animation.
        editorProgress = Math.Clamp(editorProgress + (editorOpen ? 1 : -1) * ImGui.GetIO().DeltaTime / 0.22f, 0, 1);
        var reveal = editorProgress * editorProgress * (3 - 2 * editorProgress);
        var availableHeight = Math.Max(0, ImGui.GetMainViewport().WorkSize.Y - height - 16);
        editorHeight = Math.Min(330 * Math.Max(1, defaultHeight / 17f), availableHeight) * reveal;
        height += editorHeight;
        var headerWidth = titleWidth + ImGui.GetFrameHeight() * 4
            + style.ItemSpacing.X * 4 + style.WindowPadding.X * 2 + 20;
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
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(DisplayAccent * 0.32f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(DisplayAccent * 0.45f, 1));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.48f, 0.49f, 0.47f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        // Round the actual background and use its border as the silver enclosure.
        // Drawing a second outline inside a square window leaves dark corners outside it.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(6);
    }

    public override void Draw()
    {
        // A custom faceplate replaces the normal window title bar. Drag its badge.
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (editorHeight > 0)
        {
            // Keep the expanding drawer accessible when the stereo is near the bottom.
            var viewport = ImGui.GetMainViewport();
            var bottom = viewport.WorkPos.Y + viewport.WorkSize.Y;
            if (pos.Y + size.Y > bottom)
            {
                pos.Y = Math.Max(viewport.WorkPos.Y, bottom - size.Y);
                ImGui.SetWindowPos(pos);
            }
        }
        foreach (var x in new[] { 7f, size.X - 7 })
        {
            var screw = pos + new Vector2(x, size.Y - 8);
            draw.AddCircleFilled(screw, 3, ImGui.GetColorU32(Muted));
            draw.AddLine(screw - new Vector2(2, 0), screw + new Vector2(2, 0), 0xff222222);
        }
        var badge = ImGui.GetCursorScreenPos();
        var controlsWidth = ImGui.GetFrameHeight() * 4 + ImGui.GetStyle().ItemSpacing.X * 4;
        var badgeSize = new Vector2(Math.Max(100, ImGui.GetContentRegionAvail().X - controlsWidth), headerHeight);
        ImGui.InvisibleButton("##DragFaceplate", badgeSize);
        using (titleFont?.Push())
            draw.AddText(badge + new Vector2(5, (headerHeight - ImGui.GetTextLineHeight()) / 2), ImGui.GetColorU32(Accent), FaceplateTitle);
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        if (ImGui.IsItemDeactivated())
        {
            var viewport = ImGui.GetMainViewport();
            var minimum = viewport.WorkPos;
            var maximum = Vector2.Max(minimum, minimum + viewport.WorkSize - ImGui.GetWindowSize());
            var target = Vector2.Clamp(ImGui.GetWindowPos(), minimum, maximum);
            var threshold = ImGui.GetTextLineHeight() * 1.5f;
            if (target.X - minimum.X < threshold) target.X = minimum.X;
            else if (maximum.X - target.X < threshold) target.X = maximum.X;
            if (target.Y - minimum.Y < threshold) target.Y = minimum.Y;
            else if (maximum.Y - target.Y < threshold) target.Y = maximum.Y;
            ImGui.SetWindowPos(target);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to move");
        ImGui.SameLine();
        if (PanelButton("stations", FontAwesomeIcon.BroadcastTower, "Stations", EditorPanel.Stations)) ToggleEditor(EditorPanel.Stations);
        ImGui.SameLine();
        if (PanelButton("settings", FontAwesomeIcon.Cog, "Settings", EditorPanel.Settings)) ToggleEditor(EditorPanel.Settings);
        ImGui.SameLine();
        if (IconButton("power", FontAwesomeIcon.PowerOff, plugin.Config.PoweredOn ? "Power off — standby" : "Power on"))
        {
            plugin.Config.PoweredOn = !plugin.Config.PoweredOn;
            if (!plugin.Config.PoweredOn) plugin.Player.Pause();
            plugin.Config.Save();
        }
        ImGui.SameLine();
        if (IconButton("close", FontAwesomeIcon.Times, "Hide radio")) IsOpen = false;
        DrawPlayer();
        DrawEditor();
    }

    private static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, float width = 0)
    {
        var height = ImGui.GetFrameHeight();
        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.Button(((char)icon).ToString() + "##" + id, new Vector2(width > 0 ? width : height, height));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private bool PanelButton(string id, FontAwesomeIcon icon, string tooltip, EditorPanel panel)
    {
        var selected = editorOpen && editorPanel == panel;
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(DisplayAccent * 0.35f, 1));
        var clicked = IconButton(id, icon, tooltip);
        if (selected) ImGui.PopStyleColor();
        return clicked;
    }

    private void ToggleEditor(EditorPanel panel)
    {
        editorOpen = !editorOpen || editorPanel != panel;
        editorPanel = panel;
    }

    private void DrawEditor()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        if (editorHeight <= spacing + 1) return;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.035f, 0.045f, 0.045f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.10f, 0.13f, 0.13f, 1));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(DisplayAccent * 0.25f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(DisplayAccent * 0.35f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(DisplayAccent * 0.45f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        // An explicit child height clips the contents as the enclosure grows.
        // Each editor retains its own scroll position and scrolls independently of playback.
        if (ImGui.BeginChild("StereoEditor" + editorPanel, new Vector2(0, editorHeight - spacing), true))
        {
            ImGui.BeginDisabled(!editorOpen || editorProgress < 1);
            if (IconButton("collapseEditor", FontAwesomeIcon.ChevronUp, "Collapse panel")) editorOpen = false;
            ImGui.SameLine();
            ImGui.TextColored(Accent, editorPanel == EditorPanel.Stations ? "STATIONS" : "SETTINGS");
            ImGui.Separator();
            if (editorPanel == EditorPanel.Stations) DrawStations();
            else
            {
                DrawSettings();
                ImGui.Separator();
                ImGui.TextWrapped(plugin.Player.Status);
                if (plugin.Player.IsRunning && ImGui.Button("Reconnect")) plugin.Player.Play(plugin.Config);
            }
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(6);
    }

    private void DrawPlayer()
    {
        var config = plugin.Config;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var sliderWidth = ImGui.CalcTextSize("100").X + ImGui.GetStyle().FramePadding.X * 2;
        var displayWidth = Math.Max(100, ImGui.GetContentRegionAvail().X - sliderWidth - spacing.X);
        ImGui.BeginGroup();
        DrawDisplay(displayWidth, displayHeight);
        var hasStations = config.Stations.Count > 0;
        ImGui.BeginDisabled(!hasStations || !config.PoweredOn);
        var buttonWidth = (displayWidth - spacing.X * 2) / 3;
        if (IconButton("previous", FontAwesomeIcon.StepBackward, "Previous station", buttonWidth)) ChangeStation(-1);
        ImGui.SameLine();
        var active = plugin.Player.IsRunning && !plugin.Player.IsSuspended;
        if (IconButton("playback", active ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, active ? "Pause radio" : "Play radio", buttonWidth))
        {
            if (active) plugin.Player.Pause();
            else plugin.Player.Play(config);
        }
        ImGui.SameLine();
        if (IconButton("next", FontAwesomeIcon.StepForward, "Next station", buttonWidth)) ChangeStation(1);
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
        if (!plugin.Config.PoweredOn)
        {
            using (titleFont?.Push())
            {
                const string standby = "STANDBY";
                var textSize = ImGui.CalcTextSize(standby);
                draw.AddText(origin + (new Vector2(width, height) - textSize) / 2, ImGui.GetColorU32(Muted), standby);
            }
            ImGui.Dummy(new Vector2(width, height));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Radio powered off. Use the power button to enable playback.");
            return;
        }
        var station = plugin.Player.PlayingStation;
        if (station.Length == 0 && plugin.Config.SelectedStation >= 0 && plugin.Config.SelectedStation < plugin.Config.Stations.Count)
            station = plugin.Config.Stations[plugin.Config.SelectedStation].Name;
        if (station.Length == 0) station = "NO STATION SELECTED";
        var track = plugin.Player.Track;
        var title = !plugin.Player.IsRunning || plugin.Player.IsSuspended ? "" : track.Display.Length > 0 ? (track.Artist.Length > 0 ? $"{track.Artist} - {track.Title}" : track.Title) : "Waiting for station track information…";
        var left = origin + new Vector2(10, 6);
        var right = origin + new Vector2(width - 10, height - 4);
        var statistics = plugin.Player.Statistics;
        var statisticsText = statistics == null
            ? ""
            : statistics.BitRate > 0 ? $"{statistics.Codec} {statistics.BitRate / 1000}k" : statistics.Codec;
        var bufferedSeconds = statistics?.EstimatedBufferedSeconds(Environment.TickCount64) ?? 0;
        var health = streamHealth.Update(bufferedSeconds, plugin.Player.IsPlaying);
        var statisticsWidth = 0f;
        using (stationFont?.Push()) statisticsWidth = ImGui.CalcTextSize(statisticsText).X;
        var lightRadius = stationHeight * 0.22f;
        var statisticsGap = statisticsText.Length > 0 ? lightRadius * 2 + 6 : 0;
        var statisticsLeft = right.X - statisticsWidth - statisticsGap;
        draw.PushClipRect(left, new Vector2(Math.Max(left.X, statisticsLeft - 8), right.Y), true);
        using (station.All(c => c >= ' ' && c <= '~') ? stationFont?.Push() : null)
            draw.AddText(left, ImGui.GetColorU32(Muted), station);
        draw.PopClipRect();
        if (statisticsText.Length > 0)
        {
            var light = health switch
            {
                StreamHealth.Healthy => new Vector4(0.30f, 0.90f, 0.45f, 1),
                StreamHealth.Low => new Vector4(1.00f, 0.72f, 0.20f, 1),
                StreamHealth.Starving => new Vector4(1.00f, 0.25f, 0.20f,
                    (float)(0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(ImGui.GetTime() * Math.PI * 4)))),
                _ => Muted,
            };
            var lightCenter = new Vector2(statisticsLeft + lightRadius, left.Y + stationHeight / 2);
            draw.AddCircleFilled(lightCenter, lightRadius, ImGui.GetColorU32(light));
            using (stationFont?.Push())
                draw.AddText(new Vector2(statisticsLeft + statisticsGap, left.Y), ImGui.GetColorU32(Muted), statisticsText);
        }
        var line = left + new Vector2(0, stationHeight + 3);
        draw.PushClipRect(line, right, true);
        if (scrollingText != title) { scrollingText = title; scrollStarted = ImGui.GetTime(); }
        // Bundled display font uses the atlas default glyph range: keep international
        // metadata readable by falling back to the user's normal UI font.
        using var displayScope = title.All(c => c >= ' ' && c <= '~') ? displayFont?.Push() : null;
        var textWidth = ImGui.CalcTextSize(title).X;
        var available = Math.Max(1, width - 20);
        var offset = 0f;
        if (plugin.Config.ScrollTrackText && textWidth > available)
        {
            var gap = Math.Max(available * 0.65f, ImGui.GetTextLineHeight() * 5);
            var cycle = textWidth + gap;
            const double pauseSeconds = 2;
            const double pixelsPerSecond = 28;
            var cycleSeconds = pauseSeconds + cycle / pixelsPerSecond;
            var cycleTime = Math.Max(0, ImGui.GetTime() - scrollStarted) % cycleSeconds;
            // Hold the beginning on every lap, including the first one.
            offset = (float)(Math.Max(0, cycleTime - pauseSeconds) * pixelsPerSecond);
            // The second copy enters after a gap. At wrap, it occupies precisely
            // the first copy's position, so there is no visible reset jump.
            draw.AddText(line + new Vector2(cycle - offset, 0), ImGui.GetColorU32(Accent), title);
        }
        draw.AddText(line - new Vector2(offset, 0), ImGui.GetColorU32(Accent), title);
        draw.PopClipRect();
        ImGui.Dummy(new Vector2(width, height));
        if (ImGui.IsItemHovered())
        {
            var details = statistics is { BitRate: > 0 }
                ? $"{statistics.Codec} • {statistics.BitRate / 1000} kbps • {statistics.SampleRate / 1000.0:0.#} kHz • " +
                  (statistics.Channels == 1 ? "Mono" : statistics.Channels == 2 ? "Stereo" : $"{statistics.Channels} channels") +
                  $"\nBuffer: {bufferedSeconds:0.0} seconds"
                : "Stream details unavailable";
            ImGui.SetTooltip((track.Display.Length > 0 ? track.Display + "\n" : "") + details + "\n" + plugin.Player.Status);
        }
    }

    private void Tune(int index)
    {
        var continuePlayback = plugin.Player.HasPlaybackIntent && !plugin.Player.IsSuspended;
        plugin.Config.SelectedStation = index;
        plugin.Config.Save();
        if (continuePlayback) plugin.Player.Play(plugin.Config);
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
        PollStationProbe();
        ImGui.TextColored(Accent, "SAVED STATIONS");
        if (ImGui.Button("Add station"))
        {
            CancelStationProbe();
            editedStation = null;
            editIndex = -1;
            editName = "New station";
            editUrl = "";
            editStreamType = StationStreamType.Unknown;
            inspectedUrl = "";
            editMessage = "";
        }
        if (ImGui.BeginListBox("##StationLibrary", new Vector2(-1, 110)))
        {
            for (var i = 0; i < config.Stations.Count; i++)
            {
                if (!ImGui.Selectable($"{config.Stations[i].Name}##saved{i}", editedStation == config.Stations[i])) continue;
                CancelStationProbe();
                editIndex = i;
                editedStation = config.Stations[i];
                editName = editedStation.Name;
                editUrl = editedStation.Url;
                editStreamType = editedStation.StreamType;
                inspectedUrl = editedStation.Url;
                editMessage = "";
            }
            ImGui.EndListBox();
        }
        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##StationName", ref editName, 256);
        ImGui.TextUnformatted("Stream format");
        var formatLabel = editStreamType switch
        {
            StationStreamType.Mp3 => "MP3",
            StationStreamType.OggFlac => "Ogg / FLAC",
            _ => "Choose format",
        };
        if (ImGui.BeginCombo("##StationFormat", formatLabel))
        {
            if (ImGui.Selectable("MP3", editStreamType == StationStreamType.Mp3)) editStreamType = StationStreamType.Mp3;
            if (ImGui.Selectable("Ogg / FLAC", editStreamType == StationStreamType.OggFlac)) editStreamType = StationStreamType.OggFlac;
            ImGui.EndCombo();
        }
        ImGui.TextUnformatted("Direct stream URL");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##StationUrl", ref editUrl, 2048))
        {
            CancelStationProbe();
            inspectedUrl = "";
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) StartStationProbe();
        ImGui.BeginDisabled(stationProbe != null);
        if (ImGui.SmallButton("Inspect stream")) StartStationProbe();
        ImGui.EndDisabled();
        if (stationProbe != null) { ImGui.SameLine(); ImGui.TextUnformatted("Inspecting…"); }
        if (ImGui.Button(editedStation == null ? "Save new station" : "Save changes"))
        {
            if (string.IsNullOrWhiteSpace(editName) || !Uri.TryCreate(editUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) editMessage = "Enter a name and a direct HTTP(S) audio stream URL.";
            else if (stationProbe != null)
            {
                saveAfterProbe = true;
                editMessage = "Inspecting stream before saving…";
            }
            else if (inspectedUrl != editUrl.Trim()) StartStationProbe(saveAfter: true);
            else SaveStation(uri);
        }
        if (editedStation != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Tune in")) { Tune(editIndex); if (!plugin.Player.IsRunning || plugin.Player.IsSuspended) plugin.Player.Play(config); }
            ImGui.SameLine();
            if (ImGui.Button("Delete")) ImGui.OpenPopup("Delete station?");
        }
        if (ImGui.BeginPopup("Delete station?"))
        {
            ImGui.TextUnformatted("Delete this saved station?");
            if (ImGui.Button("Delete station") && editedStation != null)
            {
                if (config.SelectedStation == editIndex) plugin.Player.Stop();
                CancelStationProbe();
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

    private void StartStationProbe(bool saveAfter = false)
    {
        CancelStationProbe();
        probeUrl = editUrl.Trim();
        saveAfterProbe = saveAfter;
        lookupCancellation = new CancellationTokenSource();
        stationProbe = StationProbe.InspectAsync(probeUrl, lookupCancellation.Token);
        editMessage = saveAfter ? "Inspecting stream before saving…" : "Inspecting stream…";
    }

    private void PollStationProbe()
    {
        if (stationProbe is not { IsCompleted: true }) return;
        var shouldSave = saveAfterProbe;
        try
        {
            var result = stationProbe.GetAwaiter().GetResult();
            if (editUrl.Trim() == probeUrl)
            {
                editStreamType = result.StreamType;
                if (result.StationName != null &&
                    (string.IsNullOrWhiteSpace(editName) || editName == "New station"))
                    editName = result.StationName;
                inspectedUrl = probeUrl;
                editMessage = $"Detected {FormatName(result.StreamType)}." +
                              (result.StationName == null ? " This station does not send a name." : " Station name found.");
                if (shouldSave && Uri.TryCreate(editUrl.Trim(), UriKind.Absolute, out var uri)) SaveStation(uri);
            }
        }
        catch (OperationCanceledException) { editMessage = "Stream inspection timed out or was cancelled. Select a format manually."; }
        catch (Exception ex)
        {
            inspectedUrl = probeUrl;
            editMessage = $"Could not identify the stream: {ex.Message} Select a format manually and save again.";
        }
        finally
        {
            lookupCancellation?.Dispose();
            lookupCancellation = null;
            stationProbe = null;
            saveAfterProbe = false;
        }
    }

    private void CancelStationProbe()
    {
        lookupCancellation?.Cancel();
        lookupCancellation?.Dispose();
        lookupCancellation = null;
        // Observe eventual failures from requests superseded by another edit.
        if (stationProbe != null)
            _ = stationProbe.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        stationProbe = null;
        saveAfterProbe = false;
    }

    private void SaveStation(Uri uri)
    {
        if (editStreamType == StationStreamType.Unknown)
        {
            editMessage = "Select a stream format before saving.";
            return;
        }

        var config = plugin.Config;
        CancelStationProbe();
        if (editedStation == null)
        {
            editedStation = new Station();
            config.Stations.Add(editedStation);
            editIndex = config.Stations.Count - 1;
        }
        var connectionChanged = editedStation.Url != uri.AbsoluteUri || editedStation.StreamType != editStreamType;
        editedStation.Name = editName.Trim();
        editedStation.Url = uri.AbsoluteUri;
        editedStation.StreamType = editStreamType;
        inspectedUrl = editUrl.Trim();
        config.Save();
        if (connectionChanged && config.SelectedStation == editIndex && plugin.Player.IsRunning && !plugin.Player.IsSuspended)
            plugin.Player.Play(config);
        editMessage = $"Saved as {FormatName(editStreamType)}.";
    }

    private static string FormatName(StationStreamType type)
        => type == StationStreamType.OggFlac ? "Ogg / FLAC" : "MP3";

    public void Dispose()
    {
        CancelStationProbe();
        displayFont?.Dispose();
        titleFont?.Dispose();
        stationFont?.Dispose();
    }

    private void DrawSettings()
    {
        var config = plugin.Config;
        ImGui.TextColored(Accent, "PLAYBACK");
        var fullTime = config.FullTimePlayback;
        if (ImGui.Checkbox("Keep playing off mount", ref fullTime)) { config.FullTimePlayback = fullTime; config.Save(); }
        ImGui.TextWrapped(fullTime ? "Play throughout your session. Logout still stops radio." : "Dismounting stops the radio. You can also start it manually.");
        var reduceInCutscenes = config.ReduceVolumeInCutscenes;
        if (ImGui.Checkbox("Reduce radio volume during cutscenes", ref reduceInCutscenes))
        {
            config.ReduceVolumeInCutscenes = reduceInCutscenes;
            config.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use 20% of normal radio volume during cutscenes; restore it afterward.");
        var autoplay = config.AutoPlay;
        if (ImGui.Checkbox(fullTime ? "Autoplay on login / enabling full-time mode" : "Autoplay when mounting", ref autoplay)) { config.AutoPlay = autoplay; config.Save(); }
        var mute = config.MuteGameMusic;
        if (ImGui.Checkbox("Mute game music while radio plays", ref mute)) { config.MuteGameMusic = mute; config.Save(); }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Accent, "DISPLAY");
        if (fontWarning.Length > 0) ImGui.TextWrapped(fontWarning);
        var accent = config.AccentColor;
        if (ImGui.ColorEdit3("Accent color", ref accent)) config.AccentColor = accent;
        if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
        var presets = new (string Name, Vector3 Color)[]
        {
            ("Amber", new(1f, 0.72f, 0.30f)),
            ("Green", new(0.4f, 1f, 0.55f)),
            ("Ice", new(0.4f, 0.85f, 1f)),
            ("Rose", new(1f, 0.5f, 0.7f)),
        };
        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.Button(presets[i].Name)) { config.AccentColor = presets[i].Color; config.Save(); }
        }
        var popup = config.OpenOnMount;
        if (ImGui.Checkbox("Open player when mounting", ref popup)) { config.OpenOnMount = popup; config.Save(); }
        var scroll = config.ScrollTrackText;
        if (ImGui.Checkbox("Scroll long track names", ref scroll)) { config.ScrollTrackText = scroll; config.Save(); }
        ImGui.TextWrapped("Track information comes from the station. Some stations do not send titles, and artist/title formatting may vary.");
    }
}
