using StitchHelper;
using Xunit;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace StitchHelper.Tests;
public class CatalogTests
{
    [Fact]
    public void AddedColorsMatchEveryWorkbookNameAndSwatch()
    {
        using var workbook = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "MissingColors.xlsx"));
        XDocument Read(string part) { using var stream = workbook.GetEntry(part)!.Open(); return XDocument.Load(stream); }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings = Read("xl/sharedStrings.xml").Root!.Elements(ns + "si").Select(s => s.Value).ToArray();
        var styles = Read("xl/styles.xml").Root!;
        var formats = styles.Element(ns + "cellXfs")!.Elements().ToArray();
        var fills = styles.Element(ns + "fills")!.Elements().ToArray();
        var catalog = DomainTests.Catalog();
        var codes = new List<string>();
        foreach (var row in Read("xl/worksheets/sheet1.xml").Descendants(ns + "row"))
        {
            var cells = row.Elements(ns + "c").ToArray();
            if ((string?)cells[0].Attribute("t") == "s") continue;
            var code = ((int)decimal.Parse(cells[0].Element(ns + "v")!.Value, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
            var name = strings[(int)cells[1].Element(ns + "v")!];
            var fill = fills[(int)formats[(int)cells[2].Attribute("s")!].Attribute("fillId")!].Element(ns + "patternFill")!;
            Assert.Equal("solid", (string?)fill.Attribute("patternType"));
            var rgb = (string)fill.Element(ns + "fgColor")!.Attribute("rgb")!;
            Assert.Equal("FF", rgb[..2]);
            Assert.Equal(name, catalog[code].Name);
            Assert.Equal("#" + rgb[2..], catalog[code].DisplayColor);
            codes.Add(code);
        }
        Assert.Equal(Enumerable.Range(1, 35).Select(n => n.ToString()), codes);
        Assert.Equal("#FCFBF8", catalog["White"].DisplayColor);
        Assert.Equal("#F0EADA", catalog["Ecru"].DisplayColor);
        Assert.Equal("#FFFFFF", catalog["B5200"].DisplayColor);
    }

    [Theory]
    [InlineData("05", "5")]
    [InlineData("01", "1")]
    [InlineData("09", "9")]
    [InlineData("035", "35")]
    [InlineData("0310", "310")]
    [InlineData("5", "5")]
    [InlineData(" 05 ", "5")]
    [InlineData("000", "0")]
    [InlineData("B5200", "B5200")]
    [InlineData("Blanc", "White")]
    public void CodesHaveOneCanonicalSpelling(string source, string expected)
        => Assert.Equal(expected, ThreadCatalog.CanonicalCode(source));

    [Fact]
    public void SavedPatternHistoryNormalizesThreadCodesWithoutChangingComponentIds()
    {
        var definition = new StitchDefinition("d", "X", "05", Components: [new("05", "05", 2)]);
        var project = new Project {
            Data = new() { Definitions = [definition] }, Substitutions = new() { ["05"] = "09" },
            Undo = [new() { Code = "05", Replacement = "01", Definition = definition }],
            Redo = [new() { Data = new() { Definitions = [definition] } }]
        };
        ThreadCatalog.Normalize(project);
        Assert.Equal("5", project.Data.Definitions[0].ThreadCode);
        Assert.Equal(new ThreadUsageComponent("05", "5", 2), Assert.Single(project.Data.Definitions[0].GetComponents()));
        Assert.Equal("9", project.Substitutions["05"]);
        Assert.Equal("05", project.Undo[0].Code);
        Assert.Equal("1", project.Undo[0].Replacement);
        Assert.Equal("5", project.Undo[0].Definition!.ThreadCode);
        Assert.Equal("5", project.Redo[0].Data!.Definitions[0].ThreadCode);
    }

    [Fact]
    public void PaddedBlendsResolveAndDuplicateSpellingsAreRejected()
    {
        var components = ThreadUsageParser.Parse("DMC 05 (1) + 09 (2)", "d", DomainTests.Catalog());
        Assert.Equal(new[] { "5", "9" }, components.Select(c => c.ThreadCode));
        Assert.Equal(new int?[] { 1, 2 }, components.Select(c => c.StrandCount));
        Assert.Throws<UserError>(() => ThreadUsageParser.Parse("5 + 05", "d", DomainTests.Catalog()));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("05")]
    public void InlinePdfLegendResolvesNewColors(string code)
    {
        var path = Path.GetTempFileName();
        try {
            File.WriteAllBytes(path, DomainTests.CreatePdf($"X   DMC {code} Driftwood"));
            var data = new PdfImporter().Parse(path, DomainTests.Catalog());
            Assert.Equal("5", Assert.Single(data.Definitions).ThreadCode);
            var project = new Project { Data = data };
            ProjectCommands.Execute(project, new(0, "confirm"), DomainTests.Catalog());
            Assert.Equal("active", project.Status);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void CatalogUsesEverySuppliedCodeDescriptionAndRgb()
    {
        using var source = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")));
        var catalog = DomainTests.Catalog(); Assert.Equal(489, catalog.Count);
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
    [InlineData("[{\"floss\":\"5\",\"description\":\"Driftwood\",\"r\":0,\"g\":0,\"b\":0},{\"floss\":\"05\",\"description\":\"Duplicate\",\"r\":1,\"g\":1,\"b\":1}]")]
    public void InvalidCatalogIsRejected(string json)
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.Throws<InvalidDataException>(() => ThreadCatalog.Load(path)); }
        finally { File.Delete(path); }
    }
}
