using Il2CppInterop.Runtime;
using Il2CppSystem.Collections.Generic;
using MelonLoader;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Valve.VR;
using Il2CppSimpleMouseRotator = Il2Cpp.SimpleMouseRotator;
using Il2CppFirstPersonCharacter = Il2Cpp.FirstPersonCharacter;
using Il2CppPlayerAnimationControl = Il2Cpp.PlayerAnimatorControl;
using static SonsVRMod.SonsVRMod;
using MelonCoroutines = MelonLoader.MelonCoroutines;
using Il2CppMono;
using System.Reflection;
using Il2CppSons.Input;
using Il2CppSons.Inventory;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using Image = UnityEngine.UI.Image;
using Mathf = UnityEngine.Mathf;
using NativeLocalPlayer = Il2CppTheForest.Utils.LocalPlayer;
using NativeVRPlayerControl = Il2CppTheForest.VR.VRPlayerControl;

namespace SonsVRMod
{
    /// <summary>
    /// Manages the VR player representation by:
    /// - Creating a VR origin rig (VROrigin + Body + Head),
    /// - Linking it to the game's FPS camera and LocalPlayer,
    /// - Aligning player rotation to HMD direction,
    /// - Handling snap-turn and locomotion input,
    /// - Disabling mouse-based look controls.
    /// </summary>
    internal class VRPlayer : MonoBehaviour
    {
        public VRPlayer(IntPtr value) : base(value)
        {
        }

        /// <summary>
        /// Singleton instance for global access.
        /// </summary>
        public static VRPlayer Instance { get; set; }

        /// <summary>
        /// Root transform of the VR coordinate system (world anchor for the player).
        /// This is moved to match the game's FPS camera position.
        /// </summary>
        public Transform Origin { get; private set; }

        /// <summary>
        /// Body transform (child of Origin) that holds the StereoRender rig.
        /// Represents the player's torso/base in VR.
        /// </summary>
        public Transform Body { get; private set; }

        /// <summary>
        /// Reference to the main VR camera rig (usually unused directly; managed by StereoRender).
        /// </summary>
        public Camera Camera { get; private set; }

        /// <summary>
        /// Reference to the original game's FPS camera (used as position source).
        /// </summary>
        public Camera FPSCam { get; private set; }

        /// <summary>
        /// The custom stereo rendering component that drives left/right eye rendering.
        /// </summary>
        public StereoRender StereoRender { get; private set; }

        /// <summary>
        /// Name of the last camera used for debugging or fallback logic.
        /// </summary>
        public string lastCameraUsed = "";

        // Prevents multiple concurrent setup coroutines
        private static bool setupLock = false;

        /// <summary>
        /// True when the game is in UI/menu mode (e.g., inventory, map) and VR look should be disabled.
        /// Currently unused but reserved for future use.
        /// </summary>
        public bool isUIMode = false;

        /// <summary>
        /// Reference to the in-game LocalPlayer GameObject (root of the player character).
        /// </summary>
        public GameObject LocalPlayer;

        /// <summary>
        /// Internal reference to the currently used FPS camera (from game logic).
        /// </summary>
        private Camera UsedCamera = null;

        /// <summary>
        /// Name of the scene currently in use (for debugging).
        /// </summary>
        private string UsedSceneName = "";

        /// <summary>
        /// Name of the last camera assigned (for logging/debugging).
        /// </summary>
        public string lastCameraName = "";

        /// <summary>
        /// Indicates whether the VR rig has been fully initialized.
        /// </summary>
        private bool isSetupComplete = false;

        /// <summary>
        /// Cached reference to the game's first-person character controller.
        /// Used to control movement, running, and animation states.
        /// </summary>
        private Il2CppFirstPersonCharacter firstPersonCharacter;

        // === CONFIGURABLE ALIGNMENT OFFSETS ===

        /// <summary>
        /// Position offset (in camera-local space) applied to align the VR origin
        /// with the player's body. Adjust Y for height, Z for forward/backward.
        /// </summary>
        public Vector3 positionOffset = new Vector3(0.0f, 0.05f, -0.10f);

        /// <summary>
        /// Yaw (Y-axis) rotation offset in degrees.
        /// Corrects the HMD-to-game forward alignment.
        /// </summary>
        public float yawOffset = 180f;

        /// <summary>
        /// Speed at which the player's body rotates to match HMD direction (degrees/second).
        /// Higher values = faster alignment.
        /// </summary>
        public float bodyRotationSpeed = 10f;

        // === INPUT SMOOTHING ===

        private Vector2 smoothedInput = Vector2.zero;
        private float inputSmoothSpeed = 8f;

        // === RUN STATE MANAGEMENT ===

        private bool isRunning = false;
        private float lastRunActivationTime = 0f;
        private float runActivationCooldown = 0.25f; // Prevents accidental run activation

        // === TURN STATE ===

        private float snapTurnOffset = 0f; // Cumulative yaw offset from snap turns
        private bool snapTurnReset = true; // Prevents continuous rotation while stick is held

        /// <summary>
        /// Cached reference to the player's animation controller (unused but reserved).
        /// </summary>
        private Il2CppPlayerAnimationControl _cachedAnimController;

        /// <summary>
        /// Indicates whether Harmony patches (if any) are ready.
        /// Currently unused.
        /// </summary>
        private bool harmonyIsReady = false;



        public bool InventoryCamIsActive = false;
        private Renderer[] _playerRenderers;
        private Transform[] _armRigCandidates = new Transform[0];
        private Renderer _leftFirstPersonArm;
        private Renderer _rightFirstPersonArm;
        private Renderer _leftArmMeshCandidate;
        private Renderer _rightArmMeshCandidate;
        private Renderer _leftHandTrimCandidate;
        private Renderer _rightHandTrimCandidate;
        private NativeVRPlayerControl _nativeVrPlayerControl;
        private GameObject _nativeLeftHandController;
        private GameObject _nativeRightHandController;
        private Renderer[] _nativeLeftHandRenderers = new Renderer[0];
        private Renderer[] _nativeRightHandRenderers = new Renderer[0];
        private Il2CppSystem.Collections.Generic.Dictionary<Renderer, bool> _originalRendererStates;
        private bool _lastInventoriCameraIsActive = false;
        private GameObject _softwareCursor;
        private InventoryCursorController _cursorController;
        private GameObject _inventorySystem;
        private GameObject _FakeGround;
        private Canvas _screenSpaceCanvas;
        private ScreenCanvasState _screenCanvasOriginalState;
        private Canvas[] _vrCanvases = new Canvas[0];
        private float _nextCanvasScanTime = 0f;
        private bool canUpdate = false;
        private Canvas _playerMiniMapCanvas;
        private Transform _PlayerMiniMapTransf;
        private Transform _DebugCanvas;

        private LayoutItemCursorSnapping _layoutCursorSnapping;
        private VirtualMouseInput _virtualMouseInput;
        private InputCursorState _inputCursorState;
        private LayoutItem _inventoryRayTarget;
        private GameObject _inventoryPointer;
        private LineRenderer _inventoryPointerRenderer;
        private Material _inventoryPointerMaterial;

        
        private bool _cursorDisableTimerStarted = false;
        private float _cursorDisableStartTime = 0f;
        private const float CursorDisableDelay = 3.0f;
        private bool _layoutCursorDisabledByVr = false;
        private bool _virtualMouseDisabledByVr = false;
        private bool _inputCursorStateEnabledByVr = false;
        private bool _cursorControllerDisabledByVr = false;

        private Quaternion _bodyRotationBeforeInventory;
        private bool _bodyRotationCapturedForInventory = false;
        private bool _leftInventoryEyeSettingsCaptured = false;
        private int _leftInventoryEyeCullingMask;
        private CameraClearFlags _leftInventoryEyeClearFlags;
        private Color _leftInventoryEyeBackgroundColor;
        private bool _leftInventoryEyeUseOcclusionCulling;
        private bool _rightInventoryEyeSettingsCaptured = false;
        private int _rightInventoryEyeCullingMask;
        private CameraClearFlags _rightInventoryEyeClearFlags;
        private Color _rightInventoryEyeBackgroundColor;
        private bool _rightInventoryEyeUseOcclusionCulling;
        private Quaternion _lastSourceCameraRotation;
        private bool _sourceCameraRotationInitialized = false;

        
        private bool _popupDisableTimerStarted = false;
        private float _popupDisableStartTime = 0f;
        private const float PopupDisableDelay = 1.0f;

        public KeyboardHack kh;

        private const int TitleUiPanelLayer = 31;
        private const float TitleUiPanelDistance = 1.3f;
        private const float TitleUiPanelHeight = 0.9f;
        private const float TitleUiPanelVerticalOffset = 0.08f;
        private const float TitleViewHeightOffset = 0.7f;
        private const float TitleViewYawOffset = 45f;
        private RenderTexture _titleUiTexture;
        private GameObject _titleUiPanel;
        private Renderer _titleUiPanelRenderer;
        private Collider _titleUiPanelCollider;
        private Material _titleUiPanelMaterial;
        private GameObject _titleUiLaser;
        private LineRenderer _titleUiLaserRenderer;
        private Material _titleUiLaserMaterial;
        private GameObject _titleUiCursor;
        private bool _titleUiCaptureStarted;

        private const int InGameMenuLayer = 31;
        private const float InGameMenuDistance = 1.15f;
        private const float InGameMenuHeight = 0.75f;
        private GameObject _inGameMenu;
        private Canvas _inGameMenuCanvas;
        private GraphicRaycaster _inGameMenuRaycaster;
        private GameObject _inGameMenuLaser;
        private LineRenderer _inGameMenuLaserRenderer;
        private Material _inGameMenuLaserMaterial;
        private bool _inGameMenuOpen;
        private bool _lastMenuButtonPressed;
        private bool _lastMenuTriggerPressed;
        private GameObject _inGameMenuHover;

