namespace AVABungelingBay.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The helicopter: where it is, which way it points (0 is north, turning clockwise), how fast, how battered.</summary>
public sealed class Heli
{
    public const double MaxSpeed = 270, Reverse = -70, Thrust = 320, Brake = 420, Turn = 2.9, Drag = 0.35;
    public const int Bombs0 = 9;
    public double X, Y, Heading, Speed;
    /// <summary>Damage taken, out of 100.</summary>
    public double Damage;
    public int Bombs = Bombs0;
    public double FireTimer, Rotor;
    public bool BombPrev, Dead, Landed;
    /// <summary>How long it has sat still over the deck (landing takes a moment).</summary>
    public double Settle;
    public Heli Copy() => (Heli)MemberwiseClone();
}

public sealed class Factory
{
    public FactoryDef Def;
    public double Hp = Factory.Hp0;
    public const double Hp0 = 12;
    public bool Down => Hp <= 0;
    /// <summary>When it last sent up a jet.</summary>
    public double Launch;
    public Factory Copy() => (Factory)MemberwiseClone();
}

public sealed class Gun
{
    public GunDef Def;
    public double Hp = 3, Cool, Aim;
    public bool Down => Hp <= 0;
    public Gun Copy() => (Gun)MemberwiseClone();
}

public sealed class Jet
{
    public const double Hp0 = 2;
    public double X, Y, Heading, Hp = Hp0, Cool, Life, Pass;
    public Jet Copy() => (Jet)MemberwiseClone();
}

/// <summary>A gun round: the helicopter's (Mine), or a jet's.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine;
    public Shot Copy() => (Shot)MemberwiseClone();
}

/// <summary>A flak shell: it flies to where the gun aimed and bursts there.</summary>
public sealed class Flak
{
    public double X, Y, Vx, Vy, Time;
    public Flak Copy() => (Flak)MemberwiseClone();
}

/// <summary>A bomb falling: it carries on with the helicopter's way across, and lands after a moment.</summary>
public sealed class Bomb
{
    public const double Fall = 0.6, Blast = 70;
    public double X, Y, Vx, Vy, Time;
    public Bomb Copy() => (Bomb)MemberwiseClone();
}

/// <summary>The battleship: built at the shipyard while the factories work, then out to sink the carrier.</summary>
public sealed class Battleship
{
    public const double Hp0 = 40, Speed = 34, Range = 520, Every = 3.2;
    public double X, Y, Heading, Hp = Hp0, Cool, FlakCool = 2;
    public Battleship Copy() => (Battleship)MemberwiseClone();
}

public enum Blast { Small, Big, Burst, Splash }

public sealed record Boom(double X, double Y, Blast Kind)
{
    public double T;
}

/// <summary>Everything in a bay that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public BayDef Bay = null!;
    public Heli Heli = new();
    public List<Factory> Factories = [];
    public List<Gun> Guns = [];
    public List<Jet> Jets = [];
    public List<Shot> Shots = [];
    public List<Flak> Flak = [];
    public List<Bomb> Bombs = [];
    public Battleship? Ship;
    /// <summary>How far on the battleship is, 0 to 1 (it sails at 1).</summary>
    public double Built;
    public bool ShipSunk, ShipLaunched;
    public double CarrierHp = 100;
    public List<Boom> Booms = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public double Clock, NextJet;
    public int Score;
    public string Cause = "";

    public int FactoriesLeft => Factories.Count(f => !f.Down);
    public bool Cleared => FactoriesLeft == 0 && Ship == null;

    public World Copy() => new()
    {
        Bay = Bay, Heli = Heli.Copy(), Factories = Factories.Select(f => f.Copy()).ToList(), Guns = Guns.Select(g => g.Copy()).ToList(),
        Jets = Jets.Select(j => j.Copy()).ToList(), Shots = Shots.Select(s => s.Copy()).ToList(), Flak = Flak.Select(f => f.Copy()).ToList(),
        Bombs = Bombs.Select(b => b.Copy()).ToList(), Ship = Ship?.Copy(), Built = Built, ShipSunk = ShipSunk, ShipLaunched = ShipLaunched,
        CarrierHp = CarrierHp, Dice = Dice, Clock = Clock, NextJet = NextJet, Score = Score, Cause = Cause,
    };
}
