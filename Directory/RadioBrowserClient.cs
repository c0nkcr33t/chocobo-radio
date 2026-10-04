using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ChocoboRadio;

internal enum RadioBrowserSearchField { Name, Tag }

internal sealed record RadioBrowserStation(
    string StationUuid,
    string Name,
    string Url,
    string Codec,
    int BitRate,
    string Tags,
    string CountryCode,
    string Language,
    int Votes,
    int ClickCount,
    StationStreamType StreamType);

internal sealed class RadioBrowserClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly HttpClient clickHttp = CreateClickClient();
    private static readonly object mirrorGate = new();
    private static string[] cachedMirrors = [];
    private static long mirrorsExpireAt;

    public RadioBrowserClient()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ChocoboRadio/1.1");
    }

    public static async Task RecordClickAsync(string stationUuid)
    {
        if (string.IsNullOrWhiteSpace(stationUuid)) return;
        try
        {
            foreach (var mirror in await GetMirrorsAsync(CancellationToken.None).ConfigureAwait(false))
            {
                try
                {
                    using var response = await clickHttp.GetAsync(
                        $"{mirror}/json/url/{Uri.EscapeDataString(stationUuid)}").ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
            }
        }
        catch { }
    }

    private static HttpClient CreateClickClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChocoboRadio/1.1");
        return client;
    }

    public async Task<IReadOnlyList<RadioBrowserStation>> SearchAsync(
        string query,
        RadioBrowserSearchField field,
        CancellationToken token)
    {
        var mirrors = await GetMirrorsAsync(token).ConfigureAwait(false);
        Exception? lastError = null;
        foreach (var mirror in mirrors)
        {
            try
            {
                var requests = new[] { "MP3", "FLAC", "OGG" }
                    .Select(codec => FetchAsync(mirror, query, field, codec, token));
                var responses = await Task.WhenAll(requests).ConfigureAwait(false);
                return responses.SelectMany(result => result)
                    .Select(Convert)
                    .Where(station => station != null)
                    .Cast<RadioBrowserStation>()
                    .GroupBy(station => station.StationUuid, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderByDescending(station => station.ClickCount)
                    .ThenByDescending(station => station.Votes)
                    .Take(75)
                    .ToArray();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                lastError = ex;
            }
        }

        throw new HttpRequestException("No Radio Browser mirror responded.", lastError);
    }

    private async Task<List<ApiStation>> FetchAsync(
        string mirror,
        string query,
        RadioBrowserSearchField field,
        string codec,
        CancellationToken token)
    {
        var parameters = new List<string>
        {
            "hidebroken=true",
            "order=clickcount",
            "reverse=true",
            "limit=50",
            "codec=" + Uri.EscapeDataString(codec),
        };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var key = field == RadioBrowserSearchField.Tag ? "tag" : "name";
            parameters.Add(key + "=" + Uri.EscapeDataString(query.Trim()));
        }

        var uri = $"{mirror}/json/stations/search?{string.Join("&", parameters)}";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<ApiStation>>(body, cancellationToken: token).ConfigureAwait(false) ?? [];
    }

    private static RadioBrowserStation? Convert(ApiStation station)
    {
        if (station.Hls != 0 || station.LastCheckOk != 1 || string.IsNullOrWhiteSpace(station.StationUuid)) return null;
        var url = string.IsNullOrWhiteSpace(station.UrlResolved) ? station.Url : station.UrlResolved;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;

        var streamType = Classify(station.Codec, uri.AbsoluteUri, station.Name, station.Tags);
        if (streamType == StationStreamType.Unknown) return null;

        return new RadioBrowserStation(
            station.StationUuid,
            Clean(station.Name, "Unnamed station"),
            uri.AbsoluteUri,
            station.Codec.ToUpperInvariant(),
            station.BitRate,
            Clean(station.Tags, ""),
            Clean(station.CountryCode, ""),
            Clean(station.Language, ""),
            station.Votes,
            station.ClickCount,
            streamType);
    }

    internal static StationStreamType Classify(string codec, string url, string name = "", string tags = "")
    {
        if (codec.Equals("MP3", StringComparison.OrdinalIgnoreCase)) return StationStreamType.Mp3;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return StationStreamType.Unknown;
        var path = uri.AbsolutePath;
        if (path.EndsWith(".oga", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return codec.Equals("FLAC", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("flac", StringComparison.OrdinalIgnoreCase) ||
                   tags.Contains("flac", StringComparison.OrdinalIgnoreCase)
                ? StationStreamType.OggFlac
                : StationStreamType.Unknown;
        return codec.Equals("OGG", StringComparison.OrdinalIgnoreCase) &&
               (name.Contains("flac", StringComparison.OrdinalIgnoreCase) ||
                tags.Contains("flac", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("flac", StringComparison.OrdinalIgnoreCase))
            ? StationStreamType.OggFlac
            : StationStreamType.Unknown;
    }

    private static string Clean(string? value, string fallback)
    {
        var cleaned = new string((value ?? "").Where(c => !char.IsControl(c)).Take(512).ToArray()).Trim();
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    private static async Task<string[]> GetMirrorsAsync(CancellationToken token)
    {
        lock (mirrorGate)
        {
            if (cachedMirrors.Length > 0 && Environment.TickCount64 < mirrorsExpireAt)
                return cachedMirrors;
        }

        var addresses = await Dns.GetHostAddressesAsync("all.api.radio-browser.info", token).ConfigureAwait(false);
        var lookups = addresses.Select(async address =>
        {
            try
            {
                var host = await Dns.GetHostEntryAsync(address).WaitAsync(token).ConfigureAwait(false);
                var name = host.HostName.TrimEnd('.');
                return name.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + name
                    : null;
            }
            catch { return null; }
        });
        var mirrors = (await Task.WhenAll(lookups).ConfigureAwait(false))
            .Where(value => value != null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();
        if (mirrors.Length == 0) mirrors = ["https://de1.api.radio-browser.info"];
        lock (mirrorGate)
        {
            cachedMirrors = mirrors;
            mirrorsExpireAt = Environment.TickCount64 + 60 * 60 * 1000;
        }
        return mirrors;
    }

    public void Dispose() => http.Dispose();

    private sealed class ApiStation
    {
        [JsonPropertyName("stationuuid")] public string StationUuid { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("url_resolved")] public string UrlResolved { get; set; } = "";
        [JsonPropertyName("tags")] public string Tags { get; set; } = "";
        [JsonPropertyName("countrycode")] public string CountryCode { get; set; } = "";
        [JsonPropertyName("language")] public string Language { get; set; } = "";
        [JsonPropertyName("codec")] public string Codec { get; set; } = "";
        [JsonPropertyName("bitrate")] public int BitRate { get; set; }
        [JsonPropertyName("hls")] public int Hls { get; set; }
        [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; set; }
        [JsonPropertyName("votes")] public int Votes { get; set; }
        [JsonPropertyName("clickcount")] public int ClickCount { get; set; }
    }
}
