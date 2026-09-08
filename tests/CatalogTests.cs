using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;
public class CatalogTests
{
    [Fact]
    public void CatalogUsesEverySuppliedCodeDescriptionAndRgb()
    {
        using var source = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")));
        var catalog = DomainTests.Catalog(); Assert.Equal(454, catalog.Count);
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var thread = catalog[row.GetProperty("floss").GetString()!];
            Assert.Equal(row.GetProperty("description").GetString(), thread.Name);
            Assert.Equal($"#{row.GetProperty("r").GetInt32():X2}{row.GetProperty("g").GetInt32():X2}{row.GetProperty("b").GetInt32():X2}", thread.DisplayColor);
        }
        Assert.Equal("#000000", catalog["310"].DisplayColor); Assert.Equal("#564A4A", catalog["309"].DisplayColor); Assert.Equal("#883E43", catalog["221"].DisplayColor);
    }
    [Theory]
    [InlineData("[{\"floss\":\"310\",\"description\":\"Black\",\"r\":256,\"g\":0,\"b\":0}]")]
    [InlineData("[{\"floss\":\"310\",\"description\":\"Black\",\"r\":0,\"g\":0,\"b\":0},{\"floss\":\"310\",\"description\":\"Duplicate\",\"r\":1,\"g\":1,\"b\":1}]")]
    [InlineData("[]")]
    public void InvalidCatalogIsRejected(string json)
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.Throws<InvalidDataException>(() => ThreadCatalog.Load(path)); }
        finally { File.Delete(path); }
    }
}
