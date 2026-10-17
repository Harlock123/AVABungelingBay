using Avalonia.Input;
using AVABungelingBay.Engine;

namespace AVABungelingBay.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avabungelingbay-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first bay, the helicopter in the air at a spot of open sea, nothing about but what a test puts there.</summary>
    private static World Bay(double x = 2400, double y = 3300)
    {
        var w = new World();
        Sim.Start(w, BayBook.Build(0), 1);
        w.Guns.Clear();
        w.Heli.Landed = false;
        w.Heli.X = x;
        w.Heli.Y = y;
        w.Heli.Heading = 0;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void Up_flies_it_forward_the_way_it_points_and_left_and_right_turn_it()
    {
        var w = Bay();
        Run(w, 120, new Controls(0, -1));
        Assert.True(w.Heli.Y < 3300 - 300, "north, the way it points");
        Assert.Equal(2400, w.Heli.X, 3);
        Assert.Equal(Heli.MaxSpeed, w.Heli.Speed, 3);
        Run(w, 30, new Controls(1, 0));
        Assert.True(w.Heli.Heading > 1.2, "turned right (clockwise)");
    }

    [Fact]
    public void Down_slows_it_and_backs_it_off_and_left_alone_it_drifts_to_a_stop()
    {
        var w = Bay();
        Run(w, 60, new Controls(0, -1));
        Run(w, 60, new Controls(0, 1));
        Assert.Equal(Heli.Reverse, w.Heli.Speed, 3);
        Run(w, 600);
        Assert.InRange(Math.Abs(w.Heli.Speed), 0, 5);
    }

    [Fact]
    public void Damage_slows_it()
    {
        var w = Bay();
        w.Heli.Damage = 65;
        Run(w, 180, new Controls(0, -1));
        Assert.True(w.Heli.Speed < Heli.MaxSpeed * 0.8);
    }

    [Fact]
    public void It_lands_on_the_carrier_and_is_patched_up_and_loaded_with_bombs()
    {
        var w = Bay();
        var b = w.Bay;
        w.Heli.X = b.CarrierX;
        w.Heli.Y = b.CarrierY;
        w.Heli.Damage = 50;
        w.Heli.Bombs = 0;
        Run(w, 60);
        Assert.True(w.Heli.Landed);
        Run(w, 60 * 5);
        Assert.Equal(0, w.Heli.Damage);
        Assert.Equal(Heli.Bombs0, w.Heli.Bombs);
        Run(w, 30, new Controls(0, -1));
        Assert.False(w.Heli.Landed);
    }

    [Fact]
    public void Too_fast_over_the_deck_it_does_not_land()
    {
        var w = Bay();
        w.Heli.X = w.Bay.CarrierX - 150;
        w.Heli.Y = w.Bay.CarrierY;
        w.Heli.Heading = Math.PI / 2;
        w.Heli.Speed = Heli.MaxSpeed;
        Run(w, 60, new Controls(0, -1));
        Assert.False(w.Heli.Landed);
    }

    // ───────────────────────────── guns and bombs ─────────────────────────────

    [Fact]
    public void The_guns_fire_a_stream_forward_and_bring_down_a_jet()
    {
        var w = Bay();
        w.Jets.Add(new Jet { X = 2400, Y = 3300 - 250, Heading = Math.PI, Life = Sim.JetLife + 1 });
        Run(w, 40, new Controls(Fire: true));
        Assert.Empty(w.Jets);
        Assert.Equal(Sim.JetPoints, w.Score);
    }

    [Fact]
    public void Bombs_fall_ahead_and_three_bring_a_factory_down()
    {
        var w = Bay();
        var f = w.Factories[0];
        int before = w.Score;
        for (int i = 0; i < 3; i++)
        {
            // Hovering over the factory, dropping one at a time.
            w.Heli.X = f.Def.X;
            w.Heli.Y = f.Def.Y;
            w.Heli.Speed = 0;
            Sim.Step(w, new Controls(Bomb: true));
            Run(w, 50);
        }
        Assert.True(f.Down);
        Assert.Equal(Heli.Bombs0 - 3, w.Heli.Bombs);
        Assert.Equal(Sim.FactoryPoints, w.Score - before);
        Assert.Contains(w.Booms, b => b.Kind == Blast.Big);
    }

    [Fact]
    public void A_bomb_carries_on_with_the_way_the_helicopter_was_going()
    {
        var w = Bay();
        w.Heli.Speed = 200;
        var (fx, fy) = Sim.BombFalls(w.Heli);
        Sim.Step(w, new Controls(Bomb: true));
        Assert.True(fy < 3300 - 50);
        Run(w, 40);
        Assert.Contains(w.Booms, b => b.Kind == Blast.Splash && Math.Abs(b.Y - fy) < 20);
    }

    [Fact]
    public void Holding_bomb_drops_one_and_none_are_left_after_nine()
    {
        var w = Bay();
        Run(w, 30, new Controls(Bomb: true));
        Assert.Equal(Heli.Bombs0 - 1, w.Heli.Bombs);
        for (int i = 0; i < 12; i++) { Sim.Step(w, new Controls(Bomb: true)); Sim.Step(w, default); }
        Assert.Equal(0, w.Heli.Bombs);
    }

    [Fact]
    public void Guns_on_the_islands_fire_flak_where_it_is_going_and_can_be_shot_out()
    {
        var w = Bay();
        var gd = new GunDef(2400, 3300 - 300);
        w.Guns.Add(new Gun { Def = gd, Cool = 0 });
        double before = w.Heli.Damage;
        Run(w, 120);
        Assert.True(w.Heli.Damage > before, "a sitting target is hit");
        Assert.Contains(w.Booms, b => b.Kind == Blast.Burst);
        Run(w, 60, new Controls(Fire: true));
        Assert.True(w.Guns[0].Down);
    }

    [Fact]
    public void Enough_damage_brings_it_down()
    {
        var w = Bay();
        w.Heli.Damage = 99;
        w.Shots.Add(new Shot { X = 2400, Y = 3300, Life = 1 });
        Run(w, 2);
        Assert.True(w.Heli.Dead);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_factory_sends_up_jets_that_chase_and_fire_and_overshoot_rather_than_ram()
    {
        var w = Bay();
        var f = w.Factories[0];
        w.Heli.X = f.Def.X + 700;
        w.Heli.Y = f.Def.Y;
        Run(w, 60 * 3 + 5);
        Assert.Single(w.Jets);
        int shots = 0;
        for (int i = 0; i < 60 * 8; i++)
        {
            Sim.Step(w, default);
            shots = Math.Max(shots, w.Shots.Count(s => !s.Mine));
        }
        Assert.True(shots > 0, "the jet fires");
        Assert.True(w.Heli.Damage > 0);
        Assert.True(w.Heli.Damage % 5 == 0, "hit only by its rounds, never rammed");
    }

    [Fact]
    public void The_shipyard_builds_the_battleship_while_the_factories_work_and_it_sails_for_the_carrier()
    {
        var w = Bay();
        w.Heli.Landed = true;
        w.Heli.X = w.Bay.CarrierX;
        w.Heli.Y = w.Bay.CarrierY;
        Run(w, 60 * 10);
        double built = w.Built;
        Assert.InRange(built, 9.9 / w.Bay.BuildSeconds, 10.1 / w.Bay.BuildSeconds);
        // Half the factories down: half the pace.
        foreach (var f in w.Factories.Where(f => !f.Def.Shipyard).Take(3)) f.Hp = 0;
        Run(w, 60 * 10);
        Assert.InRange(w.Built - built, 4.9 / w.Bay.BuildSeconds, 5.1 / w.Bay.BuildSeconds);
        w.Built = 0.99999;
        Run(w, 2);
        Assert.NotNull(w.Ship);
        double d0 = BayBook.Dist(w.Ship!.X, w.Ship.Y, w.Bay.CarrierX, w.Bay.CarrierY);
        Run(w, 60 * 20);
        Assert.True(BayBook.Dist(w.Ship.X, w.Ship.Y, w.Bay.CarrierX, w.Bay.CarrierY) < d0 - 400);
        Assert.False(w.Bay.IsLand(w.Ship.X, w.Ship.Y));
    }

    [Fact]
    public void The_shipyard_bombed_the_battleship_is_never_finished()
    {
        var w = Bay();
        w.Factories.First(f => f.Def.Shipyard).Hp = 0;
        Run(w, 60 * 10);
        Assert.Equal(0, w.Built);
    }

    [Fact]
    public void The_battleship_in_range_shells_the_carrier_and_bombs_sink_it()
    {
        var w = Bay();
        var b = w.Bay;
        w.ShipLaunched = true;
        w.Built = 1;
        w.Ship = new Battleship { X = b.CarrierX + 400, Y = b.CarrierY - 100, Heading = -Math.PI / 2, Cool = 0 };
        Run(w, 60 * 7);
        Assert.True(w.CarrierHp <= 100 - 16);
        for (int i = 0; i < 8; i++)
        {
            if (w.Ship == null) break;
            w.Heli.X = w.Ship.X;
            w.Heli.Y = w.Ship.Y;
            w.Heli.Speed = 0;
            w.Heli.Damage = 0;
            Sim.Step(w, new Controls(Bomb: true));
            Run(w, 45);
        }
        Assert.Null(w.Ship);
        Assert.True(w.ShipSunk);
        Assert.True(w.Score >= Sim.ShipPoints);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Every_factory_down_and_the_bay_is_destroyed_with_a_bonus_for_the_carrier_then_the_next()
    {
        var g = Engine();
        g.Go(0);
        foreach (var f in g.World.Factories) f.Hp = 0;
        g.World.CarrierHp = 50;
        g.Run(1);
        Assert.Equal(Phase.Cleared, g.Phase);
        Assert.Equal(GameEngine.BayBonus + 50 * GameEngine.CarrierPoints, g.Score);
        g.Run((int)(60 * GameEngine.ClearedSeconds) + 2);
        Assert.Equal(1, g.BayIndex);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
    }

    [Fact]
    public void Shot_down_the_next_helicopter_waits_on_the_deck()
    {
        var g = Engine();
        g.Go(0);
        g.World.Heli.Landed = false;
        Sim.Crash(g.World, "TEST");
        g.Run(1);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.True(g.World.Heli.Landed);
        Assert.False(g.World.Heli.Dead);
    }

    [Fact]
    public void The_carrier_sunk_is_the_end_however_many_helicopters_are_left()
    {
        var g = Engine();
        g.Go(0);
        g.World.Score = 25000;
        g.World.CarrierHp = 0;
        g.Run(1);
        Assert.Equal(Phase.Sunk, g.Phase);
        g.Run((int)(60 * GameEngine.SunkSeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "ACE") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("ACE", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 6);
        Assert.False(g.World.Heli.Landed);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go(0);
        g.Run(30, new Controls(0, -1));
        g.Pause();
        double x = g.World.Heli.X, y = g.World.Heli.Y, built = g.World.Built;
        g.Run(120, new Controls(0, -1));
        Assert.Equal((x, y, built), (g.World.Heli.X, g.World.Heli.Y, g.World.Built));
        g.Resume();
        g.Run(10, new Controls(0, -1));
        Assert.NotEqual((x, y), (g.World.Heli.X, g.World.Heli.Y));
    }

    [Fact]
    public void The_keys_fly_fire_and_bomb()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyUp(Key.Right);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Bomb);
        input.OnKeyUp(Key.X);

        // Up through to the helicopter: off the deck and forward.
        var w = Bay();
        w.Heli.Landed = true;
        input.OnKeyDown(Key.Up);
        Run(w, 60, input.Player);
        Assert.False(w.Heli.Landed);
        Assert.True(w.Heli.Y < 3300 - 50);
    }

    [Fact]
    public void The_battleship_afloat_is_announced()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(0);
        g.World.Built = 0.99999;
        g.Run(5);
        Assert.Contains(Sfx.Warning, heard);
        Assert.Equal("THE BATTLESHIP IS AFLOAT!", g.Message);
    }
}
