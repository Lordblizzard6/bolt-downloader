using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using BoltDownloader.Models;

namespace BoltDownloader.Views
{
    public partial class DownloadCompletedDialog : Window
    {
        private readonly DownloadItem _item;
        private readonly string _fullPath;

        public DownloadCompletedDialog(DownloadItem item)
        {
            InitializeComponent();
            _item = item;
            _fullPath = Path.Combine(item.SavePath, item.FileName);

            txtFileName.Text = item.FileName;
            txtFileSize.Text = FormatBytes(item.TotalBytes > 0 ? item.TotalBytes : new FileInfo(_fullPath).Length);
            txtPath.Text = _fullPath;
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

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_fullPath))
                {
                    var psi = new ProcessStartInfo(_fullPath)
                    {
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                else
                {
                    BoltDownloader.Services.Localization.Show("Info_FileNotOnDisk", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                BoltDownloader.Services.Localization.Show("Error_OpenFileFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
            }
            finally
            {
                // Cerrar el diálogo tras la acción
                try { Close(); } catch { }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_item.SavePath))
                {
                    var psi = new ProcessStartInfo("explorer.exe", $"/e,/select,\"{_fullPath}\"")
                    {
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                else
                {
                    BoltDownloader.Services.Localization.Show("Info_DestinationFolderMissing", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                BoltDownloader.Services.Localization.Show("Error_OpenFolderFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
            }
            finally
            {
                // Cerrar el diálogo tras la acción
                try { Close(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
