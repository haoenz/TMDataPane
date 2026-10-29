using System.Net;
using System.Text.Json;

namespace DataPane.Core;

public enum ResetAlert : uint { None, Watch, Strong, Scheduled, Executed }
public sealed record ResetNotice(ResetAlert Alert, string Text);

// Public announcements are independent of authenticated account usage.
public sealed class ResetMonitor : IAsyncDisposable
{
    private readonly HttpClient client;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private Task? pending;
    private DateTimeOffset next, success, greenUntil;
    private JsonElement? data;
    private string? etag;
    private readonly HashSet<string> seen = [];
    private bool initialized;
    private string completed = "";
    private int failures;
    public ResetMonitor(HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 262144 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DataPane/0.4.0");
        clock = time ?? TimeProvider.System;
    }
    public void Poll()
    {
        if (!lifetime.IsCancellationRequested && (pending is null || pending.IsCompleted) && clock.GetUtcNow() >= next)
            pending = Refresh();
    }
    public Task WhenIdle => pending ?? Task.CompletedTask;
    private async Task Refresh()
    {
        var now = clock.GetUtcNow();
        next = now.AddSeconds(60);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://codex-resets.com/api/v1/status");
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await client.SendAsync(request, lifetime.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter;
                next = now + (retry?.Delta ?? (retry?.Date - now) ?? TimeSpan.FromMinutes(5));
                if (next < now.AddSeconds(60)) next = now.AddSeconds(60);
                failures++; return;
            }
            if (response.StatusCode != HttpStatusCode.NotModified)
            {
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
                var root = doc.RootElement;
                if (root.GetProperty("meta").GetProperty("api_version").GetString() != "v1") throw new FormatException();
                var candidate = root.GetProperty("data");
                foreach (var key in new[] { "latest_reset", "scheduled_reset", "active_watch" })
                    if (candidate.GetProperty(key).ValueKind is not (JsonValueKind.Null or JsonValueKind.Object)) throw new FormatException();
                // Validate all fields used by rendering before replacing the last good response.
                Describe(candidate, now, true, 360);
                var latest = candidate.GetProperty("latest_reset");
                if (latest.ValueKind == JsonValueKind.Object)
                {
                    string id = latest.GetProperty("id").GetString() ?? throw new FormatException();
                    var announced = latest.GetProperty("announced_at").GetDateTimeOffset();
                    string kind = Kind(latest);
                    if (seen.Add(id) && initialized && announced <= now && now - announced <= TimeSpan.FromMinutes(10))
                    { greenUntil = now.AddMinutes(5); completed = kind + "：已发现新的执行记录"; }
                }
                initialized = true;
                data = candidate.Clone(); etag = response.Headers.ETag?.ToString();
            }
            if (data is null) throw new FormatException();
            success = clock.GetUtcNow(); failures = 0;
            var freshness = response.Headers.CacheControl?.MaxAge ?? TimeSpan.Zero;
            freshness -= response.Headers.Age ?? TimeSpan.Zero;
            next = success + (freshness > TimeSpan.FromSeconds(60) ? freshness : TimeSpan.FromSeconds(60));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            failures++;
            next = clock.GetUtcNow().AddSeconds(Math.Min(900, 60 * Math.Pow(2, Math.Min(failures - 1, 4))));
        }
    }
    private static string Kind(JsonElement item) => item.GetProperty("reset_type").GetString() switch
    { "regular" => "公共额度重置", "banked" => "重置次数发放（非立即恢复额度）", _ => throw new FormatException() };
    private static ResetNotice Describe(JsonElement data, DateTimeOffset now, bool predictions, int minutes)
    {
        var scheduled = data.GetProperty("scheduled_reset");
        if (scheduled.ValueKind == JsonValueKind.Object)
        {
            string kind = Kind(scheduled);
            if (scheduled.GetProperty("status").GetString() != "scheduled") throw new FormatException();
            var date = scheduled.GetProperty("scheduled_for");
            if (date.ValueKind == JsonValueKind.Null) return new(ResetAlert.Watch, kind + "：已公告，时间未定");
            var at = date.GetDateTimeOffset();
            string text = $"{kind}：{at.ToLocalTime():yyyy-MM-dd HH:mm}；等待执行确认";
            return new(at >= now && at - now <= TimeSpan.FromMinutes(minutes) ? ResetAlert.Scheduled : ResetAlert.Watch, text);
        }
        var watch = data.GetProperty("active_watch");
        if (predictions && watch.ValueKind == JsonValueKind.Object && watch.GetProperty("expires_at").GetDateTimeOffset() > now)
        {
            var level = watch.GetProperty("level").GetString();
            var alert = level switch { "strong" => ResetAlert.Strong, "elevated" => ResetAlert.Watch, _ => throw new FormatException() };
            string chance = watch.GetProperty("reset_chance_percent").ValueKind == JsonValueKind.Null ? "未知" : watch.GetProperty("reset_chance_percent").GetInt32() + "%";
            string window = watch.GetProperty("forecast_window").GetString() ?? "";
            window = new string(window.Take(100).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return new(alert, $"AI 预测（非官方承诺）：{level}，概率 {chance}；{window}");
        }
        return new(ResetAlert.None, "暂无有效公共重置预告");
    }
    public ResetNotice Notice(Source source)
    {
        var now = clock.GetUtcNow();
        const string credit = "；来源 Codex Resets https://codex-resets.com";
        if (data is null || now - success > TimeSpan.FromMinutes(5))
            return new(ResetAlert.None, "重置状态暂不可用" + credit);
        var notice = Describe(data.Value, now, source.ResetPredictions, source.ResetLeadMinutes);
        if (now < greenUntil && notice.Alert != ResetAlert.Scheduled) notice = new(ResetAlert.Executed, completed);
        return notice with { Text = notice.Text + $"；最后检查 {success.ToLocalTime():MM-dd HH:mm:ss}" + (failures > 0 ? "；更新失败，暂用缓存" : "") + credit };
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync(); await WhenIdle; client.Dispose(); lifetime.Dispose();
    }
}
