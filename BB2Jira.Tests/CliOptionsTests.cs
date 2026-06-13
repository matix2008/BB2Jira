using BB2Jira.Cli;
using Xunit;

namespace BB2Jira.Tests;

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
}
