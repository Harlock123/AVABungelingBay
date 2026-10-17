using AVABungelingBay.Engine;

namespace AVABungelingBay.Tests;

/// <summary>How the computer pilot gets on in each bay (set BAY_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("BAY_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < BayBook.Count; i++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "bay-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            int t = 0, lost = 0, landings = 0;
            var last = g.Phase;
            bool landed = false;
            var hurt = new Dictionary<int, int>();
            var doing = new Dictionary<string, int>();
            double dmg = 0;
            for (; t < 60 * 600 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? default : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
                if (g.World.Heli.Landed && !landed) landings++;
                landed = g.World.Heli.Landed;
                double d = g.World.Heli.Damage;
                if (d > dmg + 0.5) { int k = (int)Math.Round(d - dmg); hurt[k] = hurt.GetValueOrDefault(k) + 1; }
                dmg = d;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                last = g.Phase;
            }
            var w = g.World;
            output.WriteLine($"{i + 1} {w.Bay.Name}: {(g.Cleared > 0 ? "CLEARED" : g.State == GameState.Playing ? "not cleared" : "GAME OVER")} after {t / 60}s, {lost} helicopters lost, {landings} landings, factories left {w.FactoriesLeft}, ship built {w.Built:0.00} sunk {w.ShipSunk}, carrier {w.CarrierHp}, score {w.Score}, hurt {string.Join(" ", hurt.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"))}; {string.Join(", ", doing.Select(k => $"{k.Key} {k.Value / 60}s"))}; shipyard hp {w.Factories.First(f => f.Def.Shipyard).Hp}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
