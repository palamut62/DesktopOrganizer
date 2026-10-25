using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class OfficeTests
{
    static readonly DateTime Day = new(2026, 10, 25, 13, 0, 0);
    static WeatherNow W(SkyKind sky, bool isDay = true) => new(sky, isDay, Day.Date.AddHours(7), Day.Date.AddHours(18), "Ankara");

    [Theory]
    [InlineData(0, SkyKind.Clear)] [InlineData(1, SkyKind.Clear)] [InlineData(3, SkyKind.Cloudy)] [InlineData(45, SkyKind.Cloudy)]
    [InlineData(61, SkyKind.Rain)] [InlineData(81, SkyKind.Rain)] [InlineData(95, SkyKind.Rain)] [InlineData(73, SkyKind.Snow)] [InlineData(86, SkyKind.Snow)]
    public void Classify_WmoCodes(int code, SkyKind expected) => Assert.Equal(expected, OfficeLogic.Classify(code));

    [Fact] public void Noon_Clear_LightsOffInAuto()
    {
        var st = OfficeLogic.StateFor(new AppSettings(), W(SkyKind.Clear), Day);
        Assert.Equal(DayPhase.Day, st.Phase); Assert.False(st.Lights); Assert.True(st.Window);
    }

    [Fact] public void RainyDay_LightsOnInAuto() => Assert.True(OfficeLogic.StateFor(new AppSettings(), W(SkyKind.Rain), Day).Lights);

    [Fact] public void NearSunset_IsDusk_AndLightsOn()
    {
        var st = OfficeLogic.StateFor(new AppSettings(), W(SkyKind.Clear), Day.Date.AddHours(18).AddMinutes(20));
        Assert.Equal(DayPhase.Dusk, st.Phase); Assert.True(st.Lights);
    }

    [Fact] public void Night_FromWeatherFlag() =>
        Assert.Equal(DayPhase.Night, OfficeLogic.StateFor(new AppSettings(), W(SkyKind.Cloudy, isDay: false), Day.Date.AddHours(22)).Phase);

    [Fact] public void ManualLightModes_OverrideAuto()
    {
        Assert.True(OfficeLogic.StateFor(new AppSettings { CeilingLights = "on" }, W(SkyKind.Clear), Day).Lights);
        Assert.False(OfficeLogic.StateFor(new AppSettings { CeilingLights = "off" }, W(SkyKind.Rain), Day.Date.AddHours(23)).Lights);
    }

    [Fact] public void RoomFollowsCabinetTheme()
    {
        Assert.Equal("blocks", OfficeLogic.StateFor(new AppSettings { ThemeId = "blocks" }, null, Day).Theme);
        Assert.Equal("metal", OfficeLogic.StateFor(new AppSettings { ThemeId = "metal" }, null, Day).Theme);
    }

    [Fact] public void WithoutWeather_UsesSeasonalSun()
    {
        Assert.Equal(DayPhase.Day, OfficeLogic.StateFor(new AppSettings(), null, Day).Phase);
        Assert.Equal(DayPhase.Night, OfficeLogic.StateFor(new AppSettings(), null, Day.Date.AddHours(2)).Phase);
    }
}
