using BB2Jira.Cli;
using Xunit;

namespace BB2Jira.Tests;

/// <summary>Tests for command-line argument parsing in <see cref="CliOptions"/>.</summary>
public class CliOptionsTests
{
    [Fact]
    public void WhenOnlyMapKeyThenModeIsGenerateMap()
    {
        var options = CliOptions.Parse(new[] { "-m" });

        Assert.Equal(AppMode.GenerateMap, options.Mode);
    }

    [Fact]
    public void WhenCsvKeyThenModeIsGenerateCsv()
    {
        var options = CliOptions.Parse(new[] { "-c", "-i", "db-2.0.json", "-m", "map.json", "-o", "import.csv" });

        Assert.Equal(AppMode.GenerateCsv, options.Mode);
    }

    [Fact]
    public void WhenCsvModeThenMapKeyIsTreatedAsPath()
    {
        var options = CliOptions.Parse(new[] { "-c", "-m", "custom-map.json" });

        Assert.Equal("custom-map.json", options.MapPath);
    }

    [Fact]
    public void WhenInputProvidedThenInputPathIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-c", "-i", "export.json" });

        Assert.Equal("export.json", options.InputPath);
    }

    [Fact]
    public void WhenOutputProvidedThenOutputPathIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-c", "-o", "result.csv" });

        Assert.Equal("result.csv", options.OutputPath);
    }

    [Fact]
    public void WhenMapModeWithoutOutputThenOutputFollowsMapPath()
    {
        var options = CliOptions.Parse(new[] { "-m", "-o", "my-map.json" });

        Assert.Equal("my-map.json", options.OutputPath);
    }

    [Fact]
    public void WhenNoModeThenOptionsAreInvalid()
    {
        var options = CliOptions.Parse(new[] { "-i", "db-2.0.json" });

        Assert.False(options.IsValid);
    }

    [Fact]
    public void WhenUnknownArgumentThenErrorIsRecorded()
    {
        var options = CliOptions.Parse(new[] { "-c", "--unknown" });

        Assert.Contains(options.Errors, e => e.Contains("--unknown"));
    }

    [Fact]
    public void WhenMapModeWithoutInputThenInputDefaultsToDbExport()
    {
        var options = CliOptions.Parse(new[] { "-m" });

        Assert.Equal("db-2.0.json", options.InputPath);
    }

    [Fact]
    public void WhenCsvModeWithoutInputThenInputDefaultsToDbExport()
    {
        var options = CliOptions.Parse(new[] { "-c" });

        Assert.Equal("db-2.0.json", options.InputPath);
    }

    [Fact]
    public void WhenCsvModeWithoutMapThenMapDefaultsToMapJson()
    {
        var options = CliOptions.Parse(new[] { "-c" });

        Assert.Equal("map.json", options.MapPath);
    }

    [Fact]
    public void WhenCsvModeWithoutOutputThenOutputDefaultsToImportCsv()
    {
        var options = CliOptions.Parse(new[] { "-c" });

        Assert.Equal("import.csv", options.OutputPath);
    }

    [Fact]
    public void WhenVerboseFlagThenVerboseIsTrue()
    {
        var options = CliOptions.Parse(new[] { "-c", "-v" });

        Assert.True(options.Verbose);
    }

    [Fact]
    public void WhenVerboseLongFlagThenVerboseIsTrue()
    {
        var options = CliOptions.Parse(new[] { "-c", "--verbose" });

        Assert.True(options.Verbose);
    }

    [Fact]
    public void WhenNoVerboseFlagThenVerboseIsFalse()
    {
        var options = CliOptions.Parse(new[] { "-c" });

        Assert.False(options.Verbose);
    }

    [Fact]
    public void WhenNumberKeyThenModeIsViewIssue()
    {
        var options = CliOptions.Parse(new[] { "-n", "42" });

        Assert.Equal(AppMode.ViewIssue, options.Mode);
        Assert.True(options.IsValid);
    }

    [Fact]
    public void WhenNumberKeyThenIssueNumberIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-n", "7" });

        Assert.Equal(7, options.IssueNumber);
    }

    [Fact]
    public void WhenNumberKeyLongFormThenIssueNumberIsParsed()
    {
        var options = CliOptions.Parse(new[] { "--number", "15" });

        Assert.Equal(AppMode.ViewIssue, options.Mode);
        Assert.Equal(15, options.IssueNumber);
    }

    [Fact]
    public void WhenNumberKeyWithoutValueThenErrorIsRecorded()
    {
        var options = CliOptions.Parse(new[] { "-n" });

        Assert.False(options.IsValid);
        Assert.Contains(options.Errors, e => e.Contains("-n"));
    }

    [Fact]
    public void WhenNumberKeyWithNonNumericValueThenErrorIsRecorded()
    {
        var options = CliOptions.Parse(new[] { "-n", "abc" });

        Assert.False(options.IsValid);
        Assert.Contains(options.Errors, e => e.Contains("-n"));
    }

    [Fact]
    public void WhenNumberKeyWithInputThenInputPathIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-n", "5", "-i", "custom.csv" });

        Assert.Equal(AppMode.ViewIssue, options.Mode);
        Assert.Equal("custom.csv", options.InputPath);
    }

    [Fact]
    public void WhenNumberKeyWithoutInputThenInputDefaultsToImportCsv()
    {
        var options = CliOptions.Parse(new[] { "-n", "10" });

        Assert.Equal("import.csv", options.InputPath);
    }

    [Fact]
    public void WhenUpdateKeyWithoutInputThenInputDefaultsToImportCsv()
    {
        var options = CliOptions.Parse(new[] { "-u" });

        Assert.Equal("import.csv", options.InputPath);
    }

    [Fact]
    public void WhenUpdateKeyWithValueThenUpdateModeIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-u", "all" });

        Assert.Equal(AppMode.UpdateJira, options.Mode);
        Assert.Equal("all", options.UpdateMode);
    }

    [Fact]
    public void WhenUpdateKeyWithoutValueThenUpdateModeIsNull()
    {
        var options = CliOptions.Parse(new[] { "-u" });

        Assert.Null(options.UpdateMode);
    }

    [Fact]
    public void WhenCheckKeyWithoutInputThenInputDefaultsToImportCsv()
    {
        var options = CliOptions.Parse(new[] { "-k" });

        Assert.Equal("import.csv", options.InputPath);
    }

    [Fact]
    public void WhenCheckKeyWithInputThenInputPathIsParsed()
    {
        var options = CliOptions.Parse(new[] { "-k", "-i", "my.csv" });

        Assert.Equal("my.csv", options.InputPath);
    }
}
