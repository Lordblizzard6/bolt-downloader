using System;
using System.Windows;

namespace BoltDownloader.Services
{
    public static class Localization
    {
        public static string L(string key)
        {
            try { return Application.Current.Resources[key] as string ?? key; } catch { return key; }
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(L(key), args); } catch { return key; }
        }

        public static MessageBoxResult Show(string messageKey, string titleKey, MessageBoxButton buttons, MessageBoxImage icon, params object[] args)
        {
            var message = args != null && args.Length > 0 ? F(messageKey, args) : L(messageKey);
            var title = L(titleKey);
            return MessageBox.Show(message, title, buttons, icon);
        }
    }
}
