using System;
using System.IO;
using System.Windows;
using BoltDownloader.Services;
using Localization = BoltDownloader.Services.Localization;

namespace BoltDownloader.Views
{
    public partial class AddDownloadDialog : Window
    {
        private readonly ConfigurationService _configService;

        public string DownloadUrl { get; private set; } = "";
        public string FileName { get; private set; } = "";
        public string SavePath { get; private set; } = "";
        public bool StartImmediately { get; private set; }
        public string? Referrer { get; private set; }
        public string? Cookies { get; private set; }
        public string? OverrideUserAgent { get; private set; }
        public System.Collections.Generic.Dictionary<string, string> CustomHeaders { get; private set; } = new();
        public long PerDownloadSpeedLimitKBps { get; private set; } = 0;
        public string? BasicAuthUser { get; private set; }
        public string? BasicAuthPassword { get; private set; }
        public int SegmentsOverride { get; private set; } = 0;

        public AddDownloadDialog(ConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;

            // Establecer ruta de descarga predeterminada
            txtSavePath.Text = _configService.DefaultDownloadPath;
            
            // Verificar si hay una URL en el portapapeles
            if (Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                if (Uri.TryCreate(clipboardText, UriKind.Absolute, out var uri))
                {
                    txtUrl.Text = clipboardText;
                }
            }
        }

        private void txtUrl_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Intentar extraer el nombre del archivo de la URL
            if (Uri.TryCreate(txtUrl.Text, UriKind.Absolute, out var uri))
            {
                var fileName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    txtFileName.Text = fileName;
                    txtInfo.Text = "URL válida detectada";
                    txtInfo.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            else if (!string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                txtInfo.Text = "URL no válida";
                txtInfo.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                txtInfo.Text = "";
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Seleccione la carpeta de destino",
                SelectedPath = txtSavePath.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtSavePath.Text = dialog.SelectedPath;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Validar campos
            if (string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                Localization.Show("Validation_EnterValidUrl", "Title_Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Uri.TryCreate(txtUrl.Text, UriKind.Absolute, out _))
            {
                Localization.Show("Validation_InvalidUrl", "Title_Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtFileName.Text))
            {
                Localization.Show("Validation_EnterFileName", "Title_Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtSavePath.Text))
            {
                Localization.Show("Validation_SelectFolder", "Title_Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DownloadUrl = txtUrl.Text.Trim();
            FileName = txtFileName.Text.Trim();
            SavePath = txtSavePath.Text.Trim();
            StartImmediately = chkStartImmediately.IsChecked ?? false;

            // Cabeceras HTTP opcionales
            Referrer = string.IsNullOrWhiteSpace(txtReferrer.Text) ? null : txtReferrer.Text.Trim();
            Cookies = string.IsNullOrWhiteSpace(txtCookies.Text) ? null : txtCookies.Text.Trim();
            OverrideUserAgent = string.IsNullOrWhiteSpace(txtUserAgent.Text) ? null : txtUserAgent.Text.Trim();

            CustomHeaders.Clear();
            if (!string.IsNullOrWhiteSpace(txtCustomHeaders.Text))
            {
                var lines = txtCustomHeaders.Text.Split(new[] {"\r\n", "\n"}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var name = line.Substring(0, idx).Trim();
                        var value = line.Substring(idx + 1).Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            CustomHeaders[name] = value;
                        }
                    }
                }
            }

            if (long.TryParse(txtPerLimit.Text?.Trim(), out var perKbps) && perKbps >= 0)
            {
                PerDownloadSpeedLimitKBps = perKbps;
            }
            else
            {
                PerDownloadSpeedLimitKBps = 0; // sin límite si inválido
            }

            BasicAuthUser = string.IsNullOrWhiteSpace(txtAuthUser?.Text) ? null : txtAuthUser.Text.Trim();
            var rawPwd = txtAuthPass?.Password;
            BasicAuthPassword = string.IsNullOrWhiteSpace(rawPwd) ? null : rawPwd!.Trim();

            if (int.TryParse(txtSegmentsOverride.Text?.Trim(), out var segs) && segs >= 0)
            {
                SegmentsOverride = segs;
            }
            else
            {
                SegmentsOverride = 0; // usar global
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PasteFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                var clip = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(clip))
                {
                    txtUrl.Text = clip;
                }
            }
            else
            {
                Localization.Show("Info_ClipboardEmpty", "Title_Clipboard",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
