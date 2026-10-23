using DocPilot.Client.Models;

namespace DocPilot.Client.ViewModels
{
    /// Izvor ispod odgovora, sa brojem koji odgovara citatu [1], [2]... u tekstu odgovora.
    public class SourceLinkViewModel
    {
        public SourceLinkViewModel(int number, Source source)
        {
            Number = number;
            Title = source.Title;
            HeadingPath = source.HeadingPath;
            Url = source.Url;
        }

        public int Number { get; }
        public string Title { get; }
        public string HeadingPath { get; }
        public string Url { get; }

        public string Label => $"[{Number}] {Title}";
    }
}
