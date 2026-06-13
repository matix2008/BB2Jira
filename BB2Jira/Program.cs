using System.Reflection;
using BB2Jira.Cli;
using BB2Jira.Services;
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
    _ => 2,
};

static int RunGenerateMap(CliOptions options)
{
    using var loggerFactory = CreateLoggerFactory(Path.ChangeExtension(options.OutputPath, ".log"));
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
    using var loggerFactory = CreateLoggerFactory(Path.ChangeExtension(options.OutputPath, ".log"));
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

// Creates a Serilog logger factory that writes to the console and to the log file (import.log / map.log).
static ILoggerFactory CreateLoggerFactory(string logPath)
{
    var serilogLogger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(logPath, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    return LoggerFactory.Create(builder => builder.AddSerilog(serilogLogger, dispose: true));
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
