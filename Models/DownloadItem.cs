using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoltDownloader.Models
{
    /// <summary>
    /// Modelo que representa un elemento de descarga
    /// </summary>
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _status = "";
        private double _progress;
        private long _downloadedBytes;
        private long _totalBytes;
        private long _speed;
        private TimeSpan _timeRemaining;

        public Guid Id { get; set; } = Guid.NewGuid();
        private string _url = "";
        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        private string _fileName = "";
        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        private string _savePath = "";
        public string SavePath
        {
            get => _savePath;
            set { _savePath = value; OnPropertyChanged(); }
        }
        
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set
            {
                _downloadedBytes = value;
                OnPropertyChanged();
            }
        }

        public long TotalBytes
        {
            get => _totalBytes;
            set
            {
                _totalBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileSizeFormatted));
            }
        }

        public long Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedFormatted));
            }
        }

        public TimeSpan TimeRemaining
        {
            get => _timeRemaining;
            set
            {
                _timeRemaining = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimeRemainingFormatted));
            }
        }

        // Propiedades formateadas para la UI
        public string FileSizeFormatted => FormatBytes(TotalBytes);
        public string SpeedFormatted => FormatSpeed(Speed);
        public string TimeRemainingFormatted => FormatTimeSpan(TimeRemaining);
        public string ProgressText => $"{Progress:F1}%";

        // Datos adicionales para reanudación
        public List<SegmentInfo> Segments { get; set; } = new List<SegmentInfo>();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public string? Referrer { get; set; }
        public string? Cookies { get; set; }
        public string? OverrideUserAgent { get; set; }
        public long PerDownloadSpeedLimitKBps { get; set; } = 0;
        public int SegmentsOverride { get; set; } = 0; // 0 = usar global
        public string? BasicAuthUser { get; set; }
        public string? BasicAuthPassword { get; set; }

        // Datos internos para suavizado de velocidad
        public long LastSpeedBytes { get; set; } = 0;
        public DateTime? LastSpeedTimestampUtc { get; set; }

        #region Métodos de Formato

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
            
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond} B/s";
            else if (bytesPerSecond < 1024 * 1024)
                return $"{bytesPerSecond / 1024.0:F2} KB/s";
            else
                return $"{bytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan == TimeSpan.Zero || timeSpan == TimeSpan.MaxValue)
                return "--:--:--";
            
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            else
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Información de un segmento de descarga
    /// </summary>
    public class SegmentInfo
    {
        public int SegmentIndex { get; set; }
        public long StartByte { get; set; }
        public long EndByte { get; set; }
        public long DownloadedBytes { get; set; }
        public bool IsCompleted { get; set; }
        public string TempFilePath { get; set; } = "";
    }
}
