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

    /// <summary>Validate an existing import.csv (-k key).</summary>
    ValidateCsv,

    /// <summary>Update Jira issues via API (-u key).</summary>
    UpdateJira,

    /// <summary>View a single issue from the export file (-n key).</summary>
    ViewIssue,
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

    /// <summary>When true, per-issue diagnostic messages are shown on the console (-v).</summary>
    public bool Verbose { get; private set; }

    /// <summary>Issue number to view (-n).</summary>
    public int? IssueNumber { get; private set; }


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
                        options._errors.Add("Key -i is specified without a value (path to db-2.0.json).");
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
                        options._errors.Add("Key -o is specified without a value (path to the result).");
                    }

                    break;

                case "-h":
                case "--help":
                    // Help is handled by the caller; the mode stays None.
                    break;

                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;

                case "-k":
                case "--check":
                    options.Mode = AppMode.ValidateCsv;
                    break;

                case "-u":
                case "--update":
                    options.Mode = AppMode.UpdateJira;
                    break;

                case "-n":
                case "--number":
                    if (TryReadValue(args, ref i, out var numberValue) && int.TryParse(numberValue, out var issueNum))
                    {
                        options.IssueNumber = issueNum;
                        options.Mode = AppMode.ViewIssue;
                    }
                    else
                    {
                        options._errors.Add("Key -n is specified without a valid issue number.");
                    }

                    break;

                default:
                    options._errors.Add($"Unknown argument: {arg}");
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

            case AppMode.ValidateCsv:
                // In validate mode -o is the CSV to check; -i and -m provide optional cross-reference data.
                if (!outputExplicit)
                {
                    options.OutputPath = DefaultCsvPath;
                }

                break;

            case AppMode.UpdateJira:
                // In update mode -o is the CSV to read; -m provides map.json with Jira settings.
                if (!outputExplicit)
                {
                    options.OutputPath = DefaultCsvPath;
                }

                break;

            case AppMode.ViewIssue:
                // In view mode -o is the CSV to read.
                if (!outputExplicit)
                {
                    options.OutputPath = DefaultCsvPath;
                }

                break;

            case AppMode.None:
                options._errors.Add("No operation mode specified. Use -m (map.json), -c (import.csv), -k (validate import.csv), -u (update Jira), or -n (view issue).");
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
        BB2Jira -- converts a Bitbucket export (db-2.0.json) to Jira.

        Usage:
          BB2Jira -m [-i db-2.0.json] [-o map.json]
          BB2Jira -c -i db-2.0.json -m map.json -o import.csv
          BB2Jira -k [-o import.csv] [-i db-2.0.json] [-m map.json]
          BB2Jira -u [-o import.csv] [-m map.json]
          BB2Jira -n <issue_number> [-o import.csv]

        Keys:
          -m, --map     generate map.json mode (without -c/-k/-u);
                        path to map.json (together with -c, -k or -u)
          -c, --csv     generate import.csv mode
          -k, --check   validate an existing import.csv
          -u, --update  update Jira issues via API
          -n, --number  view a single issue from the export file
          -i, --input   path to the Bitbucket export file (db-2.0.json)
          -o, --output  path to the result (map.json or import.csv)
          -v, --verbose show per-issue diagnostics on the console
          -h, --help    show help
        """;
}
