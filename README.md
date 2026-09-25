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

Headless setup / compile check:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath "C:\Users\Admin\RALLY X" ^
  -executeMethod RallyX.EditorTools.ProjectSetup.RunBatch -logFile -
```

## Controls

| Action    | Keyboard          | Gamepad        |
|-----------|-------------------|----------------|
| Throttle  | W / Up Arrow      | Right Trigger  |
| Brake     | S / Down Arrow    | Left Trigger   |
| Steer     | A D / Left Right  | Left Stick     |
| Handbrake | Space             | South (A / ✕)  |
| Pause     | Esc               | Start          |

All bindings are defined in `Assets/Scripts/Core/RallyInputActions.cs`.

## Layout

```
Assets/
  Scripts/        RallyX runtime assembly (Core, Player, Vehicle, AI, Racing, Tracks,
                  UI, Garage, Audio, VFX, SaveSystem) + Editor/ (RallyX.Editor assembly)
  Prefabs/        Vehicles, Environment, UI, Track
  Scenes/         MainMenu, Garage, Tracks, Dev (Sandbox test scene)
  Settings/       URP pipeline, renderer and volume assets
  Materials/ Models/ Textures/ Audio/ VFX/
```

Namespaces follow the top-level script folder: `RallyX.Core`, `RallyX.Vehicle`, ...
Editor code uses `RallyX.EditorTools` (not `RallyX.Editor`, which would shadow `UnityEditor.Editor`).
