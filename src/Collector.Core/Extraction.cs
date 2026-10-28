using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageBridge.Core;

public static class Extraction
{
    // Deliberately small JSONPath subset: $, .property, [nonnegative index], ['property'].
    public static JsonElement Select(JsonElement root, string path)
    {
        if (path.Length is < 1 or > 4096 || path[0] != '$') throw new FormatException("JSONPath 必须从 $ 开始");
        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int start = ++i;
                while (i < path.Length && (char.IsLetterOrDigit(path[i]) || path[i] is '_' or '-')) i++;
                if (i == start) throw new FormatException("JSONPath 属性无效");
                root = Property(root, path[start..i]);
            }
            else if (path[i++] == '[')
            {
                if (i < path.Length && path[i] == '\'')
                {
                    int start = ++i;
                    while (i < path.Length && path[i] != '\'') i++;
                    if (i >= path.Length) throw new FormatException("JSONPath 引号未闭合");
                    root = Property(root, path[start..i]); i++;
                }
                else
                {
                    int start = i;
                    while (i < path.Length && char.IsAsciiDigit(path[i])) i++;
                    if (!int.TryParse(path[start..i], out int index) || root.ValueKind != JsonValueKind.Array || index >= root.GetArrayLength()) throw new FormatException("JSONPath 数组下标不存在");
                    root = root[index];
                }
                if (i >= path.Length || path[i++] != ']') throw new FormatException("JSONPath 方括号未闭合");
            }
            else throw new FormatException("不支持此 JSONPath 语法");
        }
        return root;
    }
    private static JsonElement Property(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var child)
        ? child : throw new FormatException("JSON 字段不存在");
    public static string Scalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true", JsonValueKind.False => "false",
        _ => throw new FormatException("提取值必须是非空标量")
    };
    public static string Process(string text, Field field)
    {
        if (text.Length > 65536) throw new FormatException("提取文本超过限制");
        foreach (var step in field.Steps)
        {
            text = step.Kind switch
            {
                "trim" => text.Trim(),
                "replace" when step.Pattern.Length > 0 => Replace(text, step),
                "regex" => Capture(text, step),
                _ => throw new FormatException("未知文本处理步骤")
            };
            if (text.Length > 65536) throw new FormatException("处理文本超过限制");
        }
        if (field.RequireNonEmpty && string.IsNullOrWhiteSpace(text)) throw new FormatException("目标文本为空");
        return text;
    }
    private static string Replace(string text, TextStep step)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(step.Pattern, at, StringComparison.Ordinal)) >= 0) { count++; at += step.Pattern.Length; }
        if ((long)text.Length + (long)count * (step.Replacement.Length - step.Pattern.Length) > 65536) throw new FormatException("替换结果超过限制");
        return text.Replace(step.Pattern, step.Replacement, StringComparison.Ordinal);
    }
    private static string Capture(string text, TextStep step)
    {
        if (step.Pattern.Length > 2048) throw new FormatException("正则表达式过长");
        var match = Regex.Match(text, step.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || step.Group < 0 || step.Group >= match.Groups.Count || !match.Groups[step.Group].Success) throw new FormatException("正则捕获未匹配");
        return match.Groups[step.Group].Value;
    }
    public static decimal Number(string text)
    {
        // Invariant decimal point. Thousands separators must be removed explicitly by a text step.
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("非法数字（小数点使用 .，千位分隔符须先移除）");
        return value;
    }
}
