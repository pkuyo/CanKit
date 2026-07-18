using System;
using System.Collections.Generic;

namespace CanKit.Abstractions.SPI.Registry.Core.Endpoints;

/// <summary>
/// Parsed CAN endpoint of the form <c>scheme://host[/path][?query][#fragment]</c>.
/// </summary>
/// <remarks>
/// The endpoint is parsed with a small hand-written tokenizer instead of <see cref="Uri"/>
/// so that device names such as <c>zlg://USBCANFD-200U</c> keep their original case and
/// characters like <c>-</c>, <c>_</c>, <c>.</c> and percent-encoded sequences are accepted
/// in the host and path (see internal deep code review §2.5).
/// </remarks>
public readonly struct CanEndpoint
{
    /// <summary>
    /// Scheme part (e.g. <c>socketcan</c>, <c>zlg</c>). Normalized to lower case, matching
    /// the case-insensitive lookup performed by <c>CanRegistry</c>.
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Combined <c>host[/path]</c> with the leading and trailing slash removed and case preserved.
    /// Percent-encoded sequences in the host and path are decoded.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Query key-value pairs. Both keys and values are percent-decoded; lookup is case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>
    /// Fragment without the leading <c>#</c>, percent-decoded. <c>null</c> when the input has no fragment.
    /// </summary>
    public string? Fragment { get; }

    /// <summary>
    /// The original endpoint string exactly as supplied to <see cref="Parse"/>.
    /// </summary>
    public string Original { get; }

    private CanEndpoint(string scheme, string path, Dictionary<string, string> query, string? fragment, string original)
    {
        Scheme = scheme;
        Path = path;
        Query = query;
        Fragment = fragment;
        Original = original;
    }

    /// <summary>
    /// Parse an endpoint string of the form <c>scheme://host[/path][?query][#fragment]</c>.
    /// </summary>
    /// <param name="endpoint">The endpoint string to parse.</param>
    /// <returns>The parsed <see cref="CanEndpoint"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="endpoint"/> is empty or white-space.</exception>
    /// <exception cref="FormatException">The scheme is missing/invalid or the structure is malformed.</exception>
    public static CanEndpoint Parse(string endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint must not be empty.", nameof(endpoint));

        // Grammar (deliberately more permissive than RFC 3986 for the host so device
        // names such as "USBCANFD-200U" or "My.Device_01" are accepted verbatim):
        //   endpoint  = scheme "://" host [ "/" path ] [ "?" query ] [ "#" fragment ]
        //   scheme    = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )   ; RFC 3986
        //   host/path = any character except '/', '?', '#' (host stops at first '/')
        // Host and path may contain percent-encoded octets; they are decoded on the
        // way out. The delimiters '#', '?' and (for the host) '/' bind left-to-right,
        // matching the behavior of Uri for the schemes we care about.
        int schemeSep = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep <= 0)
            throw new FormatException(
                $"Endpoint '{endpoint}' must be of the form 'scheme://host[/path][?query][#fragment]'.");

        string scheme = endpoint.Substring(0, schemeSep);
        ValidateScheme(scheme, endpoint);
        scheme = scheme.ToLowerInvariant();

        int authorityStart = schemeSep + 3;

        // Fragment binds first (its '?' and '/' are literal characters inside the fragment).
        int hashIdx = endpoint.IndexOf('#', authorityStart);
        int endBeforeFragment = hashIdx >= 0 ? hashIdx : endpoint.Length;

        // Query starts at the first '?' before the fragment.
        int qIdx = IndexOfInRange(endpoint, '?', authorityStart, endBeforeFragment);
        int endBeforeQuery = qIdx >= 0 ? qIdx : endBeforeFragment;

        // Path starts at the first '/' after the authority.
        int slashIdx = IndexOfInRange(endpoint, '/', authorityStart, endBeforeQuery);

        string hostRaw;
        string pathRaw;
        if (slashIdx < 0)
        {
            hostRaw = endpoint.Substring(authorityStart, endBeforeQuery - authorityStart);
            pathRaw = string.Empty;
        }
        else
        {
            hostRaw = endpoint.Substring(authorityStart, slashIdx - authorityStart);
            // Trim trailing slashes so that "scheme://host/" behaves like "scheme://host"
            // and "scheme://host/a/" behaves like "scheme://host/a".
            pathRaw = endpoint.Substring(slashIdx + 1, endBeforeQuery - slashIdx - 1).TrimEnd('/');
        }

        if (hostRaw.Length == 0)
        {
            // Allow "scheme://?..." and "scheme://#..." (query/fragment-only endpoints);
            // some adapters (kvaser, pcan, controlcan) read channel/type from the query
            // when no host is supplied. Reject "scheme://" alone and "scheme:///path"
            // (empty host followed by a slash-delimited path), which the prior
            // System.Uri-based parser also rejected.
            bool hasQueryOrFragment = qIdx >= 0 || hashIdx >= 0;
            bool hasSlashPath = slashIdx >= 0;
            if (!hasQueryOrFragment || hasSlashPath)
                throw new FormatException(
                    $"Endpoint '{endpoint}' is missing a host between '://' and the next delimiter.");
        }

        string host = hostRaw.Length == 0 ? string.Empty : Unescape(hostRaw);
        string path = pathRaw.Length == 0 ? string.Empty : Unescape(pathRaw);
        string combined = host.Length == 0
            ? path
            : (path.Length == 0 ? host : host + "/" + path);

        string? fragment = null;
        if (hashIdx >= 0)
        {
            string fragRaw = endpoint.Substring(hashIdx + 1);
            fragment = string.IsNullOrEmpty(fragRaw) ? null : Unescape(fragRaw);
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (qIdx >= 0)
        {
            string qRaw = endpoint.Substring(qIdx + 1, endBeforeFragment - qIdx - 1);
            if (qRaw.Length > 0)
            {
                foreach (var pair in qRaw.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split(new[] { '=' }, 2);
                    var k = Unescape(kv[0]);
                    var v = kv.Length > 1 ? Unescape(kv[1]) : string.Empty;
                    query[k] = v;
                }
            }
        }

        return new CanEndpoint(scheme, combined, query, fragment, endpoint);
    }

    /// <summary>
    /// Try get query value by key.
    /// </summary>
    public bool TryGet(string key, out string? value)
    {
        if (Query.TryGetValue(key, out var v)) { value = v; return true; }
        value = null; return false;
    }

    private static void ValidateScheme(string scheme, string endpoint)
    {
        if (scheme.Length == 0)
            throw new FormatException($"Endpoint '{endpoint}' has an empty scheme.");
        char first = scheme[0];
        if (!IsAlpha(first))
            throw new FormatException(
                $"Endpoint '{endpoint}' has an invalid scheme '{scheme}': the first character must be an ASCII letter.");
        for (int i = 1; i < scheme.Length; i++)
        {
            char c = scheme[i];
            if (!(IsAlpha(c) || IsDigit(c) || c == '+' || c == '-' || c == '.'))
                throw new FormatException(
                    $"Endpoint '{endpoint}' has an invalid scheme '{scheme}': character '{c}' is not allowed.");
        }
    }

    private static bool IsAlpha(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static int IndexOfInRange(string s, char c, int start, int endExclusive)
    {
        int count = endExclusive - start;
        return count <= 0 ? -1 : s.IndexOf(c, start, count);
    }

    private static string Unescape(string s)
    {
        if (s.Length == 0) return s;

        // Uri.UnescapeDataString silently keeps malformed escapes on some runtimes
        // and throws ArgumentException on others; validate up front so callers get
        // a consistent FormatException for bad "%XY" sequences.
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '%') continue;
            if (i + 2 >= s.Length || !IsHex(s[i + 1]) || !IsHex(s[i + 2]))
                throw new FormatException(
                    $"Endpoint contains malformed percent-encoding at position {i} in '{s}'.");
            i += 2;
        }

        try
        {
            return Uri.UnescapeDataString(s);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(
                $"Endpoint contains malformed percent-encoding in '{s}'.", ex);
        }
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
