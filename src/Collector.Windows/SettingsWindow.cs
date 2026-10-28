using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal sealed class SettingsWindow : Form
{
    private readonly TextBox editor = new() { Name = "ConfigurationEditor", Multiline = true, ScrollBars = ScrollBars.Both, AcceptsTab = true, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly TextBox output = new() { Name = "StatusOutput", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 140 };
    private readonly ComboBox sources = new() { Name = "SourceSelector", Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox enabled = new() { Name = "CollectionEnabled", Text = "启用采集", AutoSize = true };
    private bool busy, updatingEditor;
    public SettingsWindow(Configuration config, string configPath, Credentials credentials, Func<Configuration, Task> apply, Action refresh,
        Func<Configuration, Source, Task<string>> test, Func<Source, Task> login)
    {
        Text = "UsageBridge 配置 · 0.2.1"; Width = 1100; Height = 850; StartPosition = FormStartPosition.CenterScreen;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        toolbar.Controls.Add(enabled);
        enabled.CheckedChanged += (_, _) => {
            if (updatingEditor) return;
            try { var next = Read(allowEmpty: true); next.Enabled = enabled.Checked; Set(next); }
            catch {
                updatingEditor = true; enabled.Checked = !enabled.Checked; updatingEditor = false;
                SetStatus("请先修正 JSON，或点击“新建空配置”，再设置启用开关。");
            }
        };
        AddButton(toolbar, "新建空配置", () => { Set(new()); SetStatus("已新建空白草稿。点击添加按钮创建数据源；保存应用后才会替换磁盘配置。"); return Task.CompletedTask; });
        AddButton(toolbar, "恢复已保存", () => {
            bool exists = File.Exists(configPath);
            Set(exists ? Configuration.Parse(File.ReadAllText(configPath)) : new());
            SetStatus(exists ? "已重新载入磁盘配置，放弃未保存编辑。" : "尚未保存过配置，已恢复空白草稿。");
            return Task.CompletedTask;
        });
        foreach (var preset in new[] { ("codex", "添加 Codex"), ("deepseek", "添加 DeepSeek"), ("http_json", "添加 JSON"), ("http_html", "添加静态网页"), ("browser_dom", "添加动态网页"), ("demo", "添加示例") }) {
            var selected = preset;
            AddButton(toolbar, selected.Item2, () => {
                var next = Read(allowEmpty: true); Presets.Add(next, selected.Item1); Set(next);
                sources.SelectedItem = next.Sources[^1].Id;
                string help = selected.Item1 switch {
                    "codex" => "Codex 凭据路径通常不用修改，先点击“测试所选源”。",
                    "deepseek" => "先点击“保存凭据”，名称填 deepseek，值填 API Key，再测试所选源。",
                    "demo" => "示例不访问网络，用于确认显示链路。",
                    _ => "请将 url 和 fields 中的 path 改成实际地址及字段/选择器，再测试所选源。"
                };
                var ids = next.Metrics.Where(x => x.Source == next.Sources[^1].Id).Select(x => x.Id).ToHashSet();
                string slots = string.Join("、", next.Views.Where(x => ids.Contains(x.Metric)).Select(x => "UsageBridge " + x.Slot));
                SetStatus(help + "\r\n勾选“启用采集”并保存应用，然后在 TrafficMonitor 显示设置中勾选：" + slots + "。\r\n当前新增内容尚未保存。");
                return Task.CompletedTask;
            });
        }
        AddButton(toolbar, "保存应用", async () => { var next = Read(); await apply(next); SetStatus("已保存应用"); });
        AddButton(toolbar, "立即刷新", () => { refresh(); SetStatus("已请求刷新"); return Task.CompletedTask; });
        AddButton(toolbar, "读取源列表", () => { UpdateSources(Read()); return Task.CompletedTask; });
        toolbar.Controls.Add(sources);
        AddButton(toolbar, "测试所选源", async () => { var next = Read(); var source = Selected(next); SetStatus("正在测试…"); SetStatus(await test(next, source)); });
        AddButton(toolbar, "网页登录", async () => await login(Selected(Read())));
        AddButton(toolbar, "保存凭据", () => { SaveCredential(credentials); return Task.CompletedTask; });
        var pathBar = new Panel { Dock = DockStyle.Top, Height = 32 };
        var pathBox = new TextBox { Name = "ConfigurationPath", ReadOnly = true, Dock = DockStyle.Fill, Text = Path.GetFullPath(configPath) };
        var folderButton = new Button { Text = "打开配置目录", Dock = DockStyle.Right, Width = 125 };
        folderButton.Click += (_, _) => {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(configPath)!) { UseShellExecute = true }); }
            catch { SetStatus("无法打开配置目录，请复制旁边的路径手动打开。"); }
        };
        pathBar.Controls.Add(pathBox); pathBar.Controls.Add(folderButton);
        var hint = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), Text = "使用顺序：添加数据源 → 测试所选源 → 勾选启用采集 → 保存应用 → 在 TrafficMonitor 勾选对应槽位。\n编辑不会自动保存。下面是配置文件路径；首次保存前文件可能尚不存在。" };
        Controls.Add(editor); Controls.Add(output); Controls.Add(pathBar); Controls.Add(hint); Controls.Add(toolbar); Set(config);
        FormClosing += (_, e) => { if (busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    private Configuration Read(bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(editor.Text)) {
            if (allowEmpty) return new() { Enabled = enabled.Checked };
            throw new FormatException("配置为空。请先添加数据源，或点击“恢复已保存”。如需清除所有源，请点击“新建空配置”后保存。");
        }
        return Configuration.Parse(editor.Text);
    }
    private void Set(Configuration config)
    {
        updatingEditor = true;
        try { editor.Text = JsonSerializer.Serialize(config, Configuration.JsonOptions); enabled.Checked = config.Enabled; UpdateSources(config); }
        finally { updatingEditor = false; }
    }
    private void UpdateSources(Configuration config)
    {
        string? old = sources.SelectedItem as string;
        sources.Items.Clear(); sources.Items.AddRange(config.Sources.Select(x => (object)x.Id).ToArray());
        if (old is not null && sources.Items.Contains(old)) sources.SelectedItem = old;
        else if (sources.Items.Count > 0) sources.SelectedIndex = 0;
    }
    private Source Selected(Configuration config) => config.Sources.FirstOrDefault(x => x.Id == sources.SelectedItem as string) ?? throw new FormatException("请刷新源列表并选择数据源");
    private void AddButton(FlowLayoutPanel panel, string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => {
            if (busy) return; busy = true; panel.Enabled = false;
            try { await action(); }
            catch (CollectionFailure error) { SetStatus(error.State + "：" + error.Message); }
            catch (FormatException error) { SetStatus(error.Message); }
            catch (JsonException error) { SetStatus($"JSON 格式错误或包含未知字段（第 {(error.LineNumber ?? 0) + 1} 行）。请修正，或点击“恢复已保存”/“新建空配置”。"); }
            catch (OperationCanceledException) { SetStatus("操作取消或超时。"); }
            catch { SetStatus("操作失败，请检查配置、文件权限或浏览器运行时。"); }
            finally { busy = false; if (!IsDisposed) panel.Enabled = true; }
        }; panel.Controls.Add(button);
    }
    public void SetStatus(string text) { if (!IsDisposed) output.Text = text; }
    private void SaveCredential(Credentials credentials)
    {
        using var dialog = new Form { Text = "保存 Windows 用户加密凭据", Width = 500, Height = 210, StartPosition = FormStartPosition.CenterParent };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        var name = new TextBox { Width = 440, PlaceholderText = "凭据名称，例如 deepseek" };
        var value = new TextBox { Width = 440, UseSystemPasswordChar = true, PlaceholderText = "API Key；自定义 Authorization 头应包含 Bearer 前缀" };
        var save = new Button { Text = "加密保存", AutoSize = true };
        save.Click += (_, _) => {
            try { credentials.Save(name.Text, value.Text); value.Clear(); dialog.Close(); SetStatus("凭据已加密保存。"); }
            catch { MessageBox.Show(dialog, "保存失败，请检查名称、值及目录权限。"); }
        };
        layout.Controls.AddRange([name, value, save]); dialog.Controls.Add(layout); dialog.ShowDialog(this);
    }
}
