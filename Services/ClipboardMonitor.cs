using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace BoltDownloader.Services
{
    /// <summary>
    /// Monitor de portapapeles para detectar URLs automáticamente
    /// </summary>
    public class ClipboardMonitor
    {
        private readonly Window _window;
        private readonly DispatcherTimer _timer;
        private string _lastClipboardContent = "";
        private readonly Regex _urlRegex;

        public bool IsMonitoring { get; private set; }
        public event EventHandler<string>? UrlDetected;

        public ClipboardMonitor(Window window)
        {
            _window = window;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += OnTimerTick;

            // Regex para detectar URLs válidas
            _urlRegex = new Regex(
                @"^https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            );
        }

        public void Start()
        {
            if (!IsMonitoring)
            {
                IsMonitoring = true;
                _timer.Start();
            }
        }

        public void Stop()
        {
            if (IsMonitoring)
            {
                IsMonitoring = false;
                _timer.Stop();
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var clipboardText = Clipboard.GetText();

                    // Verificar si el contenido cambió
                    if (clipboardText != _lastClipboardContent)
                    {
                        _lastClipboardContent = clipboardText;

                        // Verificar si es una URL válida
                        if (IsValidUrl(clipboardText))
                        {
                            // Verificar si la URL apunta a un archivo descargable
                            if (IsDownloadableUrl(clipboardText))
                            {
                                UrlDetected?.Invoke(this, clipboardText);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignorar errores de acceso al portapapeles
            }
        }

        private bool IsValidUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();

            // Verificar con regex
            if (!_urlRegex.IsMatch(text))
                return false;

            // Verificar que sea una URI válida
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private bool IsDownloadableUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.ToLower();

                // Lista de extensiones comunes de archivos descargables
                string[] downloadableExtensions = {
                    ".zip", ".rar", ".7z", ".tar", ".gz",
                    ".exe", ".msi", ".dmg", ".pkg", ".deb", ".rpm",
                    ".iso", ".img",
                    ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm",
                    ".mp3", ".wav", ".flac", ".aac", ".ogg",
                    ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
                    ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp",
                    ".apk", ".ipa",
                    ".torrent"
                };

                // Verificar si la URL termina con una extensión descargable
                return downloadableExtensions.Any(ext => path.EndsWith(ext));
            }
            catch
            {
                return false;
            }
        }
    }
}
