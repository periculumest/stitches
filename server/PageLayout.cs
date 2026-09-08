namespace StitchHelper;

public static class PageLayout
{
    public static bool Overlaps(SourcePage a, SourcePage b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    public static PatternData Arrange(PatternData source, List<PagePlacement>? placements)
    {
        if (source.Pages.Count == 0 || placements is null || placements.Count != source.Pages.Count || placements.Any(p => p is null) || placements.Select(p => p.Number).Distinct().Count() != placements.Count || placements.Any(p => source.Pages.All(s => s.Number != p.Number)))
            throw new UserError("Include each chart page exactly once in the arrangement.");
        if (!source.PageSourcesComplete && source.Pages.Any(a => source.Pages.Any(b => a.Number < b.Number && Overlaps(a, b))))
            throw new UserError("This older import did not retain its repeated page-edge stitches. Re-import the original PDF before rearranging overlapping pages.");
        var targets = placements.ToDictionary(p => p.Number);
        var originals = source.Pages.ToDictionary(p => p.Number);
        var pages = source.Pages.Select(p => p with { X = targets[p.Number].X, Y = targets[p.Number].Y }).ToList();
        if (pages.Any(p => p.X < 0 || p.Y < 0 || p.Width < 1 || p.Height < 1 || p.X > 5000 - p.Width || p.Y > 5000 - p.Height))
            throw new UserError("Keep every page within 5,000 × 5,000 stitches.");
        var stitches = new Dictionary<(double, double, double?, double?), Stitch>();
        var hidden = new List<Stitch>();
        var ids = new HashSet<string>();
        foreach (var stitch in source.Stitches.Concat(source.PageOverlapStitches))
        {
            if (!ids.Add(stitch.Id)) throw new UserError("Source page stitches are ambiguous. Re-import the PDF before rearranging it.");
            var separator = stitch.Id.IndexOf('-');
            if (!stitch.Id.StartsWith('p') || separator < 2 || !int.TryParse(stitch.Id.AsSpan(1, separator - 1), out var pageNumber) || !originals.TryGetValue(pageNumber, out var original))
                throw new UserError("This chart contains stitches without a source page. Arrange a fresh import before making individual stitch edits.");
            if (stitch.X < original.X || stitch.X >= original.X + original.Width || stitch.Y < original.Y || stitch.Y >= original.Y + original.Height)
                throw new UserError($"An edited stitch falls outside source page {pageNumber}. Undo the stitch edit or arrange a fresh import.");
            var target = targets[pageNumber]; var dx = target.X - original.X; var dy = target.Y - original.Y;
            var moved = stitch with { X = stitch.X + dx, Y = stitch.Y + dy, EndX = stitch.EndX + dx, EndY = stitch.EndY + dy };
            var key = (moved.X, moved.Y, moved.EndX, moved.EndY);
            if (stitches.TryGetValue(key, out var previous))
            {
                if (previous.DefinitionId != moved.DefinitionId)
                    throw new UserError($"Page {pageNumber} conflicts with another page at column {moved.X + 1}, row {moved.Y + 1}. Adjust the join before saving.");
                hidden.Add(moved);
            }
            else stitches[key] = moved;
        }
        var data = Json.Copy(source);
        data.Pages = pages; data.Stitches = stitches.Values.ToList(); data.PageOverlapStitches = hidden; data.PageSourcesComplete = true;
        data.Width = pages.Max(p => p.X + p.Width); data.Height = pages.Max(p => p.Y + p.Height);
        foreach (var s in data.Stitches.Concat(hidden)) ProjectCommands.ValidateStitch(s, data);
        // Original import notes describe the original arrangement; retain them with an explicit qualification.
        const string prefix = "Page builder:";
        data.Warnings.RemoveAll(w => w.Message.StartsWith(prefix, StringComparison.Ordinal));
        data.Warnings.Insert(0, new($"{prefix} arrangement updated by you to {data.Width} × {data.Height}; {hidden.Count:N0} matching repeated stitches merged. Earlier import checks refer to the original arrangement. Check page joins and blank areas before starting."));
        return data;
    }
}
