# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A top-down helicopter raid in the style of Will Wright's 1984 Raid on Bungeling Bay, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. Fly off the carrier, bomb the factories, sink the battleship, and land to refit.
One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code
and the sounds are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVABungelingBay.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVABungelingBay.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVABungelingBay          # play it; --no-audio for silence
BAY_SHOTS=$PWD/Images dotnet test tests/AVABungelingBay.Tests --filter ScreenshotTests   # refresh the README's pictures
BAY_PROBE=1 dotnet test tests/AVABungelingBay.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                                  # how the computer pilot does in each bay (BAY_PROBE=lazy: no pilot)
python3 tools/make_sounds.py                      # rebuild src/AVABungelingBay/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVABungelingBay/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, initials, high scores), the phases of a bay and
    the fixed-step loop.
  - `Bay.cs` has `Controls`, the measures (`Field`), and `BayBook`, which builds the five bays from seeds.
  - `World.cs` holds the world's state (helicopter, guns, jets, battleship, carrier) and the `Dice` generator.
  - `Sim.cs` steps the world: flying, firing, bombing, landing to refit, flak, jets and the battleship.
  - `Pilot.cs` is the computer pilot, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVABungelingBay/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture scaled to
  the window and runs the 60 Hz timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVABungelingBay/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVABungelingBay.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `BayTests` checks every bay can be
  cleared by the computer pilot.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests can play whole bays deterministically from a seed. Draw random numbers from the
  world's seeded `Dice` (`w.Dice`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Keys and gamepad must produce the same `Dy` sign the engine expects
  (negative is up, which drives the helicopter forward). Test new controls through `InputHandler`, not by setting
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVABungelingBay`); tests
  load the table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change a bay or the pilot, run the probe above to see the pilot can still clear each bay.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
