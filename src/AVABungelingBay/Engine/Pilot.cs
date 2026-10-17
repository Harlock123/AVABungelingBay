namespace AVABungelingBay.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. It goes for the battleship if it's afloat, else the
/// shipyard (so the battleship is never finished), else the nearest factory. It turns to face jets that come
/// after it and shoots them down, shoots the guns it passes, swerves from flak about to burst where it's
/// heading, and drops its bombs where they'll fall on the target. Battered or out of bombs, it flies home and
/// lands on the carrier to refit.
/// </summary>
public sealed class Pilot
{
    private bool _bombPrev;
    private bool _home;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _bombPrev = false;
        _home = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var h = w.Heli;
        var b = w.Bay;
        if (h.Dead) return default;
        if (h.Landed)
        {
            _home = false;
            Doing = "refitting";
            if (h.Damage > 0 || h.Bombs < Heli.Bombs0 || w.Cleared) return default;
            return new Controls(0, -1);
        }
        // Home to refit when battered or out of bombs.
        if (h.Damage >= 58 || h.Bombs == 0) _home = true;
        if (_home) return Land(w, h);

        // A jet on its tail: round to face it, guns going.
        Jet? jet = null;
        double jd = 400;
        foreach (var j in w.Jets)
        {
            double d = BayBook.Dist(j.X, j.Y, h.X, h.Y);
            if (d < jd && j.Life < Sim.JetLife) { jd = d; jet = j; }
        }
        if (jet != null)
        {
            Doing = "dogfighting";
            var (jx, jy) = Sim.Ahead(jet.Heading);
            double t = jd / Sim.ShotSpeed;
            double want = Sim.Bearing(h.X, h.Y, jet.X + jx * b.JetSpeed * t, jet.Y + jy * b.JetSpeed * t);
            double err = Sim.Wrap(want - h.Heading);
            return Dodge(w, h, new Controls(Turn(err), Math.Abs(err) < 1.2 ? -1 : 1, Math.Abs(err) < 0.2, false));
        }

