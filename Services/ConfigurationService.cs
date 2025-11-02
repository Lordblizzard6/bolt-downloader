using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BoltDownloader.Models;

namespace BoltDownloader.Services
{
    /// <summary>
    /// Servicio para gestionar la configuración de la aplicación
    /// </summary>
    public class ConfigurationService
    {
        private readonly string _configPath;
        private readonly string _downloadsPath;
        private AppConfiguration _config;

        public ConfigurationService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoltDownloader"
            );

            Directory.CreateDirectory(appDataPath);

            _configPath = Path.Combine(appDataPath, "config.json");
            _downloadsPath = Path.Combine(appDataPath, "downloads.json");

            _config = LoadConfiguration();
        }

        #region Propiedades de Configuración

        public int MaxSegments
        {
            get => _config.MaxSegments;
            set { _config.MaxSegments = value; SaveConfiguration(); }
        }

        public int MaxConcurrentDownloads
        {
            get => _config.MaxConcurrentDownloads;
            set { _config.MaxConcurrentDownloads = value; SaveConfiguration(); }
        }

        public long SpeedLimitKBps
        {
            get => _config.SpeedLimitKBps;
            set { _config.SpeedLimitKBps = value; SaveConfiguration(); }
        }

        public int ConnectionTimeout
        {
            get => _config.ConnectionTimeout;
            set { _config.ConnectionTimeout = value; SaveConfiguration(); }
        }

        public int MaxRetries
        {
            get => _config.MaxRetries;
            set { _config.MaxRetries = value; SaveConfiguration(); }
        }

        public string DefaultDownloadPath
        {
            get => _config.DefaultDownloadPath;
            set { _config.DefaultDownloadPath = value; SaveConfiguration(); }
        }

        public string TempDownloadPath
        {
            get => _config.TempDownloadPath;
            set { _config.TempDownloadPath = value; SaveConfiguration(); }
        }

        public bool MonitorClipboard
        {
            get => _config.MonitorClipboard;
            set { _config.MonitorClipboard = value; SaveConfiguration(); }
        }

        public List<string> FileExtensionsToMonitor
        {
            get => _config.FileExtensionsToMonitor;
            set { _config.FileExtensionsToMonitor = value; SaveConfiguration(); }
        }

        public bool UseProxy
        {
            get => _config.UseProxy;
            set { _config.UseProxy = value; SaveConfiguration(); }
        }

        public string ProxyAddress
        {
            get => _config.ProxyAddress;
            set { _config.ProxyAddress = value; SaveConfiguration(); }
        }

        public int ProxyPort
        {
            get => _config.ProxyPort;
            set { _config.ProxyPort = value; SaveConfiguration(); }
        }

        public string ProxyUsername
        {
            get => _config.ProxyUsername;
            set { _config.ProxyUsername = value; SaveConfiguration(); }
        }

        public string ProxyPassword
        {
            get => _config.ProxyPassword;
            set { _config.ProxyPassword = value; SaveConfiguration(); }
        }

        public string UserAgent
        {
            get => _config.UserAgent;
            set { _config.UserAgent = value; SaveConfiguration(); }
        }

        public bool EnableScheduler
        {
            get => _config.EnableScheduler;
            set { _config.EnableScheduler = value; SaveConfiguration(); }
        }

        public List<ScheduledTask> ScheduledTasks
        {
            get => _config.ScheduledTasks;
            set { _config.ScheduledTasks = value; SaveConfiguration(); }
        }

        // Categorías
        public bool UseCategories
        {
            get => _config.UseCategories;
            set { _config.UseCategories = value; SaveConfiguration(); }
        }

        public Dictionary<string, string> CategoryPaths
        {
            get => _config.CategoryPaths;
            set { _config.CategoryPaths = value; SaveConfiguration(); }
        }

        public Dictionary<string, string> ExtensionToCategory
        {
            get => _config.ExtensionToCategory;
            set { _config.ExtensionToCategory = value; SaveConfiguration(); }
        }

        // Idioma
        public string Language
        {
            get => _config.Language;
            set { _config.Language = value; SaveConfiguration(); }
        }

        // Tema (light, dark)
        public string Theme
        {
            get => _config.Theme;
            set { _config.Theme = value; SaveConfiguration(); }
        }

        // Bandeja y comportamiento
        public bool CloseToTray
        {
            get => _config.CloseToTray;
            set { _config.CloseToTray = value; SaveConfiguration(); }
        }

        public bool ShowTrayBalloonOnMinimize
        {
            get => _config.ShowTrayBalloonOnMinimize;
            set { _config.ShowTrayBalloonOnMinimize = value; SaveConfiguration(); }
        }

        // Nombres de archivo
        public bool SlugifyFileNames
        {
            get => _config.SlugifyFileNames;
            set { _config.SlugifyFileNames = value; SaveConfiguration(); }
        }

        // yt-dlp settings
        public bool YtDlpEnabled
        {
            get => _config.YtDlpEnabled;
            set { _config.YtDlpEnabled = value; SaveConfiguration(); }
        }

        public bool YtDlpAskFormat
        {
            get => _config.YtDlpAskFormat;
            set { _config.YtDlpAskFormat = value; SaveConfiguration(); }
        }

        public string YtDlpFormatSelector
        {
            get => _config.YtDlpFormatSelector;
            set { _config.YtDlpFormatSelector = value; SaveConfiguration(); }
        }

        public bool YtDlpAudioOnly
        {
            get => _config.YtDlpAudioOnly;
            set { _config.YtDlpAudioOnly = value; SaveConfiguration(); }
        }

        public bool YtDlpDownloadSubtitles
        {
            get => _config.YtDlpDownloadSubtitles;
            set { _config.YtDlpDownloadSubtitles = value; SaveConfiguration(); }
        }

        public string YtDlpSubtitleLangs
        {
            get => _config.YtDlpSubtitleLangs;
            set { _config.YtDlpSubtitleLangs = value; SaveConfiguration(); }
        }

        public bool YtDlpSaveMetadata
        {
            get => _config.YtDlpSaveMetadata;
            set { _config.YtDlpSaveMetadata = value; SaveConfiguration(); }
        }

        #endregion

        /// <summary>
        /// Carga la configuración desde el archivo
        /// </summary>
        private AppConfiguration LoadConfiguration()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<AppConfiguration>(json);
                    return config ?? new AppConfiguration();
                }
                catch
                {
                    return new AppConfiguration();
                }
            }

            return new AppConfiguration();
        }

        /// <summary>
        /// Guarda la configuración en el archivo
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar configuración: {ex.Message}");
            }
        }

        /// <summary>
        /// Guarda la lista de descargas
        /// </summary>
        public void SaveDownloads(List<DownloadItem> downloads)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(downloads, options);
                File.WriteAllText(_downloadsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar descargas: {ex.Message}");
            }
        }

        /// <summary>
        /// Carga la lista de descargas guardadas
        /// </summary>
        public List<DownloadItem> LoadDownloads()
        {
            if (File.Exists(_downloadsPath))
            {
                try
                {
                    var json = File.ReadAllText(_downloadsPath);
                    var downloads = JsonSerializer.Deserialize<List<DownloadItem>>(json);
                    return downloads ?? new List<DownloadItem>();
                }
                catch
                {
                    return new List<DownloadItem>();
                }
            }

            return new List<DownloadItem>();
        }

        /// <summary>
        /// Añade una tarea programada
        /// </summary>
        public void AddScheduledTask(ScheduledTask task)
        {
            _config.ScheduledTasks.Add(task);
            SaveConfiguration();
        }

        /// <summary>
        /// Elimina una tarea programada
        /// </summary>
        public void RemoveScheduledTask(Guid taskId)
        {
            _config.ScheduledTasks.RemoveAll(t => t.Id == taskId);
            SaveConfiguration();
        }

        /// <summary>
        /// Obtiene la configuración completa
        /// </summary>
        public AppConfiguration GetConfiguration()
        {
            return _config;
        }

        /// <summary>
        /// Restablece la configuración a valores predeterminados
        /// </summary>
        public void ResetToDefaults()
        {
            _config = new AppConfiguration();
            SaveConfiguration();
        }
    }
}
