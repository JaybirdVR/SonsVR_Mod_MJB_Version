# SonsVR Project Plan

This is the canonical guide for the project. Read it before changing code,
building a DLL, deploying a DLL, or asking for another in-game test.

Last updated: 2026-08-02

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

Goal: complete one controlled opening-to-gameplay validation run without losing
the intended camera, control, or essential UI.

This milestone must be completed before motion-controlled hands or VRIK work.

### Current staged build

- Path:
  `SonsVR_Mod/bin/Debug/net6.0/SonsVR_Mod.dll`
- SHA-256:
  `18CD52772AD4882621E5B87F9C2BBDD47C681136480463EDF971CA9EE4918669`
- Configuration: Debug
- Deployment state: installed and verified by the user. Installed SHA-256
  matches the staged DLL:
  `18CD52772AD4882621E5B87F9C2BBDD47C681136480463EDF971CA9EE4918669`.
- Deployment ownership: only the user copies DLLs into the Steam/game
  directory. Codex must never write to the installed mod path.
- Staged correction: limit full authored camera rotation to an active native
  cutscene or a `MainCameraCustcene`; title/menu cameras retain the prior
  roll-stripped alignment.
- Diagnostic-only addition: expose the active intro-laptop page index, page
  count, close-trigger object, active state, and hierarchy path to the bridge.
  This does not change camera or UI behavior.
- Staged laptop interaction correction: bind the authored
  `CutsceneTrigger` observer to `VRHead`, test its collider with a forgiving
  HMD sight ray, preserve the existing grip-to-E path, and invoke the
  trigger's own event only on grip release if native E did not consume it.
  Expose `_introLaptopSightTargeted` and log native-versus-fallback handling.
- Staged page-4 crash correction: remove the unsupported IL2CPP
  `Physics.SphereCastAll` call that aborted every subsequent VR camera update;
  use the target's proven `Collider.Raycast` surface plus bounded geometric
  forgiveness without changing any cinematic transform behavior.

### Included fixes awaiting verification

- Preserve an authored cinematic camera's changing aim instead of writing the
  HMD rotation back into the game camera.
- Preserve the authored cinematic camera's full rotation, including the crash
  tumble, while retaining independent HMD look.
- Use the authored cinematic camera position without the normal gameplay
  comfort offset.
- Present an explicit black VR frame while no valid camera-owning scene exists,
  and remove the null scene-name exception from camera discovery.
- Use `CutsceneManager.IsCutscenePlaying()` and first-person-controller state to
  keep `MainCameraFP` cinematic until the boot/attacker sequence actually ends.
- Disable smooth/snap turning and game-camera rotation writes while a cutscene
  owns the view.
- Expose and log cutscene/gameplay camera-ownership transitions for the bridge.
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
| Title menu | Correct orientation, readable, controller interaction works | Pass on `4C16BB48`: corrected alignment and New Game selection worked |
| Loading transition | Headset remains black until the authored scene is visible | Pass as part of the first user-confirmed perfect opening on `18CD5277` |
| Helicopter seat | View faces the authored direction and is level | Pass: authored window view moved to an aligned laptop view; turning remained suppressed |
| Laptop prompt | Final close/power state is visible and advances the sequence | Pass on `18CD5277`: sight acquired the authored trigger and the sequence advanced without freezing camera updates |
| Helicopter crash | Scene renders continuously in the headset | Pass: headset followed the full authored tumble |
| Attacker/kick shot | Attacker is visible in the headset before the kick | Pass: complete boot/attacker action rendered correctly in FPV |
| Kelvin inspection | View begins facing Kelvin at the authored close distance | Pending |
| Gameplay spawn | Head height and forward direction are plausible | Pending |
| HUD | Minimap, vitals, and prompts are readable and sensibly placed | Pending |
| World interaction sight | Nearby targets remain selectable without exact head-gaze alignment | Fail: too strict |
| VR comfort menu | Opens, renders, pointer works, and closes | Pending |
| Game pause/options | Renders in VR, accepts navigation, and can unpause | Pending |
| Inventory mat | Mat is visible at the correct depth and position | Pass: visually correct |
| Mat interaction | Controller ray can highlight and activate items | Fail: gaze does not highlight |
| Inventory exit | Closing inventory restores locomotion and view | Pending |

### Opening camera defect contract

Expected behavior: authored first-person opening cinematics place the HMD at
the character's intended eye position and orientation. Seated or otherwise
constrained cinematics own gross body rotation; gameplay turn input must not
rotate the character independently during those states.

Observed behavior:

- During the transition into the helicopter scene, the desktop presents the
  intended black loading screen while the headset renders the unfinished world
  and shader-loading state.
- At the first helicopter/laptop start, the headset initially faces out the
  window while the seated character body is visible to the left.
- Right-stick turning remains active while the character is seatbelted and can
  be used to compensate manually.
- The initial viewpoint is too far forward for the laptop to be comfortably
  readable.
- Roughly one-third to one-half into the seated segment, a transition causes
  the view and laptop to become better aligned.
