
using System.Text.RegularExpressions;

namespace FunctionApp.Utils;

public class VisitorIdExtractor
{
    // Robust to half-width / full-width colon and surrounding spaces
    private static readonly Regex GuidRegex = new Regex(
        @"VisitorID\s*[:：]\s*([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public string? Extract(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return null;
        var m = GuidRegex.Match(bodyText);
        return m.Success ? m.Groups[1].Value : null;
    }
}
