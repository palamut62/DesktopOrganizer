using System.Text.Json;
using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class LegacySettingsTests
{
    [Fact]
    public void RemovedRoomSettingsAreIgnoredWithoutLosingDesktopPreferences()
    {
        const string json = """{"RoomStyle":"office","OfficeWindow":true,"WeatherEnabled":true,"WeatherCity":"Tunceli","ThemeId":"blocks","Sounds":false,"Scale":1.25,"FreeX":0.42,"ThemedWallpaper":true}""";
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        using var store = new CollectionStore(":memory:");
        store.SaveSettings(settings);
        var restored = store.LoadSettings();
        Assert.Equal("blocks", restored.ThemeId);
        Assert.False(restored.Sounds);
        Assert.Equal(1.25, restored.Scale);
        Assert.Equal(.42, restored.FreeX);
        Assert.True(restored.ThemedWallpaper);
        var saved = JsonSerializer.Serialize(restored);
        Assert.DoesNotContain("Weather", saved);
        Assert.DoesNotContain("RoomStyle", saved);
    }
}
