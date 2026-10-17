using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABungelingBay.Engine;
using AVABungelingBay.Views;
using Controls = AVABungelingBay.Engine.Controls;

namespace AVABungelingBay.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BAY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabungelingbay-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BAY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABungelingBay-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 48210, 3);
        g.HighScores.Insert("AVA", 21500, 1);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A bay played by the computer pilot until something worth showing.</summary>
    private static void Play(int bay, string name, Func<GameEngine, bool> moment, int seconds = 300, Action<GameEngine>? setup = null)
    {
        var (w, c, g) = Open();
        g.Go(bay);
        g.SetLives(20);
        g.Pilot.Reset();
        setup?.Invoke(g);
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static double ToNearest(GameEngine g, Func<World, IEnumerable<(double X, double Y)>> things)
    {
        var h = g.World.Heli;
        return things(g.World).Select(p => BayBook.Dist(p.X, p.Y, h.X, h.Y)).DefaultIfEmpty(1e9).Min();
    }

    [AvaloniaFact]
    public void Off_the_carrier() => Play(0, "carrier", g => !g.World.Heli.Landed && g.World.Clock > 1.6);

    [AvaloniaFact]
    public void Bombing_a_factory() => Play(0, "bombing", g => g.World.Bombs.Count >= 1 && g.World.Bombs[0].Time < 0.35 && g.World.Factories.Any(f => f.Hp < Factory.Hp0 && !f.Down));

    [AvaloniaFact]
    public void Flak() => Play(1, "flak", g => g.World.Flak.Count >= 2 && g.World.Booms.Any(b => b.Kind == Blast.Burst && b.T < 0.2) && ToNearest(g, w => w.Guns.Where(x => !x.Down).Select(x => (x.Def.X, x.Def.Y))) < 300);

    [AvaloniaFact]
    public void A_dogfight() => Play(2, "jets", g => g.World.Jets.Count >= 2 && ToNearest(g, w => w.Jets.Select(j => (j.X, j.Y))) < 260 && g.World.Shots.Count(s => s.Mine) >= 2);

    [AvaloniaFact]
    public void The_shipyard_at_work() => Play(1, "shipyard", g => g.World.Factories.Any(f => f.Def.Shipyard && f.Hp < Factory.Hp0) || ToNearest(g, w => w.Factories.Where(f => f.Def.Shipyard).Select(f => (f.Def.X, f.Def.Y))) < 330,
        setup: g => g.World.Built = 0.7);

    [AvaloniaFact]
    public void The_battleship_afloat() => Play(0, "battleship", g => g.World.Ship != null && ToNearest(g, w => [(w.Ship!.X, w.Ship.Y)]) < 260 && g.World.Clock > 2.5,
        setup: g =>
        {
            g.World.Built = 0.995;
            var b = g.World.Bay;
            g.World.Heli.Landed = false;
            g.World.Heli.X = b.SlipX - 250;
            g.World.Heli.Y = b.SlipY + 200;
        });

    [AvaloniaFact]
    public void Bay_destroyed() => Play(0, "cleared", g => g.Phase == Phase.Cleared && g.PhaseTime > 0.5, 400);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 3420;
        Sim.Crash(g.World, "TEST");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
