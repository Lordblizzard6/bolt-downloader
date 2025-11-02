using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Localization = BoltDownloader.Services.Localization;
using System.Windows.Threading;
using BoltDownloader.Models;
using BoltDownloader.Services;

namespace BoltDownloader.Views
{
    public partial class DownloadProgressDialog : Window
    {
        private readonly DownloadManager _downloadManager;
        private readonly ConfigurationService _configService;
        private readonly Guid _downloadId;
        private readonly DownloadItem _item;

        public DownloadProgressDialog(DownloadItem item, DownloadManager manager, ConfigurationService config)
        {
            InitializeComponent();
            _item = item;
            _downloadId = item.Id;
            _downloadManager = manager;
            _configService = config;

            txtFileName.Text = item.FileName;
            UpdateFromItem(item);
            RefreshSegments(item);

            // Inicializar límite actual en el UI
            if (config.SpeedLimitKBps >= 1024)
            {
                txtSpeedLimit.Text = (config.SpeedLimitKBps / 1024.0).ToString("F2");
                cmbSpeedUnit.SelectedIndex = 1; // MB/s
            }
            else
            {
                txtSpeedLimit.Text = config.SpeedLimitKBps.ToString();
                cmbSpeedUnit.SelectedIndex = 0; // KB/s
            }

            // Inicializar límite por descarga
            txtPerSpeedLimit.Text = (_item.PerDownloadSpeedLimitKBps > 0)
                ? _item.PerDownloadSpeedLimitKBps.ToString()
                : "0";
            UpdateEffectiveInfo();
        }

        public void UpdateFromItem(DownloadItem item)
        {
            pbOverall.Value = item.Progress;
            Title = $"Descarga en progreso - {item.Progress:F1}%";
            txtSpeed.Text = item.SpeedFormatted;
            txtEta.Text = item.TimeRemainingFormatted;
            txtProgress.Text = $"{FormatBytes(item.DownloadedBytes)} / {item.FileSizeFormatted}";
            RefreshSegments(item);
            UpdatePauseResumeButton(item.Status);
        }

        public void UpdateProgress(double percent, long speed, string eta, long downloaded, string totalFormatted)
        {
            pbOverall.Value = percent;
            Title = $"Descarga en progreso - {percent:F1}%";
            txtSpeed.Text = FormatSpeed(speed);
            txtEta.Text = eta;
            txtProgress.Text = $"{FormatBytes(downloaded)} / {totalFormatted}";
            // Refrescar tabla de conexiones en tiempo real
            try { RefreshSegments(_item); } catch { }
        }

        private void RefreshSegments(DownloadItem item)
        {
            List<SegmentRow> rows;
            if (item.Segments != null && item.Segments.Count > 0)
            {
                rows = item.Segments
                    .OrderBy(s => s.SegmentIndex)
                    .Select(s => new SegmentRow
                    {
                        SegmentIndex = s.SegmentIndex,
                        RangeText = $"{FormatBytes(s.StartByte)} - {FormatBytes(s.EndByte)}",
                        DownloadedText = $"{FormatBytes(s.DownloadedBytes)}",
                        StatusText = s.IsCompleted ? "Completado" : "Descargando"
                    })
                    .ToList();
            }
            else
            {
                // No hay segmentación (HLS o descarga simple): mostrar una fila representando la conexión actual
                rows = new List<SegmentRow>
                {
                    new SegmentRow
                    {
                        SegmentIndex = 1,
                        RangeText = "—",
                        DownloadedText = FormatBytes(item.DownloadedBytes),
                        StatusText = string.Equals(item.Status, "Completado", StringComparison.OrdinalIgnoreCase) ? "Completado" : "Descargando"
                    }
                };
            }
            dgSegments.ItemsSource = rows;
        }

        private void UpdatePauseResumeButton(string status)
        {
            if (status == "Descargando")
            {
                btnPauseResume.Content = "⏸ Pausar";
            }
            else if (status == "Pausado")
            {
                btnPauseResume.Content = "▶ Reanudar";
            }
            else
            {
                btnPauseResume.IsEnabled = false;
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F2} {sizes[order]}";
        }

        private string FormatSpeed(long bytesPerSecond)
        {
            if (bytesPerSecond == 0) return "0 KB/s";
            if (bytesPerSecond < 1024) return $"{bytesPerSecond} B/s";
            if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:F2} KB/s";
            return $"{bytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
        }

        private void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (btnPauseResume.Content?.ToString()?.Contains("Pausar") == true)
            {
                _downloadManager.PauseDownload(_downloadId);
                btnPauseResume.Content = "▶ Reanudar";
            }
            else
            {
                _downloadManager.ResumeDownload(_downloadId);
                btnPauseResume.Content = "⏸ Pausar";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _downloadManager.CancelDownload(_downloadId);
            Close();
        }

        private void ApplySpeedLimit_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(txtSpeedLimit.Text, out var value) || value < 0)
            {
                Localization.Show("Validation_EnterValidValue", "Title_Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            long kbps = (long)value;
            if (cmbSpeedUnit.SelectedIndex == 1)
            {
                kbps = (long)(value * 1024); // MB/s a KB/s
            }
            _configService.SpeedLimitKBps = kbps;
            _downloadManager.UpdateSpeedLimit(kbps);
            UpdateEffectiveInfo();
        }

        private void ApplyPerSpeedLimit_Click(object sender, RoutedEventArgs e)
        {
            if (!long.TryParse(txtPerSpeedLimit.Text, out var kbps) || kbps < 0)
            {
                Localization.Show("Validation_EnterValidKbps", "Title_Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _item.PerDownloadSpeedLimitKBps = kbps;
            UpdateEffectiveInfo();
        }

        private void UpdateEffectiveInfo()
        {
            long global = _configService.SpeedLimitKBps; // KB/s
            long per = _item.PerDownloadSpeedLimitKBps;  // KB/s

            if (global <= 0 && per <= 0)
            {
                txtEffectiveInfo.Text = "Sin límite efectivo (global y por descarga deshabilitados)";
                return;
            }

            long effKbps;
            if (global > 0 && per > 0)
            {
                effKbps = Math.Min(global, per);
                txtEffectiveInfo.Text = $"Límite efectivo: {effKbps} KB/s (min[global: {global}, descarga: {per}])";
            }
            else if (global > 0)
            {
                effKbps = global;
                txtEffectiveInfo.Text = $"Límite efectivo: {effKbps} KB/s (global)";
            }
            else
            {
                effKbps = per;
                txtEffectiveInfo.Text = $"Límite efectivo: {effKbps} KB/s (por descarga)";
            }
        }

        private class SegmentRow
        {
            public int SegmentIndex { get; set; }
            public string RangeText { get; set; } = string.Empty;
            public string DownloadedText { get; set; } = string.Empty;
            public string StatusText { get; set; } = string.Empty;
        }
    }
}
