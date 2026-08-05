using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class UiLanguageCatalogTests
{
    [Fact]
    public void Spanish_and_english_catalogs_have_the_same_complete_contract()
    {
        var spanish = UiLanguageCatalog.For("es");
        var english = UiLanguageCatalog.For("en");

        Assert.Equal(spanish.Keys.Order(), english.Keys.Order());
        Assert.All(spanish.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }
}
