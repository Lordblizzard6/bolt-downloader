using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BoltDownloader.Views
{
    public partial class BatchDownloadDialog : Window
    {
        public List<string> Urls { get; private set; } = new List<string>();

        public BatchDownloadDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var lines = txtUrls.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var validUrls = new List<string>();

            foreach (var line in lines)
            {
                var url = line.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    validUrls.Add(url);
                }
            }

            if (validUrls.Count == 0)
            {
                BoltDownloader.Services.Localization.Show("Validation_NoValidUrls", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Urls = validUrls;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
