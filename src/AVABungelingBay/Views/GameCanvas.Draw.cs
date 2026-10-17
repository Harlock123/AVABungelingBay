using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABungelingBay.Engine;

namespace AVABungelingBay.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABungelingBay/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 688;
    private const uint Sea = 0xFF1A4E8E, SeaLight = 0xFF3A70B0, Grass = 0xFF3E8A3A, GrassDark = 0xFF3A8236, Sand = 0xFFE0C888;
    private double _camX, _camY;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawBay(dc, g.World);
                DrawRadar(dc, g.World);
                DrawHud(dc, g.World);
                if (g.Message is { } msg)
                {
                    double wdt = Measure(msg, 44) + 40;
                    dc.FillRectangle(Brush(0xC0000000), new Rect(512 - wdt / 2, 250, wdt, 64), 8);
                    Text(dc, msg, new Point(512, 282), 44, g.Phase == Phase.Sunk ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, ViewH - 20), 22, Yellow, center: true);
    }

    private static double Measure(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(White)).Width;

    // ───────────────────────────── the bay ─────────────────────────────

    private void DrawBay(DrawingContext dc, World w)
    {
        var h = w.Heli;
        _camX = Math.Clamp(h.X - Field.Width / 2, 0, Field.MapW - Field.Width);
        _camY = Math.Clamp(h.Y - ViewH / 2, 0, Field.MapH - ViewH);
        using (dc.PushClip(new Rect(0, 0, Field.Width, ViewH)))
        using (dc.PushTransform(Matrix.CreateTranslation(-_camX, -_camY)))
        {
            DrawSea(dc);
            DrawLand(dc, w.Bay);
            DrawCarrier(dc, w);
            foreach (var f in w.Factories) DrawFactory(dc, w, f);
            foreach (var gn in w.Guns) DrawGun(dc, gn);
            if (w.Ship is { } ship) DrawShip(dc, ship.X, ship.Y, ship.Heading, 1);
            foreach (var b in w.Bombs)
            {
                double k = 0.4 + 0.6 * b.Time / Bomb.Fall;
                dc.FillEllipse(b.X, b.Y, 6 * k, 0xFF202020);
            }
            foreach (var s in w.Shots) dc.FillEllipse(s.X, s.Y, s.Mine ? 3 : 3.5, s.Mine ? 0xFFFFF080 : 0xFFFF5040);
            foreach (var f in w.Flak) dc.FillEllipse(f.X, f.Y, 3.5, 0xFF303030);
            foreach (var j in w.Jets) DrawJet(dc, j);
            if (!h.Dead) DrawHeli(dc, h.X, h.Y, h.Heading, h.Rotor, h.Landed ? 0.7 : 1);
            foreach (var b in w.Booms) DrawBoom(dc, b);
        }
    }

    private void DrawSea(DrawingContext dc)
    {
        dc.FillRectangle(Brush(Sea), new Rect(_camX, _camY, Field.Width, ViewH));
        // Little white-caps, drifting.
        int x0 = (int)(_camX / 64) - 1, y0 = (int)(_camY / 48) - 1;
        for (int gx = x0; gx < x0 + Field.Width / 64 + 3; gx++)
        for (int gy = y0; gy < y0 + ViewH / 48 + 3; gy++)
        {
            uint hh = Hash(gx, gy, 5);
            if (hh % 3 != 0) continue;
            double x = gx * 64 + hh % 40 + Math.Sin(_clock * 0.8 + gy) * 6, y = gy * 48 + (hh >> 8) % 30;
            dc.DrawLine(Pen(SeaLight, 2), new Point(x, y), new Point(x + 10, y));
        }
    }

    private void DrawLand(DrawingContext dc, BayDef b)
    {
        int c0 = Math.Max(0, (int)(_camX / Field.Cell) - 1), r0 = Math.Max(0, (int)(_camY / Field.Cell) - 1);
        int c1 = Math.Min(Field.Cols - 1, c0 + (int)(Field.Width / Field.Cell) + 3), r1 = Math.Min(Field.Rows - 1, r0 + (int)(ViewH / Field.Cell) + 3);
        const double C = Field.Cell;
        bool L(int c, int r) => c >= 0 && r >= 0 && c < Field.Cols && r < Field.Rows && b.Land[c, r];
        // Sand first, a little wider than the land, then the grass over it.
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
            if (b.Land[c, r]) dc.FillRectangle(Brush(Sand), new Rect(c * C - 8, r * C - 8, C + 16, C + 16), 10);
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
        {
            if (!b.Land[c, r]) continue;
            bool coast = !L(c - 1, r) || !L(c + 1, r) || !L(c, r - 1) || !L(c, r + 1);
            double inset = coast ? 4 : 0;
            dc.FillRectangle(Brush((Hash(c, r, 1) & 3) == 0 ? GrassDark : Grass), new Rect(c * C + (L(c - 1, r) ? 0 : inset), r * C + (L(c, r - 1) ? 0 : inset), C - (L(c - 1, r) ? 0 : inset) - (L(c + 1, r) ? 0 : inset), C - (L(c, r - 1) ? 0 : inset) - (L(c, r + 1) ? 0 : inset)));
            if ((Hash(c, r, 2) & 7) == 0 && !coast)
            {
                // A tree.
                double tx = c * C + 20, ty = r * C + 20;
                dc.FillEllipse(tx + 3, ty + 4, 9, 0x60000000);
                dc.FillEllipse(tx, ty, 9, 0xFF2A6A22);
                dc.FillEllipse(tx - 2, ty - 2, 5, 0xFF4A9A3A);
            }
        }
    }

    private void DrawCarrier(DrawingContext dc, World w)
    {
        var b = w.Bay;
        double x = b.CarrierX, y = b.CarrierY;
        Poly(dc, 0xFF5A6068, 0xFF30343A, 2, new Point(x - 190, y - 30), new Point(x + 160, y - 34), new Point(x + 200, y), new Point(x + 160, y + 34), new Point(x - 190, y + 30));
        dc.FillRectangle(Brush(0xFF3A3E46), new Rect(x - 180, y - 22, 330, 44));
        // The deck's markings, and the landing pad.
        for (double dx = -170; dx < 140; dx += 30) dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(x + dx, y - 1.5, 16, 3));
        dc.DrawEllipse(null, Pen(0xFFFFE040, 3), new Point(x + 60, y), 26, 26);
        Text(dc, "H", new Point(x + 60, y), 34, 0xFFFFE040, center: true, shadow: false);
        // The island (the bridge).
        dc.FillRectangle(Brush(0xFF6A7078), new Rect(x - 70, y - 44, 60, 16));
        dc.FillRectangle(Brush(0xFF8A9098), new Rect(x - 60, y - 40, 16, 8));
        if (w.CarrierHp < 100)
            for (int i = 0; i < (int)((100 - w.CarrierHp) / 15); i++)
            {
                double sx = x - 160 + Hash(i, 0, 7) % 300, sy = y - 16 + Hash(i, 1, 7) % 30;
                dc.FillEllipse(sx, sy - (_clock * 30 + i * 13) % 40, 8 + i % 3 * 3, 0x80303030);
                dc.FillEllipse(sx, sy, 5, (int)(_clock * 8 + i) % 2 == 0 ? 0xFFFF8020 : 0xFFFFD040);
            }
    }

    private void DrawFactory(DrawingContext dc, World w, Factory f)
    {
        double x = f.Def.X, y = f.Def.Y;
        if (f.Down)
        {
            dc.FillRectangle(Brush(0xFF3A3028), new Rect(x - 60, y - 44, 120, 88), 6);
            for (int i = 0; i < 8; i++)
            {
                uint hh = Hash((int)x, i, 11);
                dc.FillRectangle(Brush(0xFF5A5048), new Rect(x - 54 + hh % 100, y - 38 + (hh >> 8) % 70, 10 + (hh >> 16) % 12, 6 + (hh >> 20) % 8));
            }
            for (int i = 0; i < 3; i++)
                dc.FillEllipse(x - 30 + i * 30, y - (_clock * 20 + i * 17) % 50, 10 + i * 2, 0x70404040);
            return;
        }
        // Concrete apron, the works, saw-tooth roofs, chimneys.
        dc.FillRectangle(Brush(0xFF8A8A80), new Rect(x - 66, y - 50, 132, 100), 4);
        dc.FillRectangle(Brush(f.Def.Shipyard ? 0xFF7A4A3A : 0xFF6A6A78), new Rect(x - 56, y - 40, 112, 70));
        for (int i = 0; i < 4; i++)
            Poly(dc, f.Def.Shipyard ? 0xFF9A6A5A : 0xFF8A8A98, null, 0, new Point(x - 56 + i * 28, y - 40), new Point(x - 56 + i * 28 + 28, y - 40), new Point(x - 56 + i * 28 + 28, y - 26));
        dc.FillRectangle(Brush(0xFF4A4A54), new Rect(x + 30, y - 66, 12, 30));
        dc.FillRectangle(Brush(0xFF4A4A54), new Rect(x + 12, y - 60, 10, 24));
        for (int i = 0; i < 4; i++)
            dc.FillEllipse(x + 36 + i * 8, y - 70 - (_clock * 25 + i * 11) % 44, 6 + i * 2, 0x70C0C0C0);
        // Damage shows as fires.
        double hurt = 1 - f.Hp / Factory.Hp0;
        for (int i = 0; i < (int)(hurt * 5); i++)
            dc.FillEllipse(x - 40 + i * 20, y - 10, 7, (int)(_clock * 9 + i) % 2 == 0 ? 0xFFFF7020 : 0xFFFFD040);
        if (f.Def.Shipyard)
        {
            Text(dc, "SHIPYARD", new Point(x, y + 40), 20, 0xFFFFE0A0, center: true);
            // The battleship on the slip, as far on as it's got.
            if (!w.ShipLaunched && w.Built > 0.02)
            {
                var b = w.Bay;
                double heading = Sim.Bearing(b.SlipX, b.SlipY, b.CarrierX, b.CarrierY);
                using (dc.PushOpacity(0.5 + w.Built * 0.5))
                    DrawShip(dc, b.SlipX, b.SlipY, heading, w.Built);
                // Scaffolding.
                dc.DrawEllipse(null, Pen(0xC0FFE040, 2), new Point(b.SlipX, b.SlipY), 84, 84);
            }
        }
    }

    private void DrawGun(DrawingContext dc, Gun g)
    {
        double x = g.Def.X, y = g.Def.Y;
        if (g.Down)
        {
            dc.FillEllipse(x, y, 13, 0xFF2A2420);
            dc.FillEllipse(x, y, 7, 0xFF1A1410);
            return;
        }
        dc.FillEllipse(x, y, 14, 0xFF5A5A40);
        dc.FillEllipse(x, y, 10, 0xFF7A7A5A);
        var (ax, ay) = Sim.Ahead(g.Aim);
        dc.DrawLine(Pen(0xFF303028, 5), new Point(x, y), new Point(x + ax * 20, y + ay * 20));
    }

    private void DrawShip(DrawingContext dc, double x, double y, double heading, double built)
    {
        using (dc.PushTransform(Matrix.CreateRotation(heading) * Matrix.CreateTranslation(x, y)))
        {
            double len = 80 * Math.Max(0.3, built);
            Poly(dc, 0xFF6A6E78, 0xFF3A3E46, 2, new Point(0, -len - 14), new Point(18, -len + 10), new Point(18, len), new Point(-18, len), new Point(-18, -len + 10));
            if (built < 1) return;
            dc.FillRectangle(Brush(0xFF8A8E98), new Rect(-9, -16, 18, 30));
            foreach (double t in new[] { -48.0, -28, 34 })
            {
                dc.FillEllipse(0, t, 9, 0xFF4A4E58);
                dc.DrawLine(Pen(0xFF2A2E38, 4), new Point(0, t), new Point(0, t + (t < 0 ? -18 : 18)));
            }
        }
    }

    private void DrawJet(DrawingContext dc, Jet j)
    {
        using (dc.PushTransform(Matrix.CreateRotation(j.Heading) * Matrix.CreateTranslation(j.X + 16, j.Y + 22)))
            Poly(dc, 0x50000000, null, 0, new Point(0, -18), new Point(14, 12), new Point(0, 6), new Point(-14, 12));
        using (dc.PushTransform(Matrix.CreateRotation(j.Heading) * Matrix.CreateTranslation(j.X, j.Y)))
        {
            Poly(dc, 0xFFB0B4C0, 0xFF50545E, 1.5, new Point(0, -18), new Point(14, 12), new Point(0, 6), new Point(-14, 12));
            dc.FillRectangle(Brush(0xFFC03030), new Rect(-2, -6, 4, 10));
            dc.FillEllipse(0, 10, 3, (int)(_clock * 20) % 2 == 0 ? 0xFFFF9020 : 0xFFFFE040);
        }
    }

    /// <summary>The helicopter from above, its shadow on the sea below and its rotor turning.</summary>
    private void DrawHeli(DrawingContext dc, double x, double y, double heading, double rotor, double scale)
    {
        using (dc.PushTransform(Matrix.CreateRotation(heading) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x + 14 * scale, y + 20 * scale)))
        {
            dc.DrawEllipse(Brush(0x50000000), null, new Point(0, 0), 10, 16);
            dc.DrawLine(Pen(0x50000000, 4), new Point(0, 10), new Point(0, 34));
        }
        using (dc.PushTransform(Matrix.CreateRotation(heading) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
        {
            dc.DrawLine(Pen(0xFF2A5A2A, 4), new Point(0, 8), new Point(0, 34));
            dc.DrawLine(Pen(0xFF2A5A2A, 3), new Point(-8, 33), new Point(8, 33));
            dc.DrawEllipse(Brush(0xFF3A7A3A), Pen(0xFF1A3A1A, 1.5), new Point(0, 0), 11, 17);
            dc.DrawEllipse(Brush(0xFF90D0F0), null, new Point(0, -8), 7, 6);
            dc.FillRectangle(Brush(0xFF1A3A1A), new Rect(-14, -2, 4, 10));
            dc.FillRectangle(Brush(0xFF1A3A1A), new Rect(10, -2, 4, 10));
            for (int i = 0; i < 2; i++)
            {
                double a = rotor + i * Math.PI / 2;
                dc.DrawLine(Pen(0xC0202020, 3), new Point(Math.Cos(a) * -30, Math.Sin(a) * -30), new Point(Math.Cos(a) * 30, Math.Sin(a) * 30));
            }
            dc.FillEllipse(0, 0, 3, 0xFF101010);
        }
    }

    private void DrawBoom(DrawingContext dc, Boom b)
    {
        double t = b.T;
        switch (b.Kind)
        {
            case Blast.Big:
            {
                double r = Math.Min(1, t * 3) * 80;
                using (dc.PushOpacity(Math.Clamp(1.2 - t, 0, 1)))
                {
                    dc.FillEllipse(b.X, b.Y, r, 0xFFFF7020);
                    dc.FillEllipse(b.X, b.Y, r * 0.6, 0xFFFFD040);
                    dc.FillEllipse(b.X, b.Y, r * 0.25, 0xFFFFFFFF);
                }
                break;
            }
            case Blast.Small:
                using (dc.PushOpacity(Math.Clamp(1 - t * 1.6, 0, 1)))
                {
                    dc.FillEllipse(b.X, b.Y, 10 + t * 50, 0xFFFF8020);
                    dc.FillEllipse(b.X, b.Y, 6 + t * 28, 0xFFFFE060);
                }
                break;
            case Blast.Burst:
                using (dc.PushOpacity(Math.Clamp(1 - t * 1.4, 0, 1)))
                {
                    dc.FillEllipse(b.X, b.Y, 14 + t * 30, 0xFF3A3A3A);
                    if (t < 0.15) dc.FillEllipse(b.X, b.Y, 10, 0xFFFFB040);
                }
                break;
            case Blast.Splash:
                using (dc.PushOpacity(Math.Clamp(1 - t * 1.2, 0, 1)))
                {
                    dc.DrawEllipse(null, Pen(0xFFE8F4FF, 3), new Point(b.X, b.Y), 8 + t * 50, 8 + t * 50);
                    dc.FillEllipse(b.X, b.Y, Math.Max(0, 12 - t * 20), 0xFFE8F4FF);
                }
                break;
        }
    }

    // ───────────────────────────── the radar and the gauges ─────────────────────────────

    private void DrawRadar(DrawingContext dc, World w)
    {
        const double rw = 216, rh = 162;
        double rx = Field.Width - rw - 10, ry = 10, k = rw / Field.MapW;
        dc.FillRectangle(Brush(0xC0081830), new Rect(rx - 4, ry - 4, rw + 8, rh + 8), 6);
        dc.DrawRectangle(null, Pen(0xFF40A040, 2), new Rect(rx - 4, ry - 4, rw + 8, rh + 8), 6, 6);
        var b = w.Bay;
        foreach (var (x, y, r) in b.Discs) dc.FillEllipse(rx + x * k, ry + y * k, r * k, 0xFF2A6A2A);
        dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(rx + (b.CarrierX - 190) * k, ry + (b.CarrierY - 30) * k, 390 * k, 60 * k));
        foreach (var f in w.Factories)
        {
            double fx = rx + f.Def.X * k, fy = ry + f.Def.Y * k;
            if (f.Down) { dc.DrawLine(Pen(0xFF808080, 2), new Point(fx - 4, fy - 4), new Point(fx + 4, fy + 4)); dc.DrawLine(Pen(0xFF808080, 2), new Point(fx + 4, fy - 4), new Point(fx - 4, fy + 4)); }
            else dc.FillRectangle(Brush(f.Def.Shipyard ? 0xFFFF9030 : 0xFFFF4040), new Rect(fx - 4, fy - 4, 8, 8));
        }
        foreach (var j in w.Jets) dc.FillEllipse(rx + j.X * k, ry + j.Y * k, 2.5, 0xFFFF6060);
        if (w.Ship is { } s) dc.FillEllipse(rx + s.X * k, ry + s.Y * k, 5, (int)(_clock * 4) % 2 == 0 ? 0xFFFFA020 : 0xFFFFFFFF);
        var h = w.Heli;
        if (!h.Dead && (int)(_clock * 5) % 3 != 0) dc.FillEllipse(rx + h.X * k, ry + h.Y * k, 3.5, 0xFFFFF040);
        dc.DrawRectangle(null, Pen(0x60FFFFFF, 1), new Rect(rx + _camX * k, ry + _camY * k, Field.Width * k, ViewH * k));
    }

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        double y = ViewH;
        dc.FillRectangle(Brush(0xFF14181E), new Rect(0, y, Field.Width, Field.Height - y));
        dc.FillRectangle(Brush(0xFF40A040), new Rect(0, y, Field.Width, 2));
        var h = w.Heli;
        Text(dc, $"SCORE {g.Score:000000}", new Point(14, y + 20), 28, White);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(14, y + 52), 22, Dim);
        // Damage.
        Text(dc, "DAMAGE", new Point(220, y + 18), 20, Dim);
        dc.FillRectangle(Brush(0xFF303840), new Rect(290, y + 10, 150, 16), 3);
        uint dcol = h.Damage > 70 ? Red : h.Damage > 40 ? Orange : 0xFF40C040;
        dc.FillRectangle(Brush(dcol), new Rect(290, y + 10, 150 * Math.Min(1, h.Damage / 100), 16), 3);
        // Bombs.
        Text(dc, "BOMBS", new Point(220, y + 50), 20, Dim);
        for (int i = 0; i < Heli.Bombs0; i++)
            dc.FillEllipse(298 + i * 16, y + 50, 5.5, i < h.Bombs ? 0xFF202020 : 0xFF404850);
        for (int i = 0; i < Heli.Bombs0; i++)
            if (i < h.Bombs) dc.FillEllipse(296 + i * 16, y + 48, 2, 0xFF808890);
        // Helicopters left.
        for (int i = 0; i < Math.Min(6, g.Lives - 1); i++)
            DrawHeli(dc, 470 + i * 30, y + 40, 0, 0.3, 0.55);
        // Factories left, the battleship, the carrier.
        Text(dc, "FACTORIES", new Point(650, y + 18), 20, Dim);
        for (int i = 0; i < w.Factories.Count; i++)
        {
            bool down = w.Factories[i].Down;
            double fx = 740 + i * 22;
            dc.FillRectangle(Brush(down ? 0xFF404040 : w.Factories[i].Def.Shipyard ? 0xFFFF9030 : 0xFFFF4040), new Rect(fx, y + 10, 16, 16), 2);
            if (down) dc.DrawLine(Pen(White, 2), new Point(fx, y + 10), new Point(fx + 16, y + 26));
        }
        string ship = w.Ship != null ? "BATTLESHIP AFLOAT!" : w.ShipSunk ? "BATTLESHIP SUNK" : w.Factories.First(f => f.Def.Shipyard).Down ? "SHIPYARD DOWN" : $"BATTLESHIP {(int)(w.Built * 100)}%";
        Text(dc, ship, new Point(650, y + 50), 20, w.Ship != null && (int)(_clock * 3) % 2 == 0 ? Red : Orange);
        Text(dc, "CARRIER", new Point(870, y + 50), 20, Dim);
        dc.FillRectangle(Brush(0xFF303840), new Rect(940, y + 42, 70, 16), 3);
        dc.FillRectangle(Brush(w.CarrierHp > 40 ? 0xFF40A0E0 : Red), new Rect(940, y + 42, 70 * w.CarrierHp / 100, 16), 3);
    }

    // ───────────────────────────── the title and the end ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(Sea), new Rect(0, 0, Field.Width, Field.Height));
        _camX = _camY = 0;
        for (int gx = 0; gx < 18; gx++)
        for (int gy = 0; gy < 18; gy++)
            if (Hash(gx, gy, 5) % 3 == 0) dc.DrawLine(Pen(SeaLight, 2), new Point(gx * 64 + Hash(gx, gy, 5) % 40 + Math.Sin(_clock + gy) * 6, gy * 48), new Point(gx * 64 + Hash(gx, gy, 5) % 40 + 10 + Math.Sin(_clock + gy) * 6, gy * 48));
        // An island with its factory, and the helicopter coming in.
        dc.FillEllipse(cx, 330, 150, Sand);
        dc.FillEllipse(cx, 330, 138, Grass);
        dc.FillEllipse(cx + 90, 300, 70, Sand);
        dc.FillEllipse(cx + 90, 300, 60, Grass);
        var f = new Factory { Def = new FactoryDef(cx, 330, false, 0) };
        DrawFactory(dc, new World(), f);
        DrawGun(dc, new Gun { Def = new GunDef(cx + 100, 290), Aim = -2.4 });
        DrawGun(dc, new Gun { Def = new GunDef(cx - 90, 380), Aim = -0.6 });
        double hx = cx - 260 + Math.Sin(_clock * 0.6) * 30, hy = 250 + Math.Cos(_clock * 0.8) * 20;
        DrawHeli(dc, hx, hy, 2.0, _clock * 30, 1.6);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 20, Field.Width, 120));
        Text(dc, "AVA BUNGELING BAY", new Point(cx, 70), 92, Yellow, center: true);
        Text(dc, "AFTER WILL WRIGHT'S 1984 GAME FROM BRØDERBUND", new Point(cx, 122), 22, Dim, center: true);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 500, Field.Width, 268));
        Text(dc, "THE BUNGELING EMPIRE IS BUILDING A WAR MACHINE IN ITS ISLAND FACTORIES.", new Point(cx, 520), 22, White, center: true);
        Text(dc, "FLY OFF THE CARRIER AND BOMB EVERY FACTORY - THE SHIPYARD FIRST, BEFORE ITS BATTLESHIP SAILS.", new Point(cx, 546), 22, White, center: true);
        Text(dc, "LAND BACK ON THE DECK TO PATCH UP AND LOAD MORE BOMBS.", new Point(cx, 572), 22, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 626), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "LEFT/RIGHT: TURN   UP: FLY   DOWN: SLOW   SPACE: GUNS   X: BOMB   P: PAUSE   M: SOUND", new Point(cx, 680), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 722), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, Engine.World.CarrierHp <= 0 ? "THE CARRIER IS LOST" : "NO HELICOPTERS LEFT", new Point(cx, 330), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 420), 44, Yellow, center: true);
        Text(dc, $"BAYS DESTROYED {Engine.Cleared}", new Point(cx, 476), 30, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP PILOTS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE     BAYS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,6}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
