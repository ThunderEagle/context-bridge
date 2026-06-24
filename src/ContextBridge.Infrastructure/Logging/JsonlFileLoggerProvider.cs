using Microsoft.Extensions.Logging;

namespace ContextBridge.Infrastructure.Logging;

public sealed class JsonlFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly int _retainedDays;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateOnly _currentDate;

    public JsonlFileLoggerProvider(string logDirectory, int retainedDays = 7)
    {
        _logDirectory = logDirectory;
        _retainedDays = retainedDays;
        Directory.CreateDirectory(logDirectory);
    }

    public ILogger CreateLogger(string categoryName) =>
        new JsonlFileLogger(categoryName, this);

    internal void Write(string json)
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (_writer is null || today != _currentDate)
            {
                RotateTo(today);
            }
            _writer!.WriteLine(json);
        }
    }

    private void RotateTo(DateOnly date)
    {
        _writer?.Dispose();
        _currentDate = date;

        var path = Path.Combine(_logDirectory, $"contextbridge-{date:yyyyMMdd}.jsonl");
        _writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };

        PurgeOldFiles(date);
    }

    private void PurgeOldFiles(DateOnly today)
    {
        foreach (var file in Directory.GetFiles(_logDirectory, "contextbridge-*.jsonl"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            // stem format: "contextbridge-YYYYMMDD" — date is the last 8 chars
            if (stem.Length >= 8 &&
                DateOnly.TryParseExact(stem[^8..], "yyyyMMdd", out var fileDate) &&
                today.DayNumber - fileDate.DayNumber >= _retainedDays)
            {
                try { File.Delete(file); } catch { /* best-effort; file may be locked */ }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