        private sealed class ScreenCanvasState
        {
            public RenderMode RenderMode;
            public Camera WorldCamera;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool HasScaler;
            public bool ScalerEnabled;
        }
        
        
        /// <summary>
        /// Unity callback called when the object is initialized.
        /// Prevents duplicate instances and starts asynchronous setup.
        /// </summary>
        private void Awake()
        {
            if (Instance != null)
            {
                MelonLogger.Error("[VRPlayer] ## Duplicate VRPlayer detected! Destroying instance. ##");
                GameObject.Destroy(gameObject);
                return;
            }

            MelonLogger.Msg("[VRPlayer] ## Creating VRPlayer instance ##");
            Instance = this;

            // Subscribe to scene load events (currently unused but kept for extensibility)
            SonsVRMod.onSceneLoaded += OnSceneLoaded;

            setupLock = false;
            isSetupComplete = false;

            // Start async setup after a short delay to ensure game systems are ready
            MelonCoroutines.Start(Setup());
            MelonCoroutines.Start(CaptureTitleUiLoop());
        }

        /// <summary>
        /// Callback triggered when a new scene is loaded.
        /// Currently unused but reserved for future scene-specific logic.
        /// </summary>
        private void OnSceneLoaded(int buildIndex, string sceneName)
        {
            // Intentionally empty
        }

        /// <summary>
        /// Asynchronous setup coroutine that:
        /// - Destroys any existing StereoRender,
        /// - Creates the VR rig hierarchy (Origin → Body → StereoRender),
        /// - Marks the object to survive scene changes.
        /// </summary>
        public IEnumerator Setup()
        {
            if (setupLock) yield break;
            setupLock = true;

            // Clean up previous StereoRender if it exists
            if (StereoRender != null)
            {
                Destroy(StereoRender);
                MelonLogger.Msg("[VRPlayer] #### STEREORENDER DESTROYED ####");
            }

            // Wait for game to stabilize (e.g., camera setup)
            yield return new WaitForSeconds(2.0f);

            Body = transform;
            Origin = transform.parent;

            // Ensure the Origin exists as the world root of the VR rig
            if (Origin == null)
            {
                Origin = new GameObject("[VROrigin]").transform;
                transform.SetParent(Origin, false);
            }

            // Ensure VR rig persists across scene loads
            DontDestroyOnLoad(Origin);

            // Attach stereo rendering system to the body
            StereoRender = Body.gameObject.AddComponent<StereoRender>();
            isSetupComplete = true;
        }

        /// <summary>
        /// Links this VR player to a specific game camera and scene.
        /// Finds the LocalPlayer object from the camera hierarchy and disables mouse look.
        /// </summary>
        /// <param name="activeSceneAndCam">Scene and camera data from game logic.</param>
        public void SetSceneAndCamera(ActiveSceneWithCamera activeSceneAndCam)
        {
            if (activeSceneAndCam.camera == UsedCamera)
            {
                return;
            }

            canUpdate = false;
            UsedSceneName = activeSceneAndCam.scene.name;
            UsedCamera = activeSceneAndCam.camera;
            _sourceCameraRotationInitialized = false;
            if (UsedCamera != null)
            {
                MelonLogger.Msg(
                    $"[VR Camera] Selected '{UsedCamera.name}' " +
                    $"id={UsedCamera.GetInstanceID()} " +
                    $"path='{GetTransformPath(UsedCamera.transform)}' " +
                    $"position={UsedCamera.transform.position} " +
                    $"rotation={UsedCamera.transform.rotation} " +
                    $"depth={UsedCamera.depth}.");
                UsedCamera.targetTexture = null;
                //UsedCamera.pixelRect = new Rect(0, 0, 1080, 720); // Optional: small preview

                if (UsedCamera.name.Equals("InventoryCamera"))
                {
                    InventoryCamIsActive = true;
                }
                else
                {
                    InventoryCamIsActive = false;
                }

                GameObject foundLocalPlayer =
                    FindLocalPlayerFromCamera(UsedCamera, InventoryCamIsActive);
                // UnityExplorer FreeCam is unrelated to the player hierarchy.
                // Preserve the existing player reference while following it.
                if (foundLocalPlayer != null)
                {
                    LocalPlayer = foundLocalPlayer;
                    firstPersonCharacter = LocalPlayer.GetComponent<Il2CppFirstPersonCharacter>();
                    CachePlayerRigForInspection();
                    if (kh != null)
                    {
                        kh.LocalPlayer = LocalPlayer;
                    }
                }

                if (UsedCamera.name.Equals("MainCameraFP") &&
                    Body != null && StereoRender?.Head != null)
                {
                    Vector3 gameplayForward = LocalPlayer != null
                        ? LocalPlayer.transform.forward
                        : UsedCamera.transform.forward;
                    Quaternion gameplayYaw = GetLevelYawRotation(gameplayForward);
                    Quaternion headLocalYaw =
                        GetLevelYawRotation(StereoRender.Head.localRotation * Vector3.forward);
                    Body.rotation = gameplayYaw * Quaternion.Inverse(headLocalYaw);
                    _sourceCameraRotationInitialized = false;
                }

                canUpdate = true;
            }
        }

        /// <summary>
        /// Unity Update (called once per frame). Currently empty (logic in LateUpdate).
        /// </summary>
        private void Update()
        {
            // All VR logic runs in LateUpdate to ensure camera positions are final
        }

        /// <summary>
        /// Main VR logic loop, executed after all Update() calls.
        /// Handles:
        /// - Positioning the VR origin based on FPS camera,
        /// - Snap-turn input,
        /// - Body rotation alignment,
        /// - Run state management.
        /// </summary>
        private void LateUpdate()
        {
            HandleUpdate();
            DisableMouseRotation();
        }

