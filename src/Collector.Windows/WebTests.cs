using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal static class WebTests
{
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        int result = 1;
        using var context = new ApplicationContext();
        using var trigger = new System.Windows.Forms.Timer { Interval = 100 };
        trigger.Tick += async (_, _) => {
            trigger.Stop();
            try { await Check(root); result = 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(root, "web-error.txt"), e.ToString()); }
            finally { context.ExitThread(); }
        };
        trigger.Start(); Application.Run(context); return result;
    }
    private static async Task Check(string root)
    {
        string fixture = Path.Combine(root, "fixture"); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "index.html"), """
        <!doctype html><meta charset="utf-8"><div id="balance"></div><input id="control" value="12.50">
        <div id="session"></div><script>
        const previous = localStorage.getItem('test-session');
        localStorage.setItem('test-session', 'persisted');
        document.querySelector('#session').textContent = previous || 'first';
        setTimeout(() => document.querySelector('#balance').textContent = ' 1,234 ', 600);
        </script>
        """);
        using var web = new WebExtractor(root, fixture);
        var source = new Source { Id = "test", Kind = "browser_dom", Url = "https://usagebridge.test/index.html", TimeoutSeconds = 20,
            Fields = [new() { Name = "value", Path = "#balance", Steps = [new(), new() { Kind = "replace", Pattern = ",", Replacement = "" }] },
                new() { Name = "control", Path = "#control", Attribute = "value" }, new() { Name = "session", Path = "#session" }] };
        var htmlSource = new Source { Kind = "http_html", Fields = [new() { Name = "value", Path = "b.price", Steps = [new()] }] };
        if (web.Html("<b class='price'> 42 </b>", htmlSource).Fields["value"] != "42") throw new Exception("Static CSS extraction failed");
        try { web.Html("<b class='price'>1</b><b class='price'>2</b>", htmlSource); throw new Exception("Multiple match accepted"); }
        catch (CollectionFailure) { }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var data = await web.BrowserAsync(source, timeout.Token);
        if (data.Fields["value"] != "1234" || data.Fields["control"] != "12.50") throw new Exception("Dynamic extraction failed");
        if (Expression.Evaluate("5000-value", data.Fields) != 3766m) throw new Exception("DOM expression failed");
        bool expectPersisted = File.Exists(Path.Combine(root, "session-created"));
        if (expectPersisted && data.Fields["session"] != "persisted") throw new Exception("Session was not persisted");
        File.WriteAllText(Path.Combine(root, "session-created"), "1");
        var credentials = new Credentials(root); credentials.Save("test-key", "fixture-only-secret");
        if (credentials.Read("test-key") != "fixture-only-secret") throw new Exception("DPAPI round-trip failed");
        source.Fields = [new() { Name = "value", Path = "#missing" }];
        using var shortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await web.BrowserAsync(source, shortTimeout.Token); throw new Exception("Missing selector did not time out"); }
        catch (OperationCanceledException) { }
        File.WriteAllText(Path.Combine(root, "web-results.json"), JsonSerializer.Serialize(new { Passed = true, PersistedSession = expectPersisted, BrowserProcessIds = web.ProcessIds() }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
