# SonsVR Project Plan

This is the canonical guide for the project. Read it before changing code,
building a DLL, deploying a DLL, or asking for another in-game test.

Last updated: 2026-07-31

## Operating Rule

Only one milestone may be active at a time.

New discoveries that do not block the active milestone go into the Parking Lot.
They do not become active work merely because they are interesting.

Before doing work:

1. State the active milestone.
2. Identify the evidence already available.
3. Decide whether the next action is inspection, implementation, build, deploy,
   or test.
4. Do not mix those phases without saying so.

## Active Milestone: Stable Camera and UI Foundation

Goal: complete one playable opening-to-gameplay run without losing the intended
camera, control, or essential UI.

This milestone must be completed before motion-controlled hands or VRIK work.

### Current staged build

- Path:
  `SonsVR_Mod/bin/Debug/net6.0/SonsVR_Mod.dll`
- SHA-256:
  `CDCF56AD5A54B100A87490A49E1677A2F063E746CC19B4B1229FC7691F5A3BA6`
- Configuration: Debug
- Deployment state: staged only; not copied into the running game

### Included fixes awaiting verification

- Preserve an authored cinematic camera's changing aim instead of writing the
  HMD rotation back into the game camera.
- Remove cinematic camera roll while preserving pitch and yaw.
- Use the authored cinematic camera position without the normal gameplay
  comfort offset.
- Fix the VR comfort menu crash when `Shader.Find("Unlit/Color")` is unavailable
  under HDRP.
- Log selected camera ID, hierarchy path, position, rotation, and depth.
- Retain the native-hand inspection references used by the read-only bridge.
- Point at inventory `LayoutItem` objects with a forgiving right-controller
  sphere ray while preserving the game's native hover/highlight callbacks.
- Keep the native inventory cursor, cursor snapping, and virtual mouse systems
  enabled.
- Show a thin controller pointer while the inventory mat is active.

### Acceptance checklist

Run these in order. Record pass/fail and a short observation. Do not repair a
later item while an earlier blocking item is still unclassified.

| Checkpoint | Required result | Status |
| --- | --- | --- |
| Title menu | Correct orientation, readable, controller interaction works | Pending |
| Helicopter seat | View faces the authored direction and is level | Pending |
| Laptop prompt | Prompt is visible and the mapped `E` action closes it | Pending |
| Helicopter crash | Scene renders continuously in the headset | Pending |
| Attacker/kick shot | Attacker is visible in the headset before the kick | Pending |
| Kelvin inspection | View begins facing Kelvin at the authored close distance | Pending |
| Gameplay spawn | Head height and forward direction are plausible | Pending |
| HUD | Minimap, vitals, and prompts are readable and sensibly placed | Pending |
| World interaction sight | Nearby targets remain selectable without exact head-gaze alignment | Fail: too strict |
| VR comfort menu | Opens, renders, pointer works, and closes | Pending |
| Game pause/options | Renders in VR, accepts navigation, and can unpause | Pending |
| Inventory mat | Mat is visible at the correct depth and position | Pass: visually correct |
| Mat interaction | Controller ray can highlight and activate items | Fail: gaze does not highlight |
| Inventory exit | Closing inventory restores locomotion and view | Pending |

### Current UI observations

- Sight/gaze interaction is too strict and needs a more forgiving target cone or
  pointer.
- HUD elements do not share a coherent placement model. The held-rock icon and
  the nearby-rock pickup prompt do not appear at matching positions.
- The inventory mat renders correctly in VR.
- Looking at mat items does not highlight them.
- Inventory interaction should follow The Forest's controller-ray point-and-click
  model instead of relying on head gaze.
- Source inspection found legacy code that disabled
  `InventoryCursorController`, `LayoutItemCursorSnapping`, and
  `VirtualMouseInput`. Those systems are now preserved.
- Current-game metadata confirms mat objects expose `LayoutItem.OnMouseEnter`,
  `OnMouseOver`, and `OnMouseExit`; the staged build drives those callbacks
  from the right-controller ray.

### Evidence to capture

Prefer automatic evidence over asking for repeated manual descriptions.

- UnityExplorer bridge state at camera transitions.
- Selected camera log lines from the MelonLoader log.
- Exact timestamp of a brief failure.
- Desktop screenshot and headset screenshot only when they differ materially.
- DLL SHA-256 used for the run.

If a scene has already passed, say so. Use logs and captured bridge state; do
not speak as if it is still live.

## Next Milestone: Functional Game UI

Start only after the camera foundation has passed or remaining camera failures
are isolated and non-blocking.

### Required outcomes

- Pause/options menu appears in the headset.
- Controller navigation and submit/cancel work.
- Mouse interaction on the desktop is not required.
- Inventory mat items can be selected and activated.
- Inventory can always be closed.
- HUD canvases are positioned independently rather than all being forced onto
  one head-locked plane.

## Following Milestone: Real Floating Hands

Goal: controller-tracked, game-quality floating hands comparable to The Forest.

### Verified facts

- The current game still contains the `VRPlayerControl` type and old VR fields.
- `ToggleVrControllers(bool)` and `SetHandTracking(bool)` are native no-op
  stubs in the current game build.
- The live LocalPlayer has no `VRPlayerControl` instance and no surviving
  left/right controller GameObjects.
