using System.Globalization;
using System.Text.RegularExpressions;

namespace Drydock.App.ViewModels;

/// <summary>Reads amounts typed with either decimal mark, so "1,5" and "1.5" both work on any Windows language.</summary>
public static partial class Amounts
{
    public static bool TryParse(string text, out decimal value)
    {
        value = 0;
        text = text.Trim();
        if (!Shape().IsMatch(text))
            return false;
        int dots = text.Count(c => c == '.'), commas = text.Count(c => c == ',');
        char? decimalMark = null;
        if (dots > 0 && commas > 0)
            decimalMark = text.LastIndexOf('.') > text.LastIndexOf(',') ? '.' : ',';
        else if (dots + commas == 1)
        {
            char mark = dots == 1 ? '.' : ',';
            // "1.000" on German Windows means a thousand, "1.5" never does
            bool looksGrouped = text.Length - text.IndexOf(mark) - 1 == 3
                && CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator == mark.ToString();
            decimalMark = looksGrouped ? null : mark;
        }
        char? groupMark = decimalMark switch { '.' => ',', ',' => '.', _ => dots > 0 ? '.' : commas > 0 ? ',' : null };

        string whole = text, fraction = "";
        if (decimalMark is { } d)
        {
            int at = text.LastIndexOf(d);
            if (text.IndexOf(d) != at)
                return false;
            (whole, fraction) = (text[..at], text[(at + 1)..]);
        }
        if (groupMark is { } g && whole.Contains(g))
        {
            var groups = whole.Split(g);
            if (groups[0].Length is 0 or > 3 || groups.Skip(1).Any(x => x.Length != 3))
                return false;
            whole = string.Concat(groups);
        }
        if (whole.Length == 0 || fraction.Any(c => !char.IsAsciiDigit(c)) || whole.Any(c => !char.IsAsciiDigit(c)))
            return false;
        return decimal.TryParse(fraction.Length > 0 ? $"{whole}.{fraction}" : whole, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"^[0-9][0-9.,]*$")]
    private static partial Regex Shape();

    public static string Format(decimal value) => value.ToString("0.######", CultureInfo.CurrentCulture);
}
