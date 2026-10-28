using System.Globalization;
using UsageBridge.Core;
namespace UsageBridge;

internal static class TestPreview
{
    internal static string Render(Configuration config, Source source, Collected result, Func<string, string> redact)
    {
        string Safe(string name, string text) => Configuration.IsSensitiveName(name) ? "[已脱敏]" : redact(text);
        var lines = new List<string> { "① 读取成功 · " + result.Preview, "② 提取 / 文本处理" };
        foreach (var field in result.Fields) {
            string value = Safe(field.Key, field.Value);
            string raw = result.RawFields is not null && result.RawFields.TryGetValue(field.Key, out var original) ? Safe(field.Key, original) : value;
            lines.Add($"  {field.Key}：{raw} → {value}");
        }
        lines.Add("③ 计算与最终显示");
        foreach (var metric in config.Metrics.Where(x => x.Source == source.Id)) {
            string value;
            // An expression can derive a sensitive field even when its output has an innocent name.
            bool sensitive = Configuration.IsSensitiveName(metric.Input) || metric.Expression.Length > 0 && result.Fields.Keys.Any(Configuration.IsSensitiveName);
            try {
                value = metric.Text ? result.Fields[metric.Input] : (metric.Expression.Length == 0 ? Extraction.Number(result.Fields[metric.Input]) : Expression.Evaluate(metric.Expression, result.Fields)).ToString(CultureInfo.InvariantCulture);
            } catch (Exception error) {
                string reason = error switch {
                    KeyNotFoundException => "字段不存在，请检查提取路径和指标输入。",
                    DivideByZeroException => "公式除数为 0，请检查输入或公式。",
                    OverflowException => "计算结果超出数字范围，请检查输入或公式。",
                    _ when metric.Expression.Length > 0 => "公式或输入无效，请在外部配置文件中检查表达式和字段。",
                    _ => "无法转换为数字。若含 ¥ 或千位逗号，请勾选对应移除选项后重试；若无需计算，可勾选“作为文本显示”。"
                };
                lines.Add($"  {metric.Id}：计算失败 · {reason}"); continue;
            }
            lines.Add($"  {metric.Id} = {(sensitive ? "[已脱敏]" : redact(value))}");
            foreach (var view in config.Views.Where(x => x.Metric == metric.Id)) {
                try {
                    string formatted = metric.Text ? value : Extraction.Number(value).ToString(view.Format, CultureInfo.InvariantCulture);
                    lines.Add($"  显示项 {view.Slot} → {view.Label} {(sensitive ? "[已脱敏]" : redact(formatted + view.Unit))}");
                } catch { lines.Add($"  显示项 {view.Slot}：显示格式无效，请尝试 0.00 或 0.##。"); }
            }
        }
        lines.Add("测试结束 · 草稿尚未应用，确认后点击“保存配置”。");
        return string.Join("\r\n", lines);
    }
}