        /// <summary>
        /// Core update logic for VR player behavior.
        /// </summary>
        private void HandleUpdate()
        {
            if (canUpdate == false)
            {
                return;
            }

            bool isTitleScene = IsTitleScene();
            // The title screen is mirrored from the desktop framebuffer, but
            // pause/options UI is ScreenSpaceOverlay and is omitted by
            // ScreenCapture. Convert the actual pause canvas to world-space.
            SetTitleUiVisible(isTitleScene);
            SetInGameMenuVisible(!isTitleScene && _inGameMenuOpen);

            if (InventoryCamIsActive != _lastInventoriCameraIsActive)
            {
                MelonLogger.Msg($"[VRPlayer] InventoryCamera state changed: {InventoryCamIsActive}");
                if (!InventoryCamIsActive)
                    RestoreInventoryState();

                _lastInventoriCameraIsActive = InventoryCamIsActive;
            }

            if (!isTitleScene)
            {
                if (_screenSpaceCanvas == null)
                    FindInventoryCanvas();

                if (_playerMiniMapCanvas == null)
                    FindMiniMapCanvas();

                ConfigureCanvasesForVR();
                UpdateCanvasPositionForVR();
            }


            
            if (InventoryCamIsActive == true)
            {
                if (!_bodyRotationCapturedForInventory && Body != null &&
                    StereoRender?.Head != null && UsedCamera != null)
                {
                    _bodyRotationBeforeInventory = Body.rotation;
                    Quaternion inventoryYaw =
                        GetLevelYawRotation(UsedCamera.transform.forward);
                    Quaternion headLocalYaw =
                        GetLevelYawRotation(
                            StereoRender.Head.localRotation * Vector3.forward);
                    Body.rotation =
                        inventoryYaw * Quaternion.Inverse(headLocalYaw);
                    _bodyRotationCapturedForInventory = true;
                }

                // Match the VR head to the inventory camera without copying its
                // downward pitch into the whole rig or treating its world
                // position as a raw room-scale origin.
                if (UsedCamera != null && Origin != null &&
                    StereoRender?.Head != null)
                {
                    Origin.position +=
                        UsedCamera.transform.position -
                        StereoRender.Head.position;
                }

                CaptureInventoryEyeCameraSettings();
                ApplyInventoryEyeCameraSettings(UsedCamera);
                UpdateInventoryControllerPointer();
            }

            if (InventoryCamIsActive == false)
            {
                ClearInventoryControllerPointer();
                _popupDisableTimerStarted = false;
                _popupDisableStartTime = 0.0f;
                
                if (_softwareCursor != null)
                {
                    _softwareCursor.active = false;
                }

                if (_cursorController != null)
                {
                    _cursorController.enabled = true;
                    _cursorDisableTimerStarted = false;
                    _cursorDisableStartTime = 0f;
                }
                
                // --- POSITIONING: Sync VR origin to game camera ---
                if (UsedCamera != null && Origin != null)
                {
                    bool isGameplayCamera = UsedCamera.name.Equals("MainCameraFP");
                    if (!isGameplayCamera && Body != null && StereoRender?.Head != null)
                    {
                        Quaternion currentSourceRotation =
                            GetComfortCameraRotation(UsedCamera.transform.forward);
                        if (!_sourceCameraRotationInitialized)
                        {
                            Body.rotation =
                                currentSourceRotation *
                                Quaternion.Inverse(StereoRender.Head.localRotation);
                            _sourceCameraRotationInitialized = true;
                        }
                        else
                        {
                            Quaternion sourceDelta =
                                currentSourceRotation *
                                Quaternion.Inverse(_lastSourceCameraRotation);
                            Body.rotation = sourceDelta * Body.rotation;
                        }

                        _lastSourceCameraRotation = currentSourceRotation;
                    }
                    else if (isGameplayCamera)
                    {
                        _sourceCameraRotationInitialized = false;
                    }

                    Vector3 basePosition = UsedCamera.transform.position;
                    // Gameplay uses a small comfort offset. Cinematics must use
                    // their authored camera position exactly or close-up
                    // interactions (such as inspecting Kelvin) become detached.
                    Vector3 finalPosition =
                        isGameplayCamera || isTitleScene
                            ? basePosition +
                              UsedCamera.transform.TransformDirection(positionOffset)
                            : basePosition;
                    if (isTitleScene)
                        finalPosition += Vector3.up * TitleViewHeightOffset;

                    Origin.position = finalPosition;
                    if (isTitleScene)
                        UpdateTitleUiPanelPose();
                }



                // --- TURNING: Right thumbstick horizontal input ---
                UnityEngine.Vector2 rightStick = NativeVRInput.GetVector2("ThumbstickRight");
                float horizontalTurn = rightStick.x;
                float turnDeadzone = SonsVRMod.TurnDeadzone;

                if (SonsVRMod.CurrentTurnMode == TurnMode.Smooth)
                {
                    if (Mathf.Abs(horizontalTurn) > turnDeadzone && StereoRender != null)
                    {
                        float direction = horizontalTurn * SonsVRMod.SmoothTurnSpeed * Time.deltaTime;
                        StereoRender.transform.Rotate(0, direction, 0, Space.World);
                    }
                }
                else if (SonsVRMod.CurrentTurnMode == TurnMode.Snap)
                {
                    if (Mathf.Abs(horizontalTurn) > SonsVRMod.SnapTurnThreshold)
                    {
                        if (snapTurnReset)
                        {
                            float direction =
                                (horizontalTurn > 0) ? SonsVRMod.SnapTurnAngle : -SonsVRMod.SnapTurnAngle;
                            snapTurnOffset += direction;

                            if (StereoRender != null)
                            {
                                StereoRender.transform.Rotate(0, direction, 0, Space.World);
                            }

                            snapTurnReset = false;
                        }
                    }
                    else if (Mathf.Abs(horizontalTurn) <= turnDeadzone)
                    {
                        // Reset only after the stick is centered, preventing repeated snaps while held.
                        snapTurnReset = true;
                    }
                }

                // Keep the desktop/game camera aligned with the VR view even
                // on title and loading screens where LocalPlayer is absent.
                if (UsedCamera != null &&
                    UsedCamera.name.Equals("MainCameraFP") &&
                    StereoRender?.Head != null)
                {
                    UsedCamera.transform.rotation = StereoRender.Head.rotation;
                }

                // --- BODY ALIGNMENT: Rotate player to match HMD forward direction ---
                if (LocalPlayer != null &&
                    UsedCamera != null &&
                    UsedCamera.name.Equals("MainCameraFP") &&
                    StereoRender?.Head != null)
                {

                    // Project HMD forward onto horizontal plane (ignore vertical look)
                    Vector3 headForward = StereoRender.Head.forward;
                    headForward.y = 0f;

                    if (headForward.magnitude > 0.01f)
                    {
                        headForward.Normalize();
                        Quaternion targetRotation = Quaternion.LookRotation(headForward, Vector3.up);

                        // Smoothly rotate the player character toward HMD direction
                        LocalPlayer.transform.rotation = Quaternion.Slerp(
                            LocalPlayer.transform.rotation,
                            targetRotation,
                            Time.deltaTime * bodyRotationSpeed
                        );
                    }
                }

                // --- LOCOMOTION & RUNNING ---
                if (firstPersonCharacter != null &&
                    firstPersonCharacter.enabled &&
                    UsedCamera != null &&
                    UsedCamera.name.Equals("MainCameraFP"))
                {
                    UnityEngine.Vector2 leftStick = NativeVRInput.GetVector2("ThumbstickLeft");

                    // Deadzone and clamping
                    if (leftStick.magnitude < 0.15f)
                        leftStick = Vector2.zero;
                    else
                        leftStick = Vector2.ClampMagnitude(leftStick, 1f);

                    // Activate running if right stick is pushed forward (y > 0.7) and not turning
                    if (!isRunning && rightStick.y > 0.7f && Mathf.Abs(rightStick.x) < 0.3f)
                    {
                        float currentTime = Time.time;
                        if (currentTime - lastRunActivationTime > runActivationCooldown)
                        {
                            isRunning = true;
                            lastRunActivationTime = currentTime;
                            MelonLogger.Msg("[VRPlayer] Running activated");
                        }
                    }

                    // Deactivate running if moving backward or stopping
                    if (isRunning && (leftStick.magnitude < 0.01f || leftStick.y < 0f))
                    {
                        isRunning = false;
                    }

                    // Forward run state to game character controller
                    firstPersonCharacter.SetRunning(isRunning);
                }
            }
        }

        /// <summary>
        /// Attempts to locate the LocalPlayer GameObject by walking up the camera's transform hierarchy.
        /// Expects a structure like: Camera → ... → LookObject → ... → LocalPlayer.
        /// </summary>
        /// <param name="camera">The FPS camera to start from.</param>
        /// <returns>The LocalPlayer GameObject, or null if not found.</returns>
        private GameObject FindLocalPlayerFromCamera(Camera camera, bool useInventoryCam)
        {
            if (camera == null) return null;
            MelonLogger.Msg($"[VRPlayer] Find Player For camera : {camera.name}");

            if (useInventoryCam == true)
            {
                Transform parent = camera.transform.parent;
                for (int ix = 0; ix < 20; ix++)
                {
                    if (parent.name == "LocalPlayer")
                    {
                        MelonLogger.Msg($"[VRPlayer] Found player root: {parent.name} For InventoryCam");
                        return parent.gameObject;
                    }

                    Transform tempParent = parent.parent;
                    parent = tempParent;
                }
            }
            else
            {
                Transform current = camera.transform;
                for (int i = 0; i < 3; i++)
                {
                    current = current.parent;
                    if (current == null) break;

                    if (current.name == "LookObject")
                    {
                        // Climb up until we find "LocalPlayer" or reach the root
                        while (current.parent != null && current.parent.name != "LocalPlayer" &&
                               current.parent.parent != null)
                        {
                            current = current.parent;
                        }

                        if (current.parent != null && current.parent.name == "LocalPlayer")
                        {
                            MelonLogger.Msg($"[VRPlayer] Found player root: {current.parent.name}");
                            return current.parent.gameObject;
                        }
                    }
                }
            }

            MelonLogger.Warning("[VRPlayer] Could not find LocalPlayer from camera.");
            return null;
        }

        /// <summary>
        /// Disables all instances of SimpleMouseRotator on the player and camera.
        /// Prevents mouse input from interfering with HMD-based look.
        /// </summary>
        private void DisableMouseRotation()
        {
            if (LocalPlayer != null)
            {
                var rotator = LocalPlayer.GetComponent<Il2CppSimpleMouseRotator>();
                if (rotator != null)
                {
                    rotator.enabled = false;
                }
            }

            if (UsedCamera != null)
            {
                var camRotator = UsedCamera.GetComponent<Il2CppSimpleMouseRotator>();
                if (camRotator != null)
                {
                    camRotator.enabled = false;
                }
            }
        }

        /// <summary>
        /// Cleanup when the object is destroyed.
        /// Unsubscribes from events to prevent memory leaks.
        /// </summary>
        private void OnDestroy()
        {
            MelonLogger.Warning("[VRPlayer] *** VRPlayer DESTROYED ***");
            SonsVRMod.onSceneLoaded -= OnSceneLoaded;
            RestoreInventoryEyeCameraSettings();
            DestroyTitleUiResources();
            DestroyInGameMenuResources();
            DestroyInventoryPointerResources();
        }

        /// <summary>
        /// Updates the small VR-only comfort menu. The right stick click toggles it;
        /// the right controller ray and trigger activate its buttons.
        /// </summary>
        public void UpdateInGameMenu(bool menuButtonPressed, bool triggerPressed)
        {
            if (IsTitleScene())
            {
                _inGameMenuOpen = false;
                _lastMenuButtonPressed = menuButtonPressed;
                return;
            }

            if (menuButtonPressed && !_lastMenuButtonPressed)
                _inGameMenuOpen = !_inGameMenuOpen;

            _lastMenuButtonPressed = menuButtonPressed;
            if (!_inGameMenuOpen)
                return;

            EnsureInGameMenuResources();
            UpdateInGameMenuPose();

            if (_inGameMenuRaycaster == null || StereoRender == null ||
                !StereoRender.HasValidHmdTrackingPose)
            {
                _lastMenuTriggerPressed = triggerPressed;
                return;
            }

            if (!TryGetControllerRay(out Ray ray))
                return;

            Plane menuPlane = new Plane(_inGameMenuCanvas.transform.forward, _inGameMenuCanvas.transform.position);
            if (!menuPlane.Raycast(ray, out float distance))
            {
                _lastMenuTriggerPressed = triggerPressed;
                return;
            }

            Vector3 panelPoint = ray.GetPoint(distance);
            Vector3 localPoint =
                _inGameMenuCanvas.transform.InverseTransformPoint(panelPoint);
            GameObject hovered = GetMenuButtonAtPoint(
                new Vector2(localPoint.x, localPoint.y));

            if (_inGameMenuHover != hovered)
            {
                SetMenuButtonHighlighted(_inGameMenuHover, false);
                _inGameMenuHover = hovered;
                SetMenuButtonHighlighted(_inGameMenuHover, true);
            }

            if (triggerPressed && !_lastMenuTriggerPressed && hovered != null)
            {
                var button = hovered.GetComponentInParent<UnityEngine.UI.Button>();
                if (button != null && button.interactable)
                    ActivateMenuButton(button.gameObject.name);
            }
            _lastMenuTriggerPressed = triggerPressed;
        }

