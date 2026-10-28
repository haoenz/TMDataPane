using System.Globalization;
using System.Text.Json;

namespace DataPane.Core;

public static class Presets
{
    public static Collected Codex(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rate_limit", out var limits) || limits.ValueKind != JsonValueKind.Object) throw new FormatException("缺少 rate_limit");
        var fields = new Dictionary<string, string>();
        foreach (string key in new[] { "primary_window", "secondary_window" })
        {
            if (!limits.TryGetProperty(key, out var window) || window.ValueKind == JsonValueKind.Null) continue;
            if (!window.TryGetProperty("limit_window_seconds", out var length) || !length.TryGetInt64(out long seconds)) throw new FormatException("缺少窗口时长");
            string prefix = seconds switch { 18000 => "h5", 604800 => "week", _ => "" };
            if (prefix.Length == 0) continue;
            if (fields.ContainsKey(prefix + "Remaining")) throw new FormatException("窗口时长重复");
            decimal used = Extraction.Number(Extraction.Scalar(window.GetProperty("used_percent")));
            fields[prefix + "Remaining"] = (100 - Math.Clamp(used, 0, 100)).ToString(CultureInfo.InvariantCulture);
            if (window.TryGetProperty("reset_at", out var reset) && reset.TryGetInt64(out long at))
            {
                _ = DateTimeOffset.FromUnixTimeSeconds(at);
                fields[prefix + "ResetAt"] = at.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (fields.Count == 0) throw new FormatException("没有受支持的用量窗口");
        return new(fields, "Codex 用量窗口已解析（预览仅含提取字段）");
    }
    public static Collected DeepSeek(string json, string currency)
    {
        using var doc = JsonDocument.Parse(json);
        var fields = new Dictionary<string, string>();
        var available = doc.RootElement.GetProperty("is_available").GetBoolean();
        fields["isAvailable"] = available ? "true" : "false";
        var matches = doc.RootElement.GetProperty("balance_infos").EnumerateArray()
            .Where(x => x.GetProperty("currency").GetString() == currency).ToList();
        if (matches.Count != 1) throw new FormatException("币种记录缺失或重复");
        fields["currency"] = currency;
        foreach (var name in new[] { "total_balance", "granted_balance", "topped_up_balance" })
            fields[name] = Extraction.Number(matches[0].GetProperty(name).GetString() ?? "").ToString(CultureInfo.InvariantCulture);
        return new(fields, "DeepSeek 余额已解析（预览仅含提取字段）", available ? "" : "账户 is_available=false");
    }
    public static Configuration Demo() => new()
    {
        Enabled = true, Sources = [new() { Id = "demo", Kind = "demo" }],
        Metrics = [new() { Id = "demo", Source = "demo", Input = "value" }],
        Views = [new() { Slot = 1, Metric = "demo", Label = "DEMO", Format = "0.0", Unit = "%" }]
    };
    public static void Add(Configuration config, string kind)
    {
        string id = kind + "_" + Guid.NewGuid().ToString("N")[..6];
        var source = new Source { Id = id, Kind = kind };
        if (kind == "deepseek") source.Credential = "deepseek";
        if (kind is "http_json" or "http_html" or "browser_dom")
        {
            source.Url = "https://example.com/";
            source.Fields = [new() { Name = "value", Path = kind == "http_json" ? "$.value" : "#balance", Steps = [new()] }];
        }
        var names = kind switch { "codex" => new[] { "h5Remaining", "weekRemaining" }, "deepseek" => ["total_balance"], _ => ["value"] };
        foreach (string name in names)
        {
            int slot = Enumerable.Range(1, 16).FirstOrDefault(x => config.Views.All(v => v.Slot != x));
            if (slot == 0) throw new FormatException("没有空闲显示槽位");
            string metric = id + "_" + name;
            config.Metrics.Add(new() { Id = metric, Source = id, Input = name });
            config.Views.Add(new() { Slot = slot, Metric = metric, Label = kind == "codex" ? (name == "h5Remaining" ? "Codex 5h" : "Codex 周") : kind,
                Unit = kind == "codex" ? "%" : kind == "deepseek" ? " CNY" : "" });
        }
        config.Sources.Add(source);
    }
}
