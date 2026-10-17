namespace AVABungelingBay.Engine;

/// <summary>
/// A bay, step by step: the helicopter flying, firing and bombing, landing on the carrier to refit; the guns'
/// flak, the jets the factories send up, and the battleship the shipyard builds while the factories work.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double ShotSpeed = 640, ShotLife = 0.62, FireEvery = 0.12, FlakSpeed = 280, FlakRange = 470, FlakBurst = 42;
    public const double JetTurn = 1.7, JetLaunchRange = 1500, JetEvery = 9, JetLife = 26;
    public const double DeckHalfW = 160, DeckHalfH = 34;
    public const double FactoryRadius = 64;
    public const int FactoryPoints = 1000, GunPoints = 100, JetPoints = 200, ShipPoints = 3000;

    public static void Start(World w, BayDef b, int seed)
    {
        w.Bay = b;
        w.Dice = new Dice(seed);
        w.Clock = 0;
        w.Factories = b.Factories.Select(f => new Factory { Def = f, Launch = 3 - JetEvery }).ToList();
        w.Guns = b.Guns.Select(g => new Gun { Def = g, Cool = 1 + w.Dice.Next() * 2 }).ToList();
        w.Jets.Clear();
        w.NextJet = 0;
        w.Shots.Clear();
        w.Flak.Clear();
        w.Bombs.Clear();
        w.Booms.Clear();
        w.Ship = null;
        w.Built = 0;
        w.ShipSunk = w.ShipLaunched = false;
        w.CarrierHp = 100;
        w.Cause = "";
        OnDeck(w);
    }

    /// <summary>A fresh helicopter, sitting on the carrier's deck.</summary>
    public static void OnDeck(World w)
    {
        w.Heli = new Heli { X = w.Bay.CarrierX + 60, Y = w.Bay.CarrierY, Heading = Math.PI / 4, Landed = true };
        w.Jets.Clear();
        w.Shots.RemoveAll(s => !s.Mine);
        w.Flak.Clear();
    }

    public static (double X, double Y) Ahead(double heading) => (Math.Sin(heading), -Math.Cos(heading));

    public static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static double Bearing(double fx, double fy, double tx, double ty) => Math.Atan2(tx - fx, -(ty - fy));

    public static bool OverDeck(BayDef b, double x, double y) => Math.Abs(x - b.CarrierX) < DeckHalfW && Math.Abs(y - b.CarrierY) < DeckHalfH;

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var b in w.Booms) b.T += Dt;
        w.Booms.RemoveAll(b => b.T > 1.2);
        var h = w.Heli;
        if (!h.Dead) Fly(w, h, c);
        Shots(w);
        Bombs(w);
        Guns(w);
        Flak(w);
        Jets(w);
        Shipyard(w);
        if (!h.Dead && h.Damage >= 100) Crash(w, "SHOT DOWN");
    }

    private static void Fly(World w, Heli h, Controls c)
    {
        h.Rotor += Dt * (h.Landed ? 14 : 30);
        h.FireTimer -= Dt;
        h.Heading = Wrap(h.Heading + c.Dx * Heli.Turn * Dt);
        if (h.Landed)
        {
            // Refitting: patched up, and the bombs loaded again, a few at a time.
            if (h.Damage > 0) h.Damage = Math.Max(0, h.Damage - 30 * Dt);
            h.Settle += Dt;
            if (h.Bombs < Heli.Bombs0 && h.Settle > 0.3) { h.Bombs++; h.Settle = 0; w.Heard.Add(Sfx.Load); }
            if (c.Dy < 0) { h.Landed = false; h.Settle = 0; w.Heard.Add(Sfx.TakeOff); }
            h.BombPrev = c.Bomb;
            return;
        }
        double max = Heli.MaxSpeed * (1 - h.Damage / 260);
        if (c.Dy < 0) h.Speed = Math.Min(max, h.Speed + Heli.Thrust * Dt);
        else if (c.Dy > 0) h.Speed = Math.Max(Heli.Reverse, h.Speed - Heli.Brake * Dt);
        else h.Speed -= h.Speed * Heli.Drag * Dt;
        if (h.Speed > max) h.Speed = max;
        var (ax, ay) = Ahead(h.Heading);
        h.X = Math.Clamp(h.X + ax * h.Speed * Dt, 30, Field.MapW - 30);
        h.Y = Math.Clamp(h.Y + ay * h.Speed * Dt, 30, Field.MapH - 30);
        // Down onto the deck: slow, over it, for a moment.
        if (OverDeck(w.Bay, h.X, h.Y) && Math.Abs(h.Speed) < 50 && c.Dy >= 0)
        {
            h.Settle += Dt;
            if (h.Settle > 0.7) { h.Landed = true; h.Speed = 0; h.Settle = 0; w.Heard.Add(Sfx.Land); }
        }
        else h.Settle = 0;
        if (c.Fire && h.FireTimer <= 0)
        {
            h.FireTimer = FireEvery;
            double v = ShotSpeed + Math.Max(0, h.Speed);
            w.Shots.Add(new Shot { X = h.X + ax * 26, Y = h.Y + ay * 26, Vx = ax * v, Vy = ay * v, Life = ShotLife, Mine = true });
            w.Heard.Add(Sfx.Gun);
        }
        if (c.Bomb && !h.BombPrev && h.Bombs > 0)
        {
            h.Bombs--;
            w.Bombs.Add(new Bomb { X = h.X, Y = h.Y, Vx = ax * h.Speed * 0.7, Vy = ay * h.Speed * 0.7, Time = Bomb.Fall });
            w.Heard.Add(Sfx.BombDrop);
        }
        h.BombPrev = c.Bomb;
    }

    /// <summary>Where a bomb dropped now would land.</summary>
    public static (double X, double Y) BombFalls(Heli h)
    {
        var (ax, ay) = Ahead(h.Heading);
        return (h.X + ax * h.Speed * 0.7 * Bomb.Fall, h.Y + ay * h.Speed * 0.7 * Bomb.Fall);
    }

    private static void Shots(World w)
    {
        var h = w.Heli;
        foreach (var s in w.Shots)
        {
            s.X += s.Vx * Dt;
            s.Y += s.Vy * Dt;
            s.Life -= Dt;
            if (s.Life <= 0) continue;
            if (s.Mine)
            {
                foreach (var j in w.Jets)
                    if (j.Hp > 0 && Near(s.X, s.Y, j.X, j.Y, 24)) { s.Life = 0; HitJet(w, j, 1); break; }
                if (s.Life <= 0) continue;
                foreach (var g in w.Guns)
                    if (!g.Down && Near(s.X, s.Y, g.Def.X, g.Def.Y, 20)) { s.Life = 0; HitGun(w, g, 1); break; }
                if (s.Life <= 0) continue;
                if (w.Ship is { } ship && Near(s.X, s.Y, ship.X, ship.Y, 60)) { s.Life = 0; HitShip(w, 0.25, s.X, s.Y); }
            }
            else if (!h.Dead && !h.Landed && Near(s.X, s.Y, h.X, h.Y, 18))
            {
                s.Life = 0;
                Hurt(w, 5);
            }
        }
        w.Shots.RemoveAll(s => s.Life <= 0);
        w.Jets.RemoveAll(j => j.Hp <= 0);
    }

    private static void Hurt(World w, double d)
    {
        w.Heli.Damage += d;
        w.Heard.Add(Sfx.Hurt);
    }

    private static bool Near(double ax, double ay, double bx, double by, double r) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by) < r * r;

    private static void HitJet(World w, Jet j, double d)
    {
        j.Hp -= d;
        if (j.Hp > 0) { w.Heard.Add(Sfx.Clang); return; }
        w.Score += JetPoints;
        w.Booms.Add(new Boom(j.X, j.Y, Blast.Small));
        w.Heard.Add(Sfx.JetDown);
    }

    private static void HitGun(World w, Gun g, double d)
    {
        g.Hp -= d;
        if (!g.Down) { w.Heard.Add(Sfx.Clang); return; }
        w.Score += GunPoints;
        w.Booms.Add(new Boom(g.Def.X, g.Def.Y, Blast.Small));
        w.Heard.Add(Sfx.GunDown);
    }

    private static void HitShip(World w, double d, double x, double y)
    {
        var ship = w.Ship!;
        ship.Hp -= d;
        w.Booms.Add(new Boom(x, y, Blast.Small));
        if (ship.Hp > 0) { w.Heard.Add(Sfx.Clang); return; }
        w.Score += ShipPoints;
        w.Booms.Add(new Boom(ship.X, ship.Y, Blast.Big));
        w.Heard.Add(Sfx.ShipSunk);
        w.Ship = null;
        w.ShipSunk = true;
    }

    private static void Bombs(World w)
    {
        foreach (var b in w.Bombs)
        {
            b.X += b.Vx * Dt;
            b.Y += b.Vy * Dt;
            b.Time -= Dt;
            if (b.Time > 0) continue;
            bool hit = false;
            foreach (var f in w.Factories)
                if (!f.Down && Near(b.X, b.Y, f.Def.X, f.Def.Y, FactoryRadius + 26))
                {
                    hit = true;
                    f.Hp -= 4;
                    if (f.Down)
                    {
                        w.Score += FactoryPoints;
                        w.Booms.Add(new Boom(f.Def.X, f.Def.Y, Blast.Big));
                        w.Heard.Add(Sfx.FactoryDown);
                    }
                }
            foreach (var g in w.Guns)
                if (!g.Down && Near(b.X, b.Y, g.Def.X, g.Def.Y, Bomb.Blast)) { hit = true; HitGun(w, g, 9); }
            if (w.Ship is { } ship && Near(b.X, b.Y, ship.X, ship.Y, Bomb.Blast + 30)) { hit = true; HitShip(w, 5, b.X, b.Y); }
            bool sea = !w.Bay.IsLand(b.X, b.Y);
            w.Booms.Add(new Boom(b.X, b.Y, sea && !hit ? Blast.Splash : Blast.Small));
            w.Heard.Add(sea && !hit ? Sfx.Splash : Sfx.Explode);
        }
        w.Bombs.RemoveAll(b => b.Time <= 0);
    }

    private static void Guns(World w)
    {
        var h = w.Heli;
        foreach (var g in w.Guns)
        {
            if (g.Down) continue;
            g.Cool -= Dt;
            if (h.Dead || h.Landed) continue;
            double d = BayBook.Dist(g.Def.X, g.Def.Y, h.X, h.Y);
            if (d > FlakRange) continue;
            g.Aim = Bearing(g.Def.X, g.Def.Y, h.X, h.Y);
            if (g.Cool > 0) continue;
            g.Cool = w.Bay.FlakEvery * (0.8 + w.Dice.Next() * 0.5);
            // Aimed at where the helicopter will be when the shell gets there.
            var (ax, ay) = Ahead(h.Heading);
            double t = d / FlakSpeed;
            double tx = h.X + ax * h.Speed * t, ty = h.Y + ay * h.Speed * t;
            t = BayBook.Dist(g.Def.X, g.Def.Y, tx, ty) / FlakSpeed;
            w.Flak.Add(new Flak { X = g.Def.X, Y = g.Def.Y, Vx = (tx - g.Def.X) / t, Vy = (ty - g.Def.Y) / t, Time = t });
            w.Heard.Add(Sfx.FlakFire);
        }
    }

    private static void Flak(World w)
    {
        var h = w.Heli;
        foreach (var f in w.Flak)
        {
            f.X += f.Vx * Dt;
            f.Y += f.Vy * Dt;
            f.Time -= Dt;
            if (f.Time > 0) continue;
            w.Booms.Add(new Boom(f.X, f.Y, Blast.Burst));
            w.Heard.Add(Sfx.Burst);
            if (!h.Dead && !h.Landed && Near(f.X, f.Y, h.X, h.Y, FlakBurst)) Hurt(w, 9);
        }
        w.Flak.RemoveAll(f => f.Time <= 0);
    }

    private static void Jets(World w)
    {
        var h = w.Heli;
        var b = w.Bay;
        // The factories send jets up after a helicopter that comes near.
        // (One at a time from the whole bay, from the nearest factory in range.)
        if (!h.Dead && !h.Landed && w.Jets.Count < b.MaxJets && w.Clock >= w.NextJet)
            foreach (var f in w.Factories.OrderBy(f => BayBook.Dist(f.Def.X, f.Def.Y, h.X, h.Y)))
            {
                if (f.Down || w.Clock - f.Launch < JetEvery) continue;
                if (BayBook.Dist(f.Def.X, f.Def.Y, h.X, h.Y) > JetLaunchRange) break;
                f.Launch = w.Clock;
                w.NextJet = w.Clock + JetEvery * 0.6;
                w.Jets.Add(new Jet { X = f.Def.X, Y = f.Def.Y, Heading = Bearing(f.Def.X, f.Def.Y, h.X, h.Y), Cool = 1.5 });
                w.Heard.Add(Sfx.JetUp);
                break;
            }
        foreach (var j in w.Jets)
        {
            j.Life += Dt;
            j.Cool -= Dt;
            bool leaving = j.Life > JetLife || h.Dead || h.Landed;
            j.Pass -= Dt;
            double want = leaving || j.Pass > 0 ? j.Heading : Bearing(j.X, j.Y, h.X, h.Y);
            double turn = Math.Clamp(Wrap(want - j.Heading), -JetTurn * Dt, JetTurn * Dt);
            j.Heading = Wrap(j.Heading + turn);
            var (ax, ay) = Ahead(j.Heading);
            j.X += ax * b.JetSpeed * Dt;
            j.Y += ay * b.JetSpeed * Dt;
            if (leaving && (j.X < -100 || j.Y < -100 || j.X > Field.MapW + 100 || j.Y > Field.MapH + 100)) j.Hp = 0;
            if (leaving) continue;
            double d = BayBook.Dist(j.X, j.Y, h.X, h.Y);
            // Close in, it doesn't turn: it overshoots, then comes round for another pass.
            if (d < 150) j.Pass = 1.1;
            if (j.Cool <= 0 && d < 430 && Math.Abs(Wrap(Bearing(j.X, j.Y, h.X, h.Y) - j.Heading)) < 0.22)
            {
                j.Cool = 0.95;
                w.Shots.Add(new Shot { X = j.X + ax * 20, Y = j.Y + ay * 20, Vx = ax * 600, Vy = ay * 600, Life = 0.8 });
                w.Heard.Add(Sfx.JetFire);
            }
        }
        w.Jets.RemoveAll(j => j.Hp <= 0);
    }

    private static void Shipyard(World w)
    {
        var b = w.Bay;
        if (!w.ShipLaunched)
        {
            var yard = w.Factories.First(f => f.Def.Shipyard);
            if (yard.Down) return;
            w.Built += Dt * w.FactoriesLeft / w.Factories.Count / b.BuildSeconds;
            if (w.Built >= 1)
            {
                w.Built = 1;
                w.ShipLaunched = true;
                w.Ship = new Battleship { X = b.SlipX, Y = b.SlipY, Heading = Bearing(b.SlipX, b.SlipY, b.CarrierX, b.CarrierY), Cool = 2 };
                w.Heard.Add(Sfx.ShipLaunched);
            }
            return;
        }
        if (w.Ship is not { } ship) return;
        ship.Cool -= Dt;
        double d = BayBook.Dist(ship.X, ship.Y, b.CarrierX, b.CarrierY);
        if (d > Battleship.Range * 0.8)
        {
            // Round the islands: the clearest way that still heads for the carrier.
            double want = Bearing(ship.X, ship.Y, b.CarrierX, b.CarrierY);
            foreach (double off in new[] { 0, 0.35, -0.35, 0.7, -0.7, 1.1, -1.1, 1.6, -1.6, 2.2, -2.2 })
            {
                var (ax, ay) = Ahead(want + off);
                if (!b.IsLand(ship.X + ax * 140, ship.Y + ay * 140) && !b.IsLand(ship.X + ax * 70, ship.Y + ay * 70)) { want += off; break; }
            }
            double turn = Math.Clamp(Wrap(want - ship.Heading), -0.5 * Dt, 0.5 * Dt);
            ship.Heading = Wrap(ship.Heading + turn);
            var (sx, sy) = Ahead(ship.Heading);
            double nx = ship.X + sx * Battleship.Speed * Dt, ny = ship.Y + sy * Battleship.Speed * Dt;
            if (!b.IsLand(nx, ny)) { ship.X = nx; ship.Y = ny; }
            else ship.Heading = Wrap(ship.Heading + 0.6 * Dt * 10);
        }
        if (d < Battleship.Range && ship.Cool <= 0)
        {
            ship.Cool = Battleship.Every;
            w.CarrierHp = Math.Max(0, w.CarrierHp - 8);
            w.Booms.Add(new Boom(b.CarrierX + (w.Dice.Next() - 0.5) * 240, b.CarrierY + (w.Dice.Next() - 0.5) * 50, Blast.Small));
            w.Heard.Add(Sfx.Shell);
        }
        // Its own guns, at the helicopter.
        var h = w.Heli;
        ship.FlakCool -= Dt;
        if (!h.Dead && !h.Landed && ship.FlakCool <= 0 && BayBook.Dist(ship.X, ship.Y, h.X, h.Y) < FlakRange)
        {
            ship.FlakCool = 1.4;
            double t = BayBook.Dist(ship.X, ship.Y, h.X, h.Y) / FlakSpeed;
            w.Flak.Add(new Flak { X = ship.X, Y = ship.Y, Vx = (h.X - ship.X) / t, Vy = (h.Y - ship.Y) / t, Time = t });
        }
    }

    public static void Crash(World w, string cause)
    {
        var h = w.Heli;
        if (h.Dead) return;
        h.Dead = true;
        w.Cause = cause;
        w.Booms.Add(new Boom(h.X, h.Y, Blast.Big));
        w.Heard.Add(Sfx.Crash);
    }
}
