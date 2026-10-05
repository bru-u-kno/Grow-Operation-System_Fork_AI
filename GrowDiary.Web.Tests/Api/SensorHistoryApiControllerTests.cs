using GrowDiary.Web.Api.Controllers;

namespace GrowDiary.Web.Tests.Api;

public sealed class SensorHistoryApiControllerTests
{
    [Theory]
    [InlineData(null, 1, SensorHistoryApiController.Raw)]     // short window -> raw detail
    [InlineData(null, 2, SensorHistoryApiController.Raw)]
    [InlineData(null, 3, SensorHistoryApiController.Daily)]   // longer -> daily stats
    [InlineData(null, 90, SensorHistoryApiController.Daily)]
    [InlineData("raw", 90, SensorHistoryApiController.Raw)]   // explicit wins
    [InlineData("DAILY", 1, SensorHistoryApiController.Daily)]
    [InlineData("nonsense", 1, SensorHistoryApiController.Raw)]
    public void ResolveResolution_PicksSensibleMode(string? requested, int days, string expected)
        => Assert.Equal(expected, SensorHistoryApiController.ResolveResolution(requested, days));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(14, 14)]
    [InlineData(9999, 365)]
    public void ClampDays_StaysInRange(int input, int expected)
        => Assert.Equal(expected, SensorHistoryApiController.ClampDays(input));

    [Fact]
    public void ParseMetrics_TrimsSplitsAndDeduplicates()
    {
        var parsed = SensorHistoryApiController.ParseMetrics(" reservoir-ph , reservoir-ec ,reservoir-ph, ");

        Assert.Equal(new[] { "reservoir-ph", "reservoir-ec" }, parsed);
    }

    [Fact]
    public void ParseMetrics_EmptyInput_YieldsNothing()
    {
        Assert.Empty(SensorHistoryApiController.ParseMetrics(null));
        Assert.Empty(SensorHistoryApiController.ParseMetrics("  "));
    }

    [Fact]
    public void ParseMetrics_CapsTheNumberOfSeries()
    {
        var many = string.Join(',', Enumerable.Range(0, 30).Select(i => $"metric-{i}"));

        Assert.Equal(SensorHistoryApiController.MaxMetrics, SensorHistoryApiController.ParseMetrics(many).Count);
    }
}
