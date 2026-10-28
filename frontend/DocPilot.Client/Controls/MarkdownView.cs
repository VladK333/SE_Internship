using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocPilot.Client.Models;
using DocPilot.Client.Rendering;
using DocPilot.Client.Themes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DocPilot.Client.Controls
{
    /// Prikazuje Markdown odgovor kao HTML stranicu u WebView2.
    ///
    /// Koristi WebView2CompositionControl (iscrtava se kroz WPF), pa radi unutar liste poruka
    /// i ScrollViewer-a bez "airspace" problema običnog WebView2 prozora. Sve instance dele jedno
    /// WebView2 okruženje, odnosno jedan Edge proces.
    public class MarkdownView : ContentControl
    {
        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata(null, OnContentChanged));

        public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
            nameof(Sources), typeof(IReadOnlyList<Source>), typeof(MarkdownView), new PropertyMetadata(null, OnContentChanged));

        static Task<CoreWebView2Environment> sharedEnvironment;

        readonly WebView2CompositionControl web;
        Task initialization;
        bool pageLoaded, rendering, renderAgain;

        public MarkdownView()
        {
            web = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            Content = web;
            Height = 24;   // prava visina stiže iz stranice (poruka "height")
            Loaded += async (s, e) =>
            {
                ThemeManager.ThemeChanged += OnThemeChanged;
                await RenderAsync();
            };
            Unloaded += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        /// Promena teme bez ponovnog iscrtavanja: samo klasa "dark" na &lt;html&gt; (boje su u docpilot.css).
        async void OnThemeChanged(object sender, EventArgs e)
        {
            if (web.CoreWebView2 == null)
                return;
            var dark = ThemeManager.IsDark ? "true" : "false";
            await web.CoreWebView2.ExecuteScriptAsync($"document.documentElement.classList.toggle('dark', {dark});");
        }

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public IReadOnlyList<Source> Sources
        {
            get => (IReadOnlyList<Source>)GetValue(SourcesProperty);
            set => SetValue(SourcesProperty, value);
        }

        /// WebView2 nema sopstvenu širinu sadržaja: odgovor zauzima svu dostupnu širinu
        /// (balončić je ograničava sa MaxWidth), a visinu određuje stranica.
        protected override Size MeasureOverride(Size constraint)
        {
            var content = base.MeasureOverride(constraint);
            var width = double.IsInfinity(constraint.Width) ? 600 : constraint.Width;
            // bez zadate visine (rezervni prikaz kao običan tekst) visinu određuje sadržaj
            return new Size(width, double.IsNaN(Height) ? content.Height : Height);
        }

        static async void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (MarkdownView)d;
            if (view.IsLoaded)
                await view.RenderAsync();
        }

        /// Prvi prikaz učitava celu stranicu; svaka sledeća promena (delovi odgovora tokom strima) samo
        /// zamenjuje sadržaj kroz docpilot.js, bez treptanja. Promene koje stignu dok prikaz traje
        /// spajaju se u jedno osvežavanje, pa brzi delovi ne prave red čekanja.
        async Task RenderAsync()
        {
            if (rendering)
            {
                renderAgain = true;
                return;
            }
            rendering = true;
            try
            {
                await (initialization ?? (initialization = InitializeAsync()));
                do
                {
                    renderAgain = false;
                    if (pageLoaded)
                    {
                        var body = JsonSerializer.Serialize(MarkdownRenderer.RenderBody(Markdown, Sources));
                        await web.CoreWebView2.ExecuteScriptAsync($"window.docpilotSetBody({body});");
                    }
                    else
                    {
                        await LoadPageAsync(MarkdownRenderer.RenderPage(Markdown, Sources, ThemeManager.IsDark));
                        pageLoaded = true;
                    }
                } while (renderAgain);
            }
            catch (Exception ex)
            {
                // npr. WebView2 Runtime nije instaliran: prikaži bar običan tekst
                Content = new TextBox
                {
                    Text = Markdown + "\n\n[Prikaz nije dostupan: " + ex.Message + "]",
                    IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                };
                Height = double.NaN;
            }
            finally
            {
                rendering = false;
            }
        }

        Task LoadPageAsync(string html)
        {
            var loaded = new TaskCompletionSource<bool>();
            void OnCompleted(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                web.CoreWebView2.NavigationCompleted -= OnCompleted;
                loaded.TrySetResult(e.IsSuccess);
            }
            web.CoreWebView2.NavigationCompleted += OnCompleted;
            web.NavigateToString(html);
            return loaded.Task;
        }

        async Task InitializeAsync()
        {
            var env = await (sharedEnvironment ?? (sharedEnvironment = CreateEnvironmentAsync()));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Assets folder (CSS, JS, highlight.js) dostupan kao https://docpilot.assets/
            var assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            core.SetVirtualHostNameToFolderMapping(MarkdownRenderer.AssetsHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);

            // balončić nikad ne prelazi na drugu stranicu; linkovi idu kroz poruke iz docpilot.js
            core.NavigationStarting += (s, e) =>
            {
                if (!e.Uri.StartsWith("data:", StringComparison.Ordinal) && e.Uri != "about:blank")
                {
                    e.Cancel = true;
                    OpenExternal(e.Uri);
                }
            };
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                OpenExternal(e.Uri);
            };
            core.WebMessageReceived += OnWebMessage;
        }

        static Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            // podaci WebView2 u korisničkom profilu, ne pored .exe fajla (Program Files nije upisiv)
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DocPilot", "WebView2");
            return CoreWebView2Environment.CreateAsync(null, userData);
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            using (var doc = JsonDocument.Parse(e.WebMessageAsJson))
            {
                var root = doc.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "height":
                        Height = Math.Max(20, root.GetProperty("value").GetDouble());
                        break;
                    case "link":
                        OpenExternal(root.GetProperty("href").GetString());
                        break;
                    case "wheel":
                        // točkić miša iznad odgovora skroluje listu poruka, ne samu stranicu
                        ScrollParent(root.GetProperty("deltaY").GetDouble());
                        break;
                }
            }
        }

        void ScrollParent(double deltaY)
        {
            DependencyObject parent = this;
            while (parent != null && !(parent is ScrollViewer))
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is ScrollViewer sv)
                sv.ScrollToVerticalOffset(sv.VerticalOffset + deltaY);
        }

        /// Otvara samo HTTPS linkove, u podrazumevanom browseru.
        static void OpenExternal(string url)
        {
            if (MarkdownRenderer.IsHttps(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