        // The target: the battleship, the shipyard, or the nearest factory.
        double tx, ty, radius;
        if (w.Ship is { } ship)
        {
            var (sx, sy) = Sim.Ahead(ship.Heading);
            tx = ship.X + sx * Battleship.Speed * Bomb.Fall;
            ty = ship.Y + sy * Battleship.Speed * Bomb.Fall;
            radius = Bomb.Blast + 10;
            Doing = "after the battleship";
        }
        else
        {
            var yard = w.Factories.FirstOrDefault(f => f.Def.Shipyard && !f.Down);
            var f = yard ?? w.Factories.Where(f => !f.Down).OrderBy(f => BayBook.Dist(f.Def.X, f.Def.Y, h.X, h.Y)).FirstOrDefault();
            if (f == null) { _home = true; return Land(w, h); }
            tx = f.Def.X;
            ty = f.Def.Y;
            radius = Sim.FactoryRadius;
            Doing = f.Def.Shipyard ? "bombing the shipyard" : "bombing a factory";
        }
        // The guns round it first, with the guns: fewer shells to fly through on the bombing runs.
        var guard = w.Guns.Where(gn => !gn.Down && BayBook.Dist(gn.Def.X, gn.Def.Y, tx, ty) < 300)
            .OrderBy(gn => BayBook.Dist(gn.Def.X, gn.Def.Y, h.X, h.Y)).FirstOrDefault();
        if (guard != null)
        {
            Doing = "shooting the guns";
            double gd = BayBook.Dist(h.X, h.Y, guard.Def.X, guard.Def.Y);
            double ge = Sim.Wrap(Sim.Bearing(h.X, h.Y, guard.Def.X, guard.Def.Y) - h.Heading);
            // Run in at it, guns going; past it, round again.
            int gdy = Math.Abs(ge) < 0.8 ? (gd > 200 ? -1 : 0) : 1;
            return Dodge(w, h, new Controls(Turn(ge), gdy, Shootable(w, h), false));
        }
        double dist = BayBook.Dist(h.X, h.Y, tx, ty);
        double e = Sim.Wrap(Sim.Bearing(h.X, h.Y, tx, ty) - h.Heading);
        // Close in: slow enough to turn round on it for another pass.
        int dy = Math.Abs(e) < 1.0 ? -1 : dist < 260 ? 0 : 1;
        var (fx, fy) = Sim.BombFalls(h);
        bool bomb = h.Bombs > 0 && BayBook.Dist(fx, fy, tx, ty) < radius * 0.75 && !_bombPrev;
        _bombPrev = bomb;
        return Dodge(w, h, new Controls(Turn(e), dy, Shootable(w, h), bomb));
    }

    private static int Turn(double err) => Math.Abs(err) < 0.06 ? 0 : Math.Sign(err);

    /// <summary>Guns at whatever is in front and in range.</summary>
    private static bool Shootable(World w, Heli h)
    {
        bool InFront(double x, double y, double range)
        {
            double d = BayBook.Dist(h.X, h.Y, x, y);
            return d < range && Math.Abs(Sim.Wrap(Sim.Bearing(h.X, h.Y, x, y) - h.Heading)) < 0.18;
        }
        return w.Guns.Any(g => !g.Down && InFront(g.Def.X, g.Def.Y, 380)) || w.Jets.Any(j => InFront(j.X, j.Y, 420)) || w.Ship is { } s && InFront(s.X, s.Y, 380);
    }

    /// <summary>Flak about to burst where it's heading: swerve, and keep going.</summary>
    private static Controls Dodge(World w, Heli h, Controls c)
    {
        var (ax, ay) = Sim.Ahead(h.Heading);
        foreach (var f in w.Flak)
        {
            double bx = f.X + f.Vx * f.Time, by = f.Y + f.Vy * f.Time;
            double px = h.X + ax * h.Speed * f.Time, py = h.Y + ay * h.Speed * f.Time;
            if (BayBook.Dist(bx, by, px, py) > Sim.FlakBurst + 26) continue;
            // Turn away from the burst, and get moving.
            double side = ax * (by - h.Y) - ay * (bx - h.X);
            return c with { Dx = side > 0 ? -1 : 1, Dy = -1 };
        }
        return c;
    }

    private Controls Land(World w, Heli h)
    {
        var b = w.Bay;
        Doing = "home to the carrier";
        double tx = b.CarrierX, ty = b.CarrierY;
        // The guns round it first, with the guns: fewer shells to fly through on the bombing runs.
        var guard = w.Guns.Where(gn => !gn.Down && BayBook.Dist(gn.Def.X, gn.Def.Y, tx, ty) < 300)
            .OrderBy(gn => BayBook.Dist(gn.Def.X, gn.Def.Y, h.X, h.Y)).FirstOrDefault();
        if (guard != null)
        {
            Doing = "shooting the guns";
            double gd = BayBook.Dist(h.X, h.Y, guard.Def.X, guard.Def.Y);
            double ge = Sim.Wrap(Sim.Bearing(h.X, h.Y, guard.Def.X, guard.Def.Y) - h.Heading);
            // Run in at it, guns going; past it, round again.
            int gdy = Math.Abs(ge) < 0.8 ? (gd > 200 ? -1 : 0) : 1;
            return Dodge(w, h, new Controls(Turn(ge), gdy, Shootable(w, h), false));
        }
        double dist = BayBook.Dist(h.X, h.Y, tx, ty);
        double e = Sim.Wrap(Sim.Bearing(h.X, h.Y, tx, ty) - h.Heading);
        if (Sim.OverDeck(b, h.X, h.Y) && dist < 90)
        {
            Doing = "landing";
            // Hover: no forward way on.
            return new Controls(0, h.Speed > 30 ? 1 : 0, false, false);
        }
        double want = Math.Clamp(dist * 0.9, 40, Heli.MaxSpeed);
        int dy = Math.Abs(e) > 1.2 ? (h.Speed > 60 ? 1 : 0) : h.Speed < want ? -1 : h.Speed > want + 20 ? 1 : 0;
        var c = new Controls(Turn(e), dy, Shootable(w, h), false);
        // Jets about while going home: still shoot back if one's in front.
        return dist > 300 ? Dodge(w, h, c) : c;
    }
}
