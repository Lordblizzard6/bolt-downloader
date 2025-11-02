using System;
using System.Collections.Generic;
using System.IO;

namespace BoltDownloader.Models
{
    /// <summary>
    /// Configuración de la aplicación
    /// </summary>
    public class AppConfiguration
    {
        // Configuración de conexión
        public int MaxSegments { get; set; } = 8;
        public int MaxConcurrentDownloads { get; set; } = 3;
        public long SpeedLimitKBps { get; set; } = 0; // 0 = sin límite
        public int ConnectionTimeout { get; set; } = 60; // segundos
        public int MaxRetries { get; set; } = 5;
        
        // Configuración de carpetas
        public string DefaultDownloadPath { get; set; } = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public string TempDownloadPath { get; set; } = 
            Path.Combine(Path.GetTempPath(), "BoltDownloader_Temp");
        
        // Configuración de navegador
        public bool MonitorClipboard { get; set; } = true;
        public List<string> FileExtensionsToMonitor { get; set; } = new List<string>
        {
            ".zip", ".rar", ".exe", ".msi", ".mp4", ".avi", ".mkv", 
            ".mp3", ".pdf", ".iso", ".dmg", ".apk"
        };
        
        // Configuración de proxy
        public bool UseProxy { get; set; } = false;
        public string ProxyAddress { get; set; } = "";
        public int ProxyPort { get; set; } = 8080;
        public string ProxyUsername { get; set; } = "";
        public string ProxyPassword { get; set; } = "";
        
        // Headers personalizados
        public string UserAgent { get; set; } = 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public Dictionary<string, string> CustomHeaders { get; set; } = new Dictionary<string, string>();
        
        // Programador
        public bool EnableScheduler { get; set; } = false;
        public List<ScheduledTask> ScheduledTasks { get; set; } = new List<ScheduledTask>();

        // Categorías (similar a IDM)
        public bool UseCategories { get; set; } = true;
        public Dictionary<string, string> CategoryPaths { get; set; } = new Dictionary<string, string>
        {
            {"Compressed", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Compressed")},
            {"Documents", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Documents")},
            {"Music", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Music")},
            {"Videos", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Videos")},
            {"Programs", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Programs")},
            {"Images", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Images")}
        };
        public Dictionary<string, string> ExtensionToCategory { get; set; } = new Dictionary<string, string>
        {
            // Compressed
            {".zip", "Compressed"}, {".rar", "Compressed"}, {".7z", "Compressed"}, {".tar", "Compressed"}, {".gz", "Compressed"},
            // Documents
            {".pdf", "Documents"}, {".doc", "Documents"}, {".docx", "Documents"}, {".xls", "Documents"}, {".xlsx", "Documents"}, {".ppt", "Documents"}, {".pptx", "Documents"},
            // Music
            {".mp3", "Music"}, {".wav", "Music"}, {".flac", "Music"}, {".aac", "Music"}, {".ogg", "Music"},
            // Videos
            {".mp4", "Videos"}, {".avi", "Videos"}, {".mkv", "Videos"}, {".mov", "Videos"}, {".wmv", "Videos"}, {".flv", "Videos"}, {".webm", "Videos"}, {".ts", "Videos"},
            // Programs
            {".exe", "Programs"}, {".msi", "Programs"}, {".apk", "Programs"}, {".dmg", "Programs"}, {".pkg", "Programs"}, {".deb", "Programs"}, {".rpm", "Programs"},
            // Images
            {".jpg", "Images"}, {".jpeg", "Images"}, {".png", "Images"}, {".gif", "Images"}, {".bmp", "Images"}, {".svg", "Images"}, {".webp", "Images"}
        };

        // Idioma de la interfaz (es, en, de, fr)
        public string Language { get; set; } = "es";

        // Tema de la interfaz (light, dark)
        public string Theme { get; set; } = "light";

        // Bandeja y comportamiento
        public bool CloseToTray { get; set; } = false; // si true, cerrar con X minimiza a bandeja
        public bool ShowTrayBalloonOnMinimize { get; set; } = true; // mostrar tip al minimizar (solo una vez por sesión)

        // Nombres de archivo
        public bool SlugifyFileNames { get; set; } = false; // convertir a ASCII básico y reemplazar símbolos

        // yt-dlp (resolución de medios en páginas como YouTube)
        public bool YtDlpEnabled { get; set; } = true;
        public bool YtDlpAskFormat { get; set; } = false; // preguntar formato al capturar
        public string YtDlpFormatSelector { get; set; } = "bv*+ba/best"; // selector por defecto
        public bool YtDlpAudioOnly { get; set; } = false; // si true, priorizar audio
        public bool YtDlpDownloadSubtitles { get; set; } = false;
        public string YtDlpSubtitleLangs { get; set; } = "en,es";
        public bool YtDlpSaveMetadata { get; set; } = false;
    }

    /// <summary>
    /// Tarea programada
    /// </summary>
    public class ScheduledTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public ScheduleType Type { get; set; }
        public DateTime ScheduledTime { get; set; }
        public DayOfWeek[]? DaysOfWeek { get; set; }
        public TaskAction Action { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public enum ScheduleType
    {
        Once,
        Daily,
        Weekly,
        OnStartup
    }

    public enum TaskAction
    {
        StartDownloads,
        PauseDownloads,
        Shutdown,
        SpeedLimit
    }
}
