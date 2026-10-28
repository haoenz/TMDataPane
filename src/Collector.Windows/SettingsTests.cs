using System.Text.Json;
using UsageBridge.Core;
namespace UsageBridge;

internal static class SettingsTests
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.json");
        string original = JsonSerializer.Serialize(Presets.Demo(), Configuration.JsonOptions);
        File.WriteAllText(path, original);
        int checks = 0, saves = 0;
        try {
            using var window = new SettingsWindow(new(), path, new Credentials(directory), next => {
                saves++; File.WriteAllText(path, JsonSerializer.Serialize(next, Configuration.JsonOptions)); return Task.CompletedTask;
            }, () => { }, (_, _) => Task.FromResult("test preview"), _ => Task.CompletedTask) { Opacity = 0, ShowInTaskbar = false };
            window.Show();
            Control Find(string name) => window.Controls.Find(name, true).Single();
            var editor = (TextBox)Find("ConfigurationEditor");
            var status = (TextBox)Find("StatusOutput");
            var enabled = (CheckBox)Find("CollectionEnabled");
            void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
            IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
            void Click(string label) => All(window).OfType<Button>().Single(x => x.Text == label).PerformClick();
            foreach (var preset in new[] { ("codex", "添加 Codex"), ("deepseek", "添加 DeepSeek"), ("http_json", "添加 JSON"), ("http_html", "添加静态网页"), ("browser_dom", "添加动态网页"), ("demo", "添加示例") }) {
                editor.Text = " \r\n\t";
                Click(preset.Item2);
                var config = Configuration.Parse(editor.Text);
                Check(config.Sources.Count == 1 && config.Sources[0].Kind == preset.Item1, "Blank editor preset failed: " + preset.Item1);
                Check((string?)((ComboBox)Find("SourceSelector")).SelectedItem == config.Sources[0].Id, "New source not selected");
            }
            editor.Text = "{broken";
            Click("添加示例");
            Check(editor.Text == "{broken" && status.Text.Contains("JSON"), "Malformed draft was replaced");
            editor.Clear(); Click("保存应用");
            Check(saves == 0 && File.ReadAllText(path) == original && status.Text.Contains("配置为空"), "Empty save overwrote disk");
            Click("恢复已保存");
            Check(Configuration.Parse(editor.Text).Sources.Single().Kind == "demo" && enabled.Checked, "Saved settings did not reload");
            editor.Text = "{broken"; enabled.Checked = false;
            Check(enabled.Checked && editor.Text == "{broken", "Invalid checkbox edit was not rolled back");
            editor.Clear(); enabled.Checked = false;
            Check(!Configuration.Parse(editor.Text).Enabled, "Blank checkbox edit failed");
            Click("新建空配置");
            Check(Configuration.Parse(editor.Text).Sources.Count == 0 && saves == 0, "New configuration wrote to disk");
            Click("保存应用");
            Check(saves == 1 && Configuration.Parse(File.ReadAllText(path)).Sources.Count == 0, "Explicit empty configuration save failed");
            var pathBox = (TextBox)Find("ConfigurationPath");
            Check(pathBox.ReadOnly && pathBox.Text == Path.GetFullPath(path), "Wrong configuration path");
            File.Delete(path); Click("恢复已保存");
            Check(Configuration.Parse(editor.Text).Sources.Count == 0 && !File.Exists(path), "Reload created an unsaved file");
            Click("添加 Codex");
            using var picture = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(picture, new Rectangle(0, 0, picture.Width, picture.Height));
            picture.Save(Path.Combine(directory, "settings.png"));
            File.WriteAllText(Path.Combine(directory, "settings-results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks }));
            window.Close(); return 0;
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(directory, "settings-error.txt"), error.ToString()); return 1;
        }
    }
}
