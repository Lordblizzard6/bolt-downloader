using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using BoltDownloader.Models;

namespace BoltDownloader.Services
{
    /// <summary>
    /// Gestor principal de descargas con soporte multi-hilo
    /// </summary>
    public class DownloadManager
    {
        private readonly ConfigurationService _config;
        private readonly ConcurrentDictionary<Guid, DownloadTask> _activeTasks;
        private readonly SemaphoreSlim _downloadSlots;
        private readonly HttpClient _httpClient;
        private long _globalSpeedLimit;

        public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;
        public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
        public event EventHandler<DownloadStatusEventArgs>? DownloadStatusChanged;

        public DownloadManager(ConfigurationService config)
        {
            _config = config;
            _activeTasks = new ConcurrentDictionary<Guid, DownloadTask>();
            _downloadSlots = new SemaphoreSlim(config.MaxConcurrentDownloads, config.MaxConcurrentDownloads);
            _globalSpeedLimit = config.SpeedLimitKBps * 1024; // Convertir a bytes

            // Configurar HttpClient
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };

            if (config.UseProxy && !string.IsNullOrEmpty(config.ProxyAddress))
            {
                handler.Proxy = new WebProxy($"{config.ProxyAddress}:{config.ProxyPort}");
                if (!string.IsNullOrEmpty(config.ProxyUsername))
                {
                    handler.Proxy.Credentials = new NetworkCredential(
                        config.ProxyUsername, 
                        config.ProxyPassword
                    );
                }
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(config.ConnectionTimeout)
            };
            // No establecer User-Agent por defecto aquí; se aplicará por solicitud
        }

        /// <summary>
        /// Descarga básica de HLS (.m3u8): resuelve master/media playlist, descarga segmentos secuencialmente y concatena.
        /// No soporta de momento cifrado (#EXT-X-KEY). Referer/headers del item se aplican a todas las peticiones.
        /// </summary>
        private async Task DownloadHlsAsync(DownloadTask task, Uri playlistUri, CancellationToken cancellationToken)
        {
            var item = task.Item;
            // Asegurar nombre único antes de escribir
            EnsureUniqueOutputName(item);
            var outputPath = Path.Combine(item.SavePath, item.FileName);
            Directory.CreateDirectory(item.SavePath);

            // Descargar playlist
            var playlistText = await GetStringAsync(playlistUri, item, cancellationToken);
            if (string.IsNullOrWhiteSpace(playlistText)) throw new Exception("No se pudo descargar la playlist HLS");

            // ¿Master playlist?
            if (playlistText.Contains("#EXT-X-STREAM-INF"))
            {
                // Elegir la variante con mayor BANDWIDTH
                string? bestUrl = null;
                long bestBw = -1;
                var lines = playlistText.Replace("\r", string.Empty).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith("#EXT-X-STREAM-INF"))
                    {
                        long bw = -1;
                        try
                        {
                            var idx = line.IndexOf("BANDWIDTH=");
                            if (idx >= 0)
                            {
                                var after = line.Substring(idx + "BANDWIDTH=".Length);
                                var end = after.IndexOfAny(new[] { ',', ' ' });
                                var num = end >= 0 ? after.Substring(0, end) : after;
                                long.TryParse(num, out bw);
                            }
                        }
                        catch { }
                        // La URL del stream suele estar en la siguiente línea
                        if (i + 1 < lines.Length)
                        {
                            var u = lines[i + 1].Trim();
                            if (!u.StartsWith("#"))
                            {
                                if (bw > bestBw)
                                {
                                    bestBw = bw;
                                    bestUrl = ResolveUrl(playlistUri, u);
                                }
                            }
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(bestUrl)) throw new Exception("No se encontraron variantes en la playlist HLS");
                // Recurse sobre la media playlist
                await DownloadHlsAsync(task, new Uri(bestUrl), cancellationToken);
                return;
            }

            // Media playlist: recolectar segmentos
            var segs = new List<string>();
            string? initMap = null;
            var plLines = playlistText.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < plLines.Length; i++)
            {
                var line = plLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#EXT-X-MAP:"))
                {
                    // Ejemplo: #EXT-X-MAP:URI="init.mp4"
                    var uriIdx = line.IndexOf("URI=");
                    if (uriIdx >= 0)
                    {
                        var rest = line.Substring(uriIdx + 4).Trim();
                        if (rest.StartsWith("\""))
                        {
                            var endq = rest.IndexOf('"', 1);
                            if (endq > 1) initMap = rest.Substring(1, endq - 1);
                        }
                        else
                        {
                            var comma = rest.IndexOf(',');
                            initMap = comma > 0 ? rest.Substring(0, comma) : rest;
                        }
                    }
                }
                if (line.StartsWith("#EXTINF") && i + 1 < plLines.Length)
                {
                    var u = plLines[i + 1].Trim();
                    if (!u.StartsWith("#")) segs.Add(u);
                }
            }

