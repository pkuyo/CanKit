using System;
using System.Collections.Generic;

namespace CanKit.Core.Endpoints;

/// <summary>
/// Parsed CAN endpoint (解析后的 CAN Endpoint)。
/// </summary>
public readonly struct CanEndpoint
{
    /// <summary>
    /// Scheme part, e.g. socketcan/zlg (协议前缀)。
    /// </summary>
    public string Scheme { get; }
    /// <summary>
    /// Path part without leading slash (路径部分，无前导斜杠)。
    /// </summary>
    public string Path { get; }
    /// <summary>
    /// Query key-value pairs (查询参数键值对)。
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; }
    /// <summary>
    /// Fragment without leading # (片段部分，无 #)。
    /// </summary>
    public string? Fragment { get; }
    /// <summary>
    /// Original endpoint string (原始 Endpoint 字符串)。
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
    /// Parse endpoint string (解析 Endpoint 字符串)。
    /// </summary>
    public static CanEndpoint Parse(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentNullException(nameof(endpoint));

        // Accept forms:
        //  - scheme://path?key=value#frag
        //  - scheme:path (fallback)
        if (!endpoint.Contains("://"))
        {
            var idx = endpoint.IndexOf(':');
            if (idx > 0)
            {
                var scheme = endpoint.Substring(0, idx);
                var path = endpoint.Substring(idx + 1);
                return new CanEndpoint(scheme, path, new Dictionary<string, string>(), null, endpoint);
            }
        }

        var uri = new Uri(endpoint, UriKind.Absolute);
        var schemePart = uri.Scheme;
        var pathPart = uri.AbsolutePath.Trim('/');
        var frag = string.IsNullOrWhiteSpace(uri.Fragment) ? null : uri.Fragment.TrimStart('#');

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var q = uri.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(q))
        {
            foreach (var pair in q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                query[k] = v;
            }
        }

        return new CanEndpoint(schemePart, pathPart, query, frag, endpoint);
    }

    /// <summary>
    /// Try get query value by key (按键尝试获取查询值)。
    /// </summary>
    public bool TryGet(string key, out string? value)
    {
        if (Query.TryGetValue(key, out var v)) { value = v; return true; }
        value = null; return false;
    }
}
