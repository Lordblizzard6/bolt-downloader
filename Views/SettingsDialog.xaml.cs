using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BoltDownloader.Services;
using Localization = BoltDownloader.Services.Localization;

namespace BoltDownloader.Views
{
    public partial class SettingsDialog : Window
    {
        private readonly ConfigurationService _configService;

        public SettingsDialog(ConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;

            LoadSettings();
        }

        private void LoadSettings()
        {
            txtMaxSegments.Text = _configService.MaxSegments.ToString();
            txtMaxConcurrent.Text = _configService.MaxConcurrentDownloads.ToString();
            txtTimeout.Text = _configService.ConnectionTimeout.ToString();
            txtMaxRetries.Text = _configService.MaxRetries.ToString();

            txtDefaultPath.Text = _configService.DefaultDownloadPath;
            txtTempPath.Text = _configService.TempDownloadPath;

            chkUseProxy.IsChecked = _configService.UseProxy;
            txtProxyAddress.Text = _configService.ProxyAddress;
            txtProxyPort.Text = _configService.ProxyPort.ToString();
            txtProxyUsername.Text = _configService.ProxyUsername;
            txtProxyPassword.Password = _configService.ProxyPassword;

            chkMonitorClipboard.IsChecked = _configService.MonitorClipboard;
            txtUserAgent.Text = _configService.UserAgent;

            // Bandeja y nombres de archivo
            try
            {
                chkCloseToTray.IsChecked = _configService.CloseToTray;
                chkTrayBalloon.IsChecked = _configService.ShowTrayBalloonOnMinimize;
                chkSlugifyFileNames.IsChecked = _configService.SlugifyFileNames;
            }
            catch { }

            // Seleccionar idioma actual
            var currentLang = _configService.Language?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(currentLang)) currentLang = "es";
            foreach (var obj in cmbLanguage.Items)
            {
                if (obj is ComboBoxItem cbi)
                {
                    var tag = cbi.Tag?.ToString()?.Trim().ToLowerInvariant();
                    if (tag == currentLang)
                    {
                        cmbLanguage.SelectedItem = cbi;
                        break;
                    }
                }
            }

            // Seleccionar tema actual
            var currentTheme = _configService.Theme?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(currentTheme)) currentTheme = "light";
            foreach (var obj in cmbTheme.Items)
            {
                if (obj is ComboBoxItem cbi)
                {
                    var tag = cbi.Tag?.ToString()?.Trim().ToLowerInvariant();
                    if (tag == currentTheme)
                    {
                        cmbTheme.SelectedItem = cbi;
                        break;
                    }
                }
            }

            // Tipos de archivos (unir por coma)
            try
            {
                var types = _configService.FileExtensionsToMonitor ?? new System.Collections.Generic.List<string>();
                txtFileTypes.Text = string.Join(", ", types);
            }
            catch { txtFileTypes.Text = ".zip, .rar, .7z, .pdf"; }