        private GameObject GetMenuButtonAtPoint(Vector2 point)
        {
            string buttonName = null;
            if (point.y >= 65f && point.y <= 135f)
            {
                if (point.x >= -295f && point.x <= -45f)
                    buttonName = "Smooth Turn";
                else if (point.x >= 45f && point.x <= 295f)
                    buttonName = "Snap Turn";
            }
            else if (point.y >= -35f && point.y <= 35f)
            {
                if (point.x >= -295f && point.x <= -45f)
                    buttonName = "Turn Off";
                else if (point.x >= 45f && point.x <= 295f)
                    buttonName = "Recenter";
            }

            Transform button = buttonName != null
                ? _inGameMenuCanvas.transform.Find(buttonName)
                : null;
            return button != null ? button.gameObject : null;
        }

        public void RecenterView()
        {
            if (Origin == null || StereoRender?.Head == null)
                return;

            Vector3 headForward = StereoRender.Head.forward;
            headForward.y = 0f;
            Vector3 originForward = Origin.forward;
            originForward.y = 0f;
            if (headForward.sqrMagnitude < 0.0001f ||
                originForward.sqrMagnitude < 0.0001f)
                return;

            float correction = Vector3.SignedAngle(
                headForward.normalized,
                originForward.normalized,
                Vector3.up);
            StereoRender.transform.Rotate(0f, correction, 0f, Space.World);
            snapTurnOffset = 0f;
            snapTurnReset = true;
            MelonLogger.Msg("[VRPlayer] View recentered.");
        }

        private bool TryGetControllerRay(out Ray ray)
        {
            ray = new Ray();
            if (StereoRender?.Head == null || !StereoRender.HasValidHmdTrackingPose)
                return false;

            Pose controllerPose = NativeVRInput.GetPose("PoseRight");
            if (controllerPose.position.sqrMagnitude < 0.000001f &&
                controllerPose.rotation == Quaternion.identity)
                return false;

            Quaternion trackingToWorld =
                StereoRender.Head.rotation * Quaternion.Inverse(StereoRender.HmdTrackingRotation);
            Vector3 relativePosition = controllerPose.position - StereoRender.HmdTrackingPosition;
            Vector3 origin = StereoRender.Head.position + trackingToWorld * relativePosition;
            Vector3 direction = trackingToWorld * (controllerPose.rotation * Vector3.forward);
            if (direction.sqrMagnitude < 0.0001f)
                return false;

            ray = new Ray(origin, direction.normalized);
            if (_inGameMenuLaserRenderer != null)
            {
                _inGameMenuLaserRenderer.SetPosition(0, origin);
                _inGameMenuLaserRenderer.SetPosition(1, origin + direction.normalized * 3f);
            }
            return true;
        }

        /// <summary>
        /// Uses the right controller as the inventory pointer. Inventory layout
        /// items are 3D objects, not EventSystem UI, so a screen-center pointer
        /// cannot hover them. Keep the game's LayoutItem callbacks in charge of
        /// highlighting and selection and only provide the missing VR ray.
        /// </summary>
        private void UpdateInventoryControllerPointer()
        {
            if (!TryGetControllerRay(out Ray ray))
            {
                ClearInventoryControllerPointer();
                return;
            }

            EnsureInventoryPointerResources();

            const float maxDistance = 6f;
            const float forgivenessRadius = 0.045f;
            RaycastHit[] hits = Physics.SphereCastAll(
                ray,
                forgivenessRadius,
                maxDistance,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            LayoutItem closestItem = null;
            float closestDistance = maxDistance;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null)
                    continue;

                LayoutItem item = hitCollider.GetComponentInParent<LayoutItem>();
                if (item != null && item.isActiveAndEnabled &&
                    hits[i].distance < closestDistance)
                {
                    closestItem = item;
                    closestDistance = hits[i].distance;
                }
            }

            if (closestItem != _inventoryRayTarget)
            {
                if (_inventoryRayTarget != null)
                    _inventoryRayTarget.OnMouseExit();

                _inventoryRayTarget = closestItem;
                if (_inventoryRayTarget != null)
                    _inventoryRayTarget.OnMouseEnter();
            }

            if (_inventoryRayTarget != null)
                _inventoryRayTarget.OnMouseOver();

            if (_inventoryPointerRenderer != null)
            {
                _inventoryPointerRenderer.enabled = true;
                _inventoryPointerRenderer.SetPosition(0, ray.origin);
                _inventoryPointerRenderer.SetPosition(
                    1,
                    ray.GetPoint(
                        _inventoryRayTarget != null
                            ? Mathf.Max(closestDistance, 0.05f)
                            : 2.5f));
            }
        }

        private void EnsureInventoryPointerResources()
        {
            if (_inventoryPointer != null)
                return;

            _inventoryPointer = new GameObject("[VR UI] Inventory Pointer");
            SetLayerRecursively(_inventoryPointer, InGameMenuLayer);
            _inventoryPointerRenderer = _inventoryPointer.AddComponent<LineRenderer>();
            _inventoryPointerRenderer.useWorldSpace = true;
            _inventoryPointerRenderer.positionCount = 2;
            _inventoryPointerRenderer.startWidth = 0.004f;
            _inventoryPointerRenderer.endWidth = 0.002f;
            _inventoryPointerRenderer.startColor = new Color(0.2f, 0.9f, 1f, 0.9f);
            _inventoryPointerRenderer.endColor = new Color(1f, 1f, 1f, 0.75f);

            Shader pointerShader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("HDRP/Unlit") ??
                Shader.Find("Unlit/Color");
            if (pointerShader != null)
            {
                _inventoryPointerMaterial = new Material(pointerShader);
                _inventoryPointerMaterial.color = Color.white;
                _inventoryPointerRenderer.material = _inventoryPointerMaterial;
            }

            if (UsedCamera != null)
                UsedCamera.cullingMask |= 1 << InGameMenuLayer;
            if (StereoRender?.LeftCam != null)
                StereoRender.LeftCam.cullingMask |= 1 << InGameMenuLayer;
            if (StereoRender?.RightCam != null)
                StereoRender.RightCam.cullingMask |= 1 << InGameMenuLayer;
        }

        private void ClearInventoryControllerPointer()
        {
            if (_inventoryRayTarget != null)
            {
                _inventoryRayTarget.OnMouseExit();
                _inventoryRayTarget = null;
            }

            if (_inventoryPointerRenderer != null)
                _inventoryPointerRenderer.enabled = false;
        }

        private void DestroyInventoryPointerResources()
        {
            ClearInventoryControllerPointer();
            if (_inventoryPointer != null)
                Destroy(_inventoryPointer);
            if (_inventoryPointerMaterial != null)
                Destroy(_inventoryPointerMaterial);
            _inventoryPointer = null;
            _inventoryPointerRenderer = null;
            _inventoryPointerMaterial = null;
        }

        private void EnsureInGameMenuResources()
        {
            if (_inGameMenu != null)
                return;

            _inGameMenu = new GameObject("[VR UI] In-Game Menu");
            SetLayerRecursively(_inGameMenu, InGameMenuLayer);
            _inGameMenuCanvas = _inGameMenu.AddComponent<Canvas>();
            _inGameMenuCanvas.renderMode = RenderMode.WorldSpace;
            _inGameMenuCanvas.worldCamera = StereoRender?.RightCam;
            _inGameMenuCanvas.sortingOrder = 100;
            _inGameMenuCanvas.transform.SetParent(StereoRender?.Head, false);
            _inGameMenuCanvas.transform.localScale = Vector3.one * 0.001f;
            _inGameMenuCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 500f);
            _inGameMenuRaycaster = _inGameMenuCanvas.gameObject.AddComponent<GraphicRaycaster>();
            if (StereoRender?.LeftCam != null)
                StereoRender.LeftCam.cullingMask |= 1 << InGameMenuLayer;
            if (StereoRender?.RightCam != null)
                StereoRender.RightCam.cullingMask |= 1 << InGameMenuLayer;
            if (UsedCamera != null)
                UsedCamera.cullingMask &= ~(1 << InGameMenuLayer);

            CreateMenuImage("Background", _inGameMenuCanvas.transform, new Color(0.035f, 0.045f, 0.06f, 0.96f),
                new Vector2(700f, 500f), Vector2.zero);
            CreateMenuText("Title", "SONS VR", _inGameMenuCanvas.transform, 34, new Vector2(0f, 195f));
            CreateMenuText("Help", "Right stick click: close   |   Right trigger: select", _inGameMenuCanvas.transform, 18, new Vector2(0f, -195f));

            CreateMenuButton("Smooth Turn", "Smooth Turn", new Vector2(-170f, 100f));
            CreateMenuButton("Snap Turn", "Snap Turn", new Vector2(170f, 100f));
            CreateMenuButton("Turn Off", "Turn Off", new Vector2(-170f, 0f));
            CreateMenuButton("Recenter", "Recenter View", new Vector2(170f, 0f));