- During the in-flight crash, the helicopter and disembodied character body
  tumble while the VR view remains largely world-stable.
- The opening can branch into different crash outcomes, including a
  tree/falling sequence, before converging on the `PRESS` badge and kick
  cinematic.
- During the post-impact boot sequence, subtitles render correctly in VR but
  the authored first-person action does not. Live capture showed the mod had
  already switched to `MainCameraFP` in `SonsMain` and attached `LocalPlayer`,
  indicating premature gameplay-camera ownership rather than a general VR UI
  or subtitle-rendering failure.
- The `PRESS`/kick sequence is authored as first person but has not remained
  reliably attached to the intended game camera in VR.

Current hypotheses and verified evidence:

1. **Verified:** while the opening load is between valid scenes,
   `FindActiveGameplayScene()` can return a camera with a null scene name.
   `VRSystem.LateUpdate()` passes that null value to `string.Contains`, throws
   `ArgumentNullException` repeatedly, and leaves the VR rig following the
   previous `Main Camera`.
2. **Verified:** both VR eye cameras remain enabled with an all-layers culling
   mask and `Skybox` clear flags during that failure. They therefore render the
   unfinished world instead of the desktop's separate black loading overlay.

3. The opening uses multiple camera instances or Timeline states that share the
   `MainCameraCustcene` name, requiring per-transition anchoring.
4. The selected `MainCameraCustcene` may not always be the authored eye camera;
   the rig currently uses whichever camera the selector returns and copies its
   world position directly.
5. Cinematic rotation currently strips roll, explaining why the helicopter
   tumbles while the headset view remains level.
6. Gameplay turn input is not gated by cinematic/seated ownership and can add
   a persistent yaw offset before later transitions.
7. The fixed 180-degree HMD yaw correction and per-transition body alignment
   may be initialized against different authored camera orientations during the
   opening sequence.

Title regression captured on build
`1C6C9E4C436041BE4EA044C530DF21735CD61DC554DD2B1B2CF162D341B0B46A`:

- The bridge identified the visible state as `SonsTitleScene`, using
  `Main Camera`, with no active cutscene or interactive gameplay ownership.
- The rig body had inherited the title camera's full pitched/rolled rotation.
- Full source rotation must be limited to actual opening/game cinematics. The
  title scene must retain its previous roll-stripped comfort alignment.

Validation evidence from build
`4C16BB4875AFE319F80E7EE0F8DC6AE5EDEFB92AFA6A6DBF5CE5BD8B33C51CCD`:

- Title alignment and controller selection passed after full authored rotation
  was limited to actual cutscene ownership and `MainCameraCustcene`.
- Loading blackout engaged while no source camera existed, but released during
  `BlankScene` before the authored image was ready, producing a white interval.
- The helicopter used `MainCameraCustcene` instance `-110990`, then switched to
  instance `-331516` for the crash. Full authored rotation was preserved and
  the headset followed the tumble correctly.
- The intro laptop accepted scrolling and advanced to the crash, proving its
  controller remained active. The final close/power highlight was not visible;
  do not change the proven cinematic camera path to repair this UI defect.
- The post-crash sequence switched to `MainCameraFP` in `SonsMain` while native
  cutscene state remained true. The boot/attacker scene rendered correctly in
  first person.
- Native cutscene state then became false while gameplay remained disabled;
  interactive gameplay was enabled only when the first-person controller
  became active roughly 24 seconds later.

Tree-crash branch evidence from the diagnostic run:

- The RNG branch selected `MainCameraCustcene` instance `-330118` at
  `HelicopterTreeCrashCutscene(Clone)/positionOffset/introTreeCrash_ANIM_main_helicopter/Chopper/rotateOffset/chopper_CST1/moveOffset/playerCutScene/playerTactiRenderRig/Root/Hips/Spine/Spine1/Spine2/Neck/Neck1/Head/playerCameraNode/MainCameraCustcene`.
- Its initial world rotation was
  `(-0.18686, -0.25411, -0.90749, 0.27743)`. In the headset the view was nearly
  90 degrees wrong across two axes.
- The subsequent switch to `MainCameraFP` restored a correct boot/kick view.
- Do not alter the global cinematic camera path, which passes for the snow
  crash and boot/kick. Inspect the tree prefab's camera-node local orientation
  and apply only a tree-branch-specific correction if the asset evidence
  confirms an incompatible basis.

Laptop page-4 failure captured on staged build `5F041553`:

- At 22:04:00 the authored close trigger became active and the observer bound
  to `VRHead` successfully.
- The next call to `Physics.SphereCastAll` threw `NotSupportedException:
  Method unstripping failed` from `VRPlayer.UpdateIntroLaptopInteraction()`.
  The same exception repeated every frame, aborting `VRPlayer.LateUpdate()`
  before its camera-follow code ran.
- The helicopter continued along its authored animation while the VR rig
  remained at the last completed camera pose, making the user appear suspended
  in space. The live bridge's stale camera state was a consequence, not the
  initiating fault.
- Replace only the unsupported laptop cast with the proven
  `Collider.Raycast` API plus a bounded geometric forgiveness test. Preserve
  the passing seated alignment, crash tumble, and boot/kick camera logic.

