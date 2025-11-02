using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using BoltDownloader.Models;
using BoltDownloader.Services;
using Localization = BoltDownloader.Services.Localization;
using BoltDownloader.Views;
using System.Threading.Tasks;
using System.Windows.Resources;
using System.Windows.Data;
using System.ComponentModel;

namespace BoltDownloader
{
    /// <summary>
    /// Ventana principal de la aplicación Bolt Downloader
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DownloadManager _downloadManager;
        private readonly ConfigurationService _configService;
        private readonly ClipboardMonitor _clipboardMonitor;
        private readonly Dictionary<Guid, DownloadProgressDialog> _progressDialogs = new();
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _minimizeBalloonShown = false; // mostrar tip solo una vez por sesión
        public ObservableCollection<DownloadItem> Downloads { get; set; }
        private ICollectionView? _downloadsView;
        private string _selectedCategory = BoltDownloader.Services.Localization.L("Category_All");
        private string? _selectedCategoryKey = null; // null = All, "" = Others, otherwise internal key (e.g., "Compressed")
        private readonly Dictionary<string, string> _categoryLabelToKey = new();
        private readonly Dictionary<string, string> _categoryKeyToLabel = new();
        private static readonly Dictionary<string, string> _knownCategoryKeyMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Compressed"] = "Compressed",
            ["Documents"] = "Documents",
            ["Images"] = "Images",
            ["Music"] = "Music",
            ["Programs"] = "Programs",
            ["Videos"] = "Videos"
        };

        public MainWindow()
        {
            InitializeComponent();
            
            Downloads = new ObservableCollection<DownloadItem>();
            dgDownloads.ItemsSource = Downloads;
            _downloadsView = CollectionViewSource.GetDefaultView(Downloads);
            if (_downloadsView != null)
            {
                _downloadsView.Filter = DownloadFilter;
            }
            
            _configService = new ConfigurationService();
            _downloadManager = new DownloadManager(_configService);
            _clipboardMonitor = new ClipboardMonitor(this);
            
            // Suscribirse a eventos del gestor de descargas
            _downloadManager.DownloadProgressChanged += OnDownloadProgressChanged;
            _downloadManager.DownloadCompleted += OnDownloadCompleted;
            _downloadManager.DownloadStatusChanged += OnDownloadStatusChanged;
            
            // Iniciar monitoreo de portapapeles si está habilitado
            if (_configService.MonitorClipboard)
            {
                _clipboardMonitor.Start();
                _clipboardMonitor.UrlDetected += OnUrlDetectedInClipboard;
            }

            // Inicializar UI adicional
            SetupTrayIcon();
            // Minimizar a bandeja cuando se minimiza la ventana
            this.StateChanged += MainWindow_StateChanged;

            // Suscribirse al servidor de captura (desde extensión del navegador)
            try { CaptureServer.Captured += OnCapturedFromBrowser; } catch { }

            UpdateStatusBar();

            // Cargar descargas guardadas
            LoadSavedDownloads();

            // Inicializar panel de categorías
            try { SetupCategoriesPanel(); } catch { }
        }

        private void OnCapturedFromBrowser(object? sender, BoltDownloader.Services.CaptureItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Url)) return;
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var url = item.Url.Trim();

                    // Regla: usar título solo si no parece un URL; si no, usar nombre del path del URL.
                    string fileName;
                    bool meaningfulTitle = !string.IsNullOrWhiteSpace(item.Title) && !LooksLikeUrl(item.Title);
                    if (meaningfulTitle)
                    {
                        fileName = BuildFileNameFromTitleAndUrl(item.Title, url);
                    }
                    else
                    {
                        try { fileName = Path.GetFileName(new Uri(url).LocalPath); }
                        catch { fileName = "download"; }
                    }

                    TryAddOrUpdateDownload(
                        url,
                        fileName,
                        _configService.DefaultDownloadPath,
                        startImmediately: true,
                        referrer: item.Referer
                    );
                }
                catch (Exception ex)
                {
                    try { Logger.Error("OnCapturedFromBrowser error", ex); } catch { }
                }
            });
        }

        #region Métodos de Menú y Toolbar

        private void NewDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new AddDownloadDialog(_configService);
                if (dialog.ShowDialog() == true)
                {
                    TryAddOrUpdateDownload(
                        dialog.DownloadUrl,
                        dialog.FileName,
                        dialog.SavePath,
                        dialog.StartImmediately,
                        dialog.Referrer,
                        dialog.Cookies,
                        dialog.OverrideUserAgent,
                        dialog.CustomHeaders,
                        dialog.PerDownloadSpeedLimitKBps,
                        dialog.BasicAuthUser,
                        dialog.BasicAuthPassword,
                        dialog.SegmentsOverride
                    );
                }
            }
            catch (Exception ex)
            {
                Localization.Show("Error_AddDownloadFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
            }
        }

        private void BatchDownload_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new BatchDownloadDialog();
            if (dialog.ShowDialog() == true)
            {
                foreach (var url in dialog.Urls)
                {
                    var fileName = Path.GetFileName(new Uri(url).LocalPath);
                    TryAddOrUpdateDownload(url, fileName, _configService.DefaultDownloadPath, startImmediately: true);
                }
                UpdateStatusBar();

                // Seleccionar el último añadido
                if (Downloads.Any())
                {
                    dgDownloads.SelectedItem = Downloads.Last();
                    UpdateActionButtons();
                }
            }
        }

        private void PauseDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            if (selectedItem != null && selectedItem.Status == "Descargando")
            {
                _downloadManager.PauseDownload(selectedItem.Id);
            }
        }

        private void ResumeDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            if (selectedItem != null && selectedItem.Status == "Pausado")
            {
                _downloadManager.ResumeDownload(selectedItem.Id);
            }
        }

        private void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            if (selectedItem != null && selectedItem.Status == "En Cola")
            {
                _downloadManager.StartDownload(selectedItem.Id);
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            if (selectedItem != null)
            {
                var result = MessageBox.Show(
                    Localization.F("Confirm_CancelDownload", selectedItem.FileName),
                    Localization.L("Title_ConfirmCancel"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _downloadManager.CancelDownload(selectedItem.Id);
                }
            }
        }

        private void DeleteDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            if (selectedItem != null)
            {
                var result = MessageBox.Show(
                    Localization.F("Confirm_DeleteFromListWithFiles", selectedItem.FileName),
                    Localization.L("Title_ConfirmDelete"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _downloadManager.DeleteDownload(selectedItem.Id, deleteFiles: true);
                    Downloads.Remove(selectedItem);
                }
                else if (result == MessageBoxResult.No)
                {
                    _downloadManager.DeleteDownload(selectedItem.Id, deleteFiles: false);
                    Downloads.Remove(selectedItem);
                }
                
                UpdateStatusBar();
            }
        }

        private void DeleteCompleted_Click(object sender, RoutedEventArgs e)
        {
            var completedItems = Downloads.Where(d => d.Status == "Completado").ToList();
            
            if (completedItems.Any())
            {
                var result = MessageBox.Show(
                    Localization.F("Confirm_DeleteCompletedCount", completedItems.Count),
                    Localization.L("Title_ConfirmDelete"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var item in completedItems)
                    {
                        Downloads.Remove(item);
                    }
                    UpdateStatusBar();
                }
            }
            else
            {
                Localization.Show("Info_NoCompletedDownloads", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ImportDownloads_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar descargas (JSON)",
                Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var imports = JsonSerializer.Deserialize<List<DownloadItem>>(json) ?? new List<DownloadItem>();
                    foreach (var it in imports)
                    {
                        // No iniciar automáticamente al importar
                        TryAddOrUpdateDownload(
                            it.Url,
                            it.FileName,
                            string.IsNullOrWhiteSpace(it.SavePath) ? _configService.DefaultDownloadPath : it.SavePath,
                            startImmediately: false,
                            it.Referrer,
                            it.Cookies,
                            it.OverrideUserAgent,
                            it.Headers,
                            it.PerDownloadSpeedLimitKBps
                        );
                    }
                    Localization.Show("Info_ImportedCount", "Title_Import", MessageBoxButton.OK, MessageBoxImage.Information, imports.Count);
                }
                catch (Exception ex)
                {
                    Localization.Show("Error_ImportFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }
            }
        }

        private void ExportDownloads_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar descargas (JSON)",
                Filter = "Archivos JSON (*.json)|*.json",
                FileName = "downloads_export.json"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(Downloads.ToList(), options);
                    File.WriteAllText(dlg.FileName, json);
                    Localization.Show("Info_ExportCompleted", "Title_Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Localization.Show("Error_ExportFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }
            }
        }

        private void AddScheduledTask_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SchedulerDialog(_configService);
            dialog.ShowDialog();
        }

        private void ViewSchedules_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SchedulerDialog(_configService);
            dialog.ShowDialog();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog(_configService);
            if (dialog.ShowDialog() == true)
            {
                // Aplicar cambios de configuración
                if (_configService.MonitorClipboard && !_clipboardMonitor.IsMonitoring)
                {
                    _clipboardMonitor.Start();
                }
                else if (!_configService.MonitorClipboard && _clipboardMonitor.IsMonitoring)
                {
                    _clipboardMonitor.Stop();
                }
            }
        }

        private void SpeedLimit_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SpeedLimitDialog(_configService);
            if (dialog.ShowDialog() == true)
            {
                _downloadManager.UpdateSpeedLimit(_configService.SpeedLimitKBps);
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                Localization.L("About_Text"),
                Localization.L("Title_About"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Categorías y Filtros

        private void SetupCategoriesPanel()
        {
            try
            {
                _categoryLabelToKey.Clear();
                _categoryKeyToLabel.Clear();

                var labels = new List<string>();
                var labelAll = Localization.L("Category_All");
                var labelOthers = Localization.L("Category_Others");

                // All
                labels.Add(labelAll);

                // Known categories -> localized labels
                if (_configService.UseCategories && _configService.CategoryPaths != null)
                {
                    foreach (var key in _configService.CategoryPaths.Keys.OrderBy(k => k))
                    {
                        var standardized = _knownCategoryKeyMap.TryGetValue(key, out var std) ? std : null;
                        var label = standardized != null ? Localization.L($"Category_{standardized}") : key;
                        labels.Add(label);
                        _categoryLabelToKey[label] = key;
                        _categoryKeyToLabel[key] = label;
                    }
                }

                // Others
                labels.Add(labelOthers);

                lstCategories.ItemsSource = labels;
                lstCategories.SelectedItem = labelAll;
                _selectedCategory = labelAll;
                _selectedCategoryKey = null; // All
            }
            catch { }
        }

        private string GetCategoryForItem(DownloadItem item)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(item.FileName)?.ToLowerInvariant() ?? string.Empty;
                if (_configService.ExtensionToCategory != null && _configService.ExtensionToCategory.TryGetValue(ext, out var cat))
                {
                    return cat;
                }
            }
            catch { }
            // Sin categoría asignada
            return string.Empty;
        }

        private bool DownloadFilter(object obj)
        {
            if (obj is DownloadItem d)
            {
                var cat = GetCategoryForItem(d);
                // All
                if (_selectedCategoryKey == null) return true;
                // Others
                if (_selectedCategoryKey == string.Empty) return string.IsNullOrWhiteSpace(cat);
                // Specific internal key
                return string.Equals(cat, _selectedCategoryKey, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private void lstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selectedLabel = lstCategories.SelectedItem as string ?? Localization.L("Category_All");
                _selectedCategory = selectedLabel;

                var labelAll = Localization.L("Category_All");
                var labelOthers = Localization.L("Category_Others");

                if (string.Equals(selectedLabel, labelAll, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedCategoryKey = null; // All
                }
                else if (string.Equals(selectedLabel, labelOthers, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedCategoryKey = string.Empty; // Others
                }
                else if (_categoryLabelToKey.TryGetValue(selectedLabel, out var key))
                {
                    _selectedCategoryKey = key;
                }
                else
                {
                    _selectedCategoryKey = null;
                }

                _downloadsView?.Refresh();
            }
            catch { }
        }

        private void miMoveToCategory_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
            {
                mi.Items.Clear();
                try
                {
                    foreach (var kv in _configService.CategoryPaths)
                    {
                        // Mostrar etiqueta localizada
                        var header = _categoryKeyToLabel.TryGetValue(kv.Key, out var lbl) ? lbl : (_knownCategoryKeyMap.TryGetValue(kv.Key, out var std) ? Localization.L($"Category_{std}") : kv.Key);
                        var sub = new MenuItem { Header = header, Tag = kv };
                        sub.Click += MoveToCategory_Click;
                        mi.Items.Add(sub);
                    }
                }
                catch { }
            }
        }

        private void MoveToCategory_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is not DownloadItem item) return;
            try
            {
                if (sender is MenuItem sub && sub.Tag is KeyValuePair<string, string> kv)
                {
                    var targetPath = kv.Value;
                    try { Directory.CreateDirectory(targetPath); } catch { }

                    var currentFull = System.IO.Path.Combine(item.SavePath, item.FileName);
                    var newFull = System.IO.Path.Combine(targetPath, item.FileName);

                    bool movedOnDisk = false;
                    try
                    {
                        if (File.Exists(currentFull) && !File.Exists(newFull))
                        {
                            File.Move(currentFull, newFull);
                            movedOnDisk = true;
                        }
                    }
                    catch { }

                    item.SavePath = targetPath;
                    if (movedOnDisk)
                    {
                        // opcional: notificar
                        try { Logger.Info($"Archivo movido a categoría '{kv.Key}': {item.FileName}"); } catch { }
                    }
                }
            }
            catch { }
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                try { Clipboard.SetText(item.Url ?? string.Empty); } catch { }
            }
        }

        private void CopyFileName_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                try { Clipboard.SetText(item.FileName ?? string.Empty); } catch { }
            }
        }

        private void CopyFullPath_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                try
                {
                    var full = System.IO.Path.Combine(item.SavePath ?? string.Empty, item.FileName ?? string.Empty);
                    Clipboard.SetText(full);
                }
                catch { }
            }
        }

        private void OpenReferrer_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.Referrer))
                    {
                        var psi = new ProcessStartInfo(item.Referrer) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                }
                catch { }
            }
        }

        private void ShowProperties_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                try
                {
                    var info = $"Nombre: {item.FileName}\nURL: {item.Url}\nRuta: {item.SavePath}\nEstado: {item.Status}\nProgreso: {item.Progress:F1}%\nTamaño: {item.FileSizeFormatted}\nVelocidad: {item.SpeedFormatted}\nRestante: {item.TimeRemainingFormatted}";
                    MessageBox.Show(info, Localization.L("Title_Properties"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            }
        }

        #endregion

        #region Context menu acciones

        private void OpenFileFromList_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                var full = Path.Combine(item.SavePath, item.FileName);
                try
                {
                    if (File.Exists(full))
                    {
                        var psi = new ProcessStartInfo(full) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    else
                    {
                        Localization.Show("Info_FileNotFound", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    Localization.Show("Error_OpenFileFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }
            }
        }

        private void OpenFolderFromList_Click(object sender, RoutedEventArgs e)
        {
            if (dgDownloads.SelectedItem is DownloadItem item)
            {
                var full = Path.Combine(item.SavePath, item.FileName);
                try
                {
                    if (Directory.Exists(item.SavePath))
                    {
                        var psi = new ProcessStartInfo("explorer.exe", $"/e,/select,\"{full}\"") { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    else
                    {
                        Localization.Show("Info_FolderNotFound", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    Localization.Show("Error_OpenFolderFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }
            }
        }

        #endregion

        #region Acciones masivas

        private void PauseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var d in Downloads.Where(d => d.Status == "Descargando").ToList())
            {
                _downloadManager.PauseDownload(d.Id);
            }
        }

        private void ResumeAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var d in Downloads.Where(d => d.Status == "Pausado").ToList())
            {
                _downloadManager.ResumeDownload(d.Id);
            }

            // Iniciar también las que estén en cola
            foreach (var d in Downloads.Where(d => d.Status == "En Cola").ToList())
            {
                _downloadManager.StartDownload(d.Id);
            }
        }

        private void CancelAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Localization.L("Confirm_CancelAll"),
                Localization.L("Title_Confirm"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                foreach (var d in Downloads.ToList())
                {
                    _downloadManager.CancelDownload(d.Id);
                }
            }
        }

        #endregion

        #region Eventos de DownloadManager

        private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.Id == e.DownloadId);
                if (item != null)
                {
                    item.Progress = e.ProgressPercentage;
                    item.DownloadedBytes = e.BytesDownloaded;
                    item.Speed = e.Speed;
                    item.TimeRemaining = e.TimeRemaining;
                    UpdateStatusBar();
                    UpdateActionButtons();

                    if (_progressDialogs.TryGetValue(item.Id, out var dlg))
                    {
                        dlg.UpdateFromItem(item);
                    }
                }
            });
        }

        private void OnDownloadCompleted(object? sender, DownloadCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.Id == e.DownloadId);
                if (item != null)
                {
                    item.Status = e.Success ? "Completado" : "Error";
                    item.Progress = e.Success ? 100 : item.Progress;
                    
                    if (!e.Success && !string.IsNullOrEmpty(e.ErrorMessage))
                    {
                        MessageBox.Show(
                            Localization.F("Error_DownloadFailedWithName", item.FileName, e.ErrorMessage),
                            Localization.L("Title_DownloadError"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    // Cerrar diálogo de progreso si existe
                    if (_progressDialogs.TryGetValue(item.Id, out var dlg))
                    {
                        try { dlg.Close(); } catch { }
                        _progressDialogs.Remove(item.Id);
                    }

                    // Mostrar diálogo de completado
                    if (e.Success)
                    {
                        try
                        {
                            _trayIcon?.ShowBalloonTip(3000, "Descarga completada", item.FileName, System.Windows.Forms.ToolTipIcon.Info);
                        }
                        catch {}
                        var completed = new DownloadCompletedDialog(item);
                        completed.Owner = this;
                        completed.Show();
                    }

                    UpdateStatusBar();
                }
            });
        }

        private void OnDownloadStatusChanged(object? sender, DownloadStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var item = Downloads.FirstOrDefault(d => d.Id == e.DownloadId);
                if (item != null)
                {
                    item.Status = e.NewStatus;
                    UpdateStatusBar();
                    UpdateActionButtons();

                    if (e.NewStatus == "Descargando")
                    {
                        if (!_progressDialogs.ContainsKey(item.Id))
                        {
                            var dlg = new DownloadProgressDialog(item, _downloadManager, _configService);
                            dlg.Owner = this;
                            _progressDialogs[item.Id] = dlg;
                            dlg.Show();
                        }
                    }
                    else if (e.NewStatus == "Pausado")
                    {
                        if (_progressDialogs.TryGetValue(item.Id, out var dlg))
                        {
                            dlg.UpdateFromItem(item);
                        }
                    }
                    else if (e.NewStatus == "Cancelado")
                    {
                        if (_progressDialogs.TryGetValue(item.Id, out var dlg))
                        {
                            try { dlg.Close(); } catch { }
                            _progressDialogs.Remove(item.Id);
                        }
                    }
                }
            });
        }

        #endregion

        #region Métodos Auxiliares

        private string T(string key)
        {
            try { return Application.Current.Resources[key] as string ?? key; } catch { return key; }
        }

        private void UpdateStatusBar()
        {
            var activeDownloads = Downloads.Count(d => d.Status == "Descargando");
            var totalSpeed = Downloads.Where(d => d.Status == "Descargando").Sum(d => d.Speed);
            
            if (activeDownloads == 1)
            {
                txtActiveDownloads.Text = T("Status_ActiveDownloads_One");
            }
            else
            {
                txtActiveDownloads.Text = string.Format(T("Status_ActiveDownloads_Many"), activeDownloads);
            }
            txtTotalSpeed.Text = FormatSpeed(totalSpeed);
            
            if (activeDownloads > 0)
            {
                txtStatusBar.Text = T("Status_Downloading");
            }
            else if (Downloads.Any(d => d.Status == "Pausado"))
            {
                txtStatusBar.Text = T("Status_Paused");
            }
            else
            {
                txtStatusBar.Text = T("Status_Ready");
            }
        }

        public void RefreshLocalization()
        {
            // Actualizar barra de estado
            UpdateStatusBar();

            // Reconstruir etiquetas localizadas de categorías preservando selección
            try
            {
                var prevKey = _selectedCategoryKey; // null=All, ""=Others, or internal key
                SetupCategoriesPanel();

                var labelAll = Localization.L("Category_All");
                var labelOthers = Localization.L("Category_Others");

                if (prevKey == null)
                {
                    lstCategories.SelectedItem = labelAll;
                    _selectedCategory = labelAll;
                    _selectedCategoryKey = null;
                }
                else if (prevKey == string.Empty)
                {
                    lstCategories.SelectedItem = labelOthers;
                    _selectedCategory = labelOthers;
                    _selectedCategoryKey = string.Empty;
                }
                else
                {
                    if (_categoryKeyToLabel.TryGetValue(prevKey, out var lbl))
                    {
                        lstCategories.SelectedItem = lbl;
                        _selectedCategory = lbl;
                        _selectedCategoryKey = prevKey;
                    }
                    else
                    {
                        lstCategories.SelectedItem = labelAll;
                        _selectedCategory = labelAll;
                        _selectedCategoryKey = null;
                    }
                }
            }
            catch { }
        }

        private string FormatSpeed(long bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond} B/s";
            else if (bytesPerSecond < 1024 * 1024)
                return $"{bytesPerSecond / 1024.0:F2} KB/s";
            else
                return $"{bytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
        }

        private void dgDownloads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            var selectedItem = dgDownloads.SelectedItem as DownloadItem;
            btnPause.IsEnabled = selectedItem?.Status == "Descargando";
            btnResume.IsEnabled = selectedItem?.Status == "Pausado";
            btnDelete.IsEnabled = selectedItem != null;
        }

        private void dgContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            try
            {
                var cm = sender as ContextMenu;
                if (cm == null) return;

                MenuItem? Find(string name) => cm.Items.OfType<MenuItem>().FirstOrDefault(mi => string.Equals(mi.Name, name, StringComparison.Ordinal));

                var ctxOpenFile = Find("ctxOpenFile");
                var ctxOpenFolder = Find("ctxOpenFolder");
                var ctxStart = Find("ctxStart");
                var ctxPause = Find("ctxPause");
                var ctxResume = Find("ctxResume");
                var ctxCancel = Find("ctxCancel");
                var ctxDelete = Find("ctxDelete");
                var ctxCopyUrl = Find("ctxCopyUrl");
                var ctxCopyFileName = Find("ctxCopyFileName");
                var ctxCopyFullPath = Find("ctxCopyFullPath");
                var ctxOpenReferrer = Find("ctxOpenReferrer");
                var ctxProperties = Find("ctxProperties");

                if (dgDownloads.SelectedItem is not DownloadItem item)
                {
                    // No selection: disable most
                    foreach (var mi in new[] { ctxOpenFile, ctxOpenFolder, ctxStart, ctxPause, ctxResume, ctxCancel, ctxDelete, ctxCopyUrl, ctxCopyFileName, ctxCopyFullPath, ctxOpenReferrer, ctxProperties })
                        if (mi != null) mi.IsEnabled = false;
                    return;
                }

                // File/folder availability
                var fullPath = System.IO.Path.Combine(item.SavePath ?? string.Empty, item.FileName ?? string.Empty);
                bool fileExists = false, folderExists = false;
                try { fileExists = File.Exists(fullPath); } catch { }
                try { folderExists = Directory.Exists(item.SavePath ?? string.Empty); } catch { }

                ctxOpenFile?.SetCurrentValue(MenuItem.IsEnabledProperty, fileExists);
                ctxOpenFolder?.SetCurrentValue(MenuItem.IsEnabledProperty, folderExists);

                // Status-based actions
                var status = (item.Status ?? string.Empty).Trim();
                bool canStart = string.Equals(status, "En Cola", StringComparison.OrdinalIgnoreCase);
                bool canPause = string.Equals(status, "Descargando", StringComparison.OrdinalIgnoreCase);
                bool canResume = string.Equals(status, "Pausado", StringComparison.OrdinalIgnoreCase);
                bool canCancel = canStart || canPause || canResume; // cancel allowed unless completed/error?

                ctxStart?.SetCurrentValue(MenuItem.IsEnabledProperty, canStart);
                ctxPause?.SetCurrentValue(MenuItem.IsEnabledProperty, canPause);
                ctxResume?.SetCurrentValue(MenuItem.IsEnabledProperty, canResume);
                ctxCancel?.SetCurrentValue(MenuItem.IsEnabledProperty, canCancel);

                ctxDelete?.SetCurrentValue(MenuItem.IsEnabledProperty, true);
                bool hasUrl = !string.IsNullOrWhiteSpace(item.Url);
                bool hasFileName = !string.IsNullOrWhiteSpace(item.FileName);
                bool hasSavePath = !string.IsNullOrWhiteSpace(item.SavePath);
                bool hasReferrer = !string.IsNullOrWhiteSpace(item.Referrer);

                ctxCopyUrl?.SetCurrentValue(MenuItem.IsEnabledProperty, hasUrl);
                ctxCopyFileName?.SetCurrentValue(MenuItem.IsEnabledProperty, hasFileName);
                ctxCopyFullPath?.SetCurrentValue(MenuItem.IsEnabledProperty, hasFileName && hasSavePath);
                ctxOpenReferrer?.SetCurrentValue(MenuItem.IsEnabledProperty, hasReferrer);
                ctxProperties?.SetCurrentValue(MenuItem.IsEnabledProperty, true);
            }
            catch { }
        }

        private void OnUrlDetectedInClipboard(object? sender, string url)
        {
            Dispatcher.Invoke(() =>
            {
                var result = MessageBox.Show(
                    Localization.F("Confirm_DetectedUrlPrompt", url),
                    Localization.L("Title_UrlDetected"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    var fileName = Path.GetFileName(new Uri(url).LocalPath);
                    TryAddOrUpdateDownload(url, fileName, _configService.DefaultDownloadPath, startImmediately: true);
                }
            });
        }

        private void LoadSavedDownloads()
        {
            // Cargar descargas guardadas desde archivo de configuración
            var savedDownloads = _configService.LoadDownloads();
            foreach (var download in savedDownloads)
            {
                try
                {
                    ValidateAndRecoverDownload(download);
                }
                catch { }
                Downloads.Add(download);
            }
        }

        // Revisa segmentos persistidos y ajusta progreso/estado
        private void ValidateAndRecoverDownload(DownloadItem item)
        {
            try
            {
                long totalDownloaded = 0;
                if (item.Segments != null && item.Segments.Count > 0)
                {
                    foreach (var s in item.Segments)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(s.TempFilePath) && File.Exists(s.TempFilePath))
                            {
                                var len = new FileInfo(s.TempFilePath).Length;
                                s.DownloadedBytes = len;
                                var segLen = (s.EndByte >= s.StartByte) ? (s.EndByte - s.StartByte + 1) : 0;
                                s.IsCompleted = (segLen > 0 && len >= segLen);
                                totalDownloaded += len;
                            }
                        }
                        catch { }
                    }
                }

                item.DownloadedBytes = totalDownloaded;
                if (item.TotalBytes > 0)
                {
                    item.Progress = (double)totalDownloaded / item.TotalBytes * 100.0;
                }
                // Si se quedó en Descargando al cerrar, pasar a Pausado
                if (string.Equals(item.Status, "Descargando", StringComparison.OrdinalIgnoreCase))
                {
                    item.Status = "Pausado";
                }
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Si está configurado CloseToTray, cancelar cierre y minimizar a bandeja
            if (_configService.CloseToTray)
            {
                e.Cancel = true;
                try
                {
                    this.WindowState = WindowState.Minimized;
                    this.ShowInTaskbar = false;
                    this.Hide();
                }
                catch { }
                return;
            }

            base.OnClosing(e);

            // Guardar descargas antes de cerrar
            _configService.SaveDownloads(Downloads.ToList());

            // Detener monitoreo de portapapeles
            _clipboardMonitor.Stop();

            // Detener todas las descargas activas
            var activeDownloads = Downloads.Where(d => d.Status == "Descargando").ToList();
            foreach (var download in activeDownloads)
            {
                _downloadManager.PauseDownload(download.Id);
            }

            // Cerrar diálogos de progreso
            foreach (var kv in _progressDialogs.ToList())
            {
                try { kv.Value.Close(); } catch { }
            }

            // Ocultar y liberar tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        #endregion

        #region Duplicados y creación de descargas

        private void TryAddOrUpdateDownload(
            string url,
            string fileName,
            string savePath,
            bool startImmediately,
            string? referrer = null,
            string? cookies = null,
            string? overrideUserAgent = null,
            Dictionary<string, string>? customHeaders = null,
            long perDownloadSpeedLimitKBps = 0,
            string? basicAuthUser = null,
            string? basicAuthPassword = null,
            int segmentsOverride = 0)
        {
            // Normalizar nombre de archivo: evitar extensiones inválidas como .unknown_video
            fileName = NormalizeFileNameFromUrl(url, fileName);

            var candidate = new DownloadItem
            {
                Url = url,
                FileName = fileName,
                SavePath = savePath,
                Status = "En Cola",
                Progress = 0,
                Referrer = referrer,
                Cookies = cookies,
                OverrideUserAgent = overrideUserAgent,
                PerDownloadSpeedLimitKBps = perDownloadSpeedLimitKBps,
                Headers = customHeaders ?? new Dictionary<string, string>(),
                BasicAuthUser = basicAuthUser,
                BasicAuthPassword = basicAuthPassword,
                SegmentsOverride = segmentsOverride
            };

            try { Logger.Info($"TryAddOrUpdate: url={url}, file={fileName}, savePath={savePath}"); } catch { }

            var resolved = ResolveDuplicate(candidate, out var existingToUpdate, out var skip);
            if (skip)
            {
                try { Logger.Info("TryAddOrUpdate: skip=true (usuario canceló o conflicto)"); } catch { }
                Localization.Show("Info_OperationCancelled", "Title_Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (existingToUpdate != null)
            {
                // Actualizar enlace del elemento existente
                existingToUpdate.Url = candidate.Url;
                existingToUpdate.Status = "En Cola";
                existingToUpdate.Progress = 0;
                existingToUpdate.DownloadedBytes = 0;
                existingToUpdate.TotalBytes = 0;
                existingToUpdate.Speed = 0;
                existingToUpdate.TimeRemaining = TimeSpan.Zero;
                existingToUpdate.Segments.Clear();
                existingToUpdate.Referrer = referrer;
                existingToUpdate.Cookies = cookies;
                existingToUpdate.OverrideUserAgent = overrideUserAgent;
                existingToUpdate.PerDownloadSpeedLimitKBps = perDownloadSpeedLimitKBps;
                existingToUpdate.Headers = customHeaders ?? new Dictionary<string, string>();
                existingToUpdate.BasicAuthUser = basicAuthUser;
                existingToUpdate.BasicAuthPassword = basicAuthPassword;
                existingToUpdate.SegmentsOverride = segmentsOverride;

                try
                {
                    _downloadManager.AddDownload(existingToUpdate);
                    if (startImmediately)
                    {
                        _downloadManager.StartDownload(existingToUpdate.Id);
                    }
                }
                catch (Exception ex)
                {
                    try { Logger.Error("TryAddOrUpdate: error al añadir/arrancar existente", ex); } catch { }
                    Localization.Show("Error_StartUpdatedFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }

                dgDownloads.SelectedItem = existingToUpdate;
                UpdateActionButtons();
                UpdateStatusBar();
                return;
            }

            // Añadir como nueva descarga
            Downloads.Add(resolved);
            try
            {
                _downloadManager.AddDownload(resolved);
            }
            catch (Exception ex)
            {
                try { Logger.Error("TryAddOrUpdate: error en AddDownload", ex); } catch { }
                Localization.Show("Error_AddDownloadFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                return;
            }
            UpdateStatusBar();

            if (startImmediately)
            {
                try
                {
                    _downloadManager.StartDownload(resolved.Id);
                }
                catch (Exception ex)
                {
                    try { Logger.Error("TryAddOrUpdate: error en StartDownload", ex); } catch { }
                    Localization.Show("Error_StartFailed", "Title_Error", MessageBoxButton.OK, MessageBoxImage.Error, ex.Message);
                }
            }

            UpdateActionButtons();
        }

        private string NormalizeFileNameFromUrl(string url, string fileName)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(fileName);
                var ext = (Path.GetExtension(fileName) ?? string.Empty).Trim().ToLowerInvariant();
                var bad = string.IsNullOrWhiteSpace(ext) || ext == ".unknown_video" || ext == ".unkown_video" || ext == ".unknown";
                if (!bad) return fileName;

                string inferred = string.Empty;
                try { inferred = (Path.GetExtension(new Uri(url).AbsolutePath) ?? string.Empty).ToLowerInvariant(); } catch { }

                // Lista de extensiones comunes aceptadas
                var ok = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m3u8", ".mpd",
                    ".mp3", ".aac", ".flac", ".wav", ".ogg", ".m4a",
                    ".zip", ".rar", ".7z", ".pdf", ".exe", ".msi", ".iso",
                    ".png", ".jpg", ".jpeg", ".gif", ".bmp",
                    ".txt", ".csv", ".doc", ".docx", ".xls", ".xlsx"
                };

                if (!string.IsNullOrWhiteSpace(inferred) && ok.Contains(inferred))
                {
                    return name + inferred;
                }

                // Por defecto, asumir .mp4 (mejor que unknown)
                return name + ".mp4";
            }
            catch { return fileName; }
        }

        // Construye un nombre amigable a partir del título de la pestaña y la URL
        private string BuildFileNameFromTitleAndUrl(string? title, string url)
        {
            // 1) Determinar extensión a partir de la URL
            string ext = string.Empty;
            try { ext = (Path.GetExtension(new Uri(url).AbsolutePath) ?? string.Empty).Trim().ToLowerInvariant(); } catch { }
            var ok = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m3u8", ".mpd",
                ".mp3", ".aac", ".flac", ".wav", ".ogg", ".m4a",
                ".zip", ".rar", ".7z", ".pdf", ".exe", ".msi", ".iso",
                ".png", ".jpg", ".jpeg", ".gif", ".bmp",
                ".txt", ".csv", ".doc", ".docx", ".xls", ".xlsx"
            };
            if (string.IsNullOrWhiteSpace(ext) || !ok.Contains(ext) || ext == ".unknown" || ext == ".unknown_video")
            {
                // HLS: usa .ts para una mejor compatibilidad con nuestra lógica de combinación
                if (string.Equals(ext, ".m3u8", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".mpd", StringComparison.OrdinalIgnoreCase))
                    ext = ".ts";
                else
                    ext = ".mp4";
            }

            // 2) Sanitizar título
            string baseName = (title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                // Fallback al nombre derivado del URL si no hay título
                try { baseName = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath); } catch { }
                if (string.IsNullOrWhiteSpace(baseName)) baseName = Guid.NewGuid().ToString("N");
            }
            // Reemplazar caracteres inválidos de nombre de archivo
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(c, ' ');
            }
            // Opcional: slugify
            if (_configService.SlugifyFileNames)
            {
                baseName = SlugifyName(baseName);
            }
            // Colapsar espacios múltiples y recortar
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s+", " ").Trim();
            // Evitar nombres excesivamente largos
            if (baseName.Length > 120) baseName = baseName.Substring(0, 120).TrimEnd();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";

            // 3) Asegurar que no duplicamos la extensión
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return baseName;
            return baseName + ext;
        }

        // Convierte una cadena a una forma amigable para sistemas de archivos, ASCII básico
        private string SlugifyName(string input)
        {
            try
            {
                string normalized = input.Normalize(System.Text.NormalizationForm.FormD);
                var sb = new System.Text.StringBuilder(normalized.Length);
                foreach (var ch in normalized)
                {
                    var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                    {
                        // Mantener letras/dígitos básicos y reemplazar otros por espacio
                        if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '_' || ch == '-') sb.Append(ch);
                        else sb.Append(' ');
                    }
                }
                var result = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
                return result;
            }
            catch { return input; }
        }

        // Devuelve true si la cadena parece ser una URL http/https
        private bool LooksLikeUrl(string? s)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                var t = s.Trim();
                if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }
            catch { return false; }
        }

        private DownloadItem ResolveDuplicate(DownloadItem candidate, out DownloadItem? existingToUpdate, out bool skip)
        {
            skip = false;
            existingToUpdate = null;

            var finalPath = Path.Combine(candidate.SavePath, candidate.FileName);
            var existsOnDisk = File.Exists(finalPath);
            var existsInList = Downloads.FirstOrDefault(d =>
                string.Equals(d.SavePath, candidate.SavePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase));

            if (!existsOnDisk && existsInList == null)
            {
                return candidate; // No hay conflicto
            }

            // Si solo existe en disco (no en la lista), renombrar automáticamente para evitar diálogo
            if (existsOnDisk && existsInList == null)
            {
                try { Logger.Info("ResolveDuplicate: Existe en disco, no en lista -> renombrar automáticamente"); } catch { }
                candidate.FileName = EnsureUniqueFileName(candidate.SavePath, candidate.FileName);
                return candidate;
            }

            // Resolver con diálogo
            try
            {
                var dlg = new DuplicateDownloadDialog(candidate.FileName, canUpdateExisting: existsInList != null);

                if (dlg.ShowDialog() == true)
                {
                    if (dlg.Choice == DuplicateChoice.Rename)
                    {
                        var newName = EnsureUniqueFileName(candidate.SavePath, dlg.NewFileName);
                        candidate.FileName = newName;
                        return candidate;
                    }
                    else if (dlg.Choice == DuplicateChoice.Skip)
                    {
                        skip = true;
                        return candidate; // valor ignorado por caller
                    }
                    else // UpdateExisting
                    {
                        existingToUpdate = existsInList!;
                        return candidate; // caller usará existingToUpdate
                    }
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("ResolveDuplicate dialog error", ex); } catch { }
                Localization.Show("Duplicate_ErrorResolveAndRename", "Title_Warning", MessageBoxButton.OK, MessageBoxImage.Warning, ex.Message);
                candidate.FileName = EnsureUniqueFileName(candidate.SavePath, candidate.FileName);
                return candidate;
            }

            // Cancelado
            skip = true;
            return candidate;
        }

        private string EnsureUniqueFileName(string folder, string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var candidate = fileName;
            int i = 1;
            while (File.Exists(Path.Combine(folder, candidate)) || Downloads.Any(d => string.Equals(d.SavePath, folder, StringComparison.OrdinalIgnoreCase) && string.Equals(d.FileName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{name} ({i++}){ext}";
            }
            return candidate;
        }

        #endregion

        #region Tray Icon

        private void SetupTrayIcon()
        {
            try
            {
                // 1) Intentar cargar icono desde recursos WPF (pack resource)
                System.Drawing.Icon? icon = null;
                try
                {
                    var sri = Application.GetResourceStream(new Uri("Resources/icon.ico", UriKind.Relative));
                    if (sri != null && sri.Stream != null)
                    {
                        icon = new System.Drawing.Icon(sri.Stream);
                    }
                }
                catch { }

                // 2) Fallback: archivo en disco dentro de la carpeta Resources junto al ejecutable
                if (icon == null)
                {
                    var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
                    if (File.Exists(iconPath))
                    {
                        try { icon = new System.Drawing.Icon(iconPath); } catch { }
                    }
                }

                // 3) Último recurso: icono por defecto del sistema
                if (icon == null)
                {
                    icon = System.Drawing.SystemIcons.Application;
                }

                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = icon,
                    Visible = true,
                    Text = "Bolt Downloader"
                };

                var cms = new System.Windows.Forms.ContextMenuStrip();
                cms.Items.Add("Mostrar", null, (s, e) => RestoreFromTray());
                cms.Items.Add("Pausar todo", null, PauseAll_FromTray);
                cms.Items.Add("Salir", null, Exit_FromTray);
                _trayIcon.ContextMenuStrip = cms;

                // Doble clic en icono de bandeja para restaurar
                _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            }
            catch
            {
                // Ignorar errores de bandeja
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    // Ocultar a bandeja
                    this.ShowInTaskbar = false;
                    this.Hide();
                    try
                    {
                        if (_configService.ShowTrayBalloonOnMinimize && !_minimizeBalloonShown)
                        {
                            _trayIcon?.ShowBalloonTip(1500, "Bolt Downloader", "La aplicación sigue ejecutándose en la bandeja.", System.Windows.Forms.ToolTipIcon.Info);
                            _minimizeBalloonShown = true;
                        }
                    }
                    catch { }
                }
                else if (this.WindowState == WindowState.Normal)
                {
                    // Asegurar que vuelve a mostrarse en la barra de tareas
                    this.ShowInTaskbar = true;
                }
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            try
            {
                this.Show();
                this.ShowInTaskbar = true;
                if (this.WindowState == WindowState.Minimized)
                    this.WindowState = WindowState.Normal;
                this.Activate();
            }
            catch { }
        }

        private void PauseAll_FromTray(object? sender, EventArgs e)
        {
            foreach (var d in Downloads.Where(d => d.Status == "Descargando").ToList())
            {
                _downloadManager.PauseDownload(d.Id);
            }
        }

        private void Exit_FromTray(object? sender, EventArgs e)
        {
            try { Logger.Info("Exit_FromTray invoked"); } catch { }
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = false;
                        _trayIcon.Dispose();
                        _trayIcon = null;
                    }
                }
                catch { }
                try { Close(); } catch { }
                try { Application.Current.Shutdown(); } catch { }
                // Fallback duro si por algún hilo la app no cierra
                try { Task.Run(async () => { await Task.Delay(750); Environment.Exit(0); }); } catch { }
            });
        }

        #endregion
    }
}
