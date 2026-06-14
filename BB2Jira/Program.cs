using System.Reflection;
using BB2Jira.Cli;
using BB2Jira.Services;
using BB2Jira.Services.Jira;
using Microsoft.Extensions.Logging;
using Serilog;

PrintBanner();

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine(CliOptions.GetUsage());
    return args.Length == 0 ? 1 : 0;
}

var options = CliOptions.Parse(args);

if (!options.IsValid)
{
    foreach (var error in options.Errors)
    {
        Console.Error.WriteLine($"Error: {error}");
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.GetUsage());
    return 2;
}

return options.Mode switch
{
    AppMode.GenerateMap => RunGenerateMap(options),
    AppMode.GenerateCsv => RunGenerateCsv(options),
    AppMode.ValidateCsv => RunValidateCsv(options),
    AppMode.UpdateJira  => RunUpdateJira(options),
    _ => 2,
};

static int RunGenerateMap(CliOptions options)
{
    if (!ConfirmOverwrite(options.OutputPath, "map.json (manual edits will be preserved)"))
    {
        return 1;
    }

    using var loggerFactory = CreateLoggerFactory(Path.ChangeExtension(options.OutputPath, ".log"), options.Verbose);
    var logger = loggerFactory.CreateLogger("BB2Jira");

    logger.LogInformation("Mode: generate map.json");
    logger.LogInformation("Input file: {InputPath}", options.InputPath);
    logger.LogInformation("Mapping file: {MapPath}", options.OutputPath);

    try
    {
        var export = BitbucketLoader.Load(options.InputPath);
        logger.LogInformation("Loaded issues: {IssueCount}", export.Issues.Count);

        MapGenerator.Generate(export, options.OutputPath, logger);
        return 0;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.LogError("{Message}", ex.Message);
        return 1;
    }
}

static int RunGenerateCsv(CliOptions options)
{
    if (!ConfirmOverwrite(options.OutputPath, "import.csv"))
    {
        return 1;
    }

    using var loggerFactory = CreateLoggerFactory(Path.ChangeExtension(options.OutputPath, ".log"), options.Verbose);
    var logger = loggerFactory.CreateLogger("BB2Jira");

    logger.LogInformation("Mode: generate import.csv");
    logger.LogInformation("Input file: {InputPath}", options.InputPath);
    logger.LogInformation("Mapping file: {MapPath}", options.MapPath);
    logger.LogInformation("Result: {OutputPath}", options.OutputPath);

    try
    {
        var export = BitbucketLoader.Load(options.InputPath);
        logger.LogInformation("Loaded issues: {IssueCount}", export.Issues.Count);

        var map = MapLoader.Load(options.MapPath);
        CsvGenerator.Generate(export, map, options.OutputPath, logger);
        return 0;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.LogError("{Message}", ex.Message);
        return 1;
    }
}

static int RunValidateCsv(CliOptions options)
{
    // Log file is placed next to the validated CSV (import-check.log).
    var logPath = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? string.Empty,
        "import-check.log");

    using var loggerFactory = CreateLoggerFactory(logPath, options.Verbose);
    var logger = loggerFactory.CreateLogger("BB2Jira");

    logger.LogInformation("Mode: validate import.csv");
    logger.LogInformation("CSV file: {CsvPath}", options.OutputPath);

    try
    {
        // Load export and map only when they actually exist at the specified paths.
        BB2Jira.Models.Bitbucket.BitbucketExport? export = null;
        BB2Jira.Models.Mapping.MapFile? map = null;

        if (File.Exists(options.InputPath))
        {
            logger.LogInformation("Input file: {InputPath}", options.InputPath);
            export = BitbucketLoader.Load(options.InputPath);
            logger.LogInformation("Loaded issues: {IssueCount}", export.Issues.Count);
        }
        else
        {
            logger.LogDebug("Input file not found -- cross-reference check skipped: {InputPath}", options.InputPath);
        }

        if (File.Exists(options.MapPath))
        {
            logger.LogInformation("Mapping file: {MapPath}", options.MapPath);
            map = MapLoader.Load(options.MapPath);
        }
        else
        {
            logger.LogDebug("Map file not found -- Issue Type check skipped: {MapPath}", options.MapPath);
        }

        var passed = CsvValidator.Validate(options.OutputPath, logger, export, map);
        return passed ? 0 : 2;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.LogError("{Message}", ex.Message);
        return 1;
    }
}

static int RunUpdateJira(CliOptions options)
{
    var csvPath = options.OutputPath;
    var logPath = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? string.Empty,
        "import-update.log");
    var progressPath = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? string.Empty,
        "import-update.progress");

    using var loggerFactory = CreateLoggerFactory(logPath, options.Verbose);
    var logger = loggerFactory.CreateLogger("BB2Jira");

    logger.LogInformation("Mode: update Jira");
    logger.LogInformation("CSV file: {CsvPath}", csvPath);
    logger.LogInformation("Mapping file: {MapPath}", options.MapPath);

    // CSV must exist before we do anything.
    if (!File.Exists(csvPath))
    {
        logger.LogError("import.csv not found: {CsvPath}", csvPath);
        return 1;
    }

    try
    {
        var map = MapLoader.Load(options.MapPath);

        if (map.Jira is null || !map.Jira.IsConfigured)
        {
            logger.LogError(
                "The 'jira' section in map.json is missing or incomplete. "
                + "Fill in baseUrl, projectKey, email, apiToken and bitbucketRepoUrl.");
            return 1;
        }

        using var client = new JiraClient(map.Jira, logger);
        var updater = new JiraUpdater(client, map.Jira, logger);

        var passed = updater.RunAsync(csvPath, progressPath).GetAwaiter().GetResult();
        return passed ? 0 : 2;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.LogError("{Message}", ex.Message);
        return 1;
    }
}

// Prompts the user to confirm before overwriting an existing output file.
// Returns true immediately when the file does not exist.
static bool ConfirmOverwrite(string path, string description)
{
    if (!File.Exists(path))
    {
        return true;
    }

    Console.WriteLine($"Output file already exists: {path}");
    Console.Write($"Overwrite {description}? [y/N]: ");
    var answer = Console.ReadLine()?.Trim() ?? string.Empty;

    if (answer.Equals("y", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    Console.WriteLine("Operation cancelled.");
    return false;
}

// Creates a Serilog logger factory that writes to the console and to the log file (import.log / map.log).
// The file always captures Debug-level detail; the console shows it only when verbose is enabled.
static ILoggerFactory CreateLoggerFactory(string logPath, bool verbose)
{
    const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
    var consoleLevel = verbose
        ? Serilog.Events.LogEventLevel.Debug
        : Serilog.Events.LogEventLevel.Information;

    var serilogLogger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(restrictedToMinimumLevel: consoleLevel, outputTemplate: template)
        .WriteTo.File(logPath, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug, outputTemplate: template)
        .CreateLogger();

    return LoggerFactory.Create(builder => builder
        .SetMinimumLevel(LogLevel.Debug)
        .AddSerilog(serilogLogger, dispose: true));
}

// Prints the utility name, version, and copyright banner on every launch.
static void PrintBanner()
{
    var assembly = Assembly.GetExecutingAssembly();
    var name = assembly.GetName();
    var version = name.Version?.ToString(3) ?? "1.0.0";
    var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? name.Name ?? "BB2Jira";
    var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "(C) Realant Ltd., 2026. Apache license 2.0";

    Console.WriteLine($"{product} v{version}");
    Console.WriteLine(copyright);
    Console.WriteLine();
}
