using ShootDrop.Core.Media;
using ShootDrop.Core.SmartBox;

namespace ShootDrop.Core.Tests;

public class RuleParserTests
{
    private static readonly IReadOnlyList<KnownCard> Cards =
    [
        new("dji-osmo-pocket", ["pocket", "osmo"]),
        new("panasonic-lumix", ["lumix", "camera"]),
        new("dji-mic", ["mic", "dji mic"]),
    ];

    private static ParsedRequest P(string text) => RuleParser.Parse(text, Days.Today, Cards);

    [Fact]
    public void All_cards_yesterday_with_project()
    {
        var r = P("all cards, yesterday for Sinhagad trip");
        Assert.Equal(DateRule.Yesterday, r.Date);
        Assert.True(r.AllCards);
        Assert.Null(r.CardKeys);
        Assert.Equal("Sinhagad trip", r.ProjectName);
    }

    [Fact]
    public void Pocket_only_last_three_days()
    {
        var r = P("Pocket only, last 3 days");
        Assert.Equal(DateRule.LastDays(3), r.Date);
        Assert.Equal(new[] { "dji-osmo-pocket" }, r.CardKeys!);
    }

    [Fact]
    public void Audio_only_today()
    {
        var r = P("audio only today");
        Assert.Equal(DateRule.Today, r.Date);
        Assert.True(r.Kinds!.SetEquals(new[] { FileKind.Audio }));
    }

    [Fact]
    public void Hinglish_kal_sab_with_photos()
    {
        var r = P("kal ka sab footage with photos");
        Assert.Equal(DateRule.Yesterday, r.Date);
        Assert.True(r.AllCards);
        Assert.True(r.Kinds!.SetEquals(new[] { FileKind.Video, FileKind.Audio, FileKind.Photo }));
    }

    [Theory]
    [InlineData("22 Nov", 2026, 11, 22, 22)]
    [InlineData("nov 21st", 2026, 11, 21, 21)]
    [InlineData("21-22 Nov", 2026, 11, 21, 22)]
    [InlineData("25 Dec", 2025, 12, 25, 25)] // a future day means last year
    public void Explicit_dates(string text, int year, int month, int from, int to)
    {
        var r = P(text);
        Assert.Equal(DateRule.Between(new DateOnly(year, month, from), new DateOnly(year, month, to)), r.Date);
    }

    [Fact]
    public void Words_inside_the_project_name_are_not_filters()
    {
        var r = P("lumix footage for Aaj Ki Raat shoot");
        Assert.Null(r.Date);
        Assert.Equal(new[] { "panasonic-lumix" }, r.CardKeys!);
        Assert.Equal("Aaj Ki Raat shoot", r.ProjectName);
    }

    [Fact]
    public void Nonsense_understands_nothing()
    {
        Assert.False(P("make it look cinematic").UnderstoodAnything);
    }
}
