namespace StitchHelper;

public static class SamplePattern
{
    public static Pattern Create(int size = 100)
    {
        var data = new PatternData { Width = size, Height = size };
        string[] codes = ["500", "502", "3348", "3722", "761", "3865", "3820", "930"];
        string[] symbols = ["+", "/", "v", "◆", "○", "·", "●", "x"];
        for (var i = 0; i < codes.Length; i++) data.Definitions.Add(new($"d{i}", symbols[i], codes[i]));
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var px = (x + .5) * 100 / size - 50; var py = (y + .5) * 100 / size - 50;
            var r = Math.Sqrt(px * px + py * py); var a = Math.Atan2(py, px);
            var color = -1;
            if (r > 33 && r < 35) color = 0;
            for (var leaf = 0; leaf < 20; leaf++)
            {
                var angle = leaf * Math.PI * 2 / 20;
                var lx = px - Math.Cos(angle) * 35; var ly = py - Math.Sin(angle) * 35;
                var u = lx * Math.Cos(angle + .8) + ly * Math.Sin(angle + .8);
                var v = -lx * Math.Sin(angle + .8) + ly * Math.Cos(angle + .8);
                if (u * u / 46 + v * v / 9 < 1) color = v > 0 ? 1 : 2;
            }
            foreach (var (cx, cy, scale) in new[] { (-18d, -23d, 1d), (22d, -17d, .8), (-23d, 19d, .85), (15d, 28d, 1.1), (0d, -35d, .65) })
            {
                var fx = (px - cx) / scale; var fy = (py - cy) / scale;
                var fr = Math.Sqrt(fx * fx + fy * fy); var fa = Math.Atan2(fy, fx);
                if (fr < 7.5 + Math.Cos(fa * 5) * 2) color = fr > 5 ? 4 : 3;
                if (fr < 2.5) color = 6;
            }
            // A little eight-point star at the heart of the wreath.
            if ((Math.Abs(px) < 2 && Math.Abs(py) < 11) || (Math.Abs(py) < 2 && Math.Abs(px) < 11) || (Math.Abs(Math.Abs(px) - Math.Abs(py)) < 1.4 && Math.Abs(px) < 7)) color = 6;
            if (color >= 0) data.Stitches.Add(new($"p1-{x}-{y}", x, y, $"d{color}"));
        }
        data.Definitions.RemoveAll(d => !data.Stitches.Any(s => s.DefinitionId == d.Id));
        data.Pages.Add(new(1, 0, 0, size, size));
        return new(Guid.NewGuid().ToString("N"), size > 100 ? "Large-pattern sampler" : "The little garden", null, data);
    }
}
