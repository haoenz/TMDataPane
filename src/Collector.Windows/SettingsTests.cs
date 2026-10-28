using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal static class SettingsTests
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.json");
        int checks = 0, saves = 0, refreshes = 0, tests = 0;
        try {
            var credentials = new Credentials(directory);
            var pendingTest = new TaskCompletionSource<string>();
            using var window = new SettingsWindow(new(), path, credentials, next => {
                saves++; File.WriteAllText(path, JsonSerializer.Serialize(next, Configuration.JsonOptions)); return Task.CompletedTask;
            }, () => refreshes++, (config, source) => { tests++; return tests == 1 ? pendingTest.Task : Task.FromException<string>(new CollectionFailure("需要登录", "测试凭据失效")); }, _ => Task.CompletedTask) { Opacity = 0, ShowInTaskbar = false };
            window.ConfirmForTest = _ => DialogResult.Yes;
            window.Show(); Application.DoEvents();
            Control Find(string name) => window.Controls.Find(name, true).Single();


            var enabled = (CheckBox)Find("CollectionEnabled");
            var content = Find("SettingsContent");
            void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
            IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
            void Click(string label) {
                var button = All(window).OfType<Button>().SingleOrDefault(x => x.Text == label);
                if (button is not null) button.PerformClick();
                else ((Button)Find("MoreButton")).ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == label).PerformClick();
                Application.DoEvents();
            }
            void Text(string name, string text) => ((TextBox)Find(name)).Text = text;
            void Toggle(string name, bool value) => ((CheckBox)Find(name)).Checked = value;
            Check(!window.IsDirty, "Initial state");
            foreach (var kind in new[] { "codex", "deepseek", "http_json", "http_html", "browser_dom", "demo" }) {
                window.AddSource(kind);
                Check(window.Draft.Sources.Last().Kind == kind && ((ListView)Find("SourceList")).SelectedItems.Count == 1, "Add/select " + kind);
            }
            Check(saves == 0 && !File.Exists(path) && window.IsDirty, "Adding wrote to disk");
            Click("加载配置"); Check(window.Draft.Sources.Count == 0 && !window.IsDirty, "Restore absent file");
            window.AddSource("browser_dom");
            Text("SourceUrl", "https://example.com/balance"); Text("FieldPath_value", "#amount");
            Toggle("Process_value_currency", true); Toggle("Process_value_comma", true);
            Text("Label_1", "余额"); Text("Unit_1", " 元"); Text("Format_1", "0.00");
            var source = window.Draft.Sources.Single();
            Check(Extraction.Process("¥1,234.50", source.Fields.Single()) == "1234.50", "Currency processing");
            Check(window.Draft.Views.Single().Label == "余额", "View form edits");
            var config = Configuration.Parse(JsonSerializer.Serialize(window.Draft, Configuration.JsonOptions));
            config.Sources[0].Headers["X-Test"] = "preserve";
            config.Sources[0].Fields[0].Steps.Add(new() { Kind = "regex", Pattern = "([0-9.]+)", Group = 1 });
            config.Metrics[0].Expression = "value * 2";
            File.WriteAllText(path, JsonSerializer.Serialize(config, Configuration.JsonOptions));
            Click("保存配置");
            Check(saves == 0 && window.StatusText.Contains("外部修改"), "External edits overwritten by stale draft");
            Click("加载配置");
            Check(((ListView)Find("SourceList")).Items[0].SubItems[1].Text == "尚未应用", "File reload incorrectly reported as applied");
            Text("SourceUrl", "https://example.com/new");
            config = Configuration.Parse(JsonSerializer.Serialize(window.Draft, Configuration.JsonOptions));
            Check(config.Sources[0].Headers["X-Test"] == "preserve" && config.Sources[0].Fields[0].Steps.Last().Kind == "regex" && config.Metrics[0].Expression == "value * 2", "External advanced rules lost during form edits");
            File.WriteAllText(path, "{broken"); Click("加载配置");
            Check(window.Draft.Sources[0].Url == "https://example.com/new" && window.StatusText.Contains("JSON"), "Invalid file replaced draft");
            Click("保存配置"); Check(saves == 0 && File.ReadAllText(path) == "{broken", "Invalid external file overwritten");
            File.WriteAllText(path, " "); Click("加载配置");
            Check(window.Draft.Sources[0].Url == "https://example.com/new", "Empty external file discarded draft");
            File.WriteAllText(path, JsonSerializer.Serialize(config, Configuration.JsonOptions)); Click("加载配置");
            enabled.Checked = true;
            Click("测试数据"); Check(tests == 1 && !content.Enabled && saves == 0, "Test did not isolate draft/disable controls");
            window.Close(); Check(!window.IsDisposed && window.StatusText.Contains("稍候"), "Closed while testing");
            pendingTest.SetResult("测试成功"); Application.DoEvents();
            Check(content.Enabled && window.OwnedForms.Any(x => x.Name == "TestResults"), "Async result popup did not open");
            var resultDialog = window.OwnedForms.Single(x => x.Name == "TestResults");
            Check(((TextBox)resultDialog.Controls.Find("TestResultText", true).Single()).Text == "测试成功", "Popup result content");
            resultDialog.Close(); Check(window.OwnedForms.Length == 0, "Result popup was retained after close");
            Click("测试数据");
            var failureDialog = window.OwnedForms.Single(x => x.Name == "TestResults");
            Check(((TextBox)failureDialog.Controls.Find("TestResultText", true).Single()).Text.Contains("需要登录"), "Failure did not appear in result popup");
            using (var picture = new Bitmap(failureDialog.Width, failureDialog.Height)) {
                failureDialog.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height)); picture.Save(Path.Combine(directory, "test-result.png"));
            }
            failureDialog.Close();
            Click("更新数据"); Check(refreshes == 1 && saves == 0, "Refresh saved draft");
            Click("保存配置"); Check(saves == 1 && !window.IsDirty && Configuration.Parse(File.ReadAllText(path)).Enabled, "Save/apply state");
            Check(window.StatusText.Contains("重启 TM") && ((Label)Find("SaveState")).Text.Contains("重启 TM"), "Missing restart reminder for added display items");
            Text("Label_1", "未保存标题"); window.ConfirmForTest = _ => DialogResult.Cancel;
            Click("加载配置"); Check(window.Draft.Views[0].Label == "未保存标题", "Canceled restore discarded edits");
            window.Close(); Check(!window.IsDisposed, "Canceled close discarded edits");
            window.ConfirmForTest = _ => DialogResult.Yes; Click("加载配置");
            Check(window.Draft.Views[0].Label == "余额" && !window.IsDirty, "Restore did not reload saved state");
            var raw = new Dictionary<string, string> { ["value"] = "¥9.38" };
            config.Metrics[0].Expression = "";
            string preview = TestPreview.Render(config, config.Sources[0], new(raw, "网页", RawFields: raw), x => x);
            Check(preview.Contains("¥9.38") && preview.Contains("移除") && preview.Contains("计算失败"), "Currency error guidance");
            preview = TestPreview.Render(config, config.Sources[0], new(new() { ["value"] = "9.38" }, "网页", RawFields: raw), x => x);
            Check(preview.Contains("¥9.38 → 9.38") && preview.Contains("显示项 1 → 余额 9.38 元"), "Processed preview");
            using (var extractor = new WebExtractor(directory)) {
                var extracted = extractor.Html("<b id='amount'>¥1,234.50</b>", config.Sources[0]);
                Check(extracted.RawFields!["value"] == "¥1,234.50" && extracted.Fields["value"] == "1234.50", "HTML raw/processed fields");
            }
            using (var fetcher = new Fetcher(_ => "")) {
                var jsonSource = new Source { Fields = [new() { Path = "$.value", Steps = [new() { Kind = "replace", Pattern = "¥" }] }] };
                var extracted = fetcher.Parse(jsonSource, "{\"value\":\"¥9.38\"}");
                Check(extracted.RawFields!["value"] == "¥9.38" && extracted.Fields["value"] == "9.38", "JSON raw/processed fields");
            }
            config.Metrics[0].Input = "api_key"; config.Metrics[0].Text = true;
            preview = TestPreview.Render(config, config.Sources[0], new(new() { ["api_key"] = "fixture-secret" }, "网页", RawFields: new() { ["api_key"] = "fixture-secret" }), x => x);
            Check(!preview.Contains("fixture-secret"), "Preview leaked sensitive raw field");
            config.Metrics[0].Input = "value"; config.Metrics[0].Text = false;
            preview = TestPreview.Render(config, config.Sources[0], new(new() { ["value"] = "test-token" }, "网页"), x => x.Replace("test-token", "[已脱敏]"));
            Check(!preview.Contains("test-token"), "Preview leaked known secret");
            bool dpapiAvailable;
            try { credentials.Save("probe", "fixture-only"); dpapiAvailable = credentials.Read("probe") == "fixture-only"; } catch { dpapiAvailable = false; }
            window.AddSource("deepseek"); Text("ApiKey", "fixture-only-key");
            Click("保存配置"); Check(window.StatusText.Contains("尚未加密保存"), "Unsaved key ignored on save");
            bool pendingKeyBlocked = false;
            try { window.AddSource("demo"); } catch (FormatException) { pendingKeyBlocked = true; }
            Check(pendingKeyBlocked, "Unsaved key lost on source addition");
            Click("加密保存 API Key");
            if (dpapiAvailable) Check(credentials.Read("deepseek") == "fixture-only-key" && ((TextBox)Find("ApiKey")).Text == "", "Credential save");
            else Check(window.StatusText.Contains("操作失败") && ((TextBox)Find("ApiKey")).Text == "fixture-only-key", "Failed credential save discarded key");
            Check(!JsonSerializer.Serialize(window.Draft).Contains("fixture-only-key"), "Credential leaked into JSON");
            Text("ApiKey", "");
            Click("删除此数据源"); Check(window.Draft.Sources.Count == 1 && window.Draft.Views.Count == 1, "Delete did not remove dependent views");
            Check(!All(window).Any(x => x.Name is "ConfigurationPath" or "ConfigurationEditor" or "SettingsTabs"), "Removed JSON/path UI remains");
            Check(((Button)Find("MoreButton")).ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Select(x => x.Text).SequenceEqual(new[] { "加载配置", "配置密钥", "打开配置目录" }), "Incorrect More menu entries/order");
            window.SetStatus("测试结束，配置未保存。");
            using (var picture = new Bitmap(window.Width, window.Height)) {
                window.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height)); picture.Save(Path.Combine(directory, "settings.png"));
            }

            using (var picture = new Bitmap(window.Width, window.Height)) {
                window.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height)); picture.Save(Path.Combine(directory, "settings-compact.png"));
            }
            window.Size = window.MinimumSize; Application.DoEvents();
            using (var picture = new Bitmap(window.Width, window.Height)) {
                window.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height)); picture.Save(Path.Combine(directory, "settings-minimum.png"));
            }
            Text("Label_1", "关闭时保存"); window.Close(); Application.DoEvents();
            Check(window.IsDisposed && Configuration.Parse(File.ReadAllText(path)).Views[0].Label == "关闭时保存", "Save on close failed");
            using var discard = new SettingsWindow(new(), path, credentials, _ => throw new Exception("Unexpected save"), () => { }, (_, _) => Task.FromResult(""), _ => Task.CompletedTask) { Opacity = 0 };
            discard.ConfirmForTest = _ => DialogResult.No; discard.Show(); discard.AddSource("demo"); discard.Close();
            Check(discard.IsDisposed, "Discard close failed");
            var namesConfig = Presets.Demo();
            namesConfig.Views[0].Label = "账户余额";
            namesConfig.Views.Add(new() { Slot = 2, Metric = "demo", Label = "账户余额" });
            DisplayNames.Write(directory, namesConfig);
            byte[] names = File.ReadAllBytes(Path.Combine(directory, "display-names.bin"));
            string NameAt(int slot) => System.Text.Encoding.Unicode.GetString(names, 4 + slot * 128, 128).TrimEnd('\0');
            Check(names.Length == 2052 && BitConverter.ToUInt32(names) == 0x314E4255 && NameAt(0) == "账户余额" && NameAt(1) == "账户余额", "Name cache format or duplicate names");
            namesConfig.Views[0].Label = ""; namesConfig.Views.RemoveAt(1);
            DisplayNames.Write(directory, namesConfig); names = File.ReadAllBytes(Path.Combine(directory, "display-names.bin"));
            Check(NameAt(0) == "未命名" && NameAt(1) == "", "Name cache did not clear deleted item");
            File.WriteAllText(Path.Combine(directory, "settings-results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, DpapiAvailable = dpapiAvailable, CredentialRoundtrip = dpapiAvailable ? "passed" : "requires normal desktop; sandbox DPAPI unavailable" })); return 0;
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(directory, "settings-error.txt"), error.ToString()); return 1;
        }
    }
}
