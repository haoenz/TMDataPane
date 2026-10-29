using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DataPane.Core;

internal static class ResetTests
{
    public static async Task<int> Run()
    {
        int checks = 0, requests = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var clock = new Clock();
        object? scheduled = null, watch = null;
        object latest = new { id = "old", reset_type = "regular", announced_at = clock.Now.AddDays(-1) };
        HttpStatusCode status = HttpStatusCode.OK;
        string Payload() => JsonSerializer.Serialize(new { data = new { latest_reset = latest, scheduled_reset = scheduled, active_watch = watch }, meta = new { api_version = "v1", generated_at = clock.Now } });
        using var handler = new Handler(request => {
            requests++;
            Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "Public request leaked credentials");
            if (requests > 1) Check(request.Headers.IfNoneMatch.Any(), "Missing conditional request");
            var response = new HttpResponseMessage(status) { Content = new StringContent(Payload()) };
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
            if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        await using var monitor = new ResetMonitor(handler, clock);
        var source = new Source { Kind = "codex" };
        async Task Refresh() { clock.Now = clock.Now.AddMinutes(1); monitor.Poll(); await monitor.WhenIdle; }
        monitor.Poll(); await monitor.WhenIdle;
        Check(monitor.Notice(source).Alert == ResetAlert.None, "Historical reset flashed at startup");
        monitor.Poll(); Check(requests == 1, "Poll interval ignored");
        scheduled = new { status = "scheduled", reset_type = "regular", scheduled_for = clock.Now.AddHours(6).AddMinutes(1) };
        await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.Scheduled, "Inclusive six hour boundary");
        source.ResetLeadMinutes = 30; Check(monitor.Notice(source).Alert == ResetAlert.Watch, "Custom lead ignored"); source.ResetLeadMinutes = 360;
        scheduled = new { status = "scheduled", reset_type = "regular", scheduled_for = clock.Now.AddSeconds(-1) };
        await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.Watch, "Passed scheduled time invented completion");
        scheduled = new { status = "scheduled", reset_type = "banked", scheduled_for = (DateTimeOffset?)null };
        await Refresh(); Check(monitor.Notice(source).Text.Contains("非立即恢复额度"), "Banked credit confused with usage reset");
        scheduled = null;
        watch = new { level = "strong", expires_at = clock.Now.AddMinutes(3), reset_chance_percent = (int?)null, forecast_window = "soon" };
        await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.Strong, "Strong watch missing");
        source.ResetPredictions = false; Check(monitor.Notice(source).Alert == ResetAlert.None, "Prediction toggle ignored"); source.ResetPredictions = true;
        clock.Now = clock.Now.AddMinutes(2); Check(monitor.Notice(source).Alert == ResetAlert.None, "Expired watch stayed colored");
        latest = new { id = "new", reset_type = "regular", announced_at = clock.Now };
        await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.Executed, "New reset did not flash");
        clock.Now = clock.Now.AddMinutes(5); await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.None, "Duplicate reset retriggered green");
        scheduled = new { status = "scheduled", reset_type = "regular", scheduled_for = clock.Now.AddHours(1) };
        await Refresh(); status = HttpStatusCode.NotModified;
        await Refresh(); Check(monitor.Notice(source).Alert == ResetAlert.Scheduled, "304 discarded cached status");
        status = HttpStatusCode.TooManyRequests; await Refresh();
        int before = requests; await Refresh(); Check(requests == before, "Retry-After ignored");
        clock.Now = clock.Now.AddMinutes(5); Check(monitor.Notice(source).Alert == ResetAlert.None && monitor.Notice(source).Text.Contains("暂不可用"), "Stale data remained colored");
        clock.Now = clock.Now.AddMinutes(5); status = HttpStatusCode.ServiceUnavailable;
        monitor.Poll(); await monitor.WhenIdle; Check(monitor.Notice(source).Alert == ResetAlert.None, "503 invented an alert");
        var config = Presets.Demo(); config.Enabled = true;
        int publicRequests = 0;
        await using (var runner = new Runner(config, new Fetcher(_ => ""), new ResetMonitor(new Handler(_ => { publicRequests++; throw new Exception(); }), clock)))
        { runner.Poll(); Check(publicRequests == 0, "Non Codex source queried reset service"); }
        var roundtrip = Configuration.Parse(JsonSerializer.Serialize(new Configuration { Sources = [source] }, Configuration.JsonOptions));
        Check(roundtrip.Sources[0].ResetLeadMinutes == 360, "Reminder config roundtrip");
        return checks;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
