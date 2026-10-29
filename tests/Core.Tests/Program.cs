using System.Net;
using System.Text.Json;
using DataPane.Core;

int count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
void Reject(Action action, string message) { try { action(); } catch { count++; return; } throw new Exception(message); }
Check(Expression.Evaluate("5000 - 1234", new Dictionary<string,string>()) == 3766m, "Subtraction");
Check(Expression.Evaluate("round((amount*3)/2, 2)", new Dictionary<string,string> { ["amount"] = "1.234" }) == 1.85m, "Decimal arithmetic");
Reject(() => Expression.Evaluate("1/0", new Dictionary<string,string>()), "Division by zero accepted");
Reject(() => Expression.Evaluate("System.IO.File.ReadAllText(x)", new Dictionary<string,string>()), "Host call accepted");
Reject(() => Expression.Evaluate(new string('(', 50) + "1" + new string(')', 50), new Dictionary<string,string>()), "Depth accepted");
Reject(() => Extraction.Number("1,234"), "Ambiguous separator accepted");
Reject(() => Extraction.Number(""), "Empty converted to zero");
using (var doc = JsonDocument.Parse("{\"a\":[{\"b.c\":\"1234\"}]}")) {
    Check(Extraction.Scalar(Extraction.Select(doc.RootElement, "$.a[0]['b.c']")) == "1234", "JSONPath");
    Reject(() => Extraction.Select(doc.RootElement, "$.a[*]"), "Wildcard accepted");
    Reject(() => Extraction.Select(doc.RootElement, "$.missing"), "Missing property accepted");
}
var field = new Field { Steps = [new(), new() { Kind="replace", Pattern=",", Replacement="" }, new() { Kind="regex", Pattern=@"balance=(\d+\.\d+)" }] };
Check(Extraction.Process(" balance=1,234.50 USD ", field) == "1234.50", "Text pipeline");
string codex = """{"rate_limit":{"primary_window":{"limit_window_seconds":604800,"used_percent":30,"reset_at":1800000000},"secondary_window":{"limit_window_seconds":18000,"used_percent":120}}} """;
var windows = Presets.Codex(codex).Fields;
Check(windows["weekRemaining"] == "70" && windows["h5Remaining"] == "0", "Window identification/clamping");
Check(!Presets.Codex("""{"rate_limit":{"primary_window":{"limit_window_seconds":18000,"used_percent":20}}} """).Fields.ContainsKey("weekRemaining"), "Missing window invented");
Reject(() => Presets.Codex("{}"), "Invalid shape accepted");
string balance = """{"is_available":false,"balance_infos":[{"currency":"USD","total_balance":"99","granted_balance":"0","topped_up_balance":"99"},{"currency":"CNY","total_balance":"123.456789","granted_balance":"1.1","topped_up_balance":"122.356789"}]}""";
var money = Presets.DeepSeek(balance, "CNY");
Check(money.Fields["total_balance"] == "123.456789" && money.Fields["isAvailable"] == "false", "Currency or precision");
Reject(() => Presets.DeepSeek(balance, "EUR"), "Missing currency accepted");
var config = Presets.Demo(); config.Validate();
var widthConfig = Presets.Demo();
Check(widthConfig.Views[0].EffectiveWidthSample == "~999999.99 CNY", "Old config width fallback");
widthConfig.Views[0].WidthSample = new string('宽', 63); widthConfig.Validate();
Check(Configuration.Parse(JsonSerializer.Serialize(widthConfig, Configuration.JsonOptions)).Views[0].WidthSample.Length == 63, "Width sample boundary roundtrip");
widthConfig.Views[0].WidthSample += "宽"; Reject(widthConfig.Validate, "Oversize width accepted");
widthConfig.Views[0].WidthSample = "a\nb"; Reject(widthConfig.Validate, "Multiline width accepted");
widthConfig.Views[0].WidthSample = null!; Reject(widthConfig.Validate, "Null width accepted");
config.Views.Add(new() { Slot=1, Metric="demo" }); Reject(config.Validate, "Duplicate slots accepted");
config = Presets.Demo(); config.Sources[0].Headers["Authorization"] = "secret"; Reject(config.Validate, "Plaintext auth accepted");
var source = new Source { Url="https://example.test/data", Fields=[new() { Name="amount", Path="$.amount" }] };
int requests = 0;
using (var fetch = new Fetcher(_ => "fixture", null, new FakeHandler(_ => { requests++; return new(HttpStatusCode.OK) { Content = new StringContent("{\"amount\":\"12.34\"}") }; }))) {
    var data = await fetch.FetchAsync(source, CancellationToken.None);
    Check(data.Fields["amount"] == "12.34" && requests == 1, "HTTP JSON");
}
using (var fetch = new Fetcher(_ => "fixture", null, new FakeHandler(_ => new(HttpStatusCode.Unauthorized)))) {
    try { await fetch.FetchAsync(source, CancellationToken.None); throw new Exception("401 accepted"); }
    catch (CollectionFailure e) { Check(e.State == "需要登录", "401 state"); }
}
Reject(() => Extraction.Process(new string('a', 65536), new Field { Steps = [new() { Kind="replace", Pattern="a", Replacement=new string('b', 4096) }] }), "Unbounded replacement accepted");
var combined = new Configuration { Enabled = true, Sources = [source], Metrics = [new() { Id="one", Source="source", Input="amount" }, new() { Id="two", Source="source", Expression="amount*2" }], Views = [new() { Slot=1, Metric="one" }, new() { Slot=2, Metric="two" }] };
combined.Validate(); int combinedRequests = 0;
await using (var runner = new Runner(combined, new Fetcher(_=>"fixture", handler: new FakeHandler(_ => {
    combinedRequests++; return combinedRequests == 1 ? new(HttpStatusCode.OK) { Content=new StringContent("{\"amount\":\"12.34\"}") } : new(HttpStatusCode.Unauthorized);
})))) {
    runner.Poll(); await Task.Delay(50);
    var values=runner.Snapshot(); Check(combinedRequests==1 && values[0].Value=="12.34" && values[1].Value=="24.68", "One request shared by metrics");
    runner.Poll(); Check(combinedRequests==1,"Refresh interval ignored");
    runner.Poll(true); await Task.Delay(50); values=runner.Snapshot();
    Check(values[0].Value=="~12.34" && values[0].Status.Contains("需要登录"),"Failure did not preserve/mark old value");
}
var temporary = Path.Combine(Path.GetTempPath(), "datapane-auth-test-"+Guid.NewGuid().ToString("N")+".json");
try {
    var codexSource = new Source { Kind="codex", AuthPath=temporary };
    string expected = "fixture-first";
    using var fetch = new Fetcher(_=>"", handler: new FakeHandler(request => {
        Check(request.Headers.Authorization?.Parameter==expected, "Codex token was not reloaded");
        return new(HttpStatusCode.OK) {Content=new StringContent(codex)};
    }));
    await File.WriteAllTextAsync(temporary,"{\"tokens\":{\"access_token\":\"fixture-first\"}}");
    await fetch.FetchAsync(codexSource,CancellationToken.None);
    expected="fixture-second";
    await File.WriteAllTextAsync(temporary,"{\"tokens\":{\"access_token\":\"fixture-second\"}}");
    await fetch.FetchAsync(codexSource,CancellationToken.None);
    var integration = new Configuration { Enabled = true };
    Presets.Add(integration, "codex"); integration.Sources[0].AuthPath = temporary;
    int publicCalls = 0;
    await using (var runner = new Runner(integration,
        new Fetcher(_ => "", handler: new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(codex) })),
        new ResetMonitor(new FakeHandler(_ => { publicCalls++; return new(HttpStatusCode.ServiceUnavailable); })))) {
        runner.Poll(); await Task.Delay(50);
        var display = runner.Snapshot();
        Check(publicCalls == 1 && display[0].Value == "0%" && display[1].Value == "70%", "Public API failure affected account usage or duplicated requests");
        Check(display[0].Alert == ResetAlert.None && display[0].Status.Contains("重置状态暂不可用"), "Public failure state missing");
    }
} finally { File.Delete(temporary); }
count += await ResetTests.Run();
Console.WriteLine($"PASS: {count} core checks");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