- `WhiteArms/LeftHandTrim` and `WhiteArms/RightHandTrim` are live skinned
  renderers bound to the game's animated arm skeleton.
- The `WhiteArms` rig is visible during normal item animations, including
  holding a small rock.
- `OldSkin/LeftArmMesh1` and `RightArmTattooMesh1` were inactive during the
  observed gameplay state.
- Raw controller poses come from `NativeVRInput.GetPose("PoseLeft")` and
  `NativeVRInput.GetPose("PoseRight")` in
  `TrackingUniverseRawAndUncalibrated`.
- Controller poses must be transformed relative to the raw HMD pose using the
  same tracking-to-world conversion as the existing VR pointer.

### Non-negotiable constraints

- No primitive block hands.
- Do not force `LocalPlayer/PlayerAnimator/Geo` active.
- Do not override the game's arm animator.
- Do not add VRIK merely to make a first floating-hand implementation.
- Do not show controller hands over inventory, cinematics, full-body
  interactions, or native item-hand animations.
- Preserve the game's real hands whenever the game is intentionally animating
  them.

### Implementation sequence

1. Identify or extract a genuine standalone left/right hand mesh and materials.
2. Create visual-only controller hand objects with no gameplay colliders or
   native animation scripts.
3. Transform raw controller poses through the HMD tracking-to-world mapping.
4. Add configurable position and rotation offsets per hand.
5. Hide each floating hand when the corresponding native animated hand is
   active.
6. Hide both hands when the first-person controller is disabled, inventory is
   active, a cinematic camera is selected, or the pause UI owns input.
7. Verify empty-hand gameplay before testing held items.

## Later Milestone: Full Arms / VRIK

This is explicitly deferred.

- The game ships `Il2CppRootMotion.FinalIK`.
- `VRIK`, `IKSolverVR`, `VRIKCalibrator`, and related APIs are available.
- Full arms require animation-layer ownership, shoulder/root calibration, and
  reliable gameplay-state arbitration.
- Floating hands must be stable before this milestone starts.

## Build and Deployment Protocol

### Build

The normal handoff build is Debug:

```powershell
dotnet build SonsVR_Mod\SonsVR_Mod.csproj `
  -c Debug `
  --no-restore `
  -p:SonsOfTheForestDir='E:\Steam\steamapps\common\Sons Of The Forest' `
  -p:DeployToGame=false
```

Expected handoff path:

`SonsVR_Mod/bin/Debug/net6.0/SonsVR_Mod.dll`

### Before deployment

1. Confirm the game is closed.
2. Hash the staged DLL.
3. Hash the currently installed DLL.
4. State exactly which file will be replaced.
5. Do not deploy merely because a build succeeded.

### After deployment

1. Hash the installed DLL and confirm it matches the staged DLL.
2. Record that hash under the active milestone.
3. Test only the agreed checklist.
4. Do not produce another DLL while that build is still being tested unless the
   user explicitly ends the test.

## Inspection Resources

### Live bridge

- Base URL: `http://127.0.0.1:8765`
- Status: `http://127.0.0.1:8765/api/v1/status`
- OpenAPI: `http://127.0.0.1:8765/openapi.json`
- Read-only; it cannot mutate game state.

### Current game dump

- Root: `D:\SonsVR-Research\CurrentDump`
- Dummy DLLs: `D:\SonsVR-Research\CurrentDump\DummyDll`
- ISIL:
  `D:\SonsVR-Research\CurrentDump\ISIL\IsilDump`
- Current VRPlayerControl ISIL:
  `D:\SonsVR-Research\CurrentDump\ISIL\IsilDump\Sons\TheForest\VR\VRPlayerControl.txt`

### Historical dump

- Repository:
  `https://github.com/NeuralBinary/Sons-of-The-Forest-Dump`
- Treat it as architectural evidence, not proof of current method behavior.

### Runtime logs

- `E:\Steam\steamapps\common\Sons Of The Forest\MelonLoader\Logs`

## Parking Lot

Items remain here until the active milestone is complete.

- VRIK/full-body avatar.
- Motion-controlled melee refinement.
- Bow and two-handed weapon handling.
- Physical crouching.
- Room-scale body collision.
- Per-item hand poses and finger animation.
- Visual polish for controller rays.
- Additional comfort settings.
- UnityExplorer bridge array-index support.
- Project-specific reusable SonsVR Codex skill.
- Packaging, installer, release notes, and public distribution.

## Decision Log

### 2026-07-31

- Stopped using primitive hand geometry.
- Rejected forced activation of the normal first-person arm hierarchy.
- Confirmed the old native VR controller activation methods are stubs.
- Confirmed the live prefab has no native `VRPlayerControl` instance.
- Identified the live `WhiteArms` animated hand rig.
- Confirmed inventory disables the first-person controller before the inventory
  camera becomes active; hand suppression must react immediately.
- Reconstructed the opening camera sequence from logs:
  two `MainCameraCustcene` selections followed by `MainCameraFP`.
- Removed the destructive non-gameplay camera rotation write.
- Fixed the HDRP pointer-material null crash.
- Replaced inventory head-gaze assumptions with a forgiving controller sphere
  ray that retains native `LayoutItem` behavior.
- Removed the inventory-controller shutdown path.
- Standardized the staged handoff location on the Debug output folder.
