// See https://aka.ms/new-console-template for more information
using BB2Jira.Cli;
using BB2Jira.Logging;
using BB2Jira.Services;

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
        Console.Error.WriteLine($"Ошибка: {error}");
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
    var logPath = Path.ChangeExtension(options.OutputPath, ".log");
    var logger = new AppLogger();
    logger.Info("Режим: генерация map.json");
    logger.Info($"Входной файл: {options.InputPath}");
    logger.Info($"Файл маппинга: {options.OutputPath}");

    try
    {
        var export = BitbucketLoader.Load(options.InputPath);
        logger.Info($"Загружено issues: {export.Issues.Count}");

        MapGenerator.Generate(export, options.OutputPath, logger);

        logger.Info($"Готово. Предупреждений: {logger.WarningCount}, ошибок: {logger.ErrorCount}");
        return 0;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.Error(ex.Message);
        return 1;
    }
    finally
    {
        logger.Save(logPath);
    }
}

static int RunGenerateCsv(CliOptions options)
{
    var logPath = Path.ChangeExtension(options.OutputPath, ".log");
    var logger = new AppLogger();
    logger.Info("Режим: генерация import.csv");
    logger.Info($"Входной файл: {options.InputPath}");
    logger.Info($"Файл маппинга: {options.MapPath}");
    logger.Info($"Результат: {options.OutputPath}");

    try
    {
        var export = BitbucketLoader.Load(options.InputPath);
        logger.Info($"Загружено issues: {export.Issues.Count}");

        var map = MapLoader.Load(options.MapPath);
        CsvGenerator.Generate(export, map, options.OutputPath, logger);

        logger.Info($"Готово. Предупреждений: {logger.WarningCount}, ошибок: {logger.ErrorCount}");
        return 0;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        logger.Error(ex.Message);
        return 1;
    }
    finally
    {
        logger.Save(logPath);
    }
}
