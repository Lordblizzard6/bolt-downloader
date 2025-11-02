using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using BoltDownloader.Services;
using Localization = BoltDownloader.Services.Localization;

namespace BoltDownloader
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static ResourceDictionary? _currentThemeDict;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Crear directorio de configuración en AppData si no existe (migración desde nombre anterior)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var oldPath = Path.Combine(appData, "IDM_Clone");
            var newPath = Path.Combine(appData, "BoltDownloader");

            try
            {
                if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                    // Migrar archivos de config básicos si existen
                    var oldConfig = Path.Combine(oldPath, "config.json");
                    var oldDownloads = Path.Combine(oldPath, "downloads.json");
                    var newConfig = Path.Combine(newPath, "config.json");
                    var newDownloads = Path.Combine(newPath, "downloads.json");
                    if (File.Exists(oldConfig) && !File.Exists(newConfig)) File.Copy(oldConfig, newConfig, overwrite: false);
                    if (File.Exists(oldDownloads) && !File.Exists(newDownloads)) File.Copy(oldDownloads, newDownloads, overwrite: false);
                }

                if (!Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                }
            }
            catch { /* Ignorar errores de migración no críticos */ }

            // Inicializar logger
            try { Logger.Init(); } catch { }

            // Cargar idioma desde configuración
            try
            {
                var cfg = new ConfigurationService();
                ApplyLanguage(cfg.Language);
                ApplyTheme(cfg.Theme);
                try { CaptureServer.UpdateFileTypes(cfg.FileExtensionsToMonitor); } catch { }
                try { CaptureServer.Start(17890); } catch { }
            }
            catch { }

            // Manejo global de excepciones para evitar cierres silenciosos
            this.DispatcherUnhandledException += (s, exArgs) =>
            {
                try
                {
                    try { Logger.Error("DispatcherUnhandledException", exArgs.Exception); } catch { }
                    try
                    {
                        MessageBox.Show(
                            Localization.F("App_DispatcherUnhandled", exArgs.Exception.Message),
                            Localization.L("Title_AppError"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch { }
                }
                catch { }
                exArgs.Handled = true; // Evita cierre abrupto
            };

            AppDomain.CurrentDomain.UnhandledException += (s, exArgs) =>
            {
                try
                {
                    var ex = exArgs.ExceptionObject as Exception;
                    if (ex != null)
                    {
                        try { Logger.Error("UnhandledException", ex); } catch { }
                        try
                        {
                            MessageBox.Show(
                                Localization.F("App_Unhandled", ex.Message),
                                Localization.L("Title_AppError"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                        catch { }
                    }
                }
                catch { }
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, exArgs) =>
            {
                try
                {
                    try { Logger.Error("UnobservedTaskException", exArgs.Exception); } catch { }
                    try
                    {
                        MessageBox.Show(
                            Localization.F("App_UnobservedTask", exArgs.Exception.Message),
                            Localization.L("Title_AppError"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch { }
                }
                catch { }
                exArgs.SetObserved();
            };
        }

        public static void ApplyTheme(string theme)
        {
            try
            {
                try { Logger.Info($"ApplyTheme start: {theme}"); } catch { }
                if (string.IsNullOrWhiteSpace(theme)) theme = "light";
                var app = Application.Current;
                if (app == null) return;
                try { Logger.Info($"ApplyTheme: merged dictionaries BEFORE={app.Resources.MergedDictionaries.Count}"); } catch { }

                // Remover diccionarios de tema antiguos (excepto el que conservamos como instancia actual) y overrides previos
                var toRemove = app.Resources.MergedDictionaries
                    .Where(rd =>
                        rd != _currentThemeDict &&
                        (
                            (rd.Source != null && rd.Source.OriginalString.Contains("Resources/Theme.")) ||
                            (rd.Contains("__BoltThemeOverride__"))
                        )
                    ).ToList();
                foreach (var rd in toRemove) app.Resources.MergedDictionaries.Remove(rd);
                try { Logger.Info($"ApplyTheme: removed theme/override dictionaries count={toRemove.Count}"); } catch { }

                // Agregar diccionario del tema al final para que tenga prioridad sobre Styles (el último sobrescribe)
                ResourceDictionary? loadedTheme = null;
                Exception? loadEx = null;
                // Try a few patterns commonly working in WPF
                var asmName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                var urisToTry = new[]
                {
                    new Uri($"Resources/Theme.{theme}.xaml", UriKind.Relative),
                    new Uri($"/Resources/Theme.{theme}.xaml", UriKind.Relative),
                    new Uri($"pack://application:,,,/Resources/Theme.{theme}.xaml", UriKind.Absolute),
                    new Uri($"pack://application:,,,/{asmName};component/Resources/Theme.{theme}.xaml", UriKind.Absolute),
                };
                foreach (var u in urisToTry)
                {
                    try
                    {
                        loadedTheme = new ResourceDictionary { Source = u };
                        try { Logger.Info($"ApplyTheme: loaded theme uri={u}"); } catch { }
                        break;
                    }
                    catch (Exception ex)
                    {
                        loadEx = ex;
                        try { Logger.Error($"ApplyTheme: failed to load theme uri={u}", ex); } catch { }
                    }
                }
                if (loadedTheme == null)
                {
                    // Fallback to light
                    try
                    {
                        var rel = new Uri($"Resources/Theme.light.xaml", UriKind.Relative);
                        loadedTheme = new ResourceDictionary { Source = rel };
                        try { Logger.Info($"ApplyTheme: fallback loaded theme uri={rel}"); } catch { }
                    }
                    catch (Exception ex2)
                    {
                        try { Logger.Error("ApplyTheme: failed to load any theme dictionary", loadEx ?? ex2); } catch { }
                    }
                }

                // Si no tenemos diccionario de tema actual, añadimos el cargado y lo recordamos.
                try
                {
                    if (loadedTheme != null)
                    {
                        var preserved = app.Resources.MergedDictionaries
                            .Where(rd => (rd.Source == null || !rd.Source.OriginalString.Contains("Resources/Theme.")) && !rd.Contains("__BoltThemeOverride__"))
                            .ToList();

                        // Rebuild: preserved + loadedTheme (last)
                        app.Resources.MergedDictionaries.Clear();
                        foreach (var rd in preserved)
                            app.Resources.MergedDictionaries.Add(rd);
                        app.Resources.MergedDictionaries.Add(loadedTheme);
                        _currentThemeDict = loadedTheme;
                        try { Logger.Info($"ApplyTheme: rebuilt dictionaries; preserved={preserved.Count}, AFTER add mergedCount={app.Resources.MergedDictionaries.Count}"); } catch { }
                    }
                }
                catch { }

                // Actualizar en caliente (fallback): mutar las brushes por si alguna referencia quedó en memoria
                try
                {
                    if (_currentThemeDict != null && loadedTheme != null)
                    {
                        int mutated = 0, replaced = 0, added = 0;
                        foreach (var key in loadedTheme.Keys)
                        {
                            var newVal = loadedTheme[key];
                            if (_currentThemeDict.Contains(key))
                            {
                                var oldVal = _currentThemeDict[key];
                                if (oldVal is System.Windows.Media.SolidColorBrush ob && newVal is System.Windows.Media.SolidColorBrush nb)
                                {
                                    ob.Color = nb.Color; // muta instancia -> refresco inmediato
                                    mutated++;
                                }
                                else
                                {
                                    _currentThemeDict[key] = newVal;
                                    replaced++;
                                }
                            }
                            else
                            {
                                _currentThemeDict[key] = newVal;
                                added++;
                            }
                        }
                        try { Logger.Info($"ApplyTheme: mutated={mutated}, replaced={replaced}, added={added}"); } catch { }
                    }
                }
                catch { }

                try { if (loadedTheme != null) Logger.Info($"ApplyTheme merged keys: {loadedTheme.Keys.Count}"); } catch { }
                try { Logger.Info($"ApplyTheme: merged dictionaries AFTER={app.Resources.MergedDictionaries.Count}"); } catch { }

                // Log resolved values for key brushes to verify actual colors in lookup
                try
                {
                    var sc = app.TryFindResource("SurfaceColor") as SolidColorBrush;
                    var sa = app.TryFindResource("SurfaceAltColor") as SolidColorBrush;
                    var bc = app.TryFindResource("BorderColor") as SolidColorBrush;
                    var pc = app.TryFindResource("PrimaryColor") as SolidColorBrush;
                    var tp = app.TryFindResource("TextPrimaryBrush") as SolidColorBrush;
                    Logger.Info($"ApplyTheme: resolved SurfaceColor={sc?.Color.ToString() ?? "null"}, SurfaceAltColor={sa?.Color.ToString() ?? "null"}, BorderColor={bc?.Color.ToString() ?? "null"}, PrimaryColor={pc?.Color.ToString() ?? "null"}, TextPrimaryBrush={tp?.Color.ToString() ?? "null"}");
                }
                catch { }

                // Fallback explícito: establecer valores de la paleta por clave
                try
                {
                    // Diagnostics: check resource existence for dark/light dictionaries
                    try
                    {
                        var darkRel = new Uri("Resources/Theme.dark.xaml", UriKind.Relative);
                        var lightRel = new Uri("Resources/Theme.light.xaml", UriKind.Relative);
                        var darkStream = Application.GetResourceStream(darkRel);
                        var lightStream = Application.GetResourceStream(lightRel);
                        Logger.Info($"ApplyTheme: probe GetResourceStream darkExists={(darkStream!=null)} lightExists={(lightStream!=null)}");
                    }
                    catch (Exception exProbe)
                    {
                        try { Logger.Error("ApplyTheme: probe GetResourceStream failed", exProbe); } catch { }
                    }

                    // If we intended dark and loadedTheme is still light, try LoadComponent fallback
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(theme) && theme.Equals("dark", StringComparison.OrdinalIgnoreCase))
                        {
                            bool looksLight = false;
                            try
                            {
                                var sc = app.TryFindResource("SurfaceColor") as SolidColorBrush;
                                looksLight = sc?.Color == (Color)ColorConverter.ConvertFromString("#FFFFFF");
                            }
                            catch { }

                            if (looksLight)
                            {
                                var asmName2 = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                                var candidates = new[]
                                {
                                    new Uri("/Resources/Theme.dark.xaml", UriKind.Relative),
                                    new Uri($"/{asmName2};component/Resources/Theme.dark.xaml", UriKind.Relative),
                                    new Uri("pack://application:,,,/Resources/Theme.dark.xaml", UriKind.Absolute),
                                    new Uri($"pack://application:,,,/{asmName2};component/Resources/Theme.dark.xaml", UriKind.Absolute)
                                };
                                foreach (var cu in candidates)
                                {
                                    try
                                    {
                                        var obj = Application.LoadComponent(cu) as ResourceDictionary;
                                        if (obj != null)
                                        {
                                            app.Resources.MergedDictionaries.Add(obj);
                                            _currentThemeDict = obj;
                                            Logger.Info($"ApplyTheme: LoadComponent fallback succeeded with uri={cu}");
                                            break;
                                        }
                                    }
                                    catch (Exception exLC)
                                    {
                                        try { Logger.Error($"ApplyTheme: LoadComponent failed uri={cu}", exLC); } catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // Ensure final override precedence: copy brushes from loadedTheme into a small override dictionary and append last
                    if (loadedTheme != null)
                    {
                        var overrideDict = new ResourceDictionary();
                        try { overrideDict["__BoltThemeOverride__"] = true; } catch { }
                        int overrideCount = 0;
                        foreach (var key in loadedTheme.Keys)
                        {
                            try
                            {
                                var val = loadedTheme[key];
                                if (val is SolidColorBrush sb)
                                {
                                    // clone to avoid cross-dictionary ownership
                                    var clone = new SolidColorBrush(sb.Color);
                                    overrideDict[key] = clone;
                                    overrideCount++;
                                }
                            }
                            catch { }
                        }
                        if (overrideCount > 0)
                        {
                            app.Resources.MergedDictionaries.Add(overrideDict);
                            try { Logger.Info($"ApplyTheme: appended override dictionary with {overrideCount} brushes (last)"); } catch { }
                        }
                    }

                    void SetBrush(string key, string hex)
                    {
                        try
                        {
                            var color = (Color)ColorConverter.ConvertFromString(hex);
                            if (app.Resources[key] is SolidColorBrush b)
                                b.Color = color;
                            else
                                app.Resources[key] = new SolidColorBrush(color);
                        }
                        catch { }
                    }

                    if (theme.Equals("dark", StringComparison.OrdinalIgnoreCase))
                    {
                        SetBrush("PrimaryColor", "#0A84FF");
                        SetBrush("SecondaryColor", "#2D8CFF");
                        SetBrush("BackgroundColor", "#121212");
                        SetBrush("SurfaceColor", "#1E1E1E");
                        SetBrush("SurfaceAltColor", "#252525");
                        SetBrush("BorderColor", "#3A3A3A");
                        SetBrush("HoverColor", "#2A2A2A");
                        SetBrush("TextPrimaryBrush", "#EAEAEA");
                        SetBrush("TextSecondaryBrush", "#B0B0B0");
                        SetBrush("MenuBackgroundBrush", "#1E1E1E");
                        SetBrush("MenuBorderBrush", "#3A3A3A");
                        SetBrush("MenuItemHoverBrush", "#333333");
                        SetBrush("MenuItemForegroundBrush", "#EAEAEA");
                        SetBrush("MenuItemDisabledForegroundBrush", "#7A7A7A");
                    }
                    else
                    {
                        SetBrush("PrimaryColor", "#0078D7");
                        SetBrush("SecondaryColor", "#005A9E");
                        SetBrush("BackgroundColor", "#F5F7FA");
                        SetBrush("SurfaceColor", "#FFFFFF");
                        SetBrush("SurfaceAltColor", "#F3F6F9");
                        SetBrush("BorderColor", "#D0D7DE");
                        SetBrush("HoverColor", "#E7F2FF");
                        SetBrush("TextPrimaryBrush", "#1A1A1A");
                        SetBrush("TextSecondaryBrush", "#5A5A5A");
                        SetBrush("MenuBackgroundBrush", "#FFFFFF");
                        SetBrush("MenuBorderBrush", "#D0D7DE");
                        SetBrush("MenuItemHoverBrush", "#E7F2FF");
                        SetBrush("MenuItemForegroundBrush", "#1A1A1A");
                        SetBrush("MenuItemDisabledForegroundBrush", "#9AA0A6");
                    }
                }
                catch { }

                // Refrescar ventana principal
                try
                {
                    if (app.MainWindow is MainWindow mw)
                    {
                        mw.RefreshLocalization();
                    }
                }
                catch { }

                // Forzar refresco de ventanas abiertas
                try
                {
                    int windowsRefreshed = 0;
                    foreach (Window w in app.Windows)
                    {
                        try
                        {
                            var bg = app.TryFindResource("SurfaceColor") as System.Windows.Media.Brush;
                            var fg = app.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush;
                            if (bg != null) w.Background = bg;
                            if (fg != null) w.Foreground = fg;
                            w.InvalidateVisual();
                            w.UpdateLayout();
                            windowsRefreshed++;
                        }
                        catch { }
                    }
                    try { Logger.Info($"ApplyTheme: windows refreshed={windowsRefreshed}"); } catch { }
                }
                catch { }

                try { Logger.Info("ApplyTheme end"); } catch { }
            }
            catch { }
        }

        public static void ApplyLanguage(string lang)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lang)) lang = "es";
                var app = Application.Current;
                if (app == null) return;

                // Remover diccionarios de strings existentes
                var toRemove = app.Resources.MergedDictionaries
                    .Where(rd => rd.Source != null && rd.Source.OriginalString.Contains("Resources/Strings.")).ToList();
                foreach (var rd in toRemove)
                {
                    app.Resources.MergedDictionaries.Remove(rd);
                }

                // Agregar el diccionario solicitado (fallback a es)
                ResourceDictionary dict = new ResourceDictionary();
                try
                {
                    dict.Source = new Uri($"/Resources/Strings.{lang}.xaml", UriKind.Relative);
                    app.Resources.MergedDictionaries.Add(dict);
                }
                catch
                {
                    try
                    {
                        dict = new ResourceDictionary { Source = new Uri("/Resources/Strings.es.xaml", UriKind.Relative) };
                        app.Resources.MergedDictionaries.Add(dict);
                    }
                    catch { }
                }

                // Actualizar UI activa (por ejemplo, textos calculados en MainWindow)
                try
                {
                    if (app.MainWindow is MainWindow mw)
                    {
                        mw.RefreshLocalization();
                    }
                }
                catch { }
            }
            catch { }
        }
    }
}
