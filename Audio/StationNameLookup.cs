using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ChocoboRadio;

internal static class StationNameLookup
{
    public static async Task<string?> FindAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Enter a valid HTTP(S) stream URL first.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Icy-MetaData", "1");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // Dispose immediately after headers; no decoder, output device, or audio download loop.
        if (!response.Headers.TryGetValues("icy-name", out var values)) return null;
        var name = new string((values.FirstOrDefault() ?? "").Where(c => !char.IsControl(c)).Take(255).ToArray()).Trim();
        return name.Length == 0 ? null : name;
    }
}
