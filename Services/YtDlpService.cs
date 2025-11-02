using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoltDownloader.Services
{
    public class ResolvedMedia
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? Ext { get; set; }
        public List<ResolvedMedia> ExtraAssets { get; set; } = new(); // e.g., subtitles
    }

    public static class YtDlpService
    {
        private static readonly string ToolsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoltDownloader", "tools");
        private static readonly string YtDlpExe = Path.Combine(ToolsDir, "yt-dlp.exe");
        private static readonly string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        public static async Task EnsureYtDlpAsync()
        {
            Directory.CreateDirectory(ToolsDir);
            if (!File.Exists(YtDlpExe))
            {
                try
                {
                    using var http = new HttpClient();
                    var bytes = await http.GetByteArrayAsync(YtDlpUrl);
                    await File.WriteAllBytesAsync(YtDlpExe, bytes);
                }
                catch (Exception ex)
                {
                    try { Logger.Error("YtDlpService: failed to download yt-dlp", ex); } catch { }
                    throw;
                }
                return;
            }

            // Refresh if older than 30 days (best effort)
            try
            {
                var age = DateTime.Now - File.GetLastWriteTime(YtDlpExe);
                if (age.TotalDays >= 30)
                {
                    using var http = new HttpClient();
                    var bytes = await http.GetByteArrayAsync(YtDlpUrl);
                    await File.WriteAllBytesAsync(YtDlpExe, bytes);
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("YtDlpService: failed to refresh yt-dlp", ex); } catch { }
            }
        }



        public class YtFormat
        {
            public string FormatId { get; set; } = string.Empty;
            public string Ext { get; set; } = string.Empty;
            public string Vcodec { get; set; } = string.Empty;
            public string Acodec { get; set; } = string.Empty;
            public string Resolution { get; set; } = string.Empty; // e.g., 1920x1080 or 1080p
            public long? Filesize { get; set; }
            public string Note { get; set; } = string.Empty;
            public override string ToString() => $"{FormatId} | {Ext} | {Resolution} | v:{Vcodec} a:{Acodec} {(Filesize.HasValue ? $"| {Filesize/1024/1024}MB" : string.Empty)} {Note}".Trim();
        }

        public static async Task<List<YtFormat>> ListFormatsAsync(string pageUrl)
        {
            await EnsureYtDlpAsync();
            var list = new List<YtFormat>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpExe,
                    Arguments = $"-J --no-playlist \"{pageUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return list;
                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0) return list;

                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                if (root.TryGetProperty("formats", out var formatsEl) && formatsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in formatsEl.EnumerateArray())
                    {
                        var ytf = new YtFormat
                        {
                            FormatId = f.TryGetProperty("format_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                            Ext = f.TryGetProperty("ext", out var extEl) ? extEl.GetString() ?? string.Empty : string.Empty,
                            Vcodec = f.TryGetProperty("vcodec", out var vcEl) ? vcEl.GetString() ?? string.Empty : string.Empty,
                            Acodec = f.TryGetProperty("acodec", out var acEl) ? acEl.GetString() ?? string.Empty : string.Empty,
                            Resolution = f.TryGetProperty("resolution", out var rEl) ? rEl.GetString() ?? string.Empty : (f.TryGetProperty("height", out var hEl) ? (hEl.GetInt32().ToString() + "p") : string.Empty),
                            Filesize = f.TryGetProperty("filesize", out var fsEl) && fsEl.ValueKind == JsonValueKind.Number ? fsEl.GetInt64() : (long?)null,
                            Note = f.TryGetProperty("format_note", out var fnEl) ? fnEl.GetString() ?? string.Empty : string.Empty
                        };
                        list.Add(ytf);
                    }
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("YtDlpService: ListFormatsAsync failed", ex); } catch { }
            }
            return list;
        }

        public static async Task<ResolvedMedia?> ResolveAsync(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;
            await EnsureYtDlpAsync();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpExe,
                    Arguments = $"--no-playlist -f best[acodec!=none]/best --dump-json \"{pageUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0)
                {
                    try { Logger.Error($"YtDlpService: yt-dlp exit {proc.ExitCode}: {stderr}"); } catch { }
                    return null;
                }

                // yt-dlp --dump-json outputs one JSON per video (we used --no-playlist)
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                var url = root.TryGetProperty("url", out var uEl) ? uEl.GetString() : null;
                var title = root.TryGetProperty("title", out var tEl) ? tEl.GetString() : null;
                var ext = root.TryGetProperty("ext", out var eEl) ? eEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) return null;

                // Normalize extension: avoid unknown/unknown_video; try to infer from URL; default to mp4
                string? inferred = null;
                try { var u = new Uri(url); inferred = Path.GetExtension(u.LocalPath).TrimStart('.'); } catch { }
                var extNorm = ext;
                if (string.IsNullOrWhiteSpace(extNorm)
                    || extNorm.Equals("unknown_video", StringComparison.OrdinalIgnoreCase)
                    || extNorm.Equals("unkown_video", StringComparison.OrdinalIgnoreCase)
                    || extNorm.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    extNorm = string.IsNullOrWhiteSpace(inferred) ? "mp4" : inferred;
                }
                // If inference yields weird characters, sanitize basic
                if (!string.IsNullOrWhiteSpace(extNorm))
                {
                    foreach (var c in Path.GetInvalidFileNameChars()) extNorm = extNorm.Replace(c.ToString(), string.Empty);
                }
                if (string.IsNullOrWhiteSpace(extNorm)) extNorm = "mp4";

                var safeTitle = string.IsNullOrWhiteSpace(title) ? "video" : title;
                var fileName = MakeSafeFileName(safeTitle + "." + extNorm);

                var result = new ResolvedMedia { Url = url!, Title = safeTitle, FileName = fileName, Ext = extNorm };

                // Try to include subtitles if available in JSON (when and if yt-dlp emits them without extra args)
                try
                {
                    if (root.TryGetProperty("subtitles", out var subs) && subs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var langProp in subs.EnumerateObject())
                        {
                            foreach (var track in langProp.Value.EnumerateArray())
                            {
                                var surl = track.TryGetProperty("url", out var su) ? su.GetString() : null;
                                var extS = track.TryGetProperty("ext", out var se) ? se.GetString() : "vtt";
                                if (!string.IsNullOrWhiteSpace(surl))
                                {
                                    var sfile = MakeSafeFileName(safeTitle + "." + langProp.Name + "." + extS);
                                    result.ExtraAssets.Add(new ResolvedMedia { Url = surl!, Title = safeTitle + " (" + langProp.Name + ")", FileName = sfile, Ext = extS });
                                }
                            }
                        }
                    }
                }
                catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Logger.Error("YtDlpService: ResolveAsync failed", ex); } catch { }
                return null;
            }
        }

        public static async Task<ResolvedMedia?> ResolveWithFormatAsync(string pageUrl, string formatId)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) || string.IsNullOrWhiteSpace(formatId)) return null;
            await EnsureYtDlpAsync();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpExe,
                    Arguments = $"--no-playlist -f {formatId} --dump-json \"{pageUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0) return null;
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                var url = root.TryGetProperty("url", out var uEl) ? uEl.GetString() : null;
                var title = root.TryGetProperty("title", out var tEl) ? tEl.GetString() : null;
                var ext = root.TryGetProperty("ext", out var eEl) ? eEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) return null;
                string? inferred = null;
                try { var u = new Uri(url); inferred = Path.GetExtension(u.LocalPath).TrimStart('.'); } catch { }
                var extNorm = string.IsNullOrWhiteSpace(ext) ? inferred : ext;
                if (string.IsNullOrWhiteSpace(extNorm)) extNorm = "mp4";
                var safeTitle = string.IsNullOrWhiteSpace(title) ? "video" : title;
                var fileName = MakeSafeFileName(safeTitle + "." + extNorm);
                return new ResolvedMedia { Url = url!, Title = safeTitle, FileName = fileName, Ext = extNorm };
            }
            catch (Exception ex)
            {
                try { Logger.Error("YtDlpService: ResolveWithFormatAsync failed", ex); } catch { }
                return null;
            }
        }

        public static async Task<ResolvedMedia?> ResolveWithSelectorAsync(string pageUrl, string formatSelector)
        {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;
            await EnsureYtDlpAsync();
            try
            {
                var selector = string.IsNullOrWhiteSpace(formatSelector) ? "best[acodec!=none]/best" : formatSelector;
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpExe,
                    Arguments = $"--no-playlist -f {selector} --dump-json \"{pageUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0) return null;
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                var url = root.TryGetProperty("url", out var uEl) ? uEl.GetString() : null;
                var title = root.TryGetProperty("title", out var tEl) ? tEl.GetString() : null;
                var ext = root.TryGetProperty("ext", out var eEl) ? eEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) return null;
                string? inferred = null;
                try { var u = new Uri(url); inferred = Path.GetExtension(u.LocalPath).TrimStart('.'); } catch { }
                var extNorm = string.IsNullOrWhiteSpace(ext) ? inferred : ext;
                if (string.IsNullOrWhiteSpace(extNorm)) extNorm = "mp4";
                var safeTitle = string.IsNullOrWhiteSpace(title) ? "video" : title;
                var fileName = MakeSafeFileName(safeTitle + "." + extNorm);
                return new ResolvedMedia { Url = url!, Title = safeTitle, FileName = fileName, Ext = extNorm };
            }
            catch (Exception ex)
            {
                try { Logger.Error("YtDlpService: ResolveWithSelectorAsync failed", ex); } catch { }
                return null;
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