            _inGameMenuLaser = new GameObject();
            _inGameMenuLaser.name = "[VR UI] In-Game Pointer";
            SetLayerRecursively(_inGameMenuLaser, InGameMenuLayer);
            _inGameMenuLaserRenderer = _inGameMenuLaser.AddComponent<LineRenderer>();
            _inGameMenuLaserRenderer.startWidth = 0.004f;
            _inGameMenuLaserRenderer.endWidth = 0.002f;
            _inGameMenuLaserRenderer.positionCount = 2;
            Shader pointerShader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("HDRP/Unlit") ??
                Shader.Find("Unlit/Color");
            if (pointerShader != null)
            {
                _inGameMenuLaserMaterial = new Material(pointerShader)
                {
                    name = "[VR UI] In-Game Pointer Material",
                    color = new Color(0.2f, 0.8f, 1f, 0.85f)
                };
                _inGameMenuLaserRenderer.material = _inGameMenuLaserMaterial;
            }
            else
            {
                MelonLogger.Warning(
                    "[VR UI] No compatible pointer shader found; " +
                    "continuing without a custom laser material.");
            }
            SetLayerRecursively(_inGameMenu, InGameMenuLayer);
        }

        private void CreateMenuButton(string name, string label, Vector2 position)
        {
            GameObject buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(_inGameMenuCanvas.transform, false);
            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(250f, 70f);
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.18f, 0.25f, 1f);
            UnityEngine.UI.Button button = buttonObject.AddComponent<UnityEngine.UI.Button>();
            CreateMenuText("Label", label, buttonObject.transform, 22, Vector2.zero);
        }

        private void ActivateMenuButton(string buttonName)
        {
            switch (buttonName)
            {
                case "Smooth Turn":
                    SonsVRMod.SetTurnMode(TurnMode.Smooth);
                    break;
                case "Snap Turn":
                    SonsVRMod.SetTurnMode(TurnMode.Snap);
                    break;
                case "Turn Off":
                    SonsVRMod.SetTurnMode(TurnMode.Disabled);
                    break;
                case "Recenter":
                    RecenterView();
                    break;
            }
        }

        private void CachePlayerRigForInspection()
        {
            if (LocalPlayer == null)
                return;

            _playerRenderers = LocalPlayer.GetComponentsInChildren<Renderer>(true);
            Transform[] transforms = LocalPlayer.GetComponentsInChildren<Transform>(true);
            var armCandidates = new System.Collections.Generic.List<Transform>();
            _leftFirstPersonArm = null;
            _rightFirstPersonArm = null;
            _leftArmMeshCandidate = null;
            _rightArmMeshCandidate = null;
            _leftHandTrimCandidate = null;
            _rightHandTrimCandidate = null;
            foreach (Renderer playerRenderer in _playerRenderers)
            {
                if (playerRenderer == null)
                    continue;

                switch (playerRenderer.name)
                {
                    case "PlayerLeftArm":
                        _leftFirstPersonArm = playerRenderer;
                        break;
                    case "PlayerRightArm":
                        _rightFirstPersonArm = playerRenderer;
                        break;
                    case "LeftArmMesh1":
                    case "LeftArmMesh":
                        _leftArmMeshCandidate ??= playerRenderer;
                        break;
                    case "RightArmTattooMesh1":
                    case "RightArmTattooMesh":
                        _rightArmMeshCandidate ??= playerRenderer;
                        break;
                    case "LeftHandTrim":
                        _leftHandTrimCandidate = playerRenderer;
                        break;
                    case "RightHandTrim":
                        _rightHandTrimCandidate = playerRenderer;
                        break;
                }
            }

            foreach (Transform candidate in transforms)
            {
                if (candidate == null)
                    continue;

                string lowerName = candidate.name.ToLowerInvariant();
                if (lowerName.Contains("arm") ||
                    lowerName.Contains("hand") ||
                    lowerName.Contains("wrist") ||
                    lowerName.Contains("shoulder") ||
                    lowerName.Contains("clavicle"))
                {
                    armCandidates.Add(candidate);
                }
            }

            _armRigCandidates = armCandidates.ToArray();
            CacheNativeVrHandObjects();
            MelonLogger.Msg(
                $"[VRPlayer] Exposed {_playerRenderers.Length} player renderers and " +
                $"{_armRigCandidates.Length} arm/hand rig candidates for inspection.");
        }

        private void CacheNativeVrHandObjects()
        {
            _nativeVrPlayerControl = NativeLocalPlayer.vrPlayerControl;
            if (_nativeVrPlayerControl == null && LocalPlayer != null)
            {
                _nativeVrPlayerControl =
                    LocalPlayer.GetComponentInChildren<NativeVRPlayerControl>(true);
            }

            _nativeLeftHandController =
                _nativeVrPlayerControl != null
                    ? _nativeVrPlayerControl.leftHandControllerGo
                    : null;
            _nativeRightHandController =
                _nativeVrPlayerControl != null
                    ? _nativeVrPlayerControl.RightHandControllerGo
                    : null;
            _nativeLeftHandRenderers =
                _nativeLeftHandController != null
                    ? _nativeLeftHandController.GetComponentsInChildren<Renderer>(true)
                    : new Renderer[0];
            _nativeRightHandRenderers =
                _nativeRightHandController != null
                    ? _nativeRightHandController.GetComponentsInChildren<Renderer>(true)
                    : new Renderer[0];

            MelonLogger.Msg(
                "[VR Hands] Native VRPlayerControl=" +
                (_nativeVrPlayerControl != null ? "found" : "missing") +
                $", leftController='{GetObjectName(_nativeLeftHandController)}' " +
                $"({_nativeLeftHandRenderers.Length} renderers), " +
                $"rightController='{GetObjectName(_nativeRightHandController)}' " +
                $"({_nativeRightHandRenderers.Length} renderers).");
        }

        private static string GetObjectName(GameObject target)
        {
            return target != null ? target.name : "<null>";
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
                return "<null>";

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private Quaternion GetLevelYawRotation(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private Quaternion GetComfortCameraRotation(Vector3 forward)
        {
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;

            forward.Normalize();
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.999f)
                up = Body != null ? Body.up : Vector3.forward;

            // Preserve the cinematic camera's authored aim while stripping roll,
            // which is uncomfortable in a head-tracked view.
            return Quaternion.LookRotation(forward, up);
        }

        private void CreateMenuImage(string name, Transform parent, Color color, Vector2 size, Vector2 position)
        {
            GameObject imageObject = new GameObject(name);
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void CreateMenuText(string name, string value, Transform parent, int size, Vector2 position)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(650f, 80f);
            rect.anchoredPosition = position;
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private void SetMenuButtonHighlighted(GameObject target, bool highlighted)
        {
            UnityEngine.UI.Button button = target?.GetComponentInParent<UnityEngine.UI.Button>();
            if (button != null)
                button.image.color = highlighted ? new Color(0.2f, 0.55f, 0.8f, 1f) : new Color(0.12f, 0.18f, 0.25f, 1f);
        }

        private void SetInGameMenuVisible(bool visible)
        {
            if (visible)
                EnsureInGameMenuResources();
            if (_inGameMenu != null && _inGameMenu.activeSelf != visible)
                _inGameMenu.SetActive(visible);
            if (_inGameMenuLaser != null)
                _inGameMenuLaser.SetActive(visible);
        }

        private void UpdateInGameMenuPose()
        {
            if (_inGameMenuCanvas == null || StereoRender?.Head == null)
                return;

            _inGameMenuCanvas.transform.SetParent(StereoRender.Head, false);
            _inGameMenuCanvas.transform.localPosition = new Vector3(0f, 0f, InGameMenuDistance);
            _inGameMenuCanvas.transform.localRotation = Quaternion.identity;
            _inGameMenuCanvas.transform.localScale = Vector3.one * 0.001f;
        }

        private void DestroyInGameMenuResources()
        {
            if (_inGameMenu != null) Destroy(_inGameMenu);
            if (_inGameMenuLaser != null) Destroy(_inGameMenuLaser);
            if (_inGameMenuLaserMaterial != null) Destroy(_inGameMenuLaserMaterial);
            _inGameMenu = null;
            _inGameMenuLaser = null;
            _inGameMenuLaserMaterial = null;
            _inGameMenuRaycaster = null;
        }

        /// <summary>
        /// Resets the VR origin to a default home position and rotation.
        /// Currently unused but kept for debugging or comfort features.
        /// </summary>
        private void SetOriginHome()
        {
            SetOriginPosRotScl(new Vector3(0f, 0f, 0f), new Vector3(0, 90, 0), new Vector3(1, 1, 1));
        }

        /// <summary>
        /// Sets the position, rotation (as Euler angles), and scale of the VR origin.
        /// </summary>
        public void SetOriginPosRotScl(Vector3 pos, Vector3 euler, Vector3 scale)
        {
            Origin.position = pos;
            Origin.localEulerAngles = euler;
            Origin.localScale = scale;
        }

        /// <summary>
        /// Sets a uniform scale on the VR origin (useful for comfort/height adjustment).
        /// </summary>
        public void SetOriginScale(float scale)
        {
            Origin.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>
        /// Returns the current forward direction of the HMD (including snap-turn rotation).
        /// </summary>
        public Vector3 GetWorldForward()
        {
            return StereoRender?.Head?.forward ?? Vector3.forward;
        }

        /// <summary>
        /// Flattens a direction vector onto the horizontal plane (Y = 0) and normalizes it.
        /// </summary>
        public Vector3 GetFlatForwardDirection(Vector3 forward)
        {
            forward.y = 0;
            return forward.normalized;
        }

        /// <summary>
        /// Returns the current eye height of the VR user (approximate player height).
        /// </summary>
        public float GetPlayerHeight()
        {
            if (StereoRender?.Head == null)
                return 1.8f; // Default human height

            return Mathf.Abs(StereoRender.Head.localPosition.y);
        }

        /// <summary>
        /// Called when the component is enabled.
        /// </summary>
        private void OnEnable()
        {
            MelonLogger.Msg("[VRPlayer] VRPlayer ENABLED");
        }

        /// <summary>
        /// Called when the component is disabled.
        /// </summary>
        private void OnDisable()
        {
            MelonLogger.Msg("[VRPlayer] VRPlayer DISABLED");
        }

        private bool CacheOriginalRendererStates()
        {
            if (LocalPlayer == null)
            {
                return false;
            }

            _originalRendererStates = new Il2CppSystem.Collections.Generic.Dictionary<Renderer, bool>();

            var renderers = LocalPlayer.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null)
                    _originalRendererStates[r] = r.enabled;
            }

            return true;
        }

        private void HidePlayerRenderers()
        {
            bool Cached = true;
            if (_originalRendererStates == null)
            {
                Cached = false;
                Cached = CacheOriginalRendererStates();
            }

            if (Cached == true)
            {
                foreach (var kvp in _originalRendererStates)
                {
                    if (kvp.Key != null)
                        kvp.Key.enabled = false;
                }
            }
        }

        private void RestorePlayerRenderers()
        {
            if (_originalRendererStates == null)
            {
                return;
            }

            foreach (var kvp in _originalRendererStates)
            {
                if (kvp.Key != null)
                    kvp.Key.enabled = kvp.Value;
            }
        }




        private void FindInventoryCursorController()
        {
            if (LocalPlayer == null || _cursorController != null) return;
            _cursorController = LocalPlayer.GetComponentInChildren<InventoryCursorController>(true);
            if (_cursorController != null)
            {
                MelonLogger.Msg("[VRPlayer] InventoryCursorController trovato sotto LocalPlayer!");
                _cursorController._cursorMouseSpeed = 0.0f;
                _cursorController._cursorGamepadSpeed = 0.0f;
                _cursorController.ResetCursorPosition();
                if (_cursorController.transform.parent != null)
                {
                    _inventorySystem = _cursorController.transform.parent.gameObject;
                    Transform fakeGround = _inventorySystem.transform.Find("FakeGround");
                    _FakeGround = fakeGround != null ? fakeGround.gameObject : null;
                }
            }
        }
        
        private void FindSoftwareCursor()
        {
            if(_softwareCursor != null) {return;}
            GameObject SoftwareCursorTransform = GameObject.Find("PlayerStandin/PlayerUI/ScreenSpaceCanvas/SoftwareCursor");
            if (SoftwareCursorTransform != null)
            {
                SetLayerRecursively(
                    SoftwareCursorTransform,
                    TitleUiPanelLayer);
                _softwareCursor = SoftwareCursorTransform.gameObject;
                MelonLogger.Msg("[VRPlayer] SoftwareCursor trovato nella scena!");
            }
        }

        private void UpdateCursorPositionFromGaze()
        {
            if (_cursorController == null || StereoRender.Head == null) return;

            Vector3 desiredPosition = StereoRender.Head.position + StereoRender.Head.forward * 1.5f;
            Quaternion desiredRotation = Quaternion.LookRotation(StereoRender.Head.forward, Vector3.up);
            _cursorController.gameObject.transform.SetPositionAndRotation(desiredPosition, desiredRotation);
            
        }

        private bool IsTitleScene()
        {
            return UsedSceneName == "SonsTitleScene" ||
                   SceneManager.GetActiveScene().name == "SonsTitleScene";
        }

        private bool IsPauseUiActive()
        {
            return !IsTitleScene() && Time.timeScale <= 0.001f;
        }

        private bool IsMirroredGameUiActive()
        {
            return IsTitleScene() || IsPauseUiActive();
        }

        // Input routing must remain title-only. Pause UI is mirrored by
        // IsMirroredGameUiActive(), but it still needs normal gameplay
        // navigation and Escape/cancel handling.
        public bool IsTitleUiScene => IsTitleScene();
        public bool IsMirroredUiActive => IsMirroredGameUiActive();

        public float EffectiveYawOffset =>
            yawOffset + (IsTitleScene() ? TitleViewYawOffset : 0f);

        private IEnumerator CaptureTitleUiLoop()
        {
            WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

            while (Instance == this)
            {
                yield return endOfFrame;

                if (!IsTitleScene() || StereoRender?.Head == null)
                {
                    SetTitleUiVisible(false);
                    continue;
                }

                EnsureTitleUiResources();
                if (_titleUiTexture == null)
                    continue;

                ScreenCapture.CaptureScreenshotIntoRenderTexture(_titleUiTexture);
                _titleUiCaptureStarted = true;
                UpdateTitleUiPanelPose();
                SetTitleUiVisible(true);
            }
        }

        private void EnsureTitleUiResources()
        {
            int width = Mathf.Max(Screen.width, 1);
            int height = Mathf.Max(Screen.height, 1);
            if (_titleUiTexture == null ||
                _titleUiTexture.width != width ||
                _titleUiTexture.height != height)
            {
                if (_titleUiTexture != null)
                {
                    _titleUiTexture.Release();
                    Destroy(_titleUiTexture);
                }

                _titleUiTexture = new RenderTexture(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32)
                {
                    name = "[VR UI] Title Capture",
                    useMipMap = false,
                    autoGenerateMips = false
                };
                _titleUiTexture.Create();
                _titleUiCaptureStarted = false;

                if (_titleUiPanelMaterial != null)
                    _titleUiPanelMaterial.mainTexture = _titleUiTexture;
            }

            if (_titleUiPanel == null)
            {
                _titleUiPanel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _titleUiPanel.name = "[VR UI] Title Panel";
                _titleUiPanel.layer = TitleUiPanelLayer;
                _titleUiPanel.transform.SetParent(StereoRender.Head, false);
                _titleUiPanel.transform.localPosition =
                    new Vector3(0f, TitleUiPanelVerticalOffset, TitleUiPanelDistance);
                _titleUiPanel.transform.localRotation = Quaternion.identity;

                float aspect = (float)width / height;
                _titleUiPanel.transform.localScale =
                    new Vector3(TitleUiPanelHeight * aspect, TitleUiPanelHeight, 1f);

                _titleUiPanelRenderer = _titleUiPanel.GetComponent<Renderer>();
                _titleUiPanelCollider = _titleUiPanel.GetComponent<Collider>();
                FlipTitleUiPanelUvs();

                Shader panelShader = Shader.Find("Unlit/Texture") ??
                                     Shader.Find("Sprites/Default");
                if (panelShader != null)
                {
                    _titleUiPanelMaterial = new Material(panelShader)
                    {
                        name = "[VR UI] Title Panel Material",
                        mainTexture = _titleUiTexture,
                        color = Color.white
                    };
                    _titleUiPanelRenderer.material = _titleUiPanelMaterial;
                }

                CreateTitleUiPointerVisuals();
                MelonLogger.Msg(
                    "[VR UI] Original desktop title menu is now mirrored to a VR panel.");
            }
            else
            {
                float aspect = (float)width / height;
                _titleUiPanel.transform.localScale =
                    new Vector3(TitleUiPanelHeight * aspect, TitleUiPanelHeight, 1f);
            }

            if (UsedCamera != null)
                UsedCamera.cullingMask &= ~(1 << TitleUiPanelLayer);
            if (StereoRender.LeftCam != null)
                StereoRender.LeftCam.cullingMask |= 1 << TitleUiPanelLayer;
            if (StereoRender.RightCam != null)
                StereoRender.RightCam.cullingMask |= 1 << TitleUiPanelLayer;
        }

        private void UpdateTitleUiPanelPose()
        {
            if (_titleUiPanel == null || StereoRender?.Head == null)
                return;

            Vector3 viewForward = StereoRender.Head.forward;
            if (viewForward.sqrMagnitude < 0.001f)
                return;

            viewForward.Normalize();
            Vector3 panelPosition =
                StereoRender.Head.position +
                viewForward * TitleUiPanelDistance +
                Vector3.up * TitleUiPanelVerticalOffset;
            Quaternion panelRotation =
                Quaternion.LookRotation(viewForward, Vector3.up);
            _titleUiPanel.transform.SetPositionAndRotation(
                panelPosition,
                panelRotation);
        }

        private void FlipTitleUiPanelUvs()
        {
            MeshFilter meshFilter = _titleUiPanel != null
                ? _titleUiPanel.GetComponent<MeshFilter>()
                : null;
            if (meshFilter == null || meshFilter.mesh == null)
                return;

            Mesh mesh = meshFilter.mesh;
            Vector2[] uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i].y = 1f - uvs[i].y;
            }
            mesh.uv = uvs;

            MeshCollider meshCollider = _titleUiPanel.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
            }
        }

        private void CreateTitleUiPointerVisuals()
        {
            Shader pointerShader = Shader.Find("Sprites/Default") ??
                                   Shader.Find("Unlit/Color");
            if (pointerShader != null)
            {
                _titleUiLaserMaterial = new Material(pointerShader)
                {
                    name = "[VR UI] Pointer Material",
                    color = new Color(1f, 0.08f, 0.08f, 0.9f)
                };
            }

            _titleUiLaser = new GameObject("[VR UI] Controller Pointer");
            _titleUiLaser.layer = TitleUiPanelLayer;
            _titleUiLaser.transform.SetParent(StereoRender.Head, false);
            _titleUiLaserRenderer = _titleUiLaser.AddComponent<LineRenderer>();
            _titleUiLaserRenderer.positionCount = 2;
            _titleUiLaserRenderer.startWidth = 0.004f;
            _titleUiLaserRenderer.endWidth = 0.002f;
            _titleUiLaserRenderer.useWorldSpace = true;
            if (_titleUiLaserMaterial != null)
                _titleUiLaserRenderer.material = _titleUiLaserMaterial;

            _titleUiCursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _titleUiCursor.name = "[VR UI] Pointer Cursor";
            _titleUiCursor.layer = TitleUiPanelLayer;
            _titleUiCursor.transform.SetParent(StereoRender.Head, false);
            _titleUiCursor.transform.localScale = Vector3.one * 0.018f;
            Collider cursorCollider = _titleUiCursor.GetComponent<Collider>();
            if (cursorCollider != null)
                cursorCollider.enabled = false;
            Renderer cursorRenderer = _titleUiCursor.GetComponent<Renderer>();
            if (cursorRenderer != null && _titleUiLaserMaterial != null)
                cursorRenderer.material = _titleUiLaserMaterial;

            _titleUiLaser.SetActive(false);
            _titleUiCursor.SetActive(false);
        }

        public bool TryGetTitleUiPointer(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            if (!IsTitleScene() ||
                !_titleUiCaptureStarted ||
                _titleUiPanelCollider == null ||
                StereoRender == null ||
                !StereoRender.HasValidHmdTrackingPose)
            {
                SetTitleUiPointerVisuals(false, Vector3.zero, Vector3.zero, false);
                return false;
            }

            Pose controllerPose = NativeVRInput.GetPose("PoseRight");
            if (controllerPose.position.sqrMagnitude < 0.000001f &&
                controllerPose.rotation == Quaternion.identity)
            {
                SetTitleUiPointerVisuals(false, Vector3.zero, Vector3.zero, false);
                return false;
            }

            Quaternion trackingToWorld =
                StereoRender.Head.rotation *
                Quaternion.Inverse(StereoRender.HmdTrackingRotation);
            Vector3 controllerRelativeToHmd =
                controllerPose.position - StereoRender.HmdTrackingPosition;
            Vector3 rayOrigin =
                StereoRender.Head.position +
                trackingToWorld * controllerRelativeToHmd;
            Vector3 rayDirection =
                trackingToWorld *
                (controllerPose.rotation * Vector3.forward);
            rayDirection.Normalize();
            Ray ray = new Ray(rayOrigin, rayDirection);

            RaycastHit hit;
            bool panelHit = _titleUiPanelCollider.Raycast(ray, out hit, 4f);
            Vector3 rayEnd = panelHit ? hit.point : rayOrigin + rayDirection * 2.5f;
            SetTitleUiPointerVisuals(true, rayOrigin, rayEnd, panelHit);
            if (!panelHit)
                return false;

            Vector2 uv = hit.textureCoord;
            screenPosition = new Vector2(
                uv.x * Screen.width,
                (1f - uv.y) * Screen.height);
            return true;
        }

        private void SetTitleUiPointerVisuals(
            bool laserVisible,
            Vector3 rayOrigin,
            Vector3 rayEnd,
            bool cursorVisible)
        {
            if (_titleUiLaser != null)
                _titleUiLaser.SetActive(laserVisible && IsTitleScene());
            if (_titleUiLaserRenderer != null && laserVisible)
            {
                Vector3 rayVector = rayEnd - rayOrigin;
                float rayLength = rayVector.magnitude;
                Vector3 visualStart = rayOrigin;
                if (rayLength > 0.001f)
                {
                    float startOffset = Mathf.Min(0.15f, rayLength * 0.25f);
                    visualStart += rayVector / rayLength * startOffset;
                }

                _titleUiLaserRenderer.SetPosition(0, visualStart);
                _titleUiLaserRenderer.SetPosition(1, rayEnd);
            }

            if (_titleUiCursor != null)
            {
                _titleUiCursor.SetActive(cursorVisible && IsTitleScene());
                if (cursorVisible)
                    _titleUiCursor.transform.position = rayEnd;
            }
        }

        private void SetTitleUiVisible(bool visible)
        {
            bool showPanel = visible && _titleUiCaptureStarted;
            if (_titleUiPanel != null && _titleUiPanel.activeSelf != showPanel)
                _titleUiPanel.SetActive(showPanel);

            if (!showPanel)
                SetTitleUiPointerVisuals(false, Vector3.zero, Vector3.zero, false);
        }

        private void DestroyTitleUiResources()
        {
            if (_titleUiPanel != null)
                Destroy(_titleUiPanel);
            if (_titleUiLaser != null)
                Destroy(_titleUiLaser);
            if (_titleUiCursor != null)
                Destroy(_titleUiCursor);
            if (_titleUiPanelMaterial != null)
                Destroy(_titleUiPanelMaterial);
            if (_titleUiLaserMaterial != null)
                Destroy(_titleUiLaserMaterial);
            if (_titleUiTexture != null)
            {
                _titleUiTexture.Release();
                Destroy(_titleUiTexture);
            }

            _titleUiPanel = null;
            _titleUiLaser = null;
            _titleUiCursor = null;
            _titleUiTexture = null;
        }


        private void ConfigureCanvasesForVR()
        {
            if (UsedCamera == null || Time.unscaledTime < _nextCanvasScanTime)
                return;

            _nextCanvasScanTime = Time.unscaledTime + 0.5f;

            ConfigureKnownCanvasForVR(_screenSpaceCanvas);
            ConfigureKnownCanvasForVR(_playerMiniMapCanvas);
        }

        private void ConfigureKnownCanvasForVR(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay ||
                canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                ConvertCanvasToWorldSpace(canvas);
                TrackVrCanvas(canvas);
                MelonLogger.Msg(
                    $"[VR UI] Converted known canvas '{canvas.gameObject.name}' for VR.");
            }
            else if (IsTrackedVrCanvas(canvas))
            {
                canvas.worldCamera = StereoRender?.RightCam ?? UsedCamera;
            }
        }

        private void TrackVrCanvas(Canvas canvas)
        {
            if (canvas == null || IsTrackedVrCanvas(canvas))
                return;

            Canvas[] expanded = new Canvas[_vrCanvases.Length + 1];
            Array.Copy(_vrCanvases, expanded, _vrCanvases.Length);
            expanded[expanded.Length - 1] = canvas;
            _vrCanvases = expanded;
        }

        private bool IsTrackedVrCanvas(Canvas canvas)
        {
            for (int i = 0; i < _vrCanvases.Length; i++)
            {
                if (_vrCanvases[i] == canvas)
                    return true;
            }

            return false;
        }

        private void SetLayerRecursively(GameObject target, int layer)
        {
            if (target == null)
                return;

            target.layer = layer;
            for (int i = 0; i < target.transform.childCount; i++)
            {
                SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
            }
        }

        private void FindInventoryCanvas()
        {
            if (_screenSpaceCanvas != null) return;

            GameObject playerStandin = GameObject.Find("PlayerStandin");
            if (playerStandin == null)
            {
                return;
            }

            Transform canvasTransform = playerStandin.transform.Find("PlayerUI/ScreenSpaceCanvas");
            if (canvasTransform != null)
            {
                canvasTransform.localScale = Vector3.one;
                _screenSpaceCanvas = canvasTransform.GetComponent<Canvas>();
                CaptureScreenCanvasOriginalState();
            }
        }

        private void CaptureScreenCanvasOriginalState()
        {
            if (_screenSpaceCanvas == null || _screenCanvasOriginalState != null)
                return;

            CanvasScaler scaler = _screenSpaceCanvas.GetComponent<CanvasScaler>();
            _screenCanvasOriginalState = new ScreenCanvasState
            {
                RenderMode = _screenSpaceCanvas.renderMode,
                WorldCamera = _screenSpaceCanvas.worldCamera,
                Parent = _screenSpaceCanvas.transform.parent,
                LocalPosition = _screenSpaceCanvas.transform.localPosition,
                LocalRotation = _screenSpaceCanvas.transform.localRotation,
                LocalScale = _screenSpaceCanvas.transform.localScale,
                HasScaler = scaler != null,
                ScalerEnabled = scaler != null && scaler.enabled
            };
        }

        private void RestoreScreenCanvasForDesktopOverlay()
        {
            if (_screenSpaceCanvas == null || _screenCanvasOriginalState == null)
                return;

            Transform canvasTransform = _screenSpaceCanvas.transform;
            canvasTransform.SetParent(_screenCanvasOriginalState.Parent, false);
            canvasTransform.localPosition = _screenCanvasOriginalState.LocalPosition;
            canvasTransform.localRotation = _screenCanvasOriginalState.LocalRotation;
            canvasTransform.localScale = _screenCanvasOriginalState.LocalScale;
            _screenSpaceCanvas.renderMode = _screenCanvasOriginalState.RenderMode;
            _screenSpaceCanvas.worldCamera = _screenCanvasOriginalState.WorldCamera;

            CanvasScaler scaler = _screenSpaceCanvas.GetComponent<CanvasScaler>();
            if (_screenCanvasOriginalState.HasScaler && scaler != null)
                scaler.enabled = _screenCanvasOriginalState.ScalerEnabled;
        }


        private void FindMiniMapCanvas()
        {
            if (_playerMiniMapCanvas != null) return;

            GameObject playerStandin = GameObject.Find("PlayerStandin");
            if (playerStandin == null)
            {
                return;
            }

            _PlayerMiniMapTransf = playerStandin.transform.Find("PlayerUI/PlayerMiniMap");

            Transform minimapcanvasTransform =
                playerStandin.transform.Find("PlayerUI/PlayerMiniMap/GPSTrackerSystem/Canvas");
            if (minimapcanvasTransform != null)
            {
                _playerMiniMapCanvas = minimapcanvasTransform.GetComponent<Canvas>();
            }

            Transform gpsTransform = playerStandin.transform.Find("PlayerUI/PlayerMiniMap/GPSTrackerSystem");
            if (gpsTransform != null)
            {
                gpsTransform.localScale = Vector3.one;
            }
        }

        private void ConvertCanvasToWorldSpace(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                canvas.renderMode = RenderMode.WorldSpace;

                RectTransform rectTransform = canvas.GetComponent<RectTransform>();
                float canvasHeight = rectTransform != null ? rectTransform.rect.height : Screen.height;
                if (canvasHeight <= 1f)
                    canvasHeight = Screen.height;

                canvas.transform.localScale = Vector3.one * (0.8f / canvasHeight);

                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                    scaler.enabled = false;
            }

            canvas.worldCamera = StereoRender?.RightCam ?? UsedCamera;

            int uiLayer = TitleUiPanelLayer;
            SetLayerRecursively(canvas.gameObject, uiLayer);
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            if (UsedCamera != null && uiLayer >= 0)
                UsedCamera.cullingMask |= 1 << uiLayer;
            if (uiLayer >= 0 && StereoRender != null)
            {
                if (StereoRender.LeftCam != null)
                    StereoRender.LeftCam.cullingMask |= 1 << uiLayer;
                if (StereoRender.RightCam != null)
                    StereoRender.RightCam.cullingMask |= 1 << uiLayer;
            }
        }

        private void UpdateCanvasPositionForVR()
        {
            if (StereoRender.Head == null) return;

            Vector3 viewForward = StereoRender.Head.forward;
            if (viewForward.sqrMagnitude < 0.0001f)
                return;

            viewForward.Normalize();
            Vector3 headUp = Vector3.up;
            Vector3 flatForward = viewForward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Body != null ? Body.forward : Vector3.forward;
            flatForward.Normalize();
            Vector3 headRight = Vector3.Cross(headUp, flatForward).normalized;
            Vector3 desiredPosition =
                StereoRender.Head.position +
                viewForward * 1.2f +
                Vector3.up * 0.1f;

            if (_screenSpaceCanvas != null)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(viewForward, headUp);
                Camera uiCamera = StereoRender.RightCam ?? UsedCamera;
                if (_screenSpaceCanvas.worldCamera != uiCamera)
                    _screenSpaceCanvas.worldCamera = uiCamera;

                _screenSpaceCanvas.transform.SetPositionAndRotation(desiredPosition, desiredRotation);
            }

            if (_playerMiniMapCanvas != null && _PlayerMiniMapTransf != null)
            {
                float MinimapOffsetRight = 0.335f; // + = Right, - = Left
                float MinimapOffsetDown = 0.21f;

                Vector3 offset =
                    headRight * MinimapOffsetRight +
                    (-headUp) * MinimapOffsetDown;

                Vector3 finalPosition = desiredPosition + offset;

                Quaternion minidesiredRotation = Quaternion.LookRotation(viewForward, headUp);
                Camera uiCamera = StereoRender.RightCam ?? UsedCamera;
                if (_playerMiniMapCanvas.worldCamera != uiCamera)
                    _playerMiniMapCanvas.worldCamera = uiCamera;

                _playerMiniMapCanvas.transform.SetPositionAndRotation(finalPosition, minidesiredRotation);
            }

            Quaternion canvasRotation = Quaternion.LookRotation(viewForward, headUp);
            for (int i = 0; i < _vrCanvases.Length; i++)
            {
                Canvas canvas = _vrCanvases[i];
                if (canvas == null || canvas == _screenSpaceCanvas || canvas == _playerMiniMapCanvas)
                    continue;

                canvas.worldCamera = StereoRender.RightCam ?? UsedCamera;
                int uiLayer = TitleUiPanelLayer;
                if (uiLayer >= 0)
                {
                    SetLayerRecursively(canvas.gameObject, uiLayer);
                    if (UsedCamera != null)
                        UsedCamera.cullingMask |= 1 << uiLayer;
                    if (StereoRender.LeftCam != null)
                        StereoRender.LeftCam.cullingMask |= 1 << uiLayer;
                    if (StereoRender.RightCam != null)
                        StereoRender.RightCam.cullingMask |= 1 << uiLayer;
                }
                canvas.transform.SetPositionAndRotation(desiredPosition, canvasRotation);
            }
        }

        

        private void RestoreInventoryState()
        {
            RestoreInventoryEyeCameraSettings();

            if (_bodyRotationCapturedForInventory && Body != null)
            {
                Body.rotation = _bodyRotationBeforeInventory;
                _bodyRotationCapturedForInventory = false;
            }

            if (_FakeGround != null)
                _FakeGround.active = true;

            if (_layoutCursorSnapping != null && _layoutCursorDisabledByVr)
            {
                _layoutCursorSnapping.enabled = true;
                _layoutCursorDisabledByVr = false;
            }

            if (_virtualMouseInput != null && _virtualMouseDisabledByVr)
            {
                _virtualMouseInput.enabled = true;
                _virtualMouseDisabledByVr = false;
            }

            if (_inputCursorState != null && _inputCursorStateEnabledByVr)
            {
                _inputCursorState.enabled = false;
                _inputCursorState.gameObject.SetActive(false);
                _inputCursorStateEnabledByVr = false;
            }

            if (_cursorController != null && _cursorControllerDisabledByVr)
            {
                _cursorController.enabled = true;
                _cursorControllerDisabledByVr = false;
            }

            if (_softwareCursor != null)
                _softwareCursor.active = false;

            _cursorDisableTimerStarted = false;
            _cursorDisableStartTime = 0f;
        }

        private void CaptureInventoryEyeCameraSettings()
        {
            if (StereoRender == null)
                return;

            Camera left = StereoRender.LeftCam;
            if (!_leftInventoryEyeSettingsCaptured && left != null)
            {
                _leftInventoryEyeCullingMask = left.cullingMask;
                _leftInventoryEyeClearFlags = left.clearFlags;
                _leftInventoryEyeBackgroundColor = left.backgroundColor;
                _leftInventoryEyeUseOcclusionCulling = left.useOcclusionCulling;
                _leftInventoryEyeSettingsCaptured = true;
            }

            Camera right = StereoRender.RightCam;
            if (!_rightInventoryEyeSettingsCaptured && right != null)
            {
                _rightInventoryEyeCullingMask = right.cullingMask;
                _rightInventoryEyeClearFlags = right.clearFlags;
                _rightInventoryEyeBackgroundColor = right.backgroundColor;
                _rightInventoryEyeUseOcclusionCulling = right.useOcclusionCulling;
                _rightInventoryEyeSettingsCaptured = true;
            }
        }

        private void ApplyInventoryEyeCameraSettings(Camera source)
        {
            if (source == null || StereoRender == null)
                return;

            ApplyInventoryEyeCameraSettings(StereoRender.LeftCam, source);
            ApplyInventoryEyeCameraSettings(StereoRender.RightCam, source);
        }

        private void ApplyInventoryEyeCameraSettings(Camera target, Camera source)
        {
            if (target == null)
                return;

            target.cullingMask =
                source.cullingMask | (1 << TitleUiPanelLayer);
            target.clearFlags = source.clearFlags;
            target.backgroundColor = source.backgroundColor;
            target.useOcclusionCulling = source.useOcclusionCulling;
        }

        private void RestoreInventoryEyeCameraSettings()
        {
            if (StereoRender != null)
            {
                Camera left = StereoRender.LeftCam;
                if (_leftInventoryEyeSettingsCaptured && left != null)
                {
                    left.cullingMask = _leftInventoryEyeCullingMask;
                    left.clearFlags = _leftInventoryEyeClearFlags;
                    left.backgroundColor = _leftInventoryEyeBackgroundColor;
                    left.useOcclusionCulling =
                        _leftInventoryEyeUseOcclusionCulling;
                }

                Camera right = StereoRender.RightCam;
                if (_rightInventoryEyeSettingsCaptured && right != null)
                {
                    right.cullingMask = _rightInventoryEyeCullingMask;
                    right.clearFlags = _rightInventoryEyeClearFlags;
                    right.backgroundColor = _rightInventoryEyeBackgroundColor;
                    right.useOcclusionCulling =
                        _rightInventoryEyeUseOcclusionCulling;
                }
            }

            _leftInventoryEyeSettingsCaptured = false;
            _rightInventoryEyeSettingsCaptured = false;
        }

        void FixInventoryCursorController()
        {
            if (_cursorController != null)
            {
                if (_layoutCursorSnapping == null)
                {
                    _layoutCursorSnapping = _cursorController.GetComponent<LayoutItemCursorSnapping>();
                }
                if (_virtualMouseInput == null)
                {
                    _virtualMouseInput = _cursorController.GetComponent<VirtualMouseInput>();
                }
                if (_inputCursorState == null)
                {
                    Transform inputCursorState = _cursorController.transform.Find("InventoryCursorState");
                    if (inputCursorState != null)
                        _inputCursorState = inputCursorState.GetComponent<InputCursorState>();
                }
                
                
                // Preserve the game's native cursor, snapping, and virtual
                // mouse components. VR only supplies a controller ray; the
                // inventory remains responsible for its own interaction state.
                if (!_cursorController.enabled)
                    _cursorController.enabled = true;
                if (_layoutCursorSnapping != null && !_layoutCursorSnapping.enabled)
                    _layoutCursorSnapping.enabled = true;
                if (_virtualMouseInput != null && !_virtualMouseInput.enabled)
                    _virtualMouseInput.enabled = true;
            }
        }
    }
}
