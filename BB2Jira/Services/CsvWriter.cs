using System.Text;

namespace BB2Jira.Services;

/// <summary>
/// Writes CSV using the standard escaping rules (RFC 4180):
/// a value is quoted if it contains the delimiter, a quote, or a line break;
/// inner quotes are doubled. The delimiter is a comma.
/// </summary>
public sealed class CsvWriter
{
    private const char Delimiter = ',';
    private readonly StringBuilder _builder = new();

    /// <summary>Appends a CSV row from a set of fields.</summary>
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

    /// <summary>Returns the accumulated CSV text.</summary>
    public override string ToString() => _builder.ToString();

    /// <summary>Saves the CSV to a file in utf-8-sig encoding (UTF-8 with BOM).</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, _builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Escapes a single value according to CSV rules.</summary>
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
