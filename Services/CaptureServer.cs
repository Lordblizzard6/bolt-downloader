using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.IO;
using System.Linq;

namespace BoltDownloader.Services
{
    public class CaptureItem
    {
        public string Url { get; set; } = string.Empty;
        public string? Referer { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; } // e.g., video, audio, file
    }

    public static class CaptureServer
    {
        private static HttpListener? _listener;
        private static TcpListener? _tcpListener;
        private static Thread? _thread;
        private static int _port;
        private static volatile bool _running;
        private static readonly object _typesLock = new object();
        private static string[] _fileTypes = new[] { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".mp3", ".aac", ".flac", ".wav", ".m3u8", ".zip", ".rar", ".7z", ".pdf" };
        private const long MaxBodyBytes = 1024 * 1024; // 1 MB

        private static bool IsHttpOrHttps(string? url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return false;
                var u = new Uri(url);
                return string.Equals(u.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static event EventHandler<CaptureItem>? Captured;

        public static void Start(int port = 17890)
        {
            if (_running) return;
            _port = port;
            _running = true;
            _thread = new Thread(ServerLoop) { IsBackground = true, Name = "CaptureServer" };
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            _listener = null;
        }

        public static void UpdateFileTypes(System.Collections.Generic.IEnumerable<string> items)
        {
            try
            {
                var arr = items?.Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.StartsWith('.') ? s : "." + s)
                    .Distinct()
                    .ToArray() ?? Array.Empty<string>();
                if (arr.Length == 0) return;
                lock (_typesLock)
                {
                    _fileTypes = arr;
                }
                try { Logger.Info($"CaptureServer: updated filetypes -> {string.Join(",", arr)}"); } catch { }
            }
            catch { }
        }

        private static void ServerLoop()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
                try { Logger.Info($"CaptureServer: HttpListener started on {_port}"); } catch { }

                while (_running)
                {
                    HttpListenerContext? ctx = null;
                    try { ctx = _listener.GetContext(); }
                    catch when (!_running) { break; }
                    catch { continue; }

                    _ = ThreadPool.QueueUserWorkItem(_ => Handle(ctx!));
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("CaptureServer: HttpListener failed, falling back to TcpListener", ex); } catch { }
                // Fallback: raw TCP listener (no URLACL needed)
                SocketLoop();
            }
        }

