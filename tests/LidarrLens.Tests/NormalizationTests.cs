using LidarrLens.Application;
using Xunit;

namespace LidarrLens.Tests;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("  The   Artist! ", "the artist")]
    [InlineData("Mötley Crüe", "motley crue")]
    public void Text_normalizes_human_variants(string input, string expected) => Assert.Equal(expected, Normalization.Text(input));

    [Fact]
    public void TrackTitle_removes_feature_and_version_markers() => Assert.Equal("song", Normalization.TrackTitle("Song (Live) feat. Guest"));

    [Fact]
    public void ReleaseTitle_removes_only_trailing_feature_marker() => Assert.Equal("vin pengar", Normalization.ReleaseTitle("Vin & pengar (feat. William Ahlborg)"));

    [Theory]
    [InlineData("Vin & pengar (Live)", "vin pengar live")]
    [InlineData("With You", "with you")]
    public void ReleaseTitle_preserves_version_markers_and_non_suffix_words(string input, string expected) => Assert.Equal(expected, Normalization.ReleaseTitle(input));
}
