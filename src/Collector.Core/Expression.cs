namespace UsageBridge.Core;

// Bounded arithmetic grammar, without reflection, I/O, assignments, scripts or loops.
public sealed class Expression
{
    private readonly string text;
    private readonly IReadOnlyDictionary<string, string> variables;
    private int position, depth, operations;
    private Expression(string input, IReadOnlyDictionary<string, string> values) { text = input; variables = values; }
    public static decimal Evaluate(string text, IReadOnlyDictionary<string, string> variables)
    {
        if (text.Length is < 1 or > 4096) throw new FormatException("表达式为空或过长");
        var parser = new Expression(text, variables);
        decimal result = parser.Sum();
        parser.White();
        if (parser.position != text.Length) throw new FormatException("表达式含不支持的语法");
        return result;
    }
    private void White() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; }
    private bool Take(char c) { White(); if (position == text.Length || text[position] != c) return false; position++; return true; }
    private void Budget() { if (++operations > 1024) throw new FormatException("表达式运算次数超限"); }
    private decimal Sum()
    {
        decimal value = Product();
        while (true) { if (Take('+')) { Budget(); value += Product(); } else if (Take('-')) { Budget(); value -= Product(); } else return value; }
    }
    private decimal Product()
    {
        decimal value = Atom();
        while (true) { if (Take('*')) { Budget(); value *= Atom(); } else if (Take('/')) { Budget(); value /= Atom(); } else return value; }
    }
    private decimal Atom()
    {
        if (++depth > 32) throw new FormatException("表达式嵌套过深");
        Budget();
        try
        {
            if (Take('+')) return Atom();
            if (Take('-')) return -Atom();
            if (Take('(')) { decimal value = Sum(); if (!Take(')')) throw new FormatException("缺少右括号"); return value; }
            White(); int start = position;
            if (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
            {
                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.')) position++;
                return Extraction.Number(text[start..position]);
            }
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_')) position++;
            string name = text[start..position];
            if (name.Length == 0) throw new FormatException("缺少数字或变量");
            if (!Take('(')) return variables.TryGetValue(name, out var v) ? Extraction.Number(v) : throw new FormatException("表达式变量不存在");
            decimal first = Sum();
            decimal? second = Take(',') ? Sum() : null;
            if (!Take(')')) throw new FormatException("函数参数无效");
            return name switch
            {
                "abs" when second is null => Math.Abs(first),
                "round" when second is null => Math.Round(first, 0, MidpointRounding.AwayFromZero),
                "round" when second is >= 0 and <= 28 && decimal.Truncate(second.Value) == second => Math.Round(first, (int)second.Value, MidpointRounding.AwayFromZero),
                "min" when second.HasValue => Math.Min(first, second.Value),
                "max" when second.HasValue => Math.Max(first, second.Value),
                _ => throw new FormatException("未知函数或参数数量错误")
            };
        }
        finally { depth--; }
    }
}
