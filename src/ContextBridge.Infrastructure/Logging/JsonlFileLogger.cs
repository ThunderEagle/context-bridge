using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ContextBridge.Infrastructure.Logging;

internal sealed class JsonlFileLogger(string categoryName, JsonlFileLoggerProvider provider) : ILogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) { return; }

        // Two-pass: extract template + properties first so @mt appears before @m in output
        string? messageTemplate = null;
        var properties = new Dictionary<string, object?>();

        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                if (key == "{OriginalFormat}")
                {
                    messageTemplate = value?.ToString();
                }
                else
                {
                    properties[key] = value;
                }
            }
        }

        var entry = new Dictionary<string, object?>
        {
            ["@t"] = DateTimeOffset.UtcNow.ToString("O"),
            ["@l"] = ToShortLevel(logLevel)
        };

        if (messageTemplate is not null)
        {
            entry["@mt"] = messageTemplate;
        }

        entry["@m"] = formatter(state, exception);
        entry["cat"] = categoryName;

        foreach (var (key, value) in properties)
        {
            entry[key] = value;
        }

        if (exception is not null)
        {
            entry["@x"] = exception.ToString();
        }

        provider.Write(JsonSerializer.Serialize(entry, SerializerOptions));
    }

    private static string ToShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Info",
        LogLevel.Warning => "Warn",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Fatal",
        _ => level.ToString()
    };
}
