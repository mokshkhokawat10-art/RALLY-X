# RALLY X

Arcade rally racer for Windows PC, built with Unity 6 (URP) and the Unity Input System.

## Requirements

- Unity **6000.3.25f1** (Unity 6.3 LTS), installed through Unity Hub
- Git (optionally Git LFS for large art/audio assets)

## Opening the project

1. Unity Hub > **Add** > **Add project from disk** > select this folder.
2. Open it with Unity 6000.3.25f1.
3. Run **RALLY X > Setup > Run Project Setup** once if the console reports setup errors
   (it is idempotent and safe to re-run).

## Editor menus

| Menu | What it does |
|------|--------------|
| RALLY X > Setup > Run Project Setup | Project settings, physics layers, URP check, Sandbox scene |
| RALLY X > Build > Create Missing Vehicle Content | Surfaces, car config, placeholder car prefab (never overwrites) |
| RALLY X > Build > Rebuild Placeholder Car Prefab | Regenerates the car prefab (overwrites manual edits) |
| RALLY X > Build > Create Missing Camera Content | Default chase camera settings (never overwrites) |
| RALLY X > Build > Create Missing Race Content | Default race settings and the UI panel settings (never overwrites) |
| RALLY X > Build > Rebuild Sandbox Test Course | Regenerates the Sandbox test course and player car |
| RALLY X > Build > Rebuild Pine Valley Track | Regenerates the Pine Valley scene from its track definition (overwrites the scene) |
| RALLY X > Dev > Capture Pine Valley Screenshots | Renders an overview, views around the lap and both bridges to `Captures/` |

## Headless commands

Unity must not have the project open. `U` = `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe"`.

```
REM Setup + build all generated content
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuild -logFile -

REM Setup + regenerate the track scenes
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuildTracks -logFile -

REM Track review screenshots
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.TrackCaptureTool.CaptureBatch -captureDir Captures -logFile -

REM Edit Mode tests (track layout geometry, race rules)
%U% -batchmode -projectPath "C:\Users\Admin\RALLY X" -runTests -testPlatform EditMode -testResults editmode.xml -logFile -

REM Play Mode tests (vehicle physics, chase camera, Pine Valley drivability, races)
%U% -batchmode -projectPath "C:\Users\Admin\RALLY X" -runTests -testPlatform PlayMode -testResults results.xml -logFile -
```

## Controls

| Action        | Keyboard          | Gamepad        |
|---------------|-------------------|----------------|
| Throttle      | W / Up Arrow      | Right Trigger  |
| Brake/Reverse | S / Down Arrow    | Left Trigger   |
| Steer         | A D / Left Right  | Left Stick     |
| Handbrake     | Space             | South (A / ✕)  |
| Reset car     | R                 | North (Y / △)  |
| Restart race  | Backspace         | View / Select  |
| Pause         | Esc (not used yet) | Start         |

Dev panels: **F1** foundation overlay, **F2** vehicle telemetry (hidden by default in track scenes).
All bindings are defined in `Assets/Scripts/Core/RallyInputActions.cs`.

## Tuning

Handling lives in `Assets/Settings/Vehicles/RallyCar_Config.asset`, surface behaviour in
`Assets/Settings/Surfaces/` and the chase camera in `Assets/Settings/Cameras/ChaseCamera_Default.asset`.
All are read live, so changes made during Play Mode apply immediately
(and persist after stopping, like any asset edit).

Race rules (laps, countdown, wrong-way warning, results delay) are in
`Assets/Settings/Racing/RaceSettings_Default.asset`; the HUD's layout and styles in `Assets/UI/Race/`
(`RaceHud.uxml`, `RaceHud.uss`).

## Tracks

Tracks are generated rather than built by hand. `Assets/Settings/Tracks/PineValley_Track.asset` holds Pine Valley's
layout (control points with position, height, width and surface), jumps, bridges, the shortcut, terrain shape
and scenery density. Edit it, then run **RALLY X > Build > Rebuild Pine Valley Track**. The scene
`Assets/Scenes/Tracks/PineValley.unity` and the terrain and road meshes in `Assets/Scenes/Tracks/PineValley/`
are recreated on every rebuild, so edits made to them by hand are lost. In the scene, `TrackPath` holds the
baked centreline, `SpawnGrid` the four starting slots and `RaceCircuit` the checkpoint gates. Gate spacing,
width and height are in the definition's **Race** section; the builder keeps gates off main road that a
shortcut bypasses, so both routes pass every gate.

## Racing

A track scene is raceable when it has a `RaceCircuit` (its `TrackPath`, `TrackSpawnGrid` and `Checkpoint`
gates in driving order, the first being the start/finish line), a `RaceManager` and cars with a
`RaceParticipant`. The track builder adds all of these, plus the `RaceHud`.

- Checkpoints are planes, not colliders: a car passes one by crossing it forwards within its width and
  height. They never touch physics, wheel raycasts or the camera. A lap only counts if every gate was passed in order.
- Positions come from completed laps, then checkpoints passed this lap, then the distance along the track
  to the next checkpoint; never from straight-line distances.
- Each racer's progress (`RaceTracker`: laps, lap times, next gate, distance along the track, wrong way) is
  plain C#, shared by the player and AI racers.
- `VehicleController.InputLocked` holds cars on the grid during the countdown and brings finished cars to a stop.
- Disabling the `RaceManager` releases the cars for free driving.

## Layout

```
Assets/
  Scripts/        RallyX runtime assembly (Core, Player, Vehicle, AI, Racing, Tracks,
                  UI, Garage, Audio, VFX, SaveSystem) + Editor/ (RallyX.Editor assembly)
  Tests/EditMode/ RallyX.Tests.EditMode assembly (can reach editor code)
  Tests/PlayMode/ RallyX.Tests.PlayMode assembly
  Prefabs/        Vehicles, Environment, UI, Track
  Scenes/         MainMenu, Garage, Tracks (Pine Valley), Dev (Sandbox test scene)
  Settings/       URP, vehicle, camera and track configs, surfaces, physics materials
  UI/             UI Toolkit theme, panel settings and layouts (Race/ = race HUD and results)
  Materials/ Models/ Textures/ Audio/ VFX/   (Environment/ and Generated/ subfolders are builder output)
```

Namespaces follow the top-level script folder: `RallyX.Core`, `RallyX.Vehicle`, `RallyX.Cameras`, ...
(plural `Cameras` because a `RallyX.Camera` namespace would hide `UnityEngine.Camera`).
Editor code uses `RallyX.EditorTools` (not `RallyX.Editor`, which would shadow `UnityEditor.Editor`).