        private static void SocketLoop()
        {
            try
            {
                _tcpListener = new TcpListener(System.Net.IPAddress.Loopback, _port);
                _tcpListener.Start();
                try { Logger.Info($"CaptureServer: TcpListener started on {_port}"); } catch { }
                while (_running)
                {
                    TcpClient? client = null;
                    try { client = _tcpListener.AcceptTcpClient(); }
                    catch when (!_running) { break; }
                    catch { continue; }
                    _ = ThreadPool.QueueUserWorkItem(_ => HandleClient(client!));
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("CaptureServer: TcpListener failed", ex); } catch { }
                _running = false;
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    using var ns = client.GetStream();
                    using var reader = new StreamReader(ns, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
                    using var writer = new StreamWriter(ns, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

                    // Read request line and headers
                    var requestLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(requestLine)) return;
                    var parts = requestLine.Split(' ');
                    var method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
                    var path = parts.Length > 1 ? parts[1] : "/";

                    var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        var idx = line.IndexOf(':');
                        if (idx > 0)
                        {
                            var name = line.Substring(0, idx).Trim();
                            var val = line.Substring(idx + 1).Trim();
                            headers[name] = val;
                        }
                    }

                    int contentLength = 0;
                    headers.TryGetValue("Content-Length", out var clStr);
                    int.TryParse(clStr, out contentLength);
                    if (contentLength > MaxBodyBytes)
                    {
                        WriteHttpResponse(writer, 413, "payload too large", isJson: false);
                        return;
                    }
                    string body = string.Empty;
                    if (contentLength > 0)
                    {
                        var buf = new char[contentLength];
                        int read = 0;
                        while (read < contentLength)
                        {
                            var n = reader.Read(buf, read, contentLength - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        body = new string(buf, 0, read);
                    }

                    // Routing
                    var absPath = path.Trim('/').ToLowerInvariant();
                    if (method == "OPTIONS")
                    {
                        WriteHttpResponse(writer, 204, "", isJson: false);
                        return;
                    }
                    if (absPath == "health")
                    {
                        var json = JsonSerializer.Serialize(new { ok = true, port = _port });
                        WriteHttpResponse(writer, 200, json, isJson: true);
                        return;
                    }
                    if (absPath == "filetypes")
                    {
                        string[] types; lock (_typesLock) { types = _fileTypes.ToArray(); }
                        var json = JsonSerializer.Serialize(new { types });
                        WriteHttpResponse(writer, 200, json, isJson: true);
                        return;
                    }
                    if (absPath == "api/v1/settings")
                    {
                        string[] types; lock (_typesLock) { types = _fileTypes.ToArray(); }
                        var json = JsonSerializer.Serialize(new { integration_port = _port, capture_file_types = string.Join(",", types) });
                        WriteHttpResponse(writer, 200, json, isJson: true);
                        return;
                    }
                    if (absPath == "api/add")
                    {
                        if (method == "POST")
                        {
                            try
                            {
                                var map = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                var url = map != null && map.TryGetValue("url", out var u) ? u : null;
                                var referer = map != null && map.TryGetValue("referer", out var r) ? r : null;
                                var title = map != null && map.TryGetValue("title", out var t) ? t : null;
                                if (string.IsNullOrWhiteSpace(url) || !IsHttpOrHttps(url))
                                {
                                    WriteHttpResponse(writer, 400, "invalid payload", isJson: false);
                                    return;
                                }
                                try { Logger.Info($"CaptureServer: (tcp) /api/add url={(url ?? "").Substring(0, Math.Min(120, url?.Length ?? 0))}"); } catch { }
                                try { Captured?.Invoke(null!, new CaptureItem { Url = url!, Referer = referer, Title = title, Type = "file" }); } catch { }
                                var ok = JsonSerializer.Serialize(new { ok = true });
                                WriteHttpResponse(writer, 200, ok, isJson: true);
                                return;
                            }
                            catch { }
                        }
                        WriteHttpResponse(writer, 400, "invalid payload", isJson: false);
                        return;
                    }
                    if (absPath == "capture")
                    {
                        CaptureItem? item = null;
                        if (method == "POST")
                        {
                            try
                            {
                                if (body.TrimStart().StartsWith("["))
                                {
                                    var items = JsonSerializer.Deserialize<CaptureItem[]>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<CaptureItem>();
                                    try { Logger.Info($"CaptureServer: (tcp) /capture received array, count={items.Length}"); } catch { }
                                    foreach (var it in items)
                                    {
                                        if (!string.IsNullOrWhiteSpace(it?.Url) && IsHttpOrHttps(it!.Url))
                                            try { Captured?.Invoke(null!, it!); } catch { }
                                    }
                                    var json = JsonSerializer.Serialize(new { ok = true, count = items.Length });
                                    WriteHttpResponse(writer, 200, json, isJson: true);
                                    return;
                                }
                                else
                                {
                                    item = JsonSerializer.Deserialize<CaptureItem>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                }
                            }
                            catch { }
                        }
                        else if (method == "GET")
                        {
                            // Parse query string manually
                            string? query = null;
                            var qIdx = path.IndexOf('?');
                            if (qIdx >= 0) query = path.Substring(qIdx + 1);
                            var url = GetQueryValue(query, "url") ?? string.Empty;
                            var referer = GetQueryValue(query, "referer");
                            var title = GetQueryValue(query, "title");
                            var type = GetQueryValue(query, "type");
                            item = new CaptureItem { Url = url, Referer = referer, Title = title, Type = type };
                        }

                        if (item == null || string.IsNullOrWhiteSpace(item.Url) || !IsHttpOrHttps(item.Url))
                        {
                            WriteHttpResponse(writer, 400, "invalid payload", isJson: false);
                            return;
                        }
                        try { Captured?.Invoke(null!, item); } catch { }
                        WriteHttpResponse(writer, 200, JsonSerializer.Serialize(new { ok = true }), isJson: true);
                        return;
                    }

                    WriteHttpResponse(writer, 404, "not found", isJson: false);
                }
                catch { }
            }
        }

        private static string? GetQueryValue(string? query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    try { return Uri.UnescapeDataString(kv[1]); } catch { return kv[1]; }
                }
            }
            return null;
        }

        private static void WriteHttpResponse(StreamWriter writer, int status, string body, bool isJson)
        {
            writer.WriteLine($"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 204 ? "No Content" : status == 400 ? "Bad Request" : status == 404 ? "Not Found" : status == 413 ? "Payload Too Large" : "Error")}\r");
            writer.WriteLine("Access-Control-Allow-Origin: *");
            writer.WriteLine("Access-Control-Allow-Headers: Content-Type");
            writer.WriteLine("Access-Control-Allow-Methods: GET,POST,OPTIONS");
            if (isJson) writer.WriteLine("Content-Type: application/json; charset=utf-8");
            else writer.WriteLine("Content-Type: text/plain; charset=utf-8");
            var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            writer.WriteLine($"Content-Length: {bytes.Length}");
            writer.WriteLine();
            writer.Flush();
            if (bytes.Length > 0)
            {
                writer.BaseStream.Write(bytes, 0, bytes.Length);
                writer.BaseStream.Flush();
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                // CORS for extension
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                var path = ctx.Request.Url?.AbsolutePath?.Trim('/')?.ToLowerInvariant() ?? string.Empty;
                if (path == "health")
                {
                    WriteJson(ctx, new { ok = true, port = _port });
                    return;
                }
                if (path == "filetypes")
                {
                    string[] types; lock (_typesLock) { types = _fileTypes.ToArray(); }
                    WriteJson(ctx, new { types });
                    return;
                }
                if (path == "api/v1/settings")
                {
                    string[] types; lock (_typesLock) { types = _fileTypes.ToArray(); }
                    WriteJson(ctx, new { integration_port = _port, capture_file_types = string.Join(",", types) });
                    return;
                }
                if (path == "api/add")
                {
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        using var sr = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                        var body = sr.ReadToEnd();
                        try
                        {
                            var payload = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            var url = payload != null && payload.TryGetValue("url", out var u) ? u : null;
                            var referer = payload != null && payload.TryGetValue("referer", out var r) ? r : null;
                            var title = payload != null && payload.TryGetValue("title", out var t) ? t : null;
                            if (string.IsNullOrWhiteSpace(url) || !IsHttpOrHttps(url))
                            {
                                ctx.Response.StatusCode = 400; WriteString(ctx, "invalid payload"); return;
                            }
                            try { Logger.Info($"CaptureServer: /api/add url={(url ?? "").Substring(0, Math.Min(120, url?.Length ?? 0))}"); } catch { }
                            try { Captured?.Invoke(null!, new CaptureItem { Url = url!, Referer = referer, Title = title, Type = "file" }); } catch { }
                            WriteJson(ctx, new { ok = true });
                            return;
                        }
                        catch { }
                    }
                    ctx.Response.StatusCode = 400; WriteString(ctx, "invalid payload"); return;
                }
                if (path == "capture")
                {
                    CaptureItem? item = null;
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        using var sr = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                        var body = sr.ReadToEnd();
                        try
                        {
                            // Try array first
                            if (body.TrimStart().StartsWith("["))
                            {
                                var items = JsonSerializer.Deserialize<CaptureItem[]>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<CaptureItem>();
                                try { Logger.Info($"CaptureServer: /capture received array, count={items.Length}"); } catch { }
                                foreach (var it in items)
                                {
                                    if (!string.IsNullOrWhiteSpace(it?.Url) && IsHttpOrHttps(it!.Url))
                                        try { Captured?.Invoke(null!, it!); } catch { }
                                }
                                WriteJson(ctx, new { ok = true, count = items.Length });
                                return;
                            }
                            else
                            {
                                item = JsonSerializer.Deserialize<CaptureItem>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                try { if (item != null) Logger.Info($"CaptureServer: /capture received single url={(item.Url ?? "").Substring(0, Math.Min(120, item.Url?.Length ?? 0))}"); } catch { }
                            }
                        }
                        catch { }
                    }
                    else if (ctx.Request.HttpMethod == "GET")
                    {
                        var q = ctx.Request.QueryString;
                        item = new CaptureItem
                        {
                            Url = q["url"] ?? string.Empty,
                            Referer = q["referer"],
                            Title = q["title"],
                            Type = q["type"]
                        };
                    }

                    if (item == null || string.IsNullOrWhiteSpace(item.Url))
                    {
                        ctx.Response.StatusCode = 400;
                        WriteString(ctx, "invalid payload");
                        return;
                    }

                    try { Captured?.Invoke(null!, item); } catch { }
                    WriteJson(ctx, new { ok = true });
                    return;
                }

                ctx.Response.StatusCode = 404;
                WriteString(ctx, "not found");
            }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        private static void WriteString(HttpListenerContext ctx, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static void WriteJson(HttpListenerContext ctx, object o)
        {
            var json = JsonSerializer.Serialize(o);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
