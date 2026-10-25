using System.Globalization;

namespace Dolap.Core;

public static class TurkishText
{
    static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Arama icin: tr-TR kucuk harf + I/i/İ/ı ve Turkce harfleri ASCII'ye katla.</summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lower = s.ToLower(Tr);
        var sb = new System.Text.StringBuilder(lower.Length);
        foreach (var c in lower)
            sb.Append(c switch { 'ı' => 'i', 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', _ => c });
        return sb.ToString();
    }

    public static bool Matches(string haystack, string foldedQuery) => Fold(haystack).Contains(foldedQuery);
}
