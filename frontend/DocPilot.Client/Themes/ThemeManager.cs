using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DocPilot.Client.Themes
{
    /// Svetla i tamna tema 
    
    public static class ThemeManager
    {
        static readonly string PreferenceFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocPilot", "theme.txt");

        public static bool IsDark { get; private set; }

        public static event EventHandler ThemeChanged;

        /// Primenjuje sačuvanu temu, odnosno temu Windows-a; poziva se pri pokretanju.
        public static void ApplySaved() => Apply(LoadPreference(), save: false);

        public static void Toggle() => Apply(!IsDark, save: true);

        static void Apply(bool dark, bool save)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var current = dictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.StartsWith("Themes/"));
            var next = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
            if (current != null)
                dictionaries[dictionaries.IndexOf(current)] = next;
            else
                dictionaries.Insert(0, next);

            IsDark = dark;
            foreach (Window window in Application.Current.Windows)
                ApplyTitleBar(window);
            if (save)
                SavePreference(dark);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        static bool LoadPreference()
        {
            try
            {
                if (File.Exists(PreferenceFile))
                    return File.ReadAllText(PreferenceFile).Trim() == "dark";
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception)
            {
                return false;   // tema nije bitna toliko da bi aplikacija zbog nje pala
            }
        }

        static void SavePreference(bool dark)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferenceFile));
                File.WriteAllText(PreferenceFile, dark ? "dark" : "light");
            }
            catch (Exception) { }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// Naslovna linija prozora (iscrtava je Windows) u boji teme; na starijim Windows-ima se preskače.
        public static void ApplyTitleBar(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var value = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }
}
