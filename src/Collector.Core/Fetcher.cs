using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageBridge.Core;

public interface IWebExtractor : IDisposable
{
    Task<Collected> BrowserAsync(Source source, CancellationToken cancellation);
    Collected Html(string html, Source source);
}

public sealed class Fetcher : IDisposable
{
    private readonly HttpClient client;
    private readonly Func<string, string> secret;
    private readonly IWebExtractor? web;
    private readonly List<string> secrets = [];
    private readonly bool captureSecrets;
    private string Remember(string value) { if (captureSecrets && value.Length > 0) secrets.Add(value); return value; }
    public string Redact(string text) { foreach (var value in secrets.OrderByDescending(x => x.Length)) text = text.Replace(value, "[已脱敏]", StringComparison.Ordinal); return text; }
    public Fetcher(Func<string, string> secretReader, IWebExtractor? webExtractor = null, HttpMessageHandler? handler = null, bool captureSecretsForPreview = false)
    {
        secret = secretReader; web = webExtractor;
        captureSecrets = captureSecretsForPreview;
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DataPane/0.3.7");
    }
    public async Task<Collected> FetchAsync(Source source, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(source.TimeoutSeconds));
        var token = timeout.Token;
        try
        {
            if (source.Kind == "demo") return new(new() { ["value"] = "42" }, "模拟值，无网络请求");
            if (source.Kind == "browser_dom") return await (web ?? throw new CollectionFailure("提取失败", "未安装网页组件")).BrowserAsync(source, token);
            string url = source.Kind switch { "codex" => "https://chatgpt.com/backend-api/wham/usage", "deepseek" => "https://api.deepseek.com/user/balance", _ => source.Url };
            using var request = new HttpRequestMessage(source.Kind is "codex" or "deepseek" ? HttpMethod.Get : new HttpMethod(source.Method), Configuration.RequireUrl(url));
            if (source.Body is not null) request.Content = new StringContent(source.Body, Encoding.UTF8, "application/json");
            foreach (var pair in source.Headers)
                if (!request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
                {
                    request.Content ??= new StringContent("");
                    request.Content.Headers.Remove(pair.Key);
                    if (!request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value)) throw new CollectionFailure("请求失败", "请求头无效");
                }
            foreach (var pair in source.SecretHeaders)
                if (!request.Headers.TryAddWithoutValidation(pair.Key, Remember(secret(pair.Value)))) throw new CollectionFailure("请求失败", "凭据请求头无效");
            if (source.Kind == "codex") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Remember(await ReadCodexToken(source.AuthPath, token)));
            if (source.Kind == "deepseek") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Remember(secret(source.Credential)));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new CollectionFailure("需要登录", $"HTTP {(int)response.StatusCode}，请恢复登录或更新凭据");
            if (!response.IsSuccessStatusCode) throw new CollectionFailure("请求失败", $"HTTP {(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > 2_097_152) throw new CollectionFailure("请求失败", "响应超过 2 MiB");
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var body = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, token);
                if (count == 0) break;
                if (body.Length + count > 2_097_152) throw new CollectionFailure("请求失败", "响应超过 2 MiB");
                body.Write(buffer, 0, count);
            }
            Encoding encoding = Encoding.UTF8;
            string? charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            if (!string.IsNullOrWhiteSpace(charset)) encoding = Encoding.GetEncoding(charset);
            string text = encoding.GetString(body.ToArray()).TrimStart('\uFEFF');
            return Parse(source, text);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new CollectionFailure("请求失败", "采集超时"); }
        catch (HttpRequestException) { throw new CollectionFailure("请求失败", "网络连接失败"); }
        catch (JsonException) { throw new CollectionFailure("提取失败", "响应 JSON 格式无效"); }
        catch (KeyNotFoundException) { throw new CollectionFailure("提取失败", "响应字段缺失"); }
        catch (FormatException) { throw new CollectionFailure("提取失败", "响应或提取规则无效，请测试配置"); }
        catch (InvalidOperationException) { throw new CollectionFailure("提取失败", "响应字段类型无效"); }
    }
    public Collected Parse(Source source, string text)
    {
        if (source.Kind == "codex") return Presets.Codex(text);
        if (source.Kind == "deepseek") return Presets.DeepSeek(text, source.Currency);
        if (source.Kind == "http_html") return (web ?? throw new CollectionFailure("提取失败", "未安装网页组件")).Html(text, source);
        using var doc = JsonDocument.Parse(text);
        var fields = new Dictionary<string, string>();
        var raw = new Dictionary<string, string>();
        foreach (var field in source.Fields) {
            string value = Extraction.Scalar(Extraction.Select(doc.RootElement, field.Path));
            raw.Add(field.Name, value); fields.Add(field.Name, Extraction.Process(value, field));
        }
        // Do not expose raw authenticated responses; preview the selected fields only.
        return new(fields, "已提取选定字段；原始响应不写入日志", RawFields: raw);
    }
    private static async Task<string> ReadCodexToken(string path, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "auth.json");
        try
        {
            if (new FileInfo(path).Length > 1_048_576) throw new CollectionFailure("需要登录", "Codex 凭据文件过大");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellation));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens) || !tokens.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
                throw new CollectionFailure("需要登录", "Codex 凭据缺少 access_token");
            return token.GetString()!;
        }
        catch (IOException) { throw new CollectionFailure("需要登录", "无法读取 Codex 凭据文件"); }
        catch (UnauthorizedAccessException) { throw new CollectionFailure("需要登录", "没有 Codex 凭据文件读取权限"); }
        catch (JsonException) { throw new CollectionFailure("需要登录", "Codex 凭据文件格式无效"); }
    }
    public void Dispose() => client.Dispose();
}
