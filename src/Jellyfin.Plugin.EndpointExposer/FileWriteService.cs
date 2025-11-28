using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

public class FileWriteService : BackgroundService
{
    private readonly EndpointOptions _opts;
    private readonly ILogger<FileWriteService> _logger;
    private HttpListener? _listener;

    public FileWriteService(EndpointOptions opts, ILogger<FileWriteService> logger)
    {
        _opts = opts;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(_opts.ListenPrefix);
        _listener.Start();
        _logger.LogInformation("Endpoint listener started on {Prefix}", _opts.ListenPrefix);

        return Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext ctx = null!;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Listener error");
                    continue;
                }

                _ = Task.Run(() => HandleRequestAsync(ctx), stoppingToken);
            }
        }, stoppingToken);
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;

            if (req.HttpMethod != "POST" || req.Url?.AbsolutePath != "/write")
            {
                res.StatusCode = 404;
                res.Close();
                return;
            }

            // Simple bearer token auth
            var auth = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ") || auth.Substring(7) != _opts.ApiKey)
            {
                res.StatusCode = 401;
                res.Close();
                return;
            }

            using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            var body = await sr.ReadToEndAsync().ConfigureAwait(false);

            // Validate JSON
            JObject? payload;
            try
            {
                payload = JObject.Parse(body);
            }
            catch (Exception)
            {
                res.StatusCode = 400;
                var b = Encoding.UTF8.GetBytes("Invalid JSON");
                await res.OutputStream.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
                res.Close();
                return;
            }

            // Determine filename (example: payload.filename or timestamp)
            var filename = payload.Value<string>("filename") ?? $"payload-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";

            // Sanitize filename (remove path separators)
            filename = Path.GetFileName(filename);

            Directory.CreateDirectory(_opts.OutputDirectory);

            var tempPath = Path.Combine(_opts.OutputDirectory, filename + ".tmp");
            var finalPath = Path.Combine(_opts.OutputDirectory, filename);

            // Atomic write: write temp then move
            await File.WriteAllTextAsync(tempPath, payload.ToString(), Encoding.UTF8).ConfigureAwait(false);
            File.Move(tempPath, finalPath, overwrite: true);

            _logger.LogInformation("Wrote file {File}", finalPath);

            res.StatusCode = 200;
            var ok = Encoding.UTF8.GetBytes("OK");
            await res.OutputStream.WriteAsync(ok, 0, ok.Length).ConfigureAwait(false);
            res.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { }
        return base.StopAsync(cancellationToken);
    }
}
