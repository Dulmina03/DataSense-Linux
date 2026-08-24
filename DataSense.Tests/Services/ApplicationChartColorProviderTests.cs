using System.Collections.Generic;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class ApplicationChartColorProviderTests
{
    [Fact]
    public void SameApplication_ReceivesSameColor_AcrossMultipleCalls()
    {
        var provider = new ApplicationChartColorProvider();

        int firstIndex = provider.GetColorIndex("chrome");
        int secondIndex = provider.GetColorIndex("chrome");
        int thirdIndex = provider.GetColorIndex("CHROME");

        Assert.Equal(firstIndex, secondIndex);
        Assert.Equal(firstIndex, thirdIndex);

        var firstBrush = provider.GetColorBrush("chrome");
        var secondBrush = provider.GetColorBrush("chrome");
        Assert.NotNull(firstBrush);
        Assert.NotNull(secondBrush);
    }

    [Fact]
    public void KnownApplicationPresets_ReceiveDeterministicDesignatedColors()
    {
        var provider = new ApplicationChartColorProvider();

        // Presets
        Assert.Equal(0, provider.GetColorIndex("brave"));
        Assert.Equal(0, provider.GetColorIndex("chrome"));
        Assert.Equal(1, provider.GetColorIndex("steam"));
        Assert.Equal(2, provider.GetColorIndex("code"));
        Assert.Equal(3, provider.GetColorIndex("discord"));
        Assert.Equal(4, provider.GetColorIndex("spotify"));
        Assert.Equal(5, provider.GetColorIndex("node"));
        Assert.Equal(6, provider.GetColorIndex("python"));
        Assert.Equal(7, provider.GetColorIndex("git"));
    }

    [Fact]
    public void DifferentApplications_ReceiveDistinctColors()
    {
        var provider = new ApplicationChartColorProvider();

        int braveIdx = provider.GetColorIndex("brave");
        int steamIdx = provider.GetColorIndex("steam");
        int codeIdx = provider.GetColorIndex("code");
        int discordIdx = provider.GetColorIndex("discord");

        var set = new HashSet<int> { braveIdx, steamIdx, codeIdx, discordIdx };
        Assert.Equal(4, set.Count);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("OTHERS")]
    [InlineData("[other]")]
    [InlineData("other applications")]
    public void OtherProcess_ReceivesSpecialMutedToken(string otherIdentifier)
    {
        var provider = new ApplicationChartColorProvider();

        int index = provider.GetColorIndex(otherIdentifier);
        Assert.Equal(-1, index);

        string token = provider.GetColorToken(otherIdentifier);
        Assert.Equal("Brush.ChartSegmentOther", token);

        string hex = provider.GetColorHex(otherIdentifier);
        Assert.Equal("#64748B", hex);
    }
}
