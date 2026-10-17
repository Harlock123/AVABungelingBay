namespace AVABungelingBay.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The bay named; a moment on the deck before the clock starts.</summary>
    Start,
    Play,
    /// <summary>Shot down: a moment, then the next helicopter on the deck, or the end.</summary>
    Dying,
    /// <summary>Every factory down and the battleship sunk (or never built): the carrier's bonus, then the next bay.</summary>
    Cleared,
    /// <summary>The carrier sunk: the end, however many helicopters are left.</summary>
    Sunk,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Gun, BombDrop, Explode, Splash, FactoryDown, GunDown, JetDown, Clang, Hurt, FlakFire, Burst, JetUp, JetFire,
    ShipLaunched, ShipSunk, Shell, Crash, Land, TakeOff, Load, Warning, ExtraLife,
    Music, MusicStop, TitleTune, GameOver, Victory, Cleared,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 70;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, ClearedSeconds = 4.5, SunkSeconds = 4;
    public const int Lives0 = 3, ExtraEvery = 20000, BayBonus = 2000, CarrierPoints = 30;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _shipWarned, _carrierWarned;
    private int _nextExtra, _seed;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    /// <summary>Helicopters left, the one flying among them.</summary>
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int BayIndex { get; private set; }
    /// <summary>Bays cleared this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    private void Enter(int index)
    {
        BayIndex = index;
        int score = World.Score;
        Sim.Start(World, BayBook.Build(index), _seed * 31 + index);
        World.Score = score;
        _shipWarned = _carrierWarned = false;
        SetPhase(Phase.Start);
        Say(World.Bay.Name, StartSeconds);
        Pilot.Reset();
    }

    private void Say(string text, double seconds)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void StartGame()
    {
        State = GameState.Playing;
        NewGame();
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        _seed++;
        NewGame();
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                int before = w.Score;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                if (w.ShipLaunched && !_shipWarned) { _shipWarned = true; Say("THE BATTLESHIP IS AFLOAT!", 3); Play(Sfx.Warning); }
                if (w.CarrierHp < 40 && !_carrierWarned) { _carrierWarned = true; Say("THE CARRIER IS SINKING!", 3); Play(Sfx.Warning); }
                if (w.CarrierHp <= 0) { SetPhase(Phase.Sunk); Music(false); Say("THE CARRIER IS SUNK", SunkSeconds); }
                else if (w.Heli.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.Cleared)
                {
                    SetPhase(Phase.Cleared);
                    Music(false);
                    Play(Sfx.Cleared);
                    int bonus = BayBonus + (int)w.CarrierHp * CarrierPoints;
                    w.Score += bonus;
                    Say($"{w.Bay.Name} DESTROYED!  BONUS {bonus}", ClearedSeconds);
                    ExtraLives();
                }
                break;
            }
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.OnDeck(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Cleared:
                if (PhaseTime > ClearedSeconds)
                {
                    Cleared++;
                    Lives++;
                    if ((BayIndex + 1) % BayBook.Count == 0) Play(Sfx.Victory);
                    Enter(BayIndex + 1);
                }
                break;
            case Phase.Sunk:
                Sim.Step(w, default);
                if (PhaseTime > SunkSeconds) { End(); return; }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a bay, past its name.</summary>
    public void Go(int bay = 0)
    {
        State = GameState.Playing;
        NewGame();
        if (bay != 0) Enter(bay);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
