
using System.Text.RegularExpressions;

namespace FunctionApp.Utils;

public class VisitorIdExtractor
{
    // 実際のフォーマットに対応:
    // VisitorID:39906803-1789-434f-9b94-7bcf089342c7 ^^^^^^^^^ 【来客管理アドインからのお願い】^^^^^^^^^
    // 前後に任意のユーザー文字が含まれる可能性を考慮
    private static readonly Regex GuidRegex = new Regex(
        @"VisitorID\s*[:：]\s*([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public string? Extract(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText)) return null;
        
        // HTMLタグを除去してテキストのみを抽出
        var cleanText = System.Text.RegularExpressions.Regex.Replace(bodyText, @"<[^>]+>", " ");
        // HTMLエンティティをデコード
        cleanText = System.Web.HttpUtility.HtmlDecode(cleanText);
        // 余分な空白を正規化
        cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\s+", " ");
        
        var match = GuidRegex.Match(cleanText);
        return match.Success ? match.Groups[1].Value : null;
    }
}
