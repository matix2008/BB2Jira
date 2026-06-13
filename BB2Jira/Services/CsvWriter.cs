using System.Text;

namespace BB2Jira.Services;

/// <summary>
/// «апись CSV по стандартным правилам экранировани€ (RFC 4180):
/// значение берЄтс€ в кавычки, если содержит разделитель, кавычку или перевод строки;
/// внутренние кавычки удваиваютс€. –азделитель Ч зап€та€.
/// </summary>
public sealed class CsvWriter
{
    private const char Delimiter = ',';
    private readonly StringBuilder _builder = new();

    /// <summary>ƒобавл€ет строку CSV из набора полей.</summary>
    public void WriteRow(IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                _builder.Append(Delimiter);
            }

            _builder.Append(Escape(field));
            first = false;
        }

        _builder.Append("\r\n");
    }

    /// <summary>¬озвращает накопленный CSV-текст.</summary>
    public override string ToString() => _builder.ToString();

    /// <summary>—охран€ет CSV в файл в кодировке utf-8-sig (UTF-8 с BOM).</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, _builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Ёкранирует одно значение по правилам CSV.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var mustQuote = value.IndexOfAny(new[] { Delimiter, '"', '\r', '\n' }) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
