using AngleSharp.Html.Parser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal sealed class WebExtractor(string directory, string? fixtureFolder = null) : IWebExtractor
{
    private sealed class BackgroundForm : Form { protected override bool ShowWithoutActivation => true; }
    private sealed record Page(Form Window, WebView2 Browser) { public bool Interactive; public bool Failed; }
    private readonly Dictionary<string, Page> pages = [];
    private Task<CoreWebView2Environment>? environment;
    private bool disposed;
    private void Trace(string message) { if (fixtureFolder is not null) File.AppendAllText(Path.Combine(directory, "browser-trace.txt"), $"{DateTime.Now:O} T{Environment.CurrentManagedThreadId} {Thread.CurrentThread.GetApartmentState()} {SynchronizationContext.Current?.GetType().Name} {message}\n"); }
    private readonly Dictionary<string, SemaphoreSlim> gates = [];
    private SemaphoreSlim Gate(string id) { if (!gates.TryGetValue(id, out var gate)) gates.Add(id, gate = new(1, 1)); return gate; }
    public int[] ProcessIds() => environment?.IsCompletedSuccessfully == true ? environment.Result.GetProcessInfos().Select(x => x.ProcessId).ToArray() : [];
    public Collected Html(string html, Source source)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var fields = new Dictionary<string, string>();
        var raw = new Dictionary<string, string>();
        if (source.SuccessSelector.Length > 0 && doc.QuerySelector(source.SuccessSelector) is null) throw new CollectionFailure("需要登录", "成功标志未出现");
        foreach (var field in source.Fields) {
            var matches = doc.QuerySelectorAll(field.Path);
            if (matches.Length == 0 || (!field.FirstMatch && matches.Length != 1)) throw new CollectionFailure("提取失败", "选择器匹配数量不是一个");
            var element = matches[0];
            string text = field.Attribute.Length == 0 ? element.TextContent : element.GetAttribute(field.Attribute) ?? throw new FormatException("属性不存在");
            raw.Add(field.Name, text); fields.Add(field.Name, Extraction.Process(text, field));
        }
        return new(fields, "静态 HTML 选中文本", RawFields: raw);
    }
    private async Task<Page> GetPage(Source source, CancellationToken cancellation)
    {
        Trace("GetPage");
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pages.TryGetValue(source.Id, out var existing)) {
            if (!existing.Failed) return existing;
            existing.Browser.Dispose(); existing.Window.Dispose(); pages.Remove(source.Id); environment = null;
        }
        environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(directory, "browser-profile"));
        var env = await environment.WaitAsync(cancellation);
        Trace("Environment ready");
        var form = new BackgroundForm { Text = "DataPane 登录 · " + source.Id, Width = 1050, Height = 760, ShowInTaskbar = false, Opacity = 0 };
        var browser = new WebView2 { Dock = DockStyle.Fill }; form.Controls.Add(browser);
        form.Show();
        try {
            await browser.EnsureCoreWebView2Async(env).WaitAsync(cancellation);
            Trace("Core ready");
            if (fixtureFolder is not null) {
                browser.CoreWebView2.NavigationStarting += (_, e) => Trace("NavigationStarting");
                browser.CoreWebView2.NavigationCompleted += (_, e) => Trace("NavigationCompleted " + e.IsSuccess + " " + e.WebErrorStatus);
                browser.CoreWebView2.ProcessFailed += (_, e) => Trace("ProcessFailed " + e.ProcessFailedKind);
                browser.CoreWebView2.DOMContentLoaded += (_, _) => Trace("DOMContentLoaded");
            }
            if (fixtureFolder is not null) browser.CoreWebView2.SetVirtualHostNameToFolderMapping("usagebridge.test", fixtureFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
            browser.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            browser.CoreWebView2.NavigationStarting += (_, e) => {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "about")) e.Cancel = true;
            };
            var page = new Page(form, browser);
            browser.CoreWebView2.ProcessFailed += (_, e) => {
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited) page.Failed = true;
            };
            form.FormClosing += (_, e) => { if (!disposed) { e.Cancel = true; page.Interactive = false; form.Opacity = 0; form.ShowInTaskbar = false; } };
            pages.Add(source.Id, page); return page;
        } catch { browser.Dispose(); form.Dispose(); throw; }
    }
    private static async Task Navigate(Page page, string url, CancellationToken cancellation)
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args) => finished.TrySetResult(args.IsSuccess);
        void Failed(object? sender, CoreWebView2ProcessFailedEventArgs args) {
            if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                finished.TrySetException(new CollectionFailure("请求失败", "WebView2 浏览器/渲染进程退出"));
        }
        var core = page.Browser.CoreWebView2 ?? throw new CollectionFailure("请求失败", "浏览器进程已结束");
        core.NavigationCompleted += Handler;
        core.ProcessFailed += Failed;
        try {
            core.Navigate(Configuration.RequireUrl(url).AbsoluteUri);
            if (!await finished.Task.WaitAsync(cancellation)) throw new CollectionFailure("请求失败", "浏览器导航失败");
        } finally { try { core.NavigationCompleted -= Handler; core.ProcessFailed -= Failed; if (cancellation.IsCancellationRequested) core.Stop(); } catch (InvalidOperationException) { } }
    }
    public async Task LoginAsync(Source source, CancellationToken cancellation)
    {
        var gate = Gate(source.Id); await gate.WaitAsync(cancellation);
        try {
        var page = await GetPage(source, cancellation);
        page.Interactive = true;
        page.Window.Opacity = 1; page.Window.ShowInTaskbar = true; page.Window.Show(); page.Window.Activate();
        await Navigate(page, source.Url, cancellation);
        } finally { gate.Release(); }
    }
    public async Task<Collected> BrowserAsync(Source source, CancellationToken cancellation)
    {
        var gate = Gate(source.Id); await gate.WaitAsync(cancellation);
        try { return await BrowserCore(source, cancellation); } finally { gate.Release(); }
    }
    private async Task<Collected> BrowserCore(Source source, CancellationToken cancellation)
    {
        var page = await GetPage(source, cancellation);
        if (!page.Interactive) await Navigate(page, source.Url, cancellation);
        string rules = JsonSerializer.Serialize(source.Fields.Select(x => new { x.Name, Selector = x.Path, x.Attribute, x.FirstMatch, x.RequireNonEmpty }));
        string script = "(() => { const rules = " + rules + "; const success = " + JsonSerializer.Serialize(source.SuccessSelector) + ";" + """
          try {
            if (success && !document.querySelector(success)) return {pending: 'login'};
            const fields = {};
            for (const r of rules) {
              const nodes = document.querySelectorAll(r.Selector);
              if (!nodes.length) return {pending: 'selector'};
              if (!r.FirstMatch && nodes.length !== 1) return {error: 'multiple'};
              const el = nodes[0];
              const text = r.Attribute === 'value' ? el.value : r.Attribute ? el.getAttribute(r.Attribute) : el.textContent;
              if (text === null || text === undefined) return {error: 'attribute'};
              if (String(text).length > 65536) return {error: 'size'};
              if (r.RequireNonEmpty && !String(text).trim()) return {pending: 'empty'};
              fields[r.Name] = String(text);
            }
            return {fields};
          } catch { return {error: 'selector'}; }
        })()
        """;
        string pending = "selector";
        try {
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                string json = await page.Browser.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(cancellation);
                using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new CollectionFailure("提取失败", "页面没有返回 DOM 数据");
                if (root.TryGetProperty("error", out _)) throw new CollectionFailure("提取失败", "选择器无效、匹配过多或属性不存在");
                if (root.TryGetProperty("fields", out var values)) {
                    var fields = new Dictionary<string, string>();
                    var raw = new Dictionary<string, string>();
                    foreach (var field in source.Fields) {
                        string text = values.GetProperty(field.Name).GetString()!;
                        raw.Add(field.Name, text); fields.Add(field.Name, Extraction.Process(text, field));
                    }
                    return new(fields, "渲染后 DOM 选中文本", RawFields: raw);
                }
                if (root.TryGetProperty("pending", out var wait)) pending = wait.GetString()!;
                await Task.Delay(200, cancellation);
            }
        } catch (OperationCanceledException) when (pending == "login") { throw new CollectionFailure("需要登录", "等待成功标志超时，请打开登录窗口"); }
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var page in pages.Values) { page.Browser.Dispose(); page.Window.Dispose(); } pages.Clear();
    }
}
