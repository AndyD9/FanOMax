using FanOMax.Core.Analysis;

namespace FanOMax.Core.Tests.Analysis;

public class FpsCounterTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void GetTop_CountsFramesOfTheBusiestApplication()
    {
        var counter = new FpsCounter();
        for (var i = 0; i < 60; i++)
        {
            counter.AddFrame("game.exe", Ms(i * 1000.0 / 60));
        }

        for (var i = 0; i < 10; i++)
        {
            counter.AddFrame("browser.exe", Ms(i * 100.0));
        }

        var top = counter.GetTop(Ms(999));

        Assert.NotNull(top);
        Assert.Equal("game.exe", top.Value.App);
        Assert.Equal(60, top.Value.Fps, precision: 0);
    }

    [Fact]
    public void GetTop_DropsFramesOlderThanTheWindow()
    {
        var counter = new FpsCounter();
        counter.AddFrame("game.exe", Ms(0));
        counter.AddFrame("game.exe", Ms(500));

        Assert.Equal(1, counter.GetTop(Ms(1400))!.Value.Fps, precision: 0);
        Assert.Null(counter.GetTop(Ms(3000)));
    }
}
