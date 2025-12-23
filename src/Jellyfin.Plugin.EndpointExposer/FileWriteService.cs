// FileWriteService.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    /// <summary>
    /// FileWriteService: exposes safe, atomic write helpers and backup rotation.
    /// This version does NOT start an HttpListener (keeps service portable across runtimes).
    /// If you need the legacy listener, we can add it back behind a compile-time guard.
    /// </summary>
    public class FileWriteService : BackgroundService
    {
        private readonly PluginConfiguration _config;
        private readonly ILogger<FileWriteService> _logger;

        public FileWriteService(PluginConfiguration config, JellyfinAuth auth, ILogger<FileWriteService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // BackgroundService base: no background work by default (listener removed).
        protected override Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
        {
            // No background listener by default. If you want the listener behavior,
            // we can reintroduce it behind a runtime/framework check.
            _logger.LogDebug("FileWriteService started (no HttpListener).");
            return Task.CompletedTask;
        }

        #region Public atomic write helpers

        /// <summary>
        /// Atomically write text to the specified path using the provided encoding.
        /// Creates parent directory if missing. Creates a timestamped backup of the existing file if present.
        /// </summary>
        public async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(content ?? string.Empty);
            await WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        }

        /// <summary>
        /// Atomically write bytes to the specified path.
        /// Creates parent directory if missing. Creates a timestamped backup of the existing file if present.
        /// </summary>
        public async Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            bytes ??= Array.Empty<byte>();

            try
            {
                var dir = Path.GetDirectoryName(path) ?? ".";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // If target exists, create a backup copy first (in backups subfolder)
                if (File.Exists(path) && (_config?.MaxBackups ?? 0) > 0)
                {
                    try
                    {
                        var backupDir = Path.Combine(dir, "backups");
                        Directory.CreateDirectory(backupDir);
                        var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        var backupName = $"{Path.GetFileName(path)}.{ts}.bak";
                        var backupPath = Path.Combine(backupDir, backupName);
                        File.Copy(path, backupPath, overwrite: true);
                        TrimBackups(backupDir, Path.GetFileName(path));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create backup for {Path}", path);
                    }
                }

                // Write to a temp file in the same directory then move to final path
                var tempFile = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                await File.WriteAllBytesAsync(tempFile, bytes).ConfigureAwait(false);

                // Replace existing file atomically where supported
                try
                {
                    if (File.Exists(path))
                    {
                        File.Replace(tempFile, path, null);
                    }
                    else
                    {
                        File.Move(tempFile, path);
                    }
                }
                catch (PlatformNotSupportedException)
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tempFile, path);
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                    _logger.LogError(ex, "Failed to move temp file to final path {Path}", path);
                    throw;
                }

                _logger.LogDebug("Wrote file {Path} ({Bytes} bytes)", path, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteAllBytesAsync failed for {Path}", path);
                throw;
            }
        }

        private void TrimBackups(string backupDir, string originalFileName)
        {
            try
            {
                var maxBackups = _config?.MaxBackups ?? 0;
                if (maxBackups <= 0) return;

                var pattern = originalFileName + ".*.bak";
                var files = Directory.EnumerateFiles(backupDir, pattern, SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.CreationTimeUtc)
                    .ToList();

                for (int i = maxBackups; i < files.Count; i++)
                {
                    try
                    {
                        files[i].Delete();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old backup {Backup}", files[i].FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TrimBackups failed in {BackupDir} for {File}", backupDir, originalFileName);
            }
        }

        #endregion
    }
}
