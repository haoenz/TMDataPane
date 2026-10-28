using System.Globalization;

namespace DataPane.Core;

public sealed class Runner : IAsyncDisposable
{
    private sealed class State(Source source)
    {
        public Source Source = source;
        public Collected? Last;
        public DateTimeOffset? Success;
        public string Error = "初始化";
        public string Note = "";
        public long Next;
        public Task? Pending;
    }
    private readonly Configuration config;
    private readonly Fetcher fetcher;
    private readonly Dictionary<string, State> states;
    private readonly Dictionary<int, string> lastValues = [];
    private readonly CancellationTokenSource lifetime = new();
    public Runner(Configuration configuration, Fetcher sourceFetcher)
    {
        config = configuration; fetcher = sourceFetcher;
        states = config.Sources.ToDictionary(x => x.Id, x => new State(x));
    }
    // Invoked on the collector UI thread; continuations return to that same context.
    public void Poll(bool force = false)
    {
        if (!config.Enabled || lifetime.IsCancellationRequested) return;
        foreach (var state in states.Values)
            if (state.Source.Enabled && (state.Pending is null || state.Pending.IsCompleted) && (force || Environment.TickCount64 >= state.Next))
            {
                state.Next = Environment.TickCount64 + state.Source.IntervalSeconds * 1000L;
                state.Pending = Refresh(state);
            }
    }
    private async Task Refresh(State state)
    {
        try
        {
            var result = await fetcher.FetchAsync(state.Source, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            state.Last = result; state.Success = DateTimeOffset.Now; state.Error = ""; state.Note = result.Note;
        }
        catch (OperationCanceledException) { }
        catch (CollectionFailure error) { state.Error = error.State; state.Note = error.Message; }
        catch (Exception) { state.Error = "提取失败"; state.Note = "采集或解析失败，请测试配置"; }
    }
    public DisplayValue[] Snapshot()
    {
        var result = Enumerable.Range(0, 16).Select(_ => new DisplayValue("", "--", "未配置")).ToArray();
        foreach (var view in config.Views)
        {
            var metric = config.Metrics.First(x => x.Id == view.Metric);
            var state = states[metric.Source];
            string value = "--", status;
            if (!config.Enabled || !state.Source.Enabled) status = "已停止";
            else if (state.Last is null) status = state.Error + ": " + state.Note;
            else
            {
                try
                {
                    var fields = state.Last.Fields;
                    value = metric.Text ? fields[metric.Input] : (metric.Expression.Length == 0 ? Extraction.Number(fields[metric.Input]) : Expression.Evaluate(metric.Expression, fields)).ToString(view.Format, CultureInfo.InvariantCulture);
                    value += view.Unit;
                    lastValues[view.Slot] = value;
                    bool expired = state.Error.Length > 0 || DateTimeOffset.Now - state.Success > TimeSpan.FromSeconds(state.Source.IntervalSeconds * 2 + state.Source.TimeoutSeconds);
                    status = (expired ? "数据过期 " + state.Error : "正常") + $"；最后成功 {state.Success:yyyy-MM-dd HH:mm:ss}；{state.Note}";
                    foreach (var reset in fields.Where(x => x.Key.EndsWith("ResetAt", StringComparison.Ordinal)))
                        if (long.TryParse(reset.Value, out long seconds)) status += $"；{reset.Key}: {Math.Max(0, seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds())} 秒";
                    if (expired) value = "~" + value;
                }
                catch (Exception) { status = "计算失败；字段缺失、非法数字、除零或表达式/格式错误"; value = lastValues.TryGetValue(view.Slot, out var previous) ? "~" + previous : "--"; }
            }
            result[view.Slot - 1] = new(view.Label, value, status);
        }
        return result;
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Task.WhenAll(states.Values.Select(x => x.Pending ?? Task.CompletedTask));
        lifetime.Dispose(); fetcher.Dispose();
    }
}
