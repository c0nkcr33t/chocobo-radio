using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ChocoboRadio;

internal sealed record TrackInfo(string Artist, string Title)
{
    public static readonly TrackInfo Empty = new("", "");
    public string Display => Artist.Length == 0 ? Title : $"{Artist} — {Title}";

    public static TrackInfo? FromMetadata(byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        var match = Regex.Match(text, @"(?:^|;)\s*StreamTitle='(.*?)';", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return null; // No update, rather than clearing the last title.
        var value = new string(match.Groups[1].Value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        var split = value.IndexOf(" - ", StringComparison.Ordinal);
        return split > 0 ? new TrackInfo(value[..split].Trim(), value[(split + 3)..].Trim()) : new TrackInfo("", value);
    }
}
