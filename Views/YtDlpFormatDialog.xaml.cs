using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BoltDownloader.Services;

namespace BoltDownloader.Views
{
    public partial class YtDlpFormatDialog : Window
    {
        private readonly string _pageUrl;
        public ObservableCollection<YtDlpService.YtFormat> Formats { get; } = new();
        public YtDlpService.YtFormat? SelectedFormat { get; set; }
        public string? SelectedFormatId => SelectedFormat?.FormatId;

        public YtDlpFormatDialog(string pageUrl)
        {
            InitializeComponent();
            _pageUrl = pageUrl;
            DataContext = this;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            txtInfo.Text = $"Loading formats for: {_pageUrl}";
            try
            {
                var list = await YtDlpService.ListFormatsAsync(_pageUrl);
                Formats.Clear();
                foreach (var f in list.OrderByDescending(f => f.Resolution).ThenBy(f => f.Ext))
                {
                    Formats.Add(f);
                }
                txtInfo.Text = list.Count > 0 ? $"Select a format ({list.Count} found)" : "No formats found.";
                if (list.Count > 0)
                {
                    lvFormats.SelectedIndex = 0;
                    lvFormats.Focus();
                }
            }
            catch (Exception ex)
            {
                txtInfo.Text = "Failed to list formats.";
                try { Logger.Error("YtDlpFormatDialog: ListFormats failed", ex); } catch { }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (lvFormats.SelectedItem is YtDlpService.YtFormat fmt)
            {
                SelectedFormat = fmt;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }
    }
}