Successful opening validation on installed build `18CD5277`:

- The user reported the first perfect complete opening.
- The laptop advanced through screens 0-3, bound its observer to `VRHead`, and
  logged `VR sight target=True` when the authored close target was acquired.
- The rig handed off from the helicopter camera `-110998` to snow-crash camera
  `-333674`, then to `MainCameraFP` `-48324` for the boot/gameplay path.
- Camera ownership remained non-interactive until the first-person controller
  enabled, then changed to `interactiveGameplay=True` at 22:19:00.
- The run logged zero `Method unstripping failed` or IL2CPP-to-managed
  trampoline exceptions.

Priority within the active milestone:

1. Align the game's interaction sight with the HMD view without modifying
   authored cinematic camera transforms. This blocks the intro-laptop close
   target, world pickups, and other essential interactions.
2. Correct the `HelicopterTreeCrashCutscene` camera basis without changing the
   snow-crash or boot/kick paths.

### Opening camera inspection sequence

Inspect one checkpoint at a time. Do not skip ahead until the bridge capture is
complete for the current state.

The opening has three pre-game scene phases, followed by normal gameplay:

1. **Helicopter:** initial seated/laptop state through the in-flight crash.
   Capture once before touching the turn stick, again when the laptop becomes
   better aligned, and during the visible tumble.
2. **Crash outcome:** the RNG-dependent tree, hanging, falling, water, or
   equivalent post-helicopter branch.
3. **Boot/attacker:** the final pre-game first-person sequence, including the
   `PRESS` badge dialogue and kick/boot action.
4. **Main gameplay:** `MainCameraFP` ownership is expected only after the boot
   sequence has fully completed.

Do not describe the boot sequence as part of main gameplay merely because the
mod has already selected `MainCameraFP`; that premature selection is itself a
defect under investigation.

At every checkpoint capture:

- loaded scene names and active PlayableDirector/Timeline objects;
- selected camera instance ID, hierarchy path, enabled state, depth, and full
  world/local transform;
- all other enabled cameras and their hierarchy paths;
- VR origin, body, head, and eye-camera world/local transforms;
- raw HMD tracking pose and accumulated snap/smooth-turn offset;
- first-person controller enabled state and current input ownership;
- relevant character seat, constraint, animation, or cutscene components.
- the active world-interaction/look controller, its ray origin and direction,
  selected target, range, layer mask, and any aim-assist cone or radius.

Camera alignment and interaction targeting must be evaluated from the same
capture. Do not widen the interaction cone until the ray is proven to originate
from and point along the corrected VR view.

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

### Next session: high-priority input correction

- Replace gesture-based jumping with right-controller `A` mapped to the game's
  normal jump action (`Space`).
- Disable the two-controller upward jump gesture.
- Remove the current gameplay `A` to `R` mapping.
- Preserve `A` as UI submit only while a menu or interactive VR panel owns
  input.
- Verify that gameplay jump, reload/rotate actions, and menu submit do not fire
  simultaneously.

### Evidence to capture

Prefer automatic evidence over asking for repeated manual descriptions.

- UnityExplorer bridge state at camera transitions.
- Selected camera log lines from the MelonLoader log.
- Exact timestamp of a brief failure.
- Desktop screenshot and headset screenshot only when they differ materially.
- DLL SHA-256 used for the run.

If a scene has already passed, say so. Use logs and captured bridge state; do
not speak as if it is still live.

### Next opening validation run

Test only the camera foundation in this order; do not open inventory or pause
menus during this run:

1. Loading transition: headset remains black instead of exposing unfinished
   geometry.
2. Helicopter: initial seated direction and laptop distance are plausible;
   right-stick turning does nothing.
3. Helicopter tumble: headset follows the authored crash rotation continuously.
4. Crash outcome: view remains attached through the RNG branch.
5. Boot/attacker: badge, attacker, and kick action render in first person;
   subtitles remain visible.
6. Main gameplay handoff: camera ownership log changes to
   `interactiveGameplay=True`, and normal turning becomes available only then.

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

### 2026-08-02

- Live opening inspection confirmed that the headset loading leak is caused by
  a null scene-name exception in `VRSystem.LateUpdate`, followed by the VR rig
  continuing to render the previous world camera with all layers and skybox
  clearing. The loading fix must combine null-safe camera selection with an
  explicit VR blackout state; it must not depend on unfinished world geometry
  being hidden by chance.
- AssetRipper confirmed `LookAtArmTrigger` is an authored layer-28 GameObject
  with a sphere collider, `CutsceneTrigger`, and world-space prompt Canvas.
  Its event calls `OpeningCutscene.StartCrashSequence`; the current game
  exposes `CutsceneTrigger.SetObserverTransform(Transform)` and
  `EventTriggered()`. The sequence's timeout must not be treated as evidence
  that the E interaction succeeded.

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
- Classified gesture-based jumping as incorrect for normal motion-controller
  gameplay; next implementation maps right-controller `A` to jump and removes
  the upward two-hand gesture.
