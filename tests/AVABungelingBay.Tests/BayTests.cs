using AVABungelingBay.Engine;

namespace AVABungelingBay.Tests;

/// <summary>The five bays: built to be fought over, and cleared by the computer pilot.</summary>
public class BayTests(ITestOutputHelper output)
{
    public static TheoryData<int> Bays() => [.. Enumerable.Range(0, BayBook.Count)];

    [Theory]
    [MemberData(nameof(Bays))]
    public void Six_factories_on_islands_one_a_shipyard_with_its_slip_in_open_water(int index)
    {
        var b = BayBook.Build(index);
        Assert.Equal(6, b.Factories.Count);
        Assert.Single(b.Factories, f => f.Shipyard);
        Assert.All(b.Factories, f => Assert.True(b.IsLand(f.X, f.Y)));
        Assert.All(b.Guns, g => Assert.True(b.IsLand(g.X, g.Y)));
        Assert.False(b.IsLand(b.SlipX, b.SlipY));
        Assert.False(b.IsLand(b.CarrierX, b.CarrierY));
        Assert.False(b.IsLand(b.CarrierX - 200, b.CarrierY));
        Assert.False(b.IsLand(b.CarrierX + 200, b.CarrierY));
    }

    [Fact]
    public void Each_bay_is_better_defended_than_the_last()
    {
        for (int i = 1; i < BayBook.Count; i++)
        {
            var a = BayBook.Build(i - 1);
            var b = BayBook.Build(i);
            Assert.True(b.Guns.Count >= a.Guns.Count - 1);
            Assert.True(b.BuildSeconds <= a.BuildSeconds);
            Assert.True(b.FlakEvery <= a.FlakEvery);
        }
    }

    [Theory]
    [MemberData(nameof(Bays))]
    public void Every_bay_is_destroyed_by_the_computer_pilot(int index)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabungelingbay-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(index);
        g.SetLives(10);
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 600 && g.State == GameState.Playing && g.Phase != Phase.Cleared; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{g.World.Bay.Name}: {g.Phase} after {t / 60}s, {lost} helicopters lost");
        Assert.Equal(Phase.Cleared, g.Phase);
        Assert.True(lost <= 3, $"{lost} helicopters lost");
        Assert.True(g.World.CarrierHp > 0);
    }

    [Fact]
    public void Doing_nothing_the_battleship_sinks_the_carrier()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabungelingbay-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(0);
        for (int t = 0; t < 60 * 900 && g.State == GameState.Playing; t++) g.Tick(default);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(0, g.World.CarrierHp);
    }
}
