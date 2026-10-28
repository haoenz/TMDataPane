using System.Text.Json;
using DataPane.Core;
namespace DataPane;

internal sealed class SettingsWindow : Form
{
    private string statusText = "";
    internal string StatusText => statusText;
    private readonly ToolTip tips = new();
    private readonly ContextMenuStrip moreMenu = new();
    private readonly ListView sources = new() { Name = "SourceList", Dock = DockStyle.Fill, View = System.Windows.Forms.View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly FlowLayoutPanel form = new() { Name = "SourceForm", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(6) };
    private readonly CheckBox enabled = new() { Name = "CollectionEnabled", Text = "自动更新", AutoSize = true };
    private readonly Label state = new() { Name = "SaveState", AutoSize = true, Padding = new Padding(4) };
    private readonly Label statusSummary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel content = new() { Name = "SettingsContent", Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4) };
    private readonly System.Windows.Forms.Timer liveTimer = new() { Interval = 1000 };
    private readonly Credentials credentials;
    private readonly Func<Configuration, Task> apply;
    private readonly Func<Configuration, Source, Task<string>> test;
    private readonly Func<Source, Task> login;
    private readonly Func<DisplayValue[]>? snapshot;
    private readonly string configPath;
    private Configuration draft;
    private string saved, applied, selectedId = "";
    private bool busy, syncing, closeApproved, restartNeeded;
    private string? diskVersion;
    internal Func<string, DialogResult>? ConfirmForTest;
    internal Configuration Draft => draft;
    private bool HasPendingKey => form.Controls.OfType<TextBox>().Any(x => x.Name == "ApiKey" && x.TextLength > 0);
    internal bool IsDirty => HasPendingKey || Serialize(draft) != saved;
    private static string Serialize(Configuration config) => JsonSerializer.Serialize(config, Configuration.JsonOptions);
    private static readonly (string Kind, string Title, string Help)[] Types = [
        ("codex", "Codex", "读取本机登录状态，查看 5 小时和每周剩余额度"),
        ("deepseek", "DeepSeek", "使用 API Key 查看账户余额"),
        ("http_json", "JSON 接口", "从接口返回的数据中提取数值"),
        ("http_html", "静态网页", "从网页 HTML 中提取内容"),
        ("browser_dom", "动态网页", "支持页面脚本、手动登录和会话复用"),
        ("demo", "示例数据", "使用模拟数据体验插件，无需联网")];
    private static string Title(Source source) => Types.FirstOrDefault(x => x.Kind == source.Kind).Title ?? source.Kind;

