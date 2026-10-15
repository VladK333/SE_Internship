// Istrazivanje Markdown biblioteka: isti test.md se prikazuje u svakoj biblioteci,
// pravi se screenshot celog sadrzaja i meri vreme prikaza dugog odgovora.
//
// Pokretanje (iz SE_praksa):
//   dotnet run --project frontend/MarkdownResearch -- docs/research/test.md docs/research/screenshots
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Microsoft.Web.WebView2.Wpf;

namespace MarkdownResearch
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += async (s, e) =>
            {
                try { await Runner.RunAsync(args[0], args[1]); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                app.Shutdown();
            };
            app.Run();
        }
    }

    /// Dozvoljeni izvori slika (guardrail 6: samo pouzdani HTTPS izvori dokumentacije).
    public static class TrustedImages
    {
        public static readonly string[] Hosts = { "powsybl.readthedocs.io", "www.powsybl.org" };

        public static bool IsTrusted(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    public interface IRenderer
    {
        string Name { get; }
        /// Prikazuje markdown i vraca vreme (ms) koliko je UI nit bila blokirana.
        Task<double> RenderAsync(string markdown, Window window);
        Task SaveScreenshotAsync(string path);
    }

    /// Zajednicko za biblioteke koje prave WPF FlowDocument.
    public abstract class FlowDocumentRenderer : IRenderer
    {
        protected FrameworkElement Root;
        public abstract string Name { get; }
        protected abstract FrameworkElement Build(string markdown);

        public Task<double> RenderAsync(string markdown, Window window)
        {
            var sw = Stopwatch.StartNew();
            Root = Build(markdown);
            Root.Width = 900;
            Root.Height = 800;
            var canvas = new Canvas();          // Canvas ne ogranicava velicinu, pa moze da se uslika ceo sadrzaj
            canvas.Children.Add(Root);
            window.Content = canvas;
            window.UpdateLayout();
            sw.Stop();
            return Task.FromResult(sw.Elapsed.TotalMilliseconds);
        }

        public async Task SaveScreenshotAsync(string path)
        {
            // visina = visina celog sadrzaja (unutrasnji ScrollViewer), ponavlja se jer se slike ucitavaju naknadno
            for (int i = 0; i < 3; i++)
            {
                Root.UpdateLayout();
                var sv = Descendants<ScrollViewer>(Root).OrderByDescending(s => s.ExtentHeight).FirstOrDefault();
                if (sv != null && sv.ExtentHeight > sv.ViewportHeight)
                    Root.Height += sv.ExtentHeight - sv.ViewportHeight + 20;
                await Task.Delay(300);
            }
            Root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            bmp.Render(bg);
            bmp.Render(Root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(path)) enc.Save(f);
        }

        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var d in Descendants<T>(child)) yield return d;
            }
        }
    }

    public class MdXamlRenderer : FlowDocumentRenderer
    {
        public override string Name => "MdXaml";
        protected override FrameworkElement Build(string markdown)
        {
            var plugins = new MdXaml.Plugins.MdXamlPlugins();
            plugins.Setups.Add(new MdXaml.Svg.SvgPluginSetup());
            var viewer = new MdXaml.MarkdownScrollViewer
            {
                Plugins = plugins,                  // podrazumevani stil; GithubLike gubi stil naslova
            };
            viewer.Engine.DisabledTag = true;       // HTML tagovi se ne tumace
            viewer.Markdown = markdown;
            return viewer;
        }
    }

    public class MarkdigWpfRenderer : FlowDocumentRenderer
    {
        public override string Name => "Markdig.Wpf";
        protected override FrameworkElement Build(string markdown) =>
            new Markdig.Wpf.MarkdownViewer
            {
                Pipeline = Markdig.Wpf.MarkdownExtensions.UseSupportedExtensions(new MarkdownPipelineBuilder()).DisableHtml().Build(),
                Markdown = markdown,
            };
    }

    public class NeoMarkdigRenderer : FlowDocumentRenderer
    {
        public override string Name => "Neo.Markdig.Xaml";
        protected override FrameworkElement Build(string markdown)
        {
            var pipeline = Neo.Markdig.Xaml.MarkdownXaml.UseXamlSupportedExtensions(new MarkdownPipelineBuilder()).DisableHtml().Build();
            return new FlowDocumentScrollViewer { Document = Neo.Markdig.Xaml.MarkdownXaml.ToFlowDocument(markdown, pipeline, null) };
        }
    }

    public class WebView2Renderer : IRenderer
    {
        WebView2 view;
        public string Name => "Markdig + WebView2";

        // CSP: nema skripti osim highlight.js, slike samo sa pouzdanih HTTPS izvora
        static string Page(string body) => $@"<!doctype html><html><head><meta charset='utf-8'>
<meta http-equiv='Content-Security-Policy' content=""default-src 'none'; img-src {string.Join(" ", TrustedImages.Hosts.Select(h => "https://" + h))}; style-src 'unsafe-inline' https://cdnjs.cloudflare.com; script-src https://cdnjs.cloudflare.com 'unsafe-inline'"">
<link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github.min.css'>
<script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js'></script>
<style>
html {{ background: #fff; }}
body {{ font-family: 'Segoe UI', sans-serif; font-size: 14px; line-height: 1.5; color: #1f2328; margin: 16px; }}
h1, h2 {{ border-bottom: 1px solid #d1d9e0; padding-bottom: .3em; }}
code {{ font-family: Consolas, monospace; background: #f6f8fa; padding: .1em .3em; border-radius: 4px; }}
pre code {{ display: block; padding: 12px; overflow-x: auto; }}
.table-wrap {{ overflow-x: auto; }}
table {{ border-collapse: collapse; margin: 8px 0; }}
th, td {{ border: 1px solid #d1d9e0; padding: 4px 10px; }}
th {{ background: #f6f8fa; }}
img {{ max-width: 100%; }}
blockquote {{ margin: 0; padding: 0 1em; color: #59636e; border-left: 4px solid #d1d9e0; }}
</style></head><body>{body}<script>
document.querySelectorAll('table').forEach(t => {{ const w = document.createElement('div'); w.className = 'table-wrap'; t.replaceWith(w); w.appendChild(t); }});
hljs.highlightAll();</script></body></html>";

        public async Task<double> RenderAsync(string markdown, Window window)
        {
            if (view == null)
            {
                view = new WebView2 { Width = 900, Height = 800 };
                window.Content = view;
                await view.EnsureCoreWebView2Async();
            }
            var sw = Stopwatch.StartNew();
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            var html = Page(Markdown.ToHtml(markdown, pipeline));
            var done = new TaskCompletionSource<bool>();
            void OnDone(object s, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(true);
            view.NavigationCompleted += OnDone;
            view.NavigateToString(html);
            var blocked = sw.Elapsed.TotalMilliseconds;   // posle ovoga UI nit je slobodna, renderuje Edge proces
            await done.Task;
            view.NavigationCompleted -= OnDone;
            LastTotalMs = sw.Elapsed.TotalMilliseconds;
            return blocked;
        }

        public double LastTotalMs { get; private set; }

        public async Task SaveScreenshotAsync(string path)
        {
            var height = await view.ExecuteScriptAsync("document.documentElement.scrollHeight");
            var json = "{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{\"x\":0,\"y\":0,\"width\":900,\"height\":" + height + ",\"scale\":1}}";
            var result = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", json);
            var data = result.Split(new[] { "\"data\":\"" }, StringSplitOptions.None)[1].TrimEnd('}', '"');
            File.WriteAllBytes(path, Convert.FromBase64String(data));
        }
    }

    public static class Runner
    {
        public static async Task RunAsync(string mdPath, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var markdown = File.ReadAllText(mdPath, Encoding.UTF8);
            // dug odgovor: test bez slika ponovljen 8 puta (~ 2.800 reci, 24 kod bloka, 16 tabela)
            var noImages = string.Join("\n", markdown.Split('\n').Where(l => !l.TrimStart().StartsWith("![") && !l.Contains("<img")));
            var longMarkdown = string.Join("\n\n", Enumerable.Repeat(noImages, 8));

            var renderers = new IRenderer[] { new MdXamlRenderer(), new MarkdigWpfRenderer(), new NeoMarkdigRenderer(), new WebView2Renderer() };
            var report = new StringBuilder("library;screenshot;ui_blocked_ms_long_answer (median of 5);total_ms_long_answer;error\n");

            foreach (var r in renderers)
            {
                var window = new Window { Title = r.Name, Width = 940, Height = 860, Left = 0, Top = 0, ShowActivated = false };
                window.Show();
                string shot = "", error = "";
                var times = new List<double>(); var totals = new List<double>();
                try
                {
                    await r.RenderAsync(markdown, window);
                    await Task.Delay(6000);                       // slike se ucitavaju asinhrono
                    shot = Path.Combine(outDir, Slug(r.Name) + ".png");
                    await r.SaveScreenshotAsync(shot);

                    for (int i = 0; i < 5; i++)
                    {
                        times.Add(await r.RenderAsync(longMarkdown, window));
                        totals.Add(r is WebView2Renderer w ? w.LastTotalMs : times.Last());
                        await Task.Delay(500);
                    }
                }
                catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message.Replace(';', ',').Replace('\n', ' '); }
                window.Close();
                report.AppendLine($"{r.Name};{Path.GetFileName(shot)};{Median(times):F0};{Median(totals):F0};{error}");
                Console.WriteLine($"{r.Name}: blocked {Median(times):F0} ms, total {Median(totals):F0} ms {error}");
            }
            File.WriteAllText(Path.Combine(outDir, "results.csv"), report.ToString());
        }

        static double Median(List<double> xs) => xs.Count == 0 ? double.NaN : xs.OrderBy(x => x).ElementAt(xs.Count / 2);
        static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Replace("--", "-").Trim('-');
    }
}
