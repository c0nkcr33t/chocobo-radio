using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace ChocoboRadio;

[Serializable]
public sealed class Station
{
    public string Name { get; set; } = "New station";
    public string Url { get; set; } = "";
    public StationStreamType StreamType { get; set; }
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OpenOnMount { get; set; } = true;
    public bool PoweredOn { get; set; } = true;
    public bool ReduceVolumeInCutscenes { get; set; } = true;
    public bool AutoPlay { get; set; } = false;
    public bool FullTimePlayback { get; set; } = false;
    public bool ScrollTrackText { get; set; } = true;
    public bool MuteGameMusic { get; set; } = true;
    public Vector3 AccentColor { get; set; } = new(1f, 0.72f, 0.30f);
    public int Volume { get; set; } = 35;
    public int SelectedStation { get; set; }
    public List<Station> Stations { get; set; } = new();
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
