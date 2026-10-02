using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace ChocoboRadio;

[Serializable]
public sealed class Station
{
    public string Name { get; set; } = "New station";
    public string Url { get; set; } = "";
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OpenOnMount { get; set; } = true;
    public bool AutoPlay { get; set; } = false;
    public bool FullTimePlayback { get; set; } = false;
    public bool MuteGameMusic { get; set; } = true;
    public int Volume { get; set; } = 35;
    public int SelectedStation { get; set; }
    public List<Station> Stations { get; set; } = new();
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
