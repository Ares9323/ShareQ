using System.Text.RegularExpressions;

namespace ShareQ.App.Services;

/// <summary>Fallback plaintext extractors used by AutoPaster when an item's stored SearchText is
/// empty (rare — usually the clipboard reader captures CF_UNICODETEXT alongside HTML/RTF).</summary>
internal static class ClipboardCleaning
{
    public static string HtmlToPlain(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var fragStart = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        if (fragStart >= 0) html = html[(fragStart + "<!--StartFragment-->".Length)..];
        var fragEnd = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (fragEnd >= 0) html = html[..fragEnd];

        html = Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        html = Regex.Replace(html, @"\s+", " ").Trim();
        return html;
    }

    public static string RtfToPlain(string rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return string.Empty;
        rtf = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ");
        rtf = Regex.Replace(rtf, @"\\[a-zA-Z]+-?\d* ?", " ");
        rtf = rtf.Replace("{", " ").Replace("}", " ");
        rtf = Regex.Replace(rtf, @"\s+", " ").Trim();
        return rtf;
    }
}