    public SettingsWindow(Configuration config, string configPath, Credentials credentials, Func<Configuration, Task> apply, Action refresh,
        Func<Configuration, Source, Task<string>> test, Func<Source, Task> login, Func<DisplayValue[]>? snapshot = null)
    {
        this.configPath = configPath; this.credentials = credentials; this.apply = apply; this.test = test; this.login = login; this.snapshot = snapshot;
        draft = Configuration.Parse(Serialize(config)); saved = Serialize(draft); applied = saved; diskVersion = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        Text = "DataPane 配置 · 0.4.0"; Size = new(780, 520); MinimumSize = new(700, 440); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 30));
        Controls.Add(root); root.Controls.Add(toolbar, 0, 0);
        Button(toolbar, "添加数据", () => { AddDialog(); return Task.CompletedTask; });
        Button(toolbar, "保存配置", Save);
        Button(toolbar, "测试数据", TestSelected);
        Button(toolbar, "更新数据", () => { refresh(); SetStatus("已请求后台刷新。刷新使用已保存配置；草稿请使用“测试数据”。"); return Task.CompletedTask; });
        tips.SetToolTip(enabled, "后台定时采集的总开关；点击“保存配置”后生效。关闭不影响手动测试。");
        MenuItem("加载配置", "重新读取 config.json；放弃草稿前会确认，读取后需保存配置才应用到后台。", () => { Restore(); return Task.CompletedTask; });
        MenuItem("配置密钥", "按名称新增或替换加密凭据，供后续请求使用。", () => { CredentialDialog(); return Task.CompletedTask; });
        MenuItem("打开配置目录", "打开配置文件所在的文件夹，可使用外部编辑器修改 config.json。", () => {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(configPath)!) { UseShellExecute = true }); }
            catch { SetStatus("目录尚不存在或无法打开。首次保存后创建配置文件。"); }
            return Task.CompletedTask;
        });
        var more = new Button { Name = "MoreButton", Text = "更多", AutoSize = true, MinimumSize = new(0, 28), Margin = new Padding(2), ContextMenuStrip = moreMenu };
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, more.Height));
        toolbar.Controls.Add(more);
        toolbar.Controls.Add(enabled);
        enabled.Checked = draft.Enabled;
        enabled.CheckedChanged += (_, _) => { if (!syncing) { draft.Enabled = enabled.Checked; Changed(); } };
        root.Controls.Add(state, 0, 1);
        root.Controls.Add(content, 0, 2);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Size = new(820, 450), SplitterDistance = 190 };
        sources.Columns.Add("数据源", 65); sources.Columns.Add("状态", 60); sources.Columns.Add("已应用值", 60);
        split.Panel1.Controls.Add(sources); split.Panel2.Controls.Add(form); content.Controls.Add(split);
        Shown += (_, _) => {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            split.SplitterDistance = Math.Min((int)(190 * DeviceDpi / 96f), split.Width / 3);
            FitForm();
        };
        form.ClientSizeChanged += (_, _) => FitForm();
        sources.ClientSizeChanged += (_, _) => { if (sources.Columns.Count == 3) sources.Columns[0].Width = Math.Max(60, sources.ClientSize.Width - sources.Columns[1].Width - sources.Columns[2].Width - 4); };
        sources.SelectedIndexChanged += (_, _) => {
            if (syncing || sources.SelectedItems.Count == 0) return;
            if (HasPendingKey) {
                syncing = true;
                foreach (ListViewItem item in sources.Items) item.Selected = (string)item.Tag! == selectedId;
                syncing = false; SetStatus("请先加密保存已输入的 API Key，或清空输入框后再切换数据源。"); return;
            }
            selectedId = (string)sources.SelectedItems[0].Tag!; RenderSource();
        };
        var footer = new Panel { Dock = DockStyle.Fill };
        footer.Controls.Add(statusSummary);
        root.Controls.Add(footer, 0, 3);
        Rebuild(); liveTimer.Tick += (_, _) => UpdateLive(); liveTimer.Start();
        FormClosing += async (_, e) => {
            if (e.CloseReason != CloseReason.UserClosing || closeApproved) return;
            if (busy) { e.Cancel = true; SetStatus("正在处理操作，请稍候再关闭窗口。"); return; }
            if (!IsDirty) return;
            var answer = Confirm("有未保存的修改。是否保存配置？\n选择“否”放弃修改，选择“取消”继续编辑。");
            if (answer == DialogResult.No) return;
            e.Cancel = true;
            if (answer == DialogResult.Yes) await Run(async () => { await Save(); closeApproved = true; BeginInvoke(Close); });
        };
    }
    private DialogResult Confirm(string message) => ConfirmForTest?.Invoke(message) ?? MessageBox.Show(this, message, "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
    private Configuration Current() => Configuration.Parse(Serialize(draft));
    private Source Selected(Configuration config) => config.Sources.FirstOrDefault(x => x.Id == selectedId) ?? throw new FormatException("请先添加或选择一个数据源。");
    private void Changed() { UpdateDirty(); UpdateLive(); }
    private void UpdateDirty() {
        bool needsApply = Serialize(draft) != applied;
        state.Text = IsDirty ? "● 有未保存的修改 · 保存后应用到后台" : needsApply ? "● 已读取文件 · 点击“保存配置”更新后台采集" : "✓ 配置未修改 · 请在 TM 勾选对应显示槽位";
        state.ForeColor = IsDirty || needsApply ? Color.DarkOrange : Color.DarkGreen;
        if (restartNeeded) { state.Text += " · 显示项已变更，请重启 TM"; state.ForeColor = Color.DarkOrange; }
    }
    private void Rebuild()
    {
        syncing = true;
        try {
            enabled.Checked = draft.Enabled; sources.Items.Clear();
            foreach (var source in draft.Sources) {
                var ids = draft.Metrics.Where(x => x.Source == source.Id).Select(x => x.Id).ToHashSet();
                string label = draft.Views.FirstOrDefault(x => ids.Contains(x.Metric))?.Label ?? Title(source);
                var item = new ListViewItem(label) { Tag = source.Id, ToolTipText = source.Id }; item.SubItems.Add(""); item.SubItems.Add(""); sources.Items.Add(item);
            }
            if (!draft.Sources.Any(x => x.Id == selectedId)) selectedId = draft.Sources.FirstOrDefault()?.Id ?? "";
            foreach (ListViewItem item in sources.Items) item.Selected = (string)item.Tag! == selectedId;
        } finally { syncing = false; }
        RenderSource(); Changed();
    }
    private void UpdateLive()
    {
        var values = snapshot?.Invoke();
        Configuration applied;
        try { applied = Configuration.Parse(this.applied); } catch { return; }
        foreach (ListViewItem item in sources.Items) {
            var source = draft.Sources.Single(x => x.Id == (string)item.Tag!);
            var old = applied.Sources.FirstOrDefault(x => x.Id == source.Id);
            var metrics = applied.Metrics.Where(x => x.Source == source.Id).Select(x => x.Id).ToHashSet();
            var view = applied.Views.FirstOrDefault(x => metrics.Contains(x.Metric));
            var value = view is not null && values is not null ? values.ElementAtOrDefault(view.Slot - 1) : null;
            string runtimeState = value is null ? "等待采集" : new[] { "正常", "需要登录", "数据过期", "计算失败", "请求失败", "提取失败" }.FirstOrDefault(x => value.Status.StartsWith(x)) ?? "等待采集";
            item.SubItems[1].Text = old is null ? "尚未应用" : !applied.Enabled || !old.Enabled ? "未启用" : runtimeState;
            item.SubItems[2].Text = value?.Value ?? "—"; item.ToolTipText = value?.Status ?? "保存配置后开始采集";
        }
        sources.ShowItemToolTips = true;
    }
    internal void AddSource(string kind)
    {
        if (HasPendingKey) throw new FormatException("请先加密保存已输入的 API Key，或清空输入框后再添加数据。");
        var next = Current(); Presets.Add(next, kind); draft = next; selectedId = next.Sources[^1].Id;
        Rebuild(); SetStatus("已添加草稿。填写配置并测试，成功后保存配置。显示槽位见右侧“显示到 TrafficMonitor”。");
    }
    private void AddDialog()
    {
        using var dialog = new Form { Text = "添加数据", Size = new(620, 420), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, ItemHeight = 38, Font = Font };
        foreach (var type in Types) list.Items.Add(type.Title + " — " + type.Help);
        list.SelectedIndex = 0;
        var add = new Button { Text = "添加并配置", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        dialog.Controls.Add(list); dialog.Controls.Add(add); dialog.AcceptButton = add;
        if (dialog.ShowDialog(this) == DialogResult.OK) AddSource(Types[list.SelectedIndex].Kind);
    }
    private void Restore()
    {
        if (IsDirty && Confirm("重新读取文件会放弃当前草稿。确定放弃？") != DialogResult.Yes) return;
        string? text = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        var next = text is null ? new Configuration() : Configuration.Parse(text);
        draft = next; saved = Serialize(next); diskVersion = text; Rebuild();
        SetStatus("已从文件读取配置。外部修改需点击“保存配置”后才会用于后台采集。");
    }
    private async Task Save()
    {
        if (HasPendingKey) throw new FormatException("API Key 尚未加密保存。请先点击“加密保存 API Key”，再保存配置。");
        string? currentFile = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        if (currentFile != diskVersion) throw new FormatException("配置文件已被外部修改。请先点击“加载配置”读取文件，避免覆盖外部修改。");
        var next = Current();
        bool membershipChanged = !Configuration.Parse(applied).Views.Select(x => x.Slot).ToHashSet().SetEquals(next.Views.Select(x => x.Slot));
        var oldWidths = Configuration.Parse(applied).Views.ToDictionary(x => x.Slot, x => x.EffectiveWidthSample);
        bool widthChanged = next.Views.Any(x => !oldWidths.TryGetValue(x.Slot, out var old) || old != x.EffectiveWidthSample);
        await apply(next); restartNeeded |= membershipChanged || widthChanged;
        draft = next; saved = applied = Serialize(next); diskVersion = File.Exists(configPath) ? File.ReadAllText(configPath) : null; Rebuild();
        SetStatus("已保存配置。" + (!next.HasWork ? "当前未自动更新。" : "采集已启用。") + (restartNeeded ? "显示项或宽度已变更，请重启 TM 更新布局。" : ""));
    }
    private async Task TestSelected()
    {
        var next = Current(); var source = Selected(next); SetStatus("正在测试数据…（不会保存）");
        string result;
        try { result = await test(next, source); }
        catch (Exception error) { result = "测试失败\r\n" + ErrorText(error); }
        if (IsDisposed) return;
        foreach (var old in OwnedForms.Where(x => x.Name == "TestResults").ToArray()) old.Close();
        var dialog = new Form { Name = "TestResults", Text = "测试结果 · " + Title(source), Size = new(600, 400), MinimumSize = new(420, 260), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font };
        dialog.Controls.Add(new TextBox { Name = "TestResultText", Text = result, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill });
        var close = new Button { Text = "关闭", Dock = DockStyle.Bottom, Height = 32 };
        close.Click += (_, _) => dialog.Close(); dialog.Controls.Add(close); dialog.CancelButton = close;
        dialog.Show(this); SetStatus("测试结束，结果已在独立窗口显示；配置未保存。");
    }
    private void RenderSource()
    {
        form.SuspendLayout();
        try {
            foreach (Control control in form.Controls.Cast<Control>().ToArray()) control.Dispose();
            var source = draft.Sources.FirstOrDefault(x => x.Id == selectedId);
            if (source is null) { Hint("还没有数据源\n点击“添加数据”开始。可先选择“示例数据”体验显示效果。"); return; }
            Hint(Title(source), true);
            var sourceMetricIds = draft.Metrics.Where(x => x.Source == source.Id).Select(x => x.Id).ToHashSet();
            Hint("TM 显示名称：" + string.Join("、", draft.Views.Where(x => sourceMetricIds.Contains(x.Metric)).Select(x => string.IsNullOrWhiteSpace(x.Label) ? "未命名" : x.Label)));
            Toggle("启用此数据源", "SourceEnabled", source.Enabled, v => source.Enabled = v);
            Number("刷新间隔（秒）", "IntervalSeconds", source.IntervalSeconds, 5, 86400, v => source.IntervalSeconds = v);
            Number("请求超时（秒）", "TimeoutSeconds", source.TimeoutSeconds, 1, 120, v => source.TimeoutSeconds = v);
            if (source.Kind == "codex") {
                string auth = source.AuthPath.Length > 0 ? source.AuthPath : Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "auth.json");
                Hint(File.Exists(auth) ? "已找到本机凭据文件；请测试确认登录有效。" : "未找到本机凭据文件。请先登录 Codex，或指定凭据文件路径。");
                TextField("凭据文件路径", "AuthPath", source.AuthPath, v => source.AuthPath = v, "留空自动查找");
            }
            if (source.Kind == "deepseek") {
                TextField("币种（例如 CNY / USD）", "Currency", source.Currency, v => source.Currency = v);
                CredentialForm(source);
            }
            if (source.Kind is "http_json" or "http_html" or "browser_dom") {
                TextField("网页 / 接口地址", "SourceUrl", source.Url, v => source.Url = v);
                if (source.Kind != "browser_dom") TextField("请求方法", "RequestMethod", source.Method, v => source.Method = v);
                else {
                    TextField("登录成功标志", "SuccessSelector", source.SuccessSelector, v => source.SuccessSelector = v, "CSS 选择器，可留空");
                    Button(form, "打开网页登录", async () => await login(Selected(Current())));
                }
                foreach (var field in source.Fields) {
                    Hint("提取字段：" + field.Name, true);
                    TextField(source.Kind == "http_json" ? "JSON 路径" : "CSS 选择器", "FieldPath_" + field.Name, field.Path, v => field.Path = v, source.Kind == "http_json" ? "例如 $.balance" : "例如 #balance");
                    if (source.Kind != "http_json") {
                        TextField("读取属性", "Attribute_" + field.Name, field.Attribute, v => field.Attribute = v, "留空取文本；输入框填 value");
                        Toggle("多个匹配时取第一个", "FirstMatch_" + field.Name, field.FirstMatch, v => field.FirstMatch = v);
                    }
                    Processing(field, "去除首尾空格", "trim", "");
                    Processing(field, "移除货币符号 ¥", "replace", "¥");
                    Processing(field, "移除千位逗号 ,", "replace", ",");
                    Hint("处理步骤按顺序执行；其他币种、正则及复杂规则请在外部编辑 config.json。");
                }
            }
            Hint("显示到 TrafficMonitor", true);
            foreach (var metric in draft.Metrics.Where(x => x.Source == source.Id)) {
                Hint("指标：" + metric.Input + (metric.Expression.Length > 0 ? "（已有计算公式）" : ""));
                Toggle("作为文本显示（不进行数字计算）", "Text_" + metric.Id, metric.Text, v => metric.Text = v);
                foreach (var view in draft.Views.Where(x => x.Metric == metric.Id)) {
                    Hint("增删显示项后需重启 TM；仅改名可重新打开 TM 显示设置。");
                    TextField("显示名称", "Label_" + view.Slot, view.Label, v => { view.Label = v; var item = sources.Items.Cast<ListViewItem>().FirstOrDefault(x => (string)x.Tag! == source.Id); if (item is not null) item.Text = v; });
                    TextField("数字格式", "Format_" + view.Slot, view.Format, v => view.Format = v, "0.00 固定两位 / 0.## 最多两位");
                    TextField("单位后缀", "Unit_" + view.Slot, view.Unit, v => view.Unit = v, "例如 元 / %");
                    TextField("宽度参考文本", "WidthSample_" + view.Slot, view.WidthSample, v => view.WidthSample = v, "留空使用 ~999999.99 CNY");
                    Hint("按参考文本预留数值宽度，不改变实际内容。请包含单位及可能的 ~ 前缀，如 ~100% 或 ~999.99 CNY；保存后重启 TM 更新宽度。");
                }
            }
            Button(form, "删除此数据源", () => {
                if (Confirm("删除此数据源及其显示项？保存配置后才会生效。") == DialogResult.Yes) {
                    var ids = draft.Metrics.Where(x => x.Source == source.Id).Select(x => x.Id).ToHashSet();
                    draft.Views.RemoveAll(x => ids.Contains(x.Metric)); draft.Metrics.RemoveAll(x => x.Source == source.Id); draft.Sources.Remove(source); Rebuild();
                }
                return Task.CompletedTask;
            });
        } finally { FitForm(); form.ResumeLayout(); }
    }
    private void CredentialForm(Source source)
    {
        Hint("API Key 加密保存在当前 Windows 用户下，不写入配置 JSON。");
        TextField("凭据名称", "CredentialName", source.Credential, v => source.Credential = v);
        var key = new TextBox { Name = "ApiKey", Width = 510, UseSystemPasswordChar = true, PlaceholderText = "输入新的 API Key（已保存的密钥不回显）" }; form.Controls.Add(key);
        key.TextChanged += (_, _) => UpdateDirty();
        Button(form, "加密保存 API Key", () => { credentials.Save(source.Credential, key.Text); key.Clear(); SetStatus("API Key 已加密保存，可测试数据。配置修改仍需点击“保存配置”。"); return Task.CompletedTask; });
    }
    private void CredentialDialog()
    {
        using var dialog = new Form { Text = "配置密钥", Size = new(580, 260), StartPosition = FormStartPosition.CenterParent };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown };
        var name = new TextBox { Width = 520, PlaceholderText = "凭据名称，与 secretHeaders 中引用的名称一致" };
        var value = new TextBox { Width = 520, UseSystemPasswordChar = true, PlaceholderText = "凭据值；Authorization 头需要包含 Bearer 前缀" };
        var message = new Label { AutoSize = true, MaximumSize = new(520, 0), Text = "保存后立即生效。密钥使用 Windows 用户加密，不写入 JSON。" };
        var save = new Button { Text = "加密保存", AutoSize = true };
        save.Click += (_, _) => { try { credentials.Save(name.Text, value.Text); value.Clear(); dialog.Close(); SetStatus("凭据已加密保存。"); } catch { message.Text = "保存失败，请检查名称、值及目录权限。"; } };
        layout.Controls.AddRange([message, name, value, save]); dialog.Controls.Add(layout); dialog.ShowDialog(this);
    }
    private void Processing(Field field, string title, string kind, string pattern)
    {
        bool Match(TextStep step) => step.Kind == kind && (kind == "trim" || step.Pattern == pattern && step.Replacement == "");
        Toggle(title, "Process_" + field.Name + "_" + (kind == "trim" ? "trim" : pattern == "¥" ? "currency" : "comma"), field.Steps.Any(Match), value => {
            if (value && !field.Steps.Any(Match)) field.Steps.Add(new() { Kind = kind, Pattern = pattern });
            if (!value) field.Steps.RemoveAll(x => Match(x));
        });
    }
    private void FitForm()
    {
        int width = Math.Max(180, form.ClientSize.Width - form.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control control in form.Controls) {
            if (control is Label or CheckBox) control.MaximumSize = new(width, 0);
            else if (control is TableLayoutPanel) { control.MinimumSize = new(width, 0); control.MaximumSize = new(width, 0); control.Width = width; }
            else if (control is TextBox) control.Width = width;
        }
    }
    private void Hint(string text, bool bold = false) => form.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new(500, 0), Margin = new Padding(3, bold ? 6 : 2, 3, 2), Font = bold ? new Font(Font, FontStyle.Bold) : Font });
    private void FieldRow(string label, Control input)
    {
        var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Width = 500, Margin = new Padding(3, 1, 3, 1) };
        row.ColumnStyles.Add(new(SizeType.Absolute, 116 * DeviceDpi / 96f)); row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right; row.Controls.Add(input, 1, 0); form.Controls.Add(row);
    }
    private void TextField(string label, string name, string value, Action<string> change, string placeholder = "")
    {
        var box = new TextBox { Name = name, Text = value, PlaceholderText = placeholder }; box.TextChanged += (_, _) => { change(box.Text); Changed(); }; FieldRow(label, box);
    }
    private void Toggle(string label, string name, bool value, Action<bool> change)
    {
        var box = new CheckBox { Name = name, Text = label, Checked = value, AutoSize = true }; box.CheckedChanged += (_, _) => { change(box.Checked); Changed(); }; form.Controls.Add(box);
    }
    private void Number(string label, string name, int value, int min, int max, Action<int> change)
    {
        var box = new NumericUpDown { Name = name, Minimum = min, Maximum = max, Value = value, MaximumSize = new(110, 0) }; box.ValueChanged += (_, _) => { change((int)box.Value); Changed(); }; FieldRow(label, box);
    }
    private void Button(FlowLayoutPanel panel, string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new(0, 28), Margin = new Padding(2) }; button.Click += async (_, _) => await Run(action); panel.Controls.Add(button);
        string help = text switch {
            "添加数据" => "选择一种类型并添加到草稿；保存后才开始后台采集。",
            "保存配置" => "保存所有表单修改并更新后台采集，包括总开关和各数据源开关。",
            "测试数据" => "只测试当前选中的数据源，使用未保存的表单内容；结果在独立窗口显示。",
            "更新数据" => "立即请求刷新所有已应用且启用的数据源；不保存草稿，也不重读外部文件。",
            "加载配置" => "从磁盘重新读取 config.json；放弃草稿前会确认。外部修改读入后还需保存配置。",
            "配置密钥" => "按名称新增或替换 API Key 等凭据。加密保存后下次请求使用新值；不保存表单配置。",
            _ => ""
        };
        if (help.Length > 0) tips.SetToolTip(button, help);
    }
    private void MenuItem(string text, string help, Func<Task> action)
    {
        var item = new ToolStripMenuItem(text) { ToolTipText = help };
        item.Click += async (_, _) => await Run(action);
        moreMenu.Items.Add(item);
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; toolbar.Enabled = false; content.Enabled = false;
        try { await action(); } catch (Exception error) { SetError(error); }
        finally { busy = false; if (!IsDisposed) { toolbar.Enabled = true; content.Enabled = true; } }
    }
    private static string ErrorText(Exception error) => error switch {
        CollectionFailure failure => failure.State + "：" + failure.Message,
        JsonException json => $"配置文件 JSON 无效或包含未知字段（第 {(json.LineNumber ?? 0) + 1} 行）。请在外部修正文件后点击“加载配置”。",
        FormatException => error.Message,
        OperationCanceledException => "操作取消或超时。请检查网络、登录状态和超时设置。",
        _ => "操作失败，请检查配置、文件权限或浏览器运行时。"
    };
    private void SetError(Exception error) => SetStatus(ErrorText(error));
    public void SetStatus(string text) { if (!IsDisposed) { statusText = text; statusSummary.Text = text.Split('\n')[0].Trim(); tips.SetToolTip(statusSummary, text); } }
    protected override void Dispose(bool disposing) { if (disposing) { liveTimer.Dispose(); tips.Dispose(); moreMenu.Dispose(); } base.Dispose(disposing); }
}