            // yt-dlp
            try
            {
                chkYtDlpEnabled.IsChecked = _configService.YtDlpEnabled;
                chkYtDlpAskFormat.IsChecked = _configService.YtDlpAskFormat;
                chkYtDlpAudioOnly.IsChecked = _configService.YtDlpAudioOnly;
                txtYtDlpFormatSelector.Text = _configService.YtDlpFormatSelector ?? "bv*+ba/best";
                chkYtDlpSubtitles.IsChecked = _configService.YtDlpDownloadSubtitles;
                txtYtDlpSubtitleLangs.Text = _configService.YtDlpSubtitleLangs ?? "en,es";
                chkYtDlpMetadata.IsChecked = _configService.YtDlpSaveMetadata;
            }
            catch { }
        }

        private void chkUseProxy_CheckedChanged(object sender, RoutedEventArgs e)
        {
            panelProxy.IsEnabled = chkUseProxy.IsChecked ?? false;
        }

        private void BrowseDefaultPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Localization.L("Settings_BrowseDefaultFolder"),
                SelectedPath = txtDefaultPath.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDefaultPath.Text = dialog.SelectedPath;
            }
        }

        private void BrowseTempPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Localization.L("Settings_BrowseTempFolder"),
                SelectedPath = txtTempPath.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtTempPath.Text = dialog.SelectedPath;
            }
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Localization.L("Confirm_ResetDefaults"),
                Localization.L("Title_Confirm"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _configService.ResetToDefaults();
                LoadSettings();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Validar y guardar configuración
            if (!int.TryParse(txtMaxSegments.Text, out int maxSegments) || maxSegments < 1 || maxSegments > 16)
            {
                Localization.Show("Validation_MaxSegmentsRange", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtMaxConcurrent.Text, out int maxConcurrent) || maxConcurrent < 1 || maxConcurrent > 10)
            {
                Localization.Show("Validation_MaxConcurrentRange", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtTimeout.Text, out int timeout) || timeout < 10)
            {
                Localization.Show("Validation_TimeoutMin", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtMaxRetries.Text, out int maxRetries) || maxRetries < 0)
            {
                Localization.Show("Validation_RetriesPositive", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Guardar configuración
            _configService.MaxSegments = maxSegments;
            _configService.MaxConcurrentDownloads = maxConcurrent;
            _configService.ConnectionTimeout = timeout;
            _configService.MaxRetries = maxRetries;

            _configService.DefaultDownloadPath = txtDefaultPath.Text;
            _configService.TempDownloadPath = txtTempPath.Text;

            _configService.UseProxy = chkUseProxy.IsChecked ?? false;
            _configService.ProxyAddress = txtProxyAddress.Text;
            
            if (int.TryParse(txtProxyPort.Text, out int proxyPort))
            {
                _configService.ProxyPort = proxyPort;
            }
            
            _configService.ProxyUsername = txtProxyUsername.Text;
            _configService.ProxyPassword = txtProxyPassword.Password;

            _configService.MonitorClipboard = chkMonitorClipboard.IsChecked ?? false;
            _configService.UserAgent = txtUserAgent.Text;

            // Bandeja y nombres de archivo
            try
            {
                _configService.CloseToTray = chkCloseToTray.IsChecked ?? false;
                _configService.ShowTrayBalloonOnMinimize = chkTrayBalloon.IsChecked ?? true;
                _configService.SlugifyFileNames = chkSlugifyFileNames.IsChecked ?? false;
            }
            catch { }

            // Guardar idioma
            var selectedLang = "es";
            if (cmbLanguage.SelectedItem is ComboBoxItem sel && sel.Tag != null)
            {
                selectedLang = sel.Tag.ToString() ?? "es";
            }
            _configService.Language = selectedLang;
            try { App.ApplyLanguage(selectedLang); } catch { }

            // Guardar tema
            var selectedTheme = "light";
            if (cmbTheme.SelectedItem is ComboBoxItem selTheme && selTheme.Tag != null)
            {
                selectedTheme = selTheme.Tag.ToString() ?? "light";
            }
            _configService.Theme = selectedTheme;
            try { App.ApplyTheme(selectedTheme); } catch { }

            // Guardar tipos de archivos
            try
            {
                var raw = txtFileTypes.Text ?? string.Empty;
                var parts = raw.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim().ToLowerInvariant())
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Select(s => s.StartsWith('.') ? s : "." + s)
                               .Distinct()
                               .ToList();
                if (parts.Count == 0)
                {
                    parts = new System.Collections.Generic.List<string> { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".mp3", ".aac", ".flac", ".wav", ".m3u8", ".zip", ".rar", ".7z", ".pdf" };
                }
                _configService.FileExtensionsToMonitor = parts;
                try { CaptureServer.UpdateFileTypes(parts); } catch { }
            }
            catch { }

            // Guardar yt-dlp
            try
            {
                _configService.YtDlpEnabled = chkYtDlpEnabled.IsChecked ?? true;
                _configService.YtDlpAskFormat = chkYtDlpAskFormat.IsChecked ?? false;
                _configService.YtDlpAudioOnly = chkYtDlpAudioOnly.IsChecked ?? false;
                _configService.YtDlpFormatSelector = txtYtDlpFormatSelector.Text ?? "bv*+ba/best";
                _configService.YtDlpDownloadSubtitles = chkYtDlpSubtitles.IsChecked ?? false;
                _configService.YtDlpSubtitleLangs = txtYtDlpSubtitleLangs.Text ?? "en,es";
                _configService.YtDlpSaveMetadata = chkYtDlpMetadata.IsChecked ?? false;
            }
            catch { }

            DialogResult = true;
            Close();
        }

        private void cmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbLanguage.SelectedItem is ComboBoxItem sel && sel.Tag != null)
                {
                    var lang = sel.Tag.ToString() ?? "es";
                    App.ApplyLanguage(lang);
                }
            }
            catch { }
        }

        private void cmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbTheme.SelectedItem is ComboBoxItem sel && sel.Tag != null)
                {
                    var theme = sel.Tag.ToString() ?? "light";
                    App.ApplyTheme(theme);
                }
            }
            catch { }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
