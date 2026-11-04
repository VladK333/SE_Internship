using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocPilot.Client.Models;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocPilot.Client.Rendering
{
    /// Pretvara Markdown odgovor u kompletnu, bezbednu HTML stranicu za WebView2 (guardrail 6).

    public static class MarkdownRenderer
    {
        /// Virtuelni host pod kojim WebView2 vidi folder Assets (SetVirtualHostNameToFolderMapping).
        public const string AssetsHost = "docpilot.assets";

        /// Izvori sa kojih se slike smeju učitati: zvanična PowSyBl dokumentacija.
        public static readonly IReadOnlyList<string> TrustedImageHosts = new[] { "powsybl.readthedocs.io", "www.powsybl.org" };

        // namerno bez UseAdvancedExtensions(): ono uključuje i GenericAttributes ({onclick=...}),
        // kojim bi tekst odgovora mogao da doda proizvoljne HTML atribute
        static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()
            .UseListExtras()
            .UseTaskLists()
            .UseAutoLinks()
            .DisableHtml()
            .Build();

        // kod (``` blokovi i `inline`) se ne dira pri obeležavanju citata
        static readonly Regex CodePattern = new Regex(@"(```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\n]*`)", RegexOptions.Compiled);
        static readonly Regex CitationPattern = new Regex(@"\[(\d{1,2})\](?!\()", RegexOptions.Compiled);
        static readonly Regex CitationText = new Regex(@"^\[\d{1,2}\]$", RegexOptions.Compiled);

        /// Kompletna HTML stranica za prikaz jednog odgovora; dark = tamna tema (klasa "dark" na &lt;html&gt;).
        public static string RenderPage(string markdown, IReadOnlyList<Source> sources = null, bool dark = false) =>
            WrapInPage(RenderBody(markdown, sources), dark);

        /// Samo HTML sadržaj (bez &lt;html&gt;, &lt;head&gt;); koristi se i u testovima.
        public static string RenderBody(string markdown, IReadOnlyList<Source> sources = null)
        {
            var text = LinkCitations(markdown ?? "", sources ?? Array.Empty<Source>());
            var document = Markdown.Parse(text, Pipeline);
            SanitizeLinks(document);

            using (var writer = new StringWriter())
            {
                var renderer = new HtmlRenderer(writer);
                Pipeline.Setup(renderer);
                renderer.Render(document);
                writer.Flush();
                // široke tabele dobijaju horizontalni skrol umesto da se lome
                return writer.ToString()
                    .Replace("<table>", "<div class=\"table-wrap\"><table>")
                    .Replace("</table>", "</table></div>");
            }
        }

        /// [1], [2]... u tekstu postaju linkovi ka odgovarajućim izvorima (osim u kodu).
        static string LinkCitations(string markdown, IReadOnlyList<Source> sources)
        {
            if (sources.Count == 0)
                return markdown;
            var parts = CodePattern.Split(markdown);
            for (int i = 0; i < parts.Length; i += 2)   // parni delovi su tekst, neparni kod
            {
                parts[i] = CitationPattern.Replace(parts[i], m =>
                {
                    var n = int.Parse(m.Groups[1].Value);
                    if (n < 1 || n > sources.Count)
                        return m.Value;
                    var s = sources[n - 1];
                    var title = (s.HeadingPath ?? s.Title ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                    return $"[\\[{n}\\]]({s.Url} \"{title}\")";
                });
            }
            return string.Concat(parts);
        }

        static void SanitizeLinks(MarkdownDocument document)
        {
            foreach (var link in document.Descendants<LinkInline>().ToList())
            {
                if (link.IsImage)
                {
                    if (!IsTrustedImage(link.Url))
                        link.ReplaceBy(new HtmlInline(
                            $"<span class=\"blocked\">[image not shown: untrusted source – {WebUtility.HtmlEncode(link.Url ?? "")}]</span>"), copyChildren: false);
                }
                else if (!IsHttps(link.Url))
                {
                    link.ReplaceBy(new LiteralInline(PlainText(link)), copyChildren: false);
                }
                else if (CitationText.IsMatch(PlainText(link)))
                {
                    link.GetAttributes().AddClass("citation");
                }
            }
        }

        public static bool IsTrustedImage(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && TrustedImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

        public static bool IsHttps(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        static string PlainText(ContainerInline container)
        {
            var sb = new StringBuilder();
            foreach (var inline in container.Descendants<LiteralInline>())
                sb.Append(inline.Content.ToString());
            return sb.ToString();
        }

        static string WrapInPage(string body, bool dark)
        {
            var htmlClass = dark ? @" class=""dark""" : "";
            var assets = "https://" + AssetsHost;
            var images = string.Join(" ", TrustedImageHosts.Select(h => "https://" + h));
            // 'unsafe-inline' za stilove je potreban jer Markdig poravnanje kolona u tabelama piše kao style atribut
            var csp = $"default-src 'none'; img-src {images}; style-src {assets} 'unsafe-inline'; script-src {assets}";
            return $@"<!doctype html>
<html lang=""en""{htmlClass}>
<head>
<meta charset=""utf-8"">
<meta http-equiv=""Content-Security-Policy"" content=""{csp}"">
<link rel=""stylesheet"" href=""{assets}/highlight/github.min.css"">
<link rel=""stylesheet"" href=""{assets}/docpilot.css"">
<script src=""{assets}/highlight/highlight.min.js""></script>
<script src=""{assets}/docpilot.js""></script>
</head>
<body>
{body}
</body>
</html>";
        }
    }
}
