using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ChocoboRadio;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    internal Configuration Config { get; }
    internal RadioPlayer Player { get; } = new();
    private readonly WindowSystem windows = new("ChocoboRadio");
    private readonly MainWindow window;
    private readonly GameMusicController music;
    private bool wasMounted;
    private bool wasLoggedIn;
    private readonly PlaybackPolicy playbackPolicy = new();

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        music = new GameMusicController(
            () => GameConfig.System.TryGetUInt("IsSndBgm", out var muted) ? muted != 0 : null,
            muted => GameConfig.System.Set("IsSndBgm", muted ? 1u : 0u),
            ex => Log.Error(ex, "Could not update game music mute state"));
        window = new MainWindow(this);
        windows.AddWindow(window);
        Commands.AddHandler("/chocoboradio", new CommandInfo((_, _) => window.Toggle()) { HelpMessage = "Open Chocobo Radio." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += window.Toggle;
        Framework.Update += Update;
    }

    private void Update(IFramework framework)
    {
        music.Update(Config.MuteGameMusic && Player.IsPlaying);
        // Missing settings fail closed instead of unexpectedly playing at full volume.
        var settings = GameConfig.System;
        var available = settings.TryGetUInt("SoundMaster", out var master)
            & settings.TryGetUInt("SoundBgm", out var bgm)
            & settings.TryGetUInt("IsSndMaster", out var masterMuted)
            & settings.TryGetUInt("IsSndBgm", out var bgmMuted);
        Player.SetVolume(available && masterMuted == 0 && (bgmMuted == 0 || music.OwnsMute)
            ? System.Math.Clamp(Config.Volume / 100f, 0, 1)
                * System.Math.Clamp(master / 100f, 0, 1) * System.Math.Clamp(bgm / 100f, 0, 1)
            : 0);
        var loggedIn = ClientState.IsLoggedIn;
        var mounted = loggedIn && (Conditions[ConditionFlag.Mounted] ||
            Conditions[ConditionFlag.RidingPillion] || Conditions[ConditionFlag.InFlight]);
        if (mounted && !wasMounted)
        {
            if (Config.OpenOnMount) window.IsOpen = true;

        }
        else if ((!mounted && wasMounted && !Config.FullTimePlayback) || (!loggedIn && wasLoggedIn))
        {
            window.IsOpen = false;
        }
        switch (playbackPolicy.Update(loggedIn, mounted, Config.FullTimePlayback, Config.AutoPlay, Player.IsRunning))
        {
            case PlaybackAction.Play: Player.Play(Config); break;
            case PlaybackAction.Stop: Player.Stop(); break;
        }
        wasMounted = mounted;
        wasLoggedIn = loggedIn;
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= window.Toggle;
        Commands.RemoveHandler("/chocoboradio");
        windows.RemoveAllWindows();
        window.Dispose();
        Player.Dispose();
        music.Update(false);
    }
}
