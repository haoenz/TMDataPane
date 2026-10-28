using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--web-test") return WebTests.Run(args[1]);
        if (args.Length == 2 && args[0] == "--settings-test") return SettingsTests.Run(args[1]);
        try {
            if (args.Length != 4 || args[0] != "--ipc" || args[2] != "--config") return 2;
            using var context = new CollectorContext(args[1], Path.GetFullPath(args[3]));
            Application.Run(context); return 0;
        } catch { return 1; }
    }
}

internal sealed class CollectorContext : ApplicationContext
{
    private readonly string configPath, directory, gatePath;
    private readonly FileStream ownership;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor snapshot;
    private readonly Mutex snapshotMutex;
    private readonly EventWaitHandle show, stop;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Credentials credentials;
    private readonly CancellationTokenSource lifetime = new();
    private Configuration settings;
    private Runner runner;
    private WebExtractor web;
    private SettingsWindow? window;
    private bool exiting, applying;
    private string loadError = "";
    public CollectorContext(string ipc, string path)
    {
        configPath = path; directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        gatePath = Path.Combine(directory, "collection.enabled");
        ownership = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        mapping = MemoryMappedFile.OpenExisting(ipc + "-snapshot", MemoryMappedFileRights.ReadWrite);
        snapshot = mapping.CreateViewAccessor(0, 22544, MemoryMappedFileAccess.ReadWrite);
        snapshotMutex = Mutex.OpenExisting(ipc + "-mutex");
        show = EventWaitHandle.OpenExisting(ipc + "-show"); stop = EventWaitHandle.OpenExisting(ipc + "-stop");
        credentials = new(directory);
        try { settings = File.Exists(path) ? Configuration.Parse(File.ReadAllText(path)) : new(); }
        catch { settings = new(); loadError = "配置无法读取或版本不受支持；请修正配置。原文件未覆盖。"; }
        web = new(directory); runner = new(settings, new Fetcher(credentials.Read, web));
        if (!settings.HasWork) File.Delete(gatePath);
        timer.Tick += async (_, _) => await Tick(); timer.Start();
    }
    private async Task Tick()
    {
        if (exiting || applying) return;
        if (stop.WaitOne(0)) { await Shutdown(); return; }
        if (show.WaitOne(0)) OpenSettings();
        if (loadError.Length > 0 && window is null) { OpenSettings(); window!.SetStatus(loadError); loadError = ""; }
        if (!settings.HasWork && window is null) { await Shutdown(); return; }
        runner.Poll(); Publish(runner.Snapshot());
    }
    private void Publish(DisplayValue[] values)
    {
        bool acquired;
        try { acquired = snapshotMutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        try {
            snapshot.Write(0, 2u); snapshot.Write(4, (uint)Environment.ProcessId); snapshot.Write(8, (ulong)Environment.TickCount64);
            for (int i = 0; i < 16; i++) {
                int offset = 16 + i * 1408;
                WriteText(offset, 64, values[i].Label); WriteText(offset + 128, 128, values[i].Value); WriteText(offset + 384, 512, values[i].Status);
            }
        } finally { snapshotMutex.ReleaseMutex(); }
    }
    private void WriteText(long offset, int capacity, string text)
    {
        byte[] bytes = new byte[capacity * 2]; int length = Math.Min(text.Length, capacity - 1);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        Encoding.Unicode.GetBytes(text.AsSpan(0, length), bytes); snapshot.WriteArray(offset, bytes, 0, bytes.Length);
    }
    private void OpenSettings()
    {
        if (window is not null) { window.Show(); window.Activate(); return; }
        window = new SettingsWindow(settings, configPath, credentials, Apply, () => runner.Poll(true), Test, Login);
        window.FormClosed += async (_, _) => { window = null; if (!settings.HasWork) await Shutdown(); };
        window.Show();
    }
    private async Task Apply(Configuration next)
    {
        applying = true;
        try {
            File.Delete(gatePath);
            File.WriteAllText(configPath + ".tmp", JsonSerializer.Serialize(next, Configuration.JsonOptions));
            File.Move(configPath + ".tmp", configPath, true);
            if (next.HasWork) File.WriteAllText(gatePath, "1");
            await runner.DisposeAsync(); web.Dispose();
            settings = next; web = new(directory); runner = new(settings, new Fetcher(credentials.Read, web));
            if (!next.HasWork) { Publish(runner.Snapshot()); await Shutdown(); }
        } finally { applying = false; }
    }
    private async Task<string> Test(Configuration config, Source source)
    {
        using var fetcher = new Fetcher(credentials.Read, web, captureSecretsForPreview: true);
        var result = await fetcher.FetchAsync(source, lifetime.Token);
        var safe = result.Fields.ToDictionary(x => x.Key, x => Configuration.IsSensitiveName(x.Key) ? "[已脱敏]" : fetcher.Redact(x.Value));
        var lines = new List<string> { result.Preview, JsonSerializer.Serialize(safe, Configuration.JsonOptions) };
        foreach (var metric in config.Metrics.Where(x => x.Source == source.Id)) {
            try {
                string value = metric.Text ? result.Fields[metric.Input] : (metric.Expression.Length == 0 ? Extraction.Number(result.Fields[metric.Input]) : Expression.Evaluate(metric.Expression, result.Fields)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                lines.Add(metric.Id + " = " + (Configuration.IsSensitiveName(metric.Input) ? "[已脱敏]" : fetcher.Redact(value)));
                foreach (var view in config.Views.Where(x => x.Metric == metric.Id)) {
                    string formatted = metric.Text ? value : Extraction.Number(value).ToString(view.Format, System.Globalization.CultureInfo.InvariantCulture);
                    lines.Add($"显示槽位 {view.Slot}: {view.Label} " + (Configuration.IsSensitiveName(metric.Input) ? "[已脱敏]" : fetcher.Redact(formatted + view.Unit)));
                }
            } catch { lines.Add(metric.Id + " = 计算失败"); }
        }
        return string.Join(Environment.NewLine, lines);
    }
    private async Task Login(Source source)
    {
        if (source.Kind != "browser_dom") throw new FormatException("请选择 browser_dom 数据源");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(source.TimeoutSeconds)); await web.LoginAsync(source, timeout.Token);
    }
    private async Task Shutdown()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); await lifetime.CancelAsync();
        await runner.DisposeAsync(); web.Dispose(); window?.Close(); ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            timer.Dispose(); window?.Dispose(); web.Dispose(); lifetime.Dispose();
            snapshot.Dispose(); mapping.Dispose(); snapshotMutex.Dispose(); show.Dispose(); stop.Dispose(); ownership.Dispose();
        }
        base.Dispose(disposing);
    }
}