            if (segs.Count == 0) throw new Exception("Playlist HLS sin segmentos soportados");

            // Estimar tamaño desconocido; reportar progreso por segmentos
            item.TotalBytes = 0; // desconocido

            // Recalcular ruta por si el nombre fue ajustado en pasos anteriores
            outputPath = Path.Combine(item.SavePath, item.FileName);
            using var outStream = OpenFinalFileStream(item);

            // Escribir init segment si existe
            if (!string.IsNullOrWhiteSpace(initMap))
            {
                var initUrl = new Uri(ResolveUrl(playlistUri, initMap));
                var initReq = BuildRequest(HttpMethod.Get, initUrl, item);
                using var initResp = await _httpClient.SendAsync(initReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                initResp.EnsureSuccessStatusCode();
                using var initStream = await initResp.Content.ReadAsStreamAsync(cancellationToken);
                await initStream.CopyToAsync(outStream, 8192, cancellationToken);
            }

            long downloaded = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < segs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segUrl = new Uri(ResolveUrl(playlistUri, segs[i]));
                var req = BuildRequest(HttpMethod.Get, segUrl, item);
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                resp.EnsureSuccessStatusCode();
                using var segStream = await resp.Content.ReadAsStreamAsync(cancellationToken);

                var buffer = new byte[8192];
                int n;
                var lastUpdate = DateTime.Now;
                while ((n = await segStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await outStream.WriteAsync(buffer, 0, n, cancellationToken);
                    downloaded += n;
                    item.DownloadedBytes = downloaded;
                    // Límite de velocidad (throttler centralizado)
                    var limit = GetEffectiveLimitBytesPerSecond(item);
                    if (limit > 0) await task.Throttle.ThrottleAsync(downloaded, limit);
                    // Actualización de progreso basada en segmentos
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 200)
                    {
                        item.Progress = (double)(i + 1) / segs.Count * 100.0;
                        UpdateProgress(item, downloaded, sw);
                        lastUpdate = DateTime.Now;
                    }
                }
                item.Progress = (double)(i + 1) / segs.Count * 100.0;
                UpdateProgress(item, downloaded, sw);
            }
        }

        private async Task<string> GetStringAsync(Uri uri, DownloadItem item, CancellationToken ct)
        {
            var req = BuildRequest(HttpMethod.Get, uri, item);
            using var resp = await _httpClient.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        private string ResolveUrl(Uri baseUri, string relative)
        {
            try
            {
                if (Uri.TryCreate(relative, UriKind.Absolute, out var abs)) return abs.ToString();
                var u = new Uri(baseUri, relative);
                return u.ToString();
            }
            catch { return relative; }
        }

        public void AddDownload(DownloadItem item)
        {
            var task = new DownloadTask(item, _config);
            // Reemplazar o agregar tarea para este ID
            _activeTasks[item.Id] = task;
        }

        public async void StartDownload(Guid downloadId)
        {
            if (_activeTasks.TryGetValue(downloadId, out var task))
            {
                // Crear CTS antes de iniciar para evitar carreras con Pausar/Cancelar
                task.CancellationTokenSource = new CancellationTokenSource();
                await _downloadSlots.WaitAsync();
                
                try
                {
                    task.Item.Status = "Descargando";
                    OnDownloadStatusChanged(downloadId, "Descargando");
                    task.CompletionRaised = false;
                    // Resetear baseline de velocidad al iniciar
                    try
                    {
                        var baseBytes = task.Item.Segments.Any() ? task.Item.Segments.Sum(s => s.DownloadedBytes) : task.Item.DownloadedBytes;
                        task.Item.LastSpeedBytes = baseBytes;
                        task.Item.LastSpeedTimestampUtc = DateTime.UtcNow;
                        task.Item.Speed = 0;
                    }
                    catch { }
                    
                    await DownloadFileAsync(task);
                }
                finally
                {
                    _downloadSlots.Release();
                }
            }
        }

        public void PauseDownload(Guid downloadId)
        {
            if (_activeTasks.TryGetValue(downloadId, out var task))
            {
                task.CancellationTokenSource?.Cancel();
                task.Item.Status = "Pausado";
                OnDownloadStatusChanged(downloadId, "Pausado");
                // Resetear velocidad para no mostrar picos al reanudar
                try { task.Item.Speed = 0; task.Item.LastSpeedTimestampUtc = null; } catch { }
            }
        }

        public async void ResumeDownload(Guid downloadId)
        {
            if (_activeTasks.TryGetValue(downloadId, out var task))
            {
                // Solo permitir reanudar cuando realmente está pausado
                if (!string.Equals(task.Item.Status, "Pausado", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                task.CancellationTokenSource = new CancellationTokenSource();
                await _downloadSlots.WaitAsync();
                
                try
                {
                    task.Item.Status = "Descargando";
                    OnDownloadStatusChanged(downloadId, "Descargando");
                    task.CompletionRaised = false;
                    // Reestablecer baseline de velocidad al reanudar
                    try
                    {
                        var baseBytes = task.Item.Segments.Any() ? task.Item.Segments.Sum(s => s.DownloadedBytes) : task.Item.DownloadedBytes;
                        task.Item.LastSpeedBytes = baseBytes;
                        task.Item.LastSpeedTimestampUtc = DateTime.UtcNow;
                        task.Item.Speed = 0;
                    }
                    catch { }
                    
                    await DownloadFileAsync(task);
                }
                finally
                {
                    _downloadSlots.Release();
                }
            }
        }

        public void CancelDownload(Guid downloadId)
        {
            if (_activeTasks.TryGetValue(downloadId, out var task))
            {
                task.CancellationTokenSource?.Cancel();
                // Opción A: tratar 'Cancelar' como 'Pausar' (resumible)
                task.Item.Status = "Pausado";
                OnDownloadStatusChanged(downloadId, "Pausado");
                try { task.Item.Speed = 0; task.Item.LastSpeedTimestampUtc = null; } catch { }
                // No limpiar archivos temporales ni remover tarea, para poder reanudar
            }
        }

        public void DeleteDownload(Guid downloadId, bool deleteFiles)
        {
            if (_activeTasks.TryRemove(downloadId, out var task))
            {
                task.CancellationTokenSource?.Cancel();
                
                if (deleteFiles)
                {
                    CleanupTempFiles(task.Item);
                    
                    var finalPath = Path.Combine(task.Item.SavePath, task.Item.FileName);
                    if (File.Exists(finalPath))
                    {
                        try { File.Delete(finalPath); } catch { }
                    }
                }
            }
        }

        public void UpdateSpeedLimit(long speedLimitKBps)
        {
            _globalSpeedLimit = speedLimitKBps * 1024;
        }

        /// <summary>
        /// Garantiza que item.FileName sea único en item.SavePath. Si ya existe, aplica sufijos " (n)".
        /// </summary>
        private void EnsureUniqueOutputName(DownloadItem item)
        {
            try { Directory.CreateDirectory(item.SavePath); } catch { }
            var folder = item.SavePath ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(item.FileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            var ext = Path.GetExtension(item.FileName ?? string.Empty);
            var candidate = name + ext;
            int i = 1;
            try
            {
                while (File.Exists(Path.Combine(folder, candidate)))
                {
                    candidate = $"{name} ({i++}){ext}";
                }
                item.FileName = candidate;
            }
            catch { }
        }

        // Crear archivo final de forma atómica, reintentando con sufijos si hay colisión
        private FileStream OpenFinalFileStream(DownloadItem item)
        {
            try { Directory.CreateDirectory(item.SavePath); } catch { }
            var folder = item.SavePath ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(item.FileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";
            var ext = Path.GetExtension(item.FileName ?? string.Empty);
            for (int i = 0; i < 1000; i++)
            {
                var candidate = i == 0 ? (baseName + ext) : $"{baseName} ({i}){ext}";
                var path = Path.Combine(folder, candidate);
                try
                {
                    var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
                    item.FileName = candidate;
                    return fs;
                }
                catch (IOException)
                {
                    // colisión: reintentar con siguiente sufijo
                }
                catch
                {
                    // fallback: probar siguiente sufijo
                }
            }
            // último recurso: permitir sobrescritura
            return new FileStream(Path.Combine(folder, item.FileName), FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        }

        // Normaliza una extensión inválida o desconocida usando la URL como pista
        private string NormalizeBadExtension(string fileName, Uri uri)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(fileName);
                var ext = (Path.GetExtension(fileName) ?? string.Empty).Trim().ToLowerInvariant();
                var bad = string.IsNullOrWhiteSpace(ext) || ext == ".unknown_video" || ext == ".unkown_video" || ext == ".unknown";
                if (!bad) return fileName;

                var inferred = (Path.GetExtension(uri.AbsolutePath) ?? string.Empty).ToLowerInvariant();
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
                return name + ".mp4"; // fallback
            }
            catch { return fileName; }
        }

        /// <summary>
        /// Descarga un archivo usando segmentación multi-hilo
        /// </summary>
        private async Task DownloadFileAsync(DownloadTask task)
        {
            var item = task.Item;
            // Utilizar la CTS existente (creada en Start/Resume). Si no existe, crear una como respaldo.
            if (task.CancellationTokenSource == null)
            {
                task.CancellationTokenSource = new CancellationTokenSource();
            }
            var cancellationToken = task.CancellationTokenSource.Token;
            
            var stopwatch = Stopwatch.StartNew();
            task.Throttle.Reset();
            
            try
            {
                // Validar URL
                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                {
                    throw new Exception("URL inválida");
                }

                // Soporte básico para HLS (.m3u8)
                try
                {
                    var pathLower = uri.AbsolutePath?.ToLowerInvariant() ?? string.Empty;
                    if (pathLower.EndsWith(".m3u8"))
                    {
                        // Ajustar nombre de salida a .ts para unificar segmentos
                        try
                        {
                            var currentExt = (Path.GetExtension(item.FileName) ?? string.Empty).ToLowerInvariant();
                            if (string.IsNullOrWhiteSpace(currentExt) || currentExt == ".m3u8")
                            {
                                var newName = Path.ChangeExtension(item.FileName, ".ts");
                                if (!string.IsNullOrWhiteSpace(newName)) item.FileName = newName;
                            }
                        }
                        catch { }
                        await DownloadHlsAsync(task, uri, cancellationToken);
                        // Si se solicitó cancelación/pausa durante la descarga, no disparar completado
                        if (cancellationToken.IsCancellationRequested || string.Equals(item.Status, "Pausado", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Cancelado", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        // Marcado de completado lo realizará lógica al final de este método
                        goto CompletedLabel;
                    }
                }
                catch { }

                // Soporte básico para DASH (.mpd)
                try
                {
                    var pathLower2 = uri.AbsolutePath?.ToLowerInvariant() ?? string.Empty;
                    if (pathLower2.EndsWith(".mpd"))
                    {
                        // Ajustar nombre si viene como .mpd
                        try
                        {
                            var currentExt = (Path.GetExtension(item.FileName) ?? string.Empty).ToLowerInvariant();
                            if (string.IsNullOrWhiteSpace(currentExt) || currentExt == ".mpd")
                            {
                                var newName = Path.ChangeExtension(item.FileName, ".mp4");
                                if (!string.IsNullOrWhiteSpace(newName)) item.FileName = newName;
                            }
                        }
                        catch { }
                        await DownloadDashAsync(task, uri, cancellationToken);
                        if (cancellationToken.IsCancellationRequested || string.Equals(item.Status, "Pausado", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Cancelado", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        goto CompletedLabel;
                    }
                }
                catch { }

                // Normalizar extensión inválida o desconocida usando la URL como pista
                try
                {
                    var fixedName = NormalizeBadExtension(item.FileName, uri);
                    if (!string.Equals(fixedName, item.FileName, StringComparison.Ordinal))
                    {
                        item.FileName = fixedName;
                    }
                }
                catch { }

                // Obtener información del archivo con HEAD (si falla, caer a descarga simple)
                long contentLength = 0;
                bool supportsRange = false;
                try
                {
                    var request = BuildRequest(HttpMethod.Head, uri, item);
                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        contentLength = response.Content.Headers.ContentLength ?? 0;
                        item.TotalBytes = contentLength;
                        supportsRange = response.Headers.AcceptRanges?.Contains("bytes") ?? false;
                    }
                }
                catch
                {
                    // Ignorar y continuar con descarga simple
                }

                // Si HEAD no confirmó rangos, intentar un GET con Range: bytes=0-0
                if (!supportsRange)
                {
                    try
                    {
                        var probe = BuildRequest(HttpMethod.Get, uri, item);
                        probe.Headers.Range = new RangeHeaderValue(0, 0);
                        using var probeResponse = await _httpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (probeResponse.StatusCode == HttpStatusCode.PartialContent)
                        {
                            supportsRange = true;
                            var cr = probeResponse.Content.Headers.ContentRange;
                            if (cr != null && cr.Length.HasValue)
                            {
                                contentLength = cr.Length.Value;
                                item.TotalBytes = contentLength;
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar y continuar con la lógica existente
                    }
                }

                if (!supportsRange || contentLength == 0)
                {
                    // Descarga simple sin segmentación
                    await DownloadSimpleAsync(task, uri, cancellationToken, stopwatch);
                }
                else
                {
                    // Descarga multi-segmento
                    await DownloadMultiSegmentAsync(task, uri, contentLength, cancellationToken, stopwatch);
                }
                // Si se solicitó cancelación/pausa durante la descarga, no disparar completado
                if (cancellationToken.IsCancellationRequested || string.Equals(item.Status, "Pausado", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Cancelado", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

CompletedLabel:
                // Evitar duplicados de evento de completado
                if (!task.CompletionRaised)
                {
                    item.Status = "Completado";
                    item.Progress = 100;
                    item.CompletedAt = DateTime.Now;
                    OnDownloadCompleted(item.Id, true, null);
                    task.CompletionRaised = true;
                }
            }
            catch (OperationCanceledException)
            {
                // Descarga pausada o cancelada
            }
            catch (Exception ex)
            {
                item.Status = "Error";
                item.ErrorMessage = ex.Message;
                if (!task.CompletionRaised)
                {
                    OnDownloadCompleted(item.Id, false, ex.Message);
                    task.CompletionRaised = true;
                }
            }
            finally
            {
                stopwatch.Stop();
                // Si no está pausado, remover tarea activa (evita repetición de completado)
                if (!string.Equals(item.Status, "Pausado", StringComparison.OrdinalIgnoreCase))
                {
                    _activeTasks.TryRemove(item.Id, out _);
                }
            }
        }

        /// <summary>
        /// Descarga simple sin segmentación
        /// </summary>
        private async Task DownloadSimpleAsync(DownloadTask task, Uri uri, CancellationToken cancellationToken, Stopwatch stopwatch)
        {
            var item = task.Item;
            // Asegurar nombre único antes de escribir archivo final
            EnsureUniqueOutputName(item);
            var outputPath = Path.Combine(item.SavePath, item.FileName);
            
            // Crear directorio si no existe
            Directory.CreateDirectory(item.SavePath);
            
            var request = BuildRequest(HttpMethod.Get, uri, item);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            // Establecer tamaño si el servidor lo provee en GET
            var getLength = response.Content.Headers.ContentLength ?? 0;
            if (getLength > 0)
            {
                item.TotalBytes = getLength;
            }
            
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = OpenFinalFileStream(item);
            
            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;
            var lastUpdate = DateTime.Now;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;
                item.DownloadedBytes = totalBytesRead;
                // Límite de velocidad (throttler centralizado)
                var effectiveLimit = GetEffectiveLimitBytesPerSecond(item);
                if (effectiveLimit > 0) await task.Throttle.ThrottleAsync(totalBytesRead, effectiveLimit);
                
                // Actualizar progreso cada 200ms
                if ((DateTime.Now - lastUpdate).TotalMilliseconds > 200)
                {
                    UpdateProgress(item, totalBytesRead, stopwatch);
                    lastUpdate = DateTime.Now;
                }
            }
            
            UpdateProgress(item, totalBytesRead, stopwatch);
        }

        /// <summary>
        /// Descarga multi-segmento con paralelización
        /// </summary>
        private async Task DownloadMultiSegmentAsync(DownloadTask task, Uri uri, long contentLength, CancellationToken cancellationToken, Stopwatch stopwatch)
        {
            var item = task.Item;
            // Antes de cualquier operación de escritura, asegurar que el nombre de salida no colisiona
            EnsureUniqueOutputName(item);
            var segments = item.SegmentsOverride > 0 ? item.SegmentsOverride : _config.MaxSegments;
            if (segments < 1) segments = 1;
            var segmentSize = Math.Max(1, contentLength / segments);
            
            // Crear o recuperar segmentos
            if (item.Segments.Count == 0)
            {
                for (int i = 0; i < segments; i++)
                {
                    long start = i * segmentSize;
                    long end = (i == segments - 1) ? contentLength - 1 : start + segmentSize - 1;
                    
                    item.Segments.Add(new SegmentInfo
                    {
                        SegmentIndex = i,
                        StartByte = start,
                        EndByte = end,
                        DownloadedBytes = 0,
                        IsCompleted = false,
                        TempFilePath = Path.Combine(_config.TempDownloadPath, $"{item.Id}_{i}.tmp")
                    });
                }
            }
            
            // Crear directorio temporal
            Directory.CreateDirectory(_config.TempDownloadPath);
            
            // Descargar segmentos en paralelo
            var downloadTasks = item.Segments
                .Where(s => !s.IsCompleted)
                .Select(segment => DownloadSegmentAsync(task, uri, segment, cancellationToken, stopwatch));
            
            await Task.WhenAll(downloadTasks);
            
            // Verificar que todos los segmentos estén completos
            if (item.Segments.All(s => s.IsCompleted))
            {
                // Combinar segmentos
                await MergeSegmentsAsync(item);
                
                // Limpiar archivos temporales
                CleanupTempFiles(item);
            }
        }

        /// <summary>
        /// Descarga un segmento individual
        /// </summary>
        private async Task DownloadSegmentAsync(DownloadTask task, Uri uri, SegmentInfo segment, CancellationToken cancellationToken, Stopwatch stopwatch)
        {
            var request = BuildRequest(HttpMethod.Get, uri, task.Item);
            
            // Configurar rango desde donde continuar
            long startByte = segment.StartByte + segment.DownloadedBytes;
            request.Headers.Range = new RangeHeaderValue(startByte, segment.EndByte);
            
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(
                segment.TempFilePath, 
                FileMode.OpenOrCreate, 
                FileAccess.Write, 
                FileShare.None, 
                8192, 
                true
            );
            
            // Posicionar al final del archivo si ya existe
            fileStream.Seek(segment.DownloadedBytes, SeekOrigin.Begin);
            
            var buffer = new byte[8192];
            int bytesRead;
            var lastUpdate = DateTime.Now;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                segment.DownloadedBytes += bytesRead;
                
                // Límite de velocidad centralizado por descarga
                var effectiveLimit = GetEffectiveLimitBytesPerSecond(task.Item);
                if (effectiveLimit > 0)
                {
                    var totalDownloaded = task.Item.Segments.Sum(s => s.DownloadedBytes);
                    await task.Throttle.ThrottleAsync(totalDownloaded, effectiveLimit);
                }
                
                // Actualizar progreso global cada 200ms
                if ((DateTime.Now - lastUpdate).TotalMilliseconds > 200)
                {
                    var totalDownloaded = task.Item.Segments.Sum(s => s.DownloadedBytes);
                    UpdateProgress(task.Item, totalDownloaded, stopwatch);
                    lastUpdate = DateTime.Now;
                }
                
                // Permitir que otros hilos ejecuten
                await Task.Yield();
            }
            
            segment.IsCompleted = true;
        }

        /// <summary>
        /// Combina todos los segmentos en un archivo final
        /// </summary>
        private async Task MergeSegmentsAsync(DownloadItem item)
        {
            // Asegurar nombre único justo antes de crear el archivo combinado
            EnsureUniqueOutputName(item);
            var outputPath = Path.Combine(item.SavePath, item.FileName);
            Directory.CreateDirectory(item.SavePath);
            
            using var outputStream = OpenFinalFileStream(item);
            
            // Combinar segmentos en orden
            foreach (var segment in item.Segments.OrderBy(s => s.SegmentIndex))
            {
                if (File.Exists(segment.TempFilePath))
                {
                    using var inputStream = new FileStream(segment.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await inputStream.CopyToAsync(outputStream);
                }
            }
        }

        /// <summary>
        /// Limpia archivos temporales de una descarga
        /// </summary>
        private void CleanupTempFiles(DownloadItem item)
        {
            foreach (var segment in item.Segments)
            {
                if (File.Exists(segment.TempFilePath))
                {
                    try { File.Delete(segment.TempFilePath); } catch { }
                }
            }
        }

        /// <summary>
        /// Actualiza el progreso de una descarga
        /// </summary>
        private void UpdateProgress(DownloadItem item, long totalBytesDownloaded, Stopwatch stopwatch)
        {
            item.DownloadedBytes = totalBytesDownloaded;
            
            if (item.TotalBytes > 0)
            {
                item.Progress = (double)totalBytesDownloaded / item.TotalBytes * 100;
            }
            
            // Calcular velocidad suavizada basada en delta (evita picos tras reanudar)
            try
            {
                var now = DateTime.UtcNow;
                if (item.LastSpeedTimestampUtc.HasValue)
                {
                    var deltaBytes = totalBytesDownloaded - item.LastSpeedBytes;
                    var deltaSeconds = (now - item.LastSpeedTimestampUtc.Value).TotalSeconds;
                    if (deltaSeconds > 0.1)
                    {
                        var inst = deltaBytes > 0 ? (deltaBytes / deltaSeconds) : 0;
                        if (item.Speed <= 0)
                            item.Speed = (long)inst;
                        else
                            item.Speed = (long)(item.Speed * 0.7 + inst * 0.3);
                        item.LastSpeedBytes = totalBytesDownloaded;
                        item.LastSpeedTimestampUtc = now;
                    }
                }
                else
                {
                    item.LastSpeedBytes = totalBytesDownloaded;
                    item.LastSpeedTimestampUtc = now;
                }
            }
            catch { }
            
            // Calcular tiempo restante
            if (item.Speed > 0 && item.TotalBytes > totalBytesDownloaded)
            {
                var remainingBytes = item.TotalBytes - totalBytesDownloaded;
                var remainingSeconds = remainingBytes / item.Speed;
                item.TimeRemaining = TimeSpan.FromSeconds(remainingSeconds);
            }
            else
            {
                item.TimeRemaining = TimeSpan.Zero;
            }
            
            OnDownloadProgressChanged(item.Id, item.Progress, totalBytesDownloaded, item.Speed, item.TimeRemaining);
        }

        /// <summary>
        /// Descarga básica de DASH (MPD) soportando SegmentList y BaseURL progresivo.
        /// </summary>
        private async Task DownloadDashAsync(DownloadTask task, Uri mpdUri, CancellationToken cancellationToken)
        {
            var item = task.Item;
            EnsureUniqueOutputName(item);
            Directory.CreateDirectory(item.SavePath);
            var mpdText = await GetStringAsync(mpdUri, item, cancellationToken);
            if (string.IsNullOrWhiteSpace(mpdText)) throw new Exception("No se pudo descargar la MPD");
            XDocument doc;
            try { doc = XDocument.Parse(mpdText); }
            catch { throw new Exception("MPD inválida"); }
            XNamespace ns = doc.Root?.Name.Namespace ?? "";
            string mpdBase = doc.Root?.Element(ns + "BaseURL")?.Value?.Trim() ?? string.Empty;
            var aSets = doc.Descendants(ns + "AdaptationSet");
            var videoSet = aSets.FirstOrDefault(x =>
                string.Equals((string?)x.Attribute("contentType"), "video", StringComparison.OrdinalIgnoreCase)
                || (((string?)x.Attribute("mimeType")) ?? string.Empty).StartsWith("video", StringComparison.OrdinalIgnoreCase)
            );
            if (videoSet == null) videoSet = aSets.FirstOrDefault();
            if (videoSet == null) throw new Exception("MPD sin AdaptationSet soportado");
            XElement? bestRep = null;
            long bestBw = -1;
            foreach (var rep in videoSet.Elements(ns + "Representation"))
            {
                long bw = 0; long.TryParse((string?)rep.Attribute("bandwidth"), out bw);
                if (bw > bestBw) { bestBw = bw; bestRep = rep; }
            }
            if (bestRep == null) throw new Exception("No se encontró Representation en MPD");
            var repBase = bestRep.Element(ns + "BaseURL")?.Value?.Trim() ?? string.Empty;
            // Intento 1: Representation con BaseURL progresivo (archivo único)
            if (!string.IsNullOrWhiteSpace(repBase))
            {
                var base1 = string.IsNullOrWhiteSpace(mpdBase) ? mpdUri.ToString() : ResolveUrl(mpdUri, mpdBase);
                var abs = ResolveUrl(new Uri(base1), repBase);
                await DownloadSimpleAsync(task, new Uri(abs), cancellationToken, Stopwatch.StartNew());
                return;
            }
            // Intento 2: SegmentList con Initialization + SegmentURL/@media
            var segList = bestRep.Element(ns + "SegmentList");
            if (segList == null) throw new Exception("MPD sin SegmentList soportado (SegmentTemplate no implementado)");
            var initEl = segList.Element(ns + "Initialization");
            var initUrl = initEl?.Attribute("sourceURL")?.Value;
            var segUrls = segList.Elements(ns + "SegmentURL").Select(e => (string?)e.Attribute("media")).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (segUrls.Count == 0) throw new Exception("MPD sin SegmentURL/media");
            using var outStream = OpenFinalFileStream(item);
            long downloaded = 0;
            var sw = Stopwatch.StartNew();
            string baseUrlStr = string.IsNullOrWhiteSpace(mpdBase) ? mpdUri.ToString() : ResolveUrl(mpdUri, mpdBase);
            var baseUri2 = new Uri(baseUrlStr);
            // Initialization primero
            if (!string.IsNullOrWhiteSpace(initUrl))
            {
                var initAbs = ResolveUrl(baseUri2, initUrl);
                var initReq = BuildRequest(HttpMethod.Get, new Uri(initAbs), item);
                using var initResp = await _httpClient.SendAsync(initReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                initResp.EnsureSuccessStatusCode();
                using var initStream = await initResp.Content.ReadAsStreamAsync(cancellationToken);
                await initStream.CopyToAsync(outStream, 8192, cancellationToken);
            }
            // Segments
            for (int i = 0; i < segUrls.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mediaRel = segUrls[i]!;
                var mediaAbs = ResolveUrl(baseUri2, mediaRel);
                var req = BuildRequest(HttpMethod.Get, new Uri(mediaAbs), item);
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                resp.EnsureSuccessStatusCode();
                using var segStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[8192];
                int n;
                var lastUpdate = DateTime.Now;
                while ((n = await segStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await outStream.WriteAsync(buffer, 0, n, cancellationToken);
                    downloaded += n;
                    item.DownloadedBytes = downloaded;
                    var limit = GetEffectiveLimitBytesPerSecond(item);
                    if (limit > 0) await task.Throttle.ThrottleAsync(downloaded, limit);
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 200)
                    {
                        item.Progress = (double)(i + 1) / segUrls.Count * 100.0;
                        UpdateProgress(item, downloaded, sw);
                        lastUpdate = DateTime.Now;
                    }
                }
                item.Progress = (double)(i + 1) / segUrls.Count * 100.0;
                UpdateProgress(item, downloaded, sw);
            }
        }

        private long GetEffectiveLimitBytesPerSecond(DownloadItem item)
        {
            long global = _globalSpeedLimit; // ya en bytes/seg
            long per = item.PerDownloadSpeedLimitKBps > 0 ? item.PerDownloadSpeedLimitKBps * 1024 : 0;

            if (global <= 0 && per <= 0) return 0; // sin límite
            if (global > 0 && per > 0) return Math.Min(global, per);
            return global > 0 ? global : per;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, DownloadItem item)
        {
            var req = new HttpRequestMessage(method, uri);

            // User-Agent
            var ua = string.IsNullOrWhiteSpace(item.OverrideUserAgent) ? _config.UserAgent : item.OverrideUserAgent;
            if (!string.IsNullOrWhiteSpace(ua))
            {
                // Evitar validación estricta para permitir UA arbitrarios
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
            }

            // Referer
            if (!string.IsNullOrWhiteSpace(item.Referrer))
            {
                if (Uri.TryCreate(item.Referrer, UriKind.Absolute, out var refUri))
                {
                    req.Headers.Referrer = refUri;
                }
                else
                {
                    // Algunos servidores aceptan Referer como header plano si no es URL estricta
                    req.Headers.TryAddWithoutValidation("Referer", item.Referrer);
                }
            }

            // Cookies
            if (!string.IsNullOrWhiteSpace(item.Cookies))
            {
                req.Headers.TryAddWithoutValidation("Cookie", item.Cookies);
            }

            // Basic Auth
            if (!string.IsNullOrWhiteSpace(item.BasicAuthUser))
            {
                var pwd = item.BasicAuthPassword ?? string.Empty;
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{item.BasicAuthUser}:{pwd}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            // Headers personalizados
            if (item.Headers != null)
            {
                foreach (var kv in item.Headers)
                {
                    // Evitar sobrescribir Range que se establece en algunas peticiones
                    if (string.Equals(kv.Key, "Range", StringComparison.OrdinalIgnoreCase)) continue;
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            return req;
        }

        #region Eventos

        private void OnDownloadProgressChanged(Guid downloadId, double progress, long bytesDownloaded, long speed, TimeSpan timeRemaining)
        {
            DownloadProgressChanged?.Invoke(this, new DownloadProgressEventArgs
            {
                DownloadId = downloadId,
                ProgressPercentage = progress,
                BytesDownloaded = bytesDownloaded,
                Speed = speed,
                TimeRemaining = timeRemaining
            });
        }

        private void OnDownloadCompleted(Guid downloadId, bool success, string? errorMessage)
        {
            DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
            {
                DownloadId = downloadId,
                Success = success,
                ErrorMessage = errorMessage
            });
        }

        private void OnDownloadStatusChanged(Guid downloadId, string newStatus)
        {
            DownloadStatusChanged?.Invoke(this, new DownloadStatusEventArgs
            {
                DownloadId = downloadId,
                NewStatus = newStatus
            });
        }

        #endregion
    }

    /// <summary>
    /// Tarea de descarga interna
    /// </summary>
    internal class DownloadTask
    {
        public DownloadItem Item { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public ConfigurationService Config { get; set; }
        public bool CompletionRaised { get; set; } = false;
        public DownloadThrottle Throttle { get; } = new DownloadThrottle();

        public DownloadTask(DownloadItem item, ConfigurationService config)
        {
            Item = item;
            Config = config;
        }
    }

    internal sealed class DownloadThrottle
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Stopwatch _sw = Stopwatch.StartNew();

        public void Reset()
        {
            try { _sw.Restart(); } catch { _sw = Stopwatch.StartNew(); }
        }

        public async Task ThrottleAsync(long totalBytesDownloaded, long bytesPerSecondLimit)
        {
            if (bytesPerSecondLimit <= 0) return;
            await _gate.WaitAsync();
            try
            {
                var elapsed = _sw.Elapsed.TotalSeconds;
                if (elapsed <= 0) return;
                var expected = (double)totalBytesDownloaded / Math.Max(1, bytesPerSecondLimit);
                var delta = expected - elapsed;
                if (delta > 0)
                {
                    var delayMs = (int)(delta * 1000);
                    if (delayMs > 0) await Task.Delay(delayMs);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    #region Event Args

    public class DownloadProgressEventArgs : EventArgs
    {
        public Guid DownloadId { get; set; }
        public double ProgressPercentage { get; set; }
        public long BytesDownloaded { get; set; }
        public long Speed { get; set; }
        public TimeSpan TimeRemaining { get; set; }
    }

    public class DownloadCompletedEventArgs : EventArgs
    {
        public Guid DownloadId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadStatusEventArgs : EventArgs
    {
        public Guid DownloadId { get; set; }
        public string NewStatus { get; set; } = "";
    }

    #endregion
}
