using System;
using System.IO;

namespace Il2CppAssemblyFixer.Shared;

/// <summary>
/// Append-only log writer that mirrors structured log lines into
/// &lt;MelonLoaderDir&gt;/fixer.log alongside MelonLoader's Latest.log.
/// Failures during open/write are swallowed — logging must never crash the host.
/// </summary>
internal sealed class FileLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    public string? Path { get; }

    public FileLogger(string melonLoaderDir, string fileName, string runBanner)
    {
        try
        {
            Directory.CreateDirectory(melonLoaderDir);
            Path = System.IO.Path.Combine(melonLoaderDir, System.IO.Path.GetFileName(fileName));

            var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(fs) { AutoFlush = true };
            _writer.WriteLine();
            _writer.WriteLine($"════════ {runBanner} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ════════");
        }
        catch (Exception)
        {
            // Logging is best-effort and must not prevent the fixer from running.
            _writer = null;
        }
    }

    public void WriteLine(string line)
    {
        if (_writer == null) return;
        try
        {
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss}  {line}");
        }
        catch (Exception)
        {
            // Logging is best-effort; failed writes are intentionally ignored.
        }
    }

    public void Dispose()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch (Exception)
        {
            // Cleanup errors are not actionable for callers and must not mask
            // the fixer result.
        }
    }
}
