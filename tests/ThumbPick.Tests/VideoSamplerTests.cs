using ThumbPick.Core;
using Xunit;

namespace ThumbPick.Tests;

public class VideoSamplerTests
{
    [Theory]
    [InlineData(10, 1, 11)]
    [InlineData(30, 2, 61)]
    public void GeneratesExpectedCount(double duration, double rate, int expected)
    {
        var sampler = new VideoSampler();
        var stamps = sampler.GenerateTimestamps(duration, rate);
        Assert.Equal(expected, stamps.Count);
        Assert.True(Math.Abs(stamps[0]) < 1e-6);
        Assert.True(Math.Abs(stamps[^1] - duration) < 1e-6);
    }
}
