using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LidarrLens.Application;

public static partial class Normalization
{
    public static string Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return NonWord().Replace(builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), " ").Trim();
    }

    public static string TrackTitle(string? value)
    {
        var normalized = Text(value);
        normalized = VersionMarker().Replace(normalized, string.Empty);
        normalized = Featuring().Replace(normalized, string.Empty);
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    [GeneratedRegex("[^\\p{L}\\p{N}]+", RegexOptions.Compiled)]
    private static partial Regex NonWord();

    [GeneratedRegex("\\s+(feat|featuring|with)\\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex("\\s+(remix|live|acoustic|radio edit|edit|version|remaster|mono|stereo)(\\s+.*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VersionMarker();
}
