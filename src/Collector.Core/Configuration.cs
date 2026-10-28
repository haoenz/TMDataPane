using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageBridge.Core;

public sealed class Configuration
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; }
    public List<Source> Sources { get; set; } = [];
    public List<Metric> Metrics { get; set; } = [];
    public List<View> Views { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    [JsonIgnore] public bool HasWork => Enabled && Sources.Any(x => x.Enabled);
    public static Configuration Parse(string json)
    {
        if (json.Length > 1_048_576) throw new FormatException("配置超过 1 MiB");
        var result = JsonSerializer.Deserialize<Configuration>(json, JsonOptions) ?? throw new FormatException("配置为空");
        result.Validate();
        return result;
    }
    public void Validate()
    {
        if (Version != 2) throw new FormatException("配置版本必须是 2");
        if (Sources is null || Metrics is null || Views is null) throw new FormatException("配置列表不能为 null");
        if (Sources.Count > 32 || Metrics.Count > 128 || Views.Count > 16) throw new FormatException("最多 32 个源、128 个指标、16 个显示项");
        Unique(Sources.Select(x => x.Id)); Unique(Metrics.Select(x => x.Id));
        var sourceIds = Sources.Select(x => x.Id).ToHashSet();
        var metricIds = Metrics.Select(x => x.Id).ToHashSet();
        foreach (var source in Sources)
        {
            if (!new[] { "demo", "codex", "deepseek", "http_json", "http_html", "browser_dom" }.Contains(source.Kind)) throw new FormatException("未知数据源类型");
            if (source.IntervalSeconds < 5 || source.IntervalSeconds > 86400 || source.TimeoutSeconds < 1 || source.TimeoutSeconds > 120) throw new FormatException("刷新间隔须为 5–86400 秒，超时须为 1–120 秒");
            if (source.Kind.StartsWith("http_") || source.Kind == "browser_dom") RequireUrl(source.Url);
            if (source.Headers is null || source.SecretHeaders is null || source.Fields is null) throw new FormatException("请求与提取字段不能为 null");
            if (source.Headers.Keys.Any(IsSensitiveName)) throw new FormatException("认证头应使用 secretHeaders 引用加密凭据");
            if (source.Fields.Count > 64 || source.Body?.Length > 262144) throw new FormatException("请求或字段数量过大");
            Unique(source.Fields.Select(x => x.Name));
            foreach (var field in source.Fields)
                if (field.Steps is null || field.Steps.Count > 16 || field.Path.Length > 4096) throw new FormatException("提取规则过长");
        }
        foreach (var metric in Metrics)
            if (!sourceIds.Contains(metric.Source) || metric.Expression.Length > 4096) throw new FormatException("指标引用不存在的数据源或表达式过长");
        if (Views.Select(x => x.Slot).Distinct().Count() != Views.Count) throw new FormatException("显示槽位不能重复");
        foreach (var view in Views)
        {
            if (view.Slot is < 1 or > 16 || !metricIds.Contains(view.Metric)) throw new FormatException("显示槽位或指标引用无效");
            if (view.Label.Length > 48 || view.Unit.Length > 20 || view.Format.Length > 32) throw new FormatException("显示格式过长");
        }
    }
    private static void Unique(IEnumerable<string> values)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !set.Add(value)) throw new FormatException("ID/字段名为空、过长或重复");
    }
    public static Uri RequireUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0)
            throw new FormatException("URL 必须是无内嵌凭据的 HTTP(S) 地址");
        return uri;
    }
    public static bool IsSensitiveName(string name) => new[] { "authorization", "cookie", "token", "secret", "password", "api-key", "api_key", "apikey" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class Source
{
    public string Id { get; set; } = "source";
    public string Kind { get; set; } = "http_json";
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = [];
    public Dictionary<string, string> SecretHeaders { get; set; } = [];
    public string? Body { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 15;
    public string AuthPath { get; set; } = "";
    public string Credential { get; set; } = "";
    public string Currency { get; set; } = "CNY";
    public string SuccessSelector { get; set; } = "";
    public List<Field> Fields { get; set; } = [];
}
public sealed class Field
{
    public string Name { get; set; } = "value";
    public string Path { get; set; } = "$.value";
    public string Attribute { get; set; } = "";
    public bool FirstMatch { get; set; }
    public bool RequireNonEmpty { get; set; } = true;
    public List<TextStep> Steps { get; set; } = [];
}
public sealed class TextStep
{
    public string Kind { get; set; } = "trim";
    public string Pattern { get; set; } = "";
    public string Replacement { get; set; } = "";
    public int Group { get; set; } = 1;
}
public sealed class Metric
{
    public string Id { get; set; } = "metric";
    public string Source { get; set; } = "source";
    public string Input { get; set; } = "value";
    public string Expression { get; set; } = "";
    public bool Text { get; set; }
}
public sealed class View
{
    public int Slot { get; set; } = 1;
    public string Metric { get; set; } = "metric";
    public string Label { get; set; } = "Usage";
    public string Format { get; set; } = "0.##";
    public string Unit { get; set; } = "";
}
public sealed class CollectionFailure(string state, string message) : Exception(message)
{
    public string State { get; } = state;
}
public sealed record Collected(Dictionary<string, string> Fields, string Preview, string Note = "");
public sealed record DisplayValue(string Label, string Value, string Status);
