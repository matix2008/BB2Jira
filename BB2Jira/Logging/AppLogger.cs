using System.Text;

namespace BB2Jira.Logging;

/// <summary>Уровень сообщения лога.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Простой логгер: пишет сообщения в консоль и накапливает их для последующей
/// записи в файл (import.log / map.log).
/// </summary>
public sealed class AppLogger
{
    private readonly List<string> _lines = new();
    private readonly bool _echoToConsole;

    public AppLogger(bool echoToConsole = true)
    {
        _echoToConsole = echoToConsole;
    }

    public int WarningCount { get; private set; }

    public int ErrorCount { get; private set; }

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warning(string message)
    {
        WarningCount++;
        Write(LogLevel.Warning, message);
    }

    public void Error(string message)
    {
        ErrorCount++;
        Write(LogLevel.Error, message);
    }

    /// <summary>Записывает «сырую» строку без отметки времени и уровня (для заголовков/разделителей).</summary>
    public void Raw(string message)
    {
        _lines.Add(message);
        if (_echoToConsole)
        {
            Console.WriteLine(message);
        }
    }

    private void Write(LogLevel level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"{timestamp} [{level.ToString().ToUpperInvariant()}] {message}";
        _lines.Add(line);

        if (_echoToConsole)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => previous,
            };
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
        }
    }

    /// <summary>Сохраняет накопленный лог в файл в кодировке UTF-8.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(path, _lines, new UTF8Encoding(false));
    }
}
