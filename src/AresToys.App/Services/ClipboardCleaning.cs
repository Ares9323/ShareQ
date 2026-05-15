using System.Text.RegularExpressions;

namespace AresToys.App.Services;

/// <summary>Fallback plaintext extractors used by AutoPaster when an item's stored SearchText is
/// empty (rare — usually the clipboard reader captures CF_UNICODETEXT alongside HTML/RTF).</summary>
internal static class ClipboardCleaning
{
    public static string HtmlToPlain(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // Strip the CF_HTML header block ("Version:1.0\r\nStartHTML:N\r\n...") that prefixes
        // every clipboard HTML payload. Most producers also emit <!--StartFragment-->/
        // <!--EndFragment--> comments inside the body so the marker-based slice below kicks
        // in and the header gets discarded as a side effect — but some apps (Rider64.exe is
        // the one that surfaced the bug) write a minimal CF_HTML with the header followed by
        // raw text and NO fragment comments. Without this preamble pass the headers would
        // paste through verbatim ("Version:1.0\nStartHTML:0000000128\n...Whitelist").
        if (html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = 0;
            while (idx < html.Length)
            {
                var lineEnd = html.IndexOf('\n', idx);
                if (lineEnd < 0) break;
                // A header line is "Word:..." appearing before any '<' on that line. As soon
                // as we hit a line that doesn't fit that shape (blank line, opening tag, or
                // body text), the header block is over and the rest is the actual content.
                var line = html.AsSpan(idx, lineEnd - idx);
                var colon = line.IndexOf(':');
                var lt = line.IndexOf('<');
                if (colon > 0 && (lt < 0 || lt > colon))
                {
                    idx = lineEnd + 1;
                    continue;
                }
                break;
            }
            html = html[idx..];
        }

        var fragStart = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        if (fragStart >= 0) html = html[(fragStart + "<!--StartFragment-->".Length)..];
        var fragEnd = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (fragEnd >= 0) html = html[..fragEnd];

        // Block-level tags get converted to newlines BEFORE the generic tag-strip so the
        // resulting plaintext keeps line structure. Without this, copying an ASCII-art block
        // from a web page (or any paragraph-heavy snippet) collapses to a single line because
        // the previous strip-then-whitespace-collapse step erased every \n.
        // Order matters: <br> first, then closing block tags, then opening block tags. We
        // insert one '\n' for closing tags and leave the rest in place — the strip pass below
        // removes the tag itself, leaving the newline as a real plaintext break.
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|tr|li|h[1-6]|pre|blockquote|article|section|header|footer|nav|aside|table|thead|tbody|tfoot|ul|ol|dl|dt|dd|figure|figcaption)\s*>", "\n", RegexOptions.IgnoreCase);

        // Now strip every remaining tag with empty replacement (NOT space) so inline tags
        // like <span> / <strong> don't insert spurious gaps between adjacent characters in
        // pre-formatted blocks.
        html = Regex.Replace(html, "<[^>]+>", string.Empty);
        html = System.Net.WebUtility.HtmlDecode(html);
        // Normalise CRLF / lone CR to LF so downstream consumers see a single line ending.
        // Don't collapse \s+ — that would destroy ASCII-art alignment (consecutive spaces are
        // meaningful inside <pre> blocks). Only fold runs of 3+ newlines so a paragraph-soup
        // page doesn't paste with comically tall vertical gaps; up to 2 consecutive newlines
        // (the standard "blank line between paragraphs" rhythm) survive unchanged.
        html = html.Replace("\r\n", "\n").Replace('\r', '\n');
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        return html.Trim();
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
