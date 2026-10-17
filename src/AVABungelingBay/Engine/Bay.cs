namespace AVABungelingBay.Engine;

/// <summary>
/// The helicopter's controls: left and right turn it, up drives it forward and down slows it (and backs it off);
/// fire for the guns (held for a stream), and bomb.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Bomb = false);

/// <summary>The screen and the bay.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bay: open sea with islands, this many pixels across and down.</summary>
    public const double MapW = 4800, MapH = 3600;
    /// <summary>The land is kept as squares this size.</summary>
    public const double Cell = 40;
    public const int Cols = (int)(MapW / Cell), Rows = (int)(MapH / Cell);
}

/// <summary>A factory on an island: what it makes (the shipyard builds the battleship), and where.</summary>
public readonly record struct FactoryDef(double X, double Y, bool Shipyard, int Island);

/// <summary>An anti-aircraft gun on an island.</summary>
public readonly record struct GunDef(double X, double Y);

/// <summary>
/// A bay: its islands (as squares of land), its factories (one of them the shipyard), the guns guarding them, the
/// carrier the helicopter flies from, and how hard the enemy pushes back.
/// </summary>
public sealed class BayDef
{
    public int Index;
    public string Name = "";
    public bool[,] Land = new bool[Field.Cols, Field.Rows];
    public List<(double X, double Y, double R)> Discs = [];
    public List<FactoryDef> Factories = [];
    public List<GunDef> Guns = [];
    /// <summary>The middle of the carrier's deck (it lies east and west).</summary>
    public double CarrierX = 640, CarrierY = 3180;
    /// <summary>Where the battleship is launched from, once built: the sea off the shipyard.</summary>
    public double SlipX, SlipY;
    /// <summary>How many seconds the battleship takes to build with every factory working.</summary>
    public double BuildSeconds = 240;
    public int MaxJets = 2;
    public double JetSpeed = 320, FlakEvery = 1.8;

    public bool IsLand(double x, double y)
    {
        int c = (int)Math.Floor(x / Field.Cell), r = (int)Math.Floor(y / Field.Cell);
        return c >= 0 && r >= 0 && c < Field.Cols && r < Field.Rows && Land[c, r];
    }
}

/// <summary>The bays, one after another, each better defended than the last.</summary>
public static class BayBook
{
    private static readonly string[] Names = ["BUNGELING BAY", "COG HARBOUR", "SMELTER SOUND", "RIVET REACH", "THE IRON GULF"];

    public static int Count => Names.Length;

    public static BayDef Build(int index, int seed = 0)
    {
        for (int attempt = 0; ; attempt++)
        {
            var b = Try(index, 1000 + index * 131 + seed * 7 + attempt * 7919);
            if (b != null) return b;
        }
    }

    private static BayDef? Try(int index, int seed)
    {
        var d = new Dice(seed);
        int level = index;
        var b = new BayDef
        {
            Index = index, Name = Names[index % Names.Length],
            BuildSeconds = Math.Max(150, 260 - level * 25),
            MaxJets = Math.Min(3, 1 + (level + 1) / 2),
            JetSpeed = Math.Min(400, 300 + level * 20),
            FlakEvery = Math.Max(1.0, 1.9 - level * 0.18),
        };
        // Islands: each a cluster of round lumps, well apart, and clear of the carrier's corner.
        var centres = new List<(double X, double Y)>();
        for (int tries = 0; centres.Count < 7 && tries < 400; tries++)
        {
            double x = 500 + d.Next() * (Field.MapW - 1000), y = 450 + d.Next() * (Field.MapH - 900);
            if (Math.Abs(x - b.CarrierX) < 1100 && Math.Abs(y - b.CarrierY) < 800) continue;
            if (centres.Any(c => Math.Abs(c.X - x) < 900 && Math.Abs(c.Y - y) < 760)) continue;
            centres.Add((x, y));
        }
        if (centres.Count < 6) return null;
        for (int i = 0; i < centres.Count; i++)
        {
            var (cx, cy) = centres[i];
            double big = 200 + d.Next() * 70;
            b.Discs.Add((cx, cy, big));
            for (int k = 0; k < 2 + d.Next(3); k++)
            {
                double a = d.Next() * Math.PI * 2, r = big * (0.6 + d.Next() * 0.4);
                b.Discs.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, 90 + d.Next() * 90));
            }
        }
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
        {
            double x = (c + 0.5) * Field.Cell, y = (r + 0.5) * Field.Cell;
            b.Land[c, r] = b.Discs.Any(o => (o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y) < o.R * o.R);
        }
        // A factory in the middle of six of the islands; the one furthest from the carrier is the shipyard.
        var order = Enumerable.Range(0, centres.Count).OrderBy(_ => d.Next()).Take(6).ToList();
        int yard = order.OrderByDescending(i => Dist(centres[i].X, centres[i].Y, b.CarrierX, b.CarrierY)).First();
        foreach (int i in order) b.Factories.Add(new FactoryDef(centres[i].X, centres[i].Y, i == yard, i));
        // The battleship's slip: the nearest open sea to the shipyard, on the carrier's side.
        var sy = b.Factories.First(f => f.Shipyard);
        double best = double.MaxValue;
        for (double a = 0; a < Math.PI * 2; a += Math.PI / 24)
            for (double r = 200; r < 800; r += 20)
            {
                double x = sy.X + Math.Cos(a) * r, y = sy.Y + Math.Sin(a) * r;
                if (x < 100 || y < 100 || x > Field.MapW - 100 || y > Field.MapH - 100) break;
                // Open water all round, room for the hull.
                bool clear = true;
                for (double q = 0; q < Math.PI * 2 && clear; q += Math.PI / 6)
                    clear = !b.IsLand(x + Math.Cos(q) * 110, y + Math.Sin(q) * 110);
                if (b.IsLand(x, y) || !clear) continue;
                double score = r * 2 + Dist(x, y, b.CarrierX, b.CarrierY) * 0.3;
                if (score < best) { best = score; b.SlipX = x; b.SlipY = y; }
                break;
            }
        if (best == double.MaxValue) return null;
        // Guns round each factory, more of them further into the bay and the game.
        foreach (var f in b.Factories)
        {
            int guns = 2 + Math.Min(3, level) + (f.Shipyard ? 1 : 0);
            double turn = d.Next() * Math.PI * 2;
            for (int k = 0; k < guns; k++)
            {
                double a = turn + k * Math.PI * 2 / guns, r = 120 + d.Next() * 50;
                double x = f.X + Math.Cos(a) * r, y = f.Y + Math.Sin(a) * r;
                if (b.IsLand(x, y)) b.Guns.Add(new GunDef(x, y));
            }
        }
        // Guns on the islands without factories too.
        for (int i = 0; i < centres.Count; i++)
            if (!order.Contains(i))
                b.Guns.Add(new GunDef(centres[i].X, centres[i].Y));
        return b;
    }

    public static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}
