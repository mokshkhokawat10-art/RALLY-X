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
| RALLY X > Build > Rebuild Placeholder Car Prefab | Regenerates the RallyCar (Falcon RX) prefab (overwrites manual edits; its config is kept) |
| RALLY X > Build > Rebuild All Car Prefabs | Regenerates every garage car's prefab from its spec (handling configs, definitions and roster are kept) |
| RALLY X > Build > Create Missing Effects Content | The tyre/impact/exhaust effect textures and materials, and the effects config (the config is never overwritten) |
| RALLY X > Build > Create Missing Camera Content | Default chase camera settings (never overwrites) |
| RALLY X > Build > Create Missing Race Content | Default race settings and the UI panel settings (never overwrites) |
| RALLY X > Build > Create Missing AI Content | AI settings (incl. difficulty levels) and the three AI driver profiles (never overwrites) |
| RALLY X > Build > Create Missing Garage Content | The garage cars' configs, placeholder prefabs and definitions, and the vehicle roster (never overwrites) |
| RALLY X > Build > Create Missing Progression Content | Credits per position, starting credits, the upgrade catalogue and the track list (never overwrites) |
| RALLY X > Build > Rebuild Menu Scenes | Regenerates the MainMenu and Garage scenes and puts them first in the build list |
| RALLY X > Build > Rebuild Sandbox Test Course | Regenerates the Sandbox test course and player car |
| RALLY X > Build > Rebuild Pine Valley Track | Regenerates the Pine Valley scene from its track definition (overwrites the scene) |
| RALLY X > Build > Rebuild Red Canyon Track | Regenerates the Red Canyon scene from its track definition (overwrites the scene) |
| RALLY X > Build > Rebuild All Tracks | Regenerates every track scene |
| RALLY X > Dev > Capture Track Screenshots | Renders an overview, views around the lap and each bridge of every track to `Captures/<track>/` |
| RALLY X > Dev > Capture Car Screenshots | Renders every garage car from the front, side, rear, above and at a wheel to `Captures/Cars/` |
| RALLY X > Dev > Unlock All Cars | Unlocks every garage car in the player profile |
| RALLY X > Dev > Add 10,000 Credits | Adds credits to the player profile, for trying upgrades |
| RALLY X > Dev > Reset Player Profile | Deletes the player profile (credits, upgrades, chosen car, wins, unlocks, best laps) |
| RALLY X > Dev > Show Player Profile File | Opens the folder holding `profile.json` |

## Headless commands

Unity must not have the project open. `U` = `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe"`.

