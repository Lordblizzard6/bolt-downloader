using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BoltDownloader.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        private static SolidColorBrush GetBrush(string key, Color fallback)
        {
            try
            {
                var res = Application.Current?.Resources[key] as SolidColorBrush;
                if (res != null) return res;
            }
            catch { }
            return new SolidColorBrush(fallback);
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = (value as string)?.Trim() ?? string.Empty;
            var s = status.ToLowerInvariant();

            // Support ES + EN + DE + FR common labels
            if (s.Contains("descarg") || s.Contains("download"))
                return GetBrush("RowDownloadingBrush", Color.FromRgb(224, 245, 255));
            if (s.Contains("pausad") || s == "paused" || s.Contains("pause") || s.Contains("pausiert"))
                return GetBrush("RowPausedBrush", Color.FromRgb(255, 248, 224));
            if (s.Contains("error") || s.Contains("fehler"))
                return GetBrush("RowErrorBrush", Color.FromRgb(255, 235, 238));
            if (s.Contains("complet") || s.Contains("fertig") || s.Contains("abgeschlossen") || s.Contains("terminé"))
                return GetBrush("RowCompletedBrush", Color.FromRgb(232, 245, 233));

            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
