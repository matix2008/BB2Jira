namespace BB2Jira.Cli;

/// <summary>Utility operation mode.</summary>
public enum AppMode
{
    /// <summary>Mode is not defined (no -m or -c keys).</summary>
    None,

    /// <summary>Generate map.json (-m key without -c).</summary>
    GenerateMap,

    /// <summary>Generate import.csv (-c key).</summary>
    GenerateCsv,
}

/// <summary>
/// Parses command-line arguments.
///
/// The -m key is overloaded:
///   * without -c  -- map.json generation mode;
///   * together with -c  -- path to an existing map.json.
/// </summary>
public sealed class CliOptions
{
    private const string DefaultInputPath = "db-2.0.json";
    private const string DefaultMapPath = "map.json";
    private const string DefaultCsvPath = "import.csv";

    public AppMode Mode { get; private set; } = AppMode.None;

    /// <summary>Path to the Bitbucket export file (-i).</summary>
    public string InputPath { get; private set; } = DefaultInputPath;

    /// <summary>Path to the map.json mapping file (-m). Source in CSV mode, result in map mode.</summary>
    public string MapPath { get; private set; } = DefaultMapPath;

    /// <summary>Path to the result (-o): import.csv in CSV mode, map.json in map mode.</summary>
    public string OutputPath { get; private set; } = DefaultCsvPath;

    public IReadOnlyList<string> Errors => _errors;

    private readonly List<string> _errors = new();

    public bool IsValid => _errors.Count == 0 && Mode != AppMode.None;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var mapKeyPresent = false;
        bool? outputExplicit = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-c":
                case "--csv":
                    options.Mode = AppMode.GenerateCsv;
                    break;

                case "-m":
                case "--map":
                    mapKeyPresent = true;
                    if (TryReadValue(args, ref i, out var mapValue))
                    {
                        options.MapPath = mapValue;
                    }

                    break;

                case "-i":
                case "--input":
                    if (TryReadValue(args, ref i, out var inputValue))
                    {
                        options.InputPath = inputValue;
                    }
                    else
                    {
                        options._errors.Add("Ключ -i указан без значения (путь к db-2.0.json).");
                    }

                    break;

                case "-o":
                case "--output":
                    if (TryReadValue(args, ref i, out var outputValue))
                    {
                        options.OutputPath = outputValue;
                        outputExplicit = true;
                    }
                    else
                    {
                        options._errors.Add("Ключ -o указан без значения (путь к результату).");
                    }

                    break;

                case "-h":
                case "--help":
                    // Help is handled by the caller; the mode stays None.
                    break;

                default:
                    options._errors.Add($"Неизвестный аргумент: {arg}");
                    break;
            }
        }

        // -m without -c means map.json generation mode.
        if (options.Mode != AppMode.GenerateCsv && mapKeyPresent)
        {
            options.Mode = AppMode.GenerateMap;
        }

        ApplyModeDefaults(options, outputExplicit ?? false);
        return options;
    }

    private static void ApplyModeDefaults(CliOptions options, bool outputExplicit)
    {
        switch (options.Mode)
        {
            case AppMode.GenerateMap:
                // In map mode the result is map.json. If -o is not set, use the path from -m.
                if (!outputExplicit)
                {
                    options.OutputPath = options.MapPath;
                }

                break;

            case AppMode.GenerateCsv:
                if (!outputExplicit)
                {
                    options.OutputPath = DefaultCsvPath;
                }

                break;

            case AppMode.None:
                options._errors.Add("Не указан режим работы. Используйте -m (map.json) или -c (import.csv).");
                break;
        }
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 < args.Length && !IsFlag(args[index + 1]))
        {
            index++;
            value = args[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsFlag(string arg) =>
        arg.Length > 1 && arg[0] == '-' && !char.IsDigit(arg[1]);

    public static string GetUsage() =>
        """
        BB2Jira — конвертер экспорта Bitbucket (db-2.0.json) в Jira.

        Использование:
          BB2Jira -m [-i db-2.0.json] [-o map.json]
          BB2Jira -c -i db-2.0.json -m map.json -o import.csv

        Ключи:
          -m, --map     режим генерации map.json (без -c);
                        путь к map.json (вместе с -c)
          -c, --csv     режим генерации import.csv
          -i, --input   путь к файлу экспорта Bitbucket (db-2.0.json)
          -o, --output  путь к результату (map.json или import.csv)
          -h, --help    показать справку
        """;
}