```
REM Setup + build all generated content (including the scene of any track that has none yet)
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuild -logFile -

REM Setup + garage content + regenerate the main menu and garage scenes
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuildMenus -logFile -

REM Setup + regenerate every car prefab (handling configs, definitions and the roster are kept)
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndRebuildCars -logFile -

REM Car review screenshots (every garage car) into <captureDir>/Cars
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.VehicleCaptureTool.CaptureBatch -captureDir Captures -logFile -

REM Setup + regenerate every track scene
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuildTracks -logFile -

REM Setup + regenerate one track scene (by scene name)
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.BatchTasks.SetupAndBuildTrack -track RedCanyon -logFile -

REM Track review screenshots (every track, or one with -track <scene name>)
%U% -batchmode -quit -projectPath "C:\Users\Admin\RALLY X" -executeMethod RallyX.EditorTools.TrackCaptureTool.CaptureBatch -captureDir Captures -logFile -

REM Edit Mode tests (every track's layout and terrain, track themes and look, race rules and laps, AI racing lines and speed plans, difficulty tuning, garage cars, their stats, look and collision boxes, effects config, player profile and save file, upgrades, credits, track unlocks)
%U% -batchmode -projectPath "C:\Users\Admin\RALLY X" -runTests -testPlatform EditMode -testResults editmode.xml -logFile -

REM Play Mode tests (vehicle physics and Phase 10 handling, tyre/landing/impact effects, lights and body lean, chase camera, drivability, races and AI races on every difficulty on every track, pause menu, menus and track selection, garage, every car, upgrades driven for real, race rewards)
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
| Pause         | Esc: pause menu (Resume, Restart Race, Main Menu); on the results screen: back to the main menu | Start |

Menus and garage: arrows or WASD / D-pad or left stick to move, Enter or Space / South (A / X) to confirm,
Esc / East (B / O) to go back, Q E / right stick (or drag with the mouse) to turn the car in the garage.
On the main menu Left / Right (or the arrows by the track name) choose the track. In the pause menu Esc / Start or
East (B / O) resumes. The mouse works everywhere.

Dev panels: **F1** foundation overlay, **F2** vehicle telemetry (hidden by default in track scenes).
All bindings are defined in `Assets/Scripts/Core/RallyInputActions.cs`.

## Tuning

Everything below is data: edit the assets, not the code.

- **Handling** (`VehicleConfig`, one per car in `Assets/Settings/Vehicles/`). Besides engine, brakes, steering, grip, drift,
  suspension and air control: *drive traction* (wheelspin limit), *brake bias* (front share of braking; the stopping
  distance doesn't change), *weight transfer* (extra load moved between the axles under braking and power, and how fast),
  *drift sustain grip* (full throttle holds a handbrake drift; lifting off lets the rear grip), *rebound damping* and
  *bump stops* (settle after bumps and hard landings without bottoming out), *air yaw rate*, *landing alignment* (level
  towards the slope below) and *air pitch control* (the brake dips the nose in the air), and *settling* after landings and
  hard knocks. The acceleration curve is a soft launch, a mid-range plateau and a fade to top speed; each car has its own.
  Each car differs: the Falcon (all-wheel drive, the AI's car) is balanced; the Kestrel is front-biased, stiff and quick
  to turn; the Titan is rear-biased and heavy with a lot of weight transfer, so its tail steps out under braking and it
  holds throttle drifts. The garage stats are still worked out from the config and match the cars when driven.
- **Surfaces** (`Assets/Settings/Surfaces/`): grip, top speed, pull and drag, plus *slide falloff* (loose surfaces keep
  more grip in a slide, so slides are progressive), what the tyres throw up (*spray*: tyre smoke, dust, gravel, mud or
  grass), its colour, the tyre-mark colour, dust trailed at speed (*rolling spray*) and *roughness* (body chatter and
  camera rumble). A track can recolour its surfaces' dust and marks with its `SurfacePalette` (set from its theme).
- **Effects** (`Assets/Settings/VFX/VehicleEffects_Default.asset`): when a tyre counts as sliding, rates, sizes and
  lifetimes of smoke, dust, stones, mud, sparks and exhaust, landing dust, skid mark width and capacity, backfires and
  the particle budget per car.
- **Body lean and lights** (`VehicleVisuals` on each car prefab): pitch and roll per g, lean spring, chatter, light colours.
- **Chase camera** (`ChaseCamera_Default.asset`): besides framing, smoothing, FOV and shake, the extra look-ahead at speed,
  a small pull-out while drifting, the spring dip on landings and push away from hits, and a faint rumble at speed that
  grows on rough ground (set *speed rumble* to 0 to turn it off). Shake still fades out completely.

Each car's handling lives in its config in `Assets/Settings/Vehicles/` (`RallyCar_Config.asset` for the Falcon RX), surface behaviour in
`Assets/Settings/Surfaces/` and the chase camera in `Assets/Settings/Cameras/ChaseCamera_Default.asset`.
All are read live, so changes made during Play Mode apply immediately
(and persist after stopping, like any asset edit). The exception is a player's car with upgrades installed: it drives
a copy of its config made when the race loads, so config edits reach it from the next race load.

Race rules (default laps, countdown, wrong-way warning, results delay) are in
`Assets/Settings/Racing/RaceSettings_Default.asset`; a track can set its own laps (its `TrackInfo`, see Career).
The HUD's and pause menu's layout and styles are in `Assets/UI/Race/` (`RaceHud.uxml`, `RaceHud.uss`).

## Tracks

Tracks are generated rather than built by hand. Each has a definition in `Assets/Settings/Tracks/`
(`PineValley_Track.asset`, `RedCanyon_Track.asset`): its layout (control points with position, height, width and
surface), jumps, bridges, shortcuts, terrain shape (including optional terraces: flat benches and steep walls)
and scenery density. Edit it, then run **RALLY X > Build > Rebuild Pine Valley Track** (or Red Canyon, or All Tracks). The
scene `Assets/Scenes/Tracks/<Track>.unity` and the terrain and road meshes in `Assets/Scenes/Tracks/<Track>/` are
recreated on every rebuild, so edits made to them by hand are lost. In the scene, `TrackPath` holds the
baked centreline, `SpawnGrid` the four starting slots and `RaceCircuit` the checkpoint gates. Gate spacing,
width and height are in the definition's **Race** section; the builder keeps gates off main road that a
shortcut bypasses, so both routes pass every gate.

- **Pine Valley** (1.6 km): pine forest, asphalt start, dirt, gravel and mud, two jumps, two bridges over creeks, a mud
  shortcut. Open from the start.
- **Red Canyon** (1.9 km): dry red-rock canyon, clockwise. Asphalt start straight, fast gravel canyon floor over a dry
  wash, two switchbacks up onto the mesa, a crest jump on the mesa top, and a gravel descent with a second jump and a
  narrow gully shortcut past a butte. Off the road is loose sand.
- **Themes.** A definition's `TrackTheme` (`Assets/Settings/Tracks/Themes/`) sets the look: sun, fog, sky and ambient
  light, the colours of the ground, roads, rock and trees, tree shapes, whether creeks hold water, and the off-road
  surface. The forest theme (Pine Valley) uses the shared art folders; other themes generate their art once into
  their own subfolder (e.g. `Assets/Materials/Environment/RedCanyon/`). Delete a theme's art folders to regenerate
  its art after changing its colours or shapes.
- **Look (Phase 10).** A theme also sets the colour grade (a global volume in the scene: ACES tonemapping, exposure,
  contrast, saturation, white balance, shadow and highlight tints, bloom, vignette), shadow strength, and the trackside
  grass tufts and bushes (terrain details: colours and density). Road textures show wheel ruts, stones and frayed edges;
  the roads' sloped shoulders are drawn with the verge material, which matches the worn ground painted beside the roads;
  ground layers have normal and mask maps and blend by height. Guard rails collide as the same plain boxes as before but
  are drawn as galvanised rails on posts; bridges gain timber posts and beams; a warning board stands before each jump.
  Each build bakes the track's reflections into `Reflections.exr` (used by car paint and glass). Generated textures carry
  a revision (`EnvironmentAssetBuilder.ArtRevision`); bump it after changing a painter and they are repainted in place.
  None of this touches the racing: roads, colliders, surfaces, terrain heights, trees, checkpoints and the grid are
  rebuilt exactly as before.
- **Adding a track:** write its authoring class (like `PineValleyTrack` / `RedCanyonTrack` in
  `Assets/Scripts/Editor/Tracks/`), list it in `TrackCatalog`, give it a `TrackInfo` in the career's track roster
  (`ProgressionContentBuilder`), then build it. Every track-based test runs on each track listed in the test fixtures.

## Racing

A track scene is raceable when it has a `RaceCircuit` (its `TrackPath`, `TrackSpawnGrid` and `Checkpoint`
gates in driving order, the first being the start/finish line), a `RaceManager` and cars with a
`RaceParticipant`. The track builder adds all of these, plus the `RaceHud`; the player's
car is spawned when the scene loads by `PlayerRaceEntry` (the car chosen in the garage), and the AI cars by `AIOpponents`.

- Checkpoints are planes, not colliders: a car passes one by crossing it forwards within its width and
  height. They never touch physics, wheel raycasts or the camera. A lap only counts if every gate was passed in order.
- Positions come from completed laps, then checkpoints passed this lap, then the distance along the track
  to the next checkpoint; never from straight-line distances.
- Each racer's progress (`RaceTracker`: laps, lap times, next gate, distance along the track, wrong way) is
  plain C#, shared by the player and AI racers.
- `VehicleController.InputLocked` holds cars on the grid during the countdown and brings finished cars to a stop.
- The race runs over the track's laps (`RaceManager.SetLaps`, set from the track's `TrackInfo` when the scene loads).
- **Pause** (Esc / Start) stops time and opens the pause menu: RESUME, RESTART RACE or MAIN MENU. Time comes back at
  the speed it ran before (tests run faster than real time), including when leaving for the main menu.
- Disabling the `RaceManager` releases the cars for free driving.

## AI opponents

Track scenes get an `AIOpponents` object that spawns one car per driver profile on the grid behind the player
(Lind, Petrov, Moreno: fastest first, each with its own paint, pace, lane and shortcut habit). AI cars are raced
through the same `VehicleController` inputs, `RaceParticipant` and `RaceTracker` as the player, so they use
the same physics, checkpoints, laps and positions.

- **Difficulty** (Easy / Normal / Hard) is set on `AIOpponents` in the Inspector. Changing it during Play applies
  straight away; restart the race (Backspace) for a fair start. Normal is each driver's own profile; Easy and Hard scale
  every driver's cornering, braking and top speed by the factors in `AISettings_Default.asset` (**Difficulty**).
- There is no rubber-banding: an AI driver's pace depends only on its profile and the difficulty, never on
  where the player is.
- Each driver follows a racing line worked out from the track (wide-apex-wide within the road edges), plans its
  speed for corners, surfaces and jumps, passes slower cars, countersteers and eases off the throttle in slides,
  takes the shortcut on some laps, and recovers when stuck (reverses out, then is put back on the track),
  rolled over, lost off the road or going the wrong way.
- Tuning: shared behaviour and difficulty factors in `Assets/Settings/AI/AISettings_Default.asset`, each driver in
  `Assets/Settings/AI/Drivers/`. Speeds are planned at every race start, so edits apply from the next restart.
  The generated scene keeps its `AIOpponents` difficulty only until the track is rebuilt.

## Garage and cars

The game starts on `Assets/Scenes/MainMenu/MainMenu.unity` (RACE, GARAGE, QUIT, with the player's car on a turntable,
the credits and the track to race, chosen with Left / Right). The garage (`Assets/Scenes/Garage/Garage.unity`) shows
each car on the turntable with its stats, SELECT saves the choice and UPGRADES opens the car's upgrades (see Career
below). RACE loads the chosen track, where `PlayerRaceEntry` spawns the chosen car, with its upgrades, on pole and
points the chase camera at it; the results screen shows the credits earned and has a MAIN MENU button. Both menu scenes are generated (RALLY X > Build > Rebuild Menu Scenes), so
change the builder, the layouts (`Assets/UI/Menus/`) or the assets rather than the scenes.

- Cars are data: `Assets/Settings/Vehicles/VehicleRoster.asset` lists the `VehicleDefinition`s (in
  `Assets/Settings/Vehicles/Definitions/`) in garage order, the first being the default car. A definition has a stable
  id, name, class, description, the car prefab and the race wins needed to unlock it. Handling lives in the prefab's
  `VehicleConfig` (`RallyCar_Config`, `Kestrel_Config`, `Titan_Config`).
- A real model only needs a new prefab with a `VehicleController`, its config and four `VehicleWheel`s; point the
  definition at it. The garage turntable shows the same prefab with its physics switched off.
- Stats are worked out from the config and wheel layout (`VehicleStats`): top speed, 0-100 km/h, steering response at
  100 km/h, cornering grip and 100-0 km/h braking distance. Play Mode tests check them against the real cars. The
  roster's **Stat Bars** section sets what an empty and a full bar mean (wide enough for every car fully upgraded; an
  Edit Mode test checks it).
- **How the cars are built.** `PlaceholderCarBuilder` generates each car from its spec (`VehicleContentBuilder` for the
  Falcon, `GarageContentBuilder` for the others): a lofted body with wheel arches and flares, a glass cabin with pillars and
  a roof, bumpers, lights, mirrors, mud flaps, door numbers, exhausts and the car's own details, and spoked wheels with
  knobbly tyres that spin inside steering-only brakes. The body's visible parts hang from a pivot that `VehicleVisuals`
  leans (visual only); the collision boxes are separate and unchanged. Rebuilding keeps the prefab's root, so the garage
  and track scenes keep pointing at it. AI cars share the Falcon's prefab with their own paint and door numbers.
- **Effects.** `VehicleEffects` gives each car one particle system per kind of effect, fed only from what the car is
  doing, plus a skid-mark mesh that reuses its oldest marks: nothing is created while driving.
- Placeholder cars: Falcon RX (the original RallyCar, balanced), Kestrel S (light and nimble, lower top speed) and
  Titan GT (fastest, heavy; locked until the player wins a race).

## Career: credits, upgrades and tracks

The rules live in `Assets/Settings/Progression/` (created by RALLY X > Build > Create Missing Progression Content, never
overwritten, so tune them in the Inspector): `ProgressionSettings` (credits per finishing position, starting credits),
`UpgradeCatalog` (every part's stages, prices and effects) and `TrackRoster` with one `TrackInfo` per track.

- **Credits.** A new career starts with 2,000 CR. Each finished race pays by position (1,500 / 1,000 / 700 / 400; places
  past the list earn the last amount) times the track's credit multiplier (Red Canyon x1.5: 2,250 / 1,500 / 1,050 / 600),
  rounded to 10, shown on the results screen with the new total. The balance is shown on the main menu and in the garage.
- **Upgrades.** Engine, Turbo, Tires, Brakes, Suspension and Gearbox, three stages each, every stage dearer than the last.
  They are bought per car in the garage (UPGRADES, or U / gamepad Y): the list shows each part's installed stages and
  next price, the stat bars preview the next stage in green with the current and upgraded figures, and BUY asks for
  confirmation. A purchase is refused with a message when credits are short, the part is maxed or the car is locked.
- **What upgrades do.** A stage's `VehicleTuning` is the car's total improvement at that stage: Engine raises
  acceleration, Turbo adds pull that grows with speed plus a little top speed, Tires raise tyre friction, Brakes raise
  braking deceleration, Suspension adds steering at speed and anti-roll, Gearbox raises the top speed (downforce is
  kept the same at every speed). When a race loads, `PlayerRaceEntry` gives the player's car its own tuned copy of its
  `VehicleConfig` (`VehicleConfig.CreateTuned`, applied with `VehicleController.SetConfig`); the config assets and the AI
  cars are never changed. Play Mode tests drive each part stock and maxed and measure the difference.
- **Tracks.** A `TrackInfo` (`Assets/Settings/Progression/Tracks/`) holds a track's id, name, scene, the line shown
  when choosing it, its laps and credit multiplier, and what unlocks it: nothing (open from the start), enough race wins
  and/or a finish (optionally top N) on the track before it. Pine Valley is open from the start; Red Canyon unlocks
  with a top-3 finish at Pine Valley. Unlocks are announced on the results screen and kept in the profile. On the
  main menu Left / Right browse the tracks in roster order: a locked one shows what unlocks it and RACE is off;
  showing an unlocked one chooses it (saved). RACE loads the chosen track if it is unlocked, otherwise the first
  unlocked one.
- **Save file.** `PlayerProfile` (`profile.json` in `Application.persistentDataPath`) keeps credits, the chosen car,
  each car's upgrade stages, unlocked cars and tracks, and per-track records (best lap and the car it was set in,
  best position, finishes), and is saved on every change. `SaveFile` writes to a temporary file and swaps it in,
  keeping the previous save as `profile.json.bak`; a damaged save falls back to the backup. Ids are stored as strings,
  and Phase 7 profiles are upgraded in place. A locked or missing chosen car falls back to the default. Tests use a
  profile kept in memory.

## Layout

```
Assets/
  Scripts/        RallyX runtime assembly (Core, Player, Vehicle, AI, Racing, Tracks,
                  UI, Garage, Audio, VFX, SaveSystem) + Editor/ (RallyX.Editor assembly)
  Tests/EditMode/ RallyX.Tests.EditMode assembly (can reach editor code)
  Tests/PlayMode/ RallyX.Tests.PlayMode assembly
  Prefabs/        Vehicles, Environment, UI, Track
  Scenes/         MainMenu (opens first), Garage, Tracks (Pine Valley, Red Canyon), Dev (Sandbox test scene)
  Settings/       URP, vehicle, camera and track configs, surfaces, physics materials
  UI/             UI Toolkit theme, panel settings and layouts (Race/ = race HUD, results and pause menu; Menus/ = main menu and garage)
  Materials/ Models/ Textures/ Audio/ VFX/   (Environment/ and Generated/ subfolders are builder output; a theme's art is in its own subfolder)
```

Namespaces follow the top-level script folder: `RallyX.Core`, `RallyX.Vehicle`, `RallyX.Cameras`, ...
(plural `Cameras` because a `RallyX.Camera` namespace would hide `UnityEngine.Camera`).
Editor code uses `RallyX.EditorTools` (not `RallyX.Editor`, which would shadow `UnityEditor.Editor`).
