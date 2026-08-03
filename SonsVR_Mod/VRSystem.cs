using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppEndnight.Utilities;

namespace SonsVRMod
{
    /// <summary>
    /// Central manager responsible for detecting the active gameplay scene and camera,
    /// and spawning/refreshing the VR player rig when needed.
    /// Acts as the bridge between the game's dynamic scene/camera system and the VR subsystem.
    /// </summary>
    internal class VRSystem : MonoBehaviour
    {
        public VRSystem(IntPtr value) : base(value) { }

        /// <summary>
        /// Singleton instance for global access.
        /// </summary>
        public static VRSystem Instance { get; private set; }

        // Frame counter to limit how often we scan for scene/camera changes (performance optimization)
        private int UpdateCounter = 0;

        /// <summary>
        /// Reference to the currently active in-game camera (e.g., MainCameraFP).
        /// Used as the positional source for the VR rig.
        /// </summary>
        private Camera UsedCamera = null;

        /// <summary>
        /// Name of the scene currently being used for gameplay.
        /// Helps detect scene transitions (e.g., loading screen → main world).
        /// </summary>
        private string UsedSceneName = "";

        /// <summary>
        /// Flag indicating whether the player rig needs to be respawned after a scene change.
        /// Currently unused but reserved for future logic.
        /// </summary>
        private bool RespawnPlayer = false;

        /// <summary>
        /// Indicates whether the VR camera rig has already been created.
        /// Prevents duplicate rig instantiation.
        /// </summary>
        private bool RigCreated = false;

        /// <summary>
        /// Unity callback called when the object is initialized.
        /// Sets up singleton, ensures persistence across scenes, and subscribes to scene events.
        /// </summary>
        private void Awake()
        {
            MelonLogger.Msg("########### INITIALIZING VR SYSTEM #############");

            // Enforce singleton pattern
            if (Instance != null)
            {
                MelonLogger.Error("Trying to create duplicate VRSystem instance! Disabling this one.");
                enabled = false;
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject); // Survive scene transitions

            // Subscribe to scene load events (currently used for cleanup/extensibility)
            SonsVRMod.onSceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// Main logic loop, executed after all Update() calls.
        /// Periodically scans for the active gameplay scene and camera,
        /// and triggers VR rig creation or update when they change.
        /// </summary>
        private void LateUpdate()
        {
            UpdateCounter++;

            // Cinematic cameras can exist for only a handful of frames. A long
            // polling interval leaves the headset on the previous camera while
            // the desktop shows the actual shot.
            if (UpdateCounter >= 3)
            {
                UpdateCounter = 0;

                // Locate the most relevant active scene and its main camera
                var sceneAndCam = FindActiveGameplayScene();

                string sceneName = sceneAndCam.scene.IsValid()
                    ? sceneAndCam.scene.name
                    : string.Empty;
                bool hasValidScene = !string.IsNullOrEmpty(sceneName);

                // Only proceed in known gameplay scenes (avoid menus, loading screens, etc.)
                bool isUnityExplorerFreecam =
                    sceneAndCam.camera != null &&
                    sceneAndCam.camera.name.Equals("UE_Freecam");
                bool isSonsWorldCamera =
                    sceneAndCam.camera != null &&
                    sceneAndCam.camera.GetComponent<ActiveWorldCamera>() != null;
                bool isKnownScene =
                    sceneName == "BlankScene" ||
                    sceneName == "SonsMain" ||
                    sceneName == "SonsTitleScene";
                if (hasValidScene &&
                    (isKnownScene ||
                    isUnityExplorerFreecam ||
                    isSonsWorldCamera))
                {
                    var activeCam = sceneAndCam.camera;
                    if (activeCam != null)
                    {
                        SetLoadingBlackout(false);

                        // Detect scene or camera change
                        if (sceneName != UsedSceneName || UsedCamera == null || activeCam != UsedCamera)
                        {
                            MelonLogger.Warning($"[VRSystem] Scene or camera changed — respawning VR player rig");
                            UsedSceneName = sceneName;
                            UsedCamera = activeCam;

                            // Create the VR rig if not already present
                            if (!RigCreated)
                            {
                                CreateCameraRig(UsedCamera);
                            }
                            else if (VRPlayer.Instance != null)
                            {
                                VRPlayer.Instance.SetSceneAndCamera(sceneAndCam);
                            }
                        }
                        else
                        {
                            // Scene and camera are stable — just refresh the VR player's reference
                            if (VRPlayer.Instance != null && sceneAndCam.camera != null)
                            {
                                VRPlayer.Instance.SetSceneAndCamera(sceneAndCam);
                            }
                        }
                    }
                    else
                    {
                        SetLoadingBlackout(true);
                        MelonLogger.Msg($"[VRSystem] No active camera found in scene: {sceneName}");
                    }
                }
                else
                {
                    // A scene transition can temporarily return a camera with
                    // no valid scene. Keep submitting black until a supported
                    // camera-owning scene is available again.
                    SetLoadingBlackout(true);
                    UsedCamera = null;
                    UsedSceneName = string.Empty;
                }
            }
        }

        private void SetLoadingBlackout(bool active)
        {
            if (VRPlayer.Instance?.StereoRender != null)
                VRPlayer.Instance.StereoRender.SetLoadingBlackout(active);
        }

        /// <summary>
        /// Scans all loaded scenes to find the one containing an active gameplay camera.
        /// Prioritizes "MainCameraFP" (the game's standard first-person camera),
        /// but falls back to any enabled camera if not found.
        /// </summary>
        /// <returns>A struct containing the scene and its active camera (or null if none found).</returns>
        private ActiveSceneWithCamera FindActiveGameplayScene()
        {
            ActiveSceneWithCamera result = new ActiveSceneWithCamera();
            ActiveSceneWithCamera mainCameraResult = new ActiveSceneWithCamera();
            ActiveSceneWithCamera inventoryCameraResult = new ActiveSceneWithCamera();
            ActiveSceneWithCamera worldCameraResult = new ActiveSceneWithCamera();
            Camera desktopMainCamera = Camera.main;
            float bestWorldCameraScore = float.NegativeInfinity;

            // UnityExplorer creates FreeCam in DontDestroyOnLoad, which is not
            // included in SceneManager's normal loaded-scene enumeration.
            GameObject freecamObject = GameObject.Find("UE_Freecam");
            Camera freecam = freecamObject != null
                ? freecamObject.GetComponent<Camera>()
                : null;
            if (freecam != null && freecam.isActiveAndEnabled)
            {
                result.scene = SceneManager.GetActiveScene();
                result.camera = freecam;
                return result;
            }

            // Cinematic cameras are sometimes created under DontDestroyOnLoad
            // or other transient containers that SceneManager.sceneCount does
            // not enumerate. Inspect all loaded Camera components instead.
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || !cam.isActiveAndEnabled ||
                    cam.gameObject == null || !cam.gameObject.activeInHierarchy)
                    continue;

                string cameraName = cam.name ?? string.Empty;
                string lowerName = cameraName.ToLowerInvariant();
                if (lowerName.Equals("vrlefteye") ||
                    lowerName.Equals("vrrighteye") ||
                    lowerName.Contains("minimap") ||
                    lowerName.Contains("reflection") ||
                    lowerName.Contains("probe") ||
                    lowerName.Contains("preview") ||
                    lowerName.Contains("unityexplorer"))
                {
                    continue;
                }

                // Cameras rendering to a texture are generally mirrors,
                // minimaps, or effects rather than the desktop world view.
                if (cam.targetTexture != null)
                    continue;

                Scene cameraScene = cam.gameObject.scene;
                Scene reportedScene =
                    cameraScene.IsValid() && cameraScene.name != "DontDestroyOnLoad"
                        ? cameraScene
                        : SceneManager.GetActiveScene();

                if (cameraName.Equals("InventoryCamera"))
                {
                    inventoryCameraResult.scene = reportedScene;
                    inventoryCameraResult.camera = cam;
                    continue;
                }

                if (cameraName.Equals("MainCameraFP"))
                {
                    mainCameraResult.scene = reportedScene;
                    mainCameraResult.camera = cam;
                }

                bool fullScreen =
                    cam.rect.width >= 0.95f && cam.rect.height >= 0.95f;
                bool activeWorldCamera =
                    cam.GetComponent<ActiveWorldCamera>() != null;
                if (!fullScreen && !activeWorldCamera && cam != desktopMainCamera)
                    continue;

                // Camera.depth determines render order. ActiveWorldCamera is
                // Sons' own marker for the current world view. Camera.main is
                // only a tie-breaker because cinematics may render through an
                // untagged camera while MainCameraFP remains tagged.
                float score = cam.depth * 1000f;
                if (activeWorldCamera)
                    score += 100f;
                if (fullScreen)
                    score += 10f;
                if (cam == desktopMainCamera)
                    score += 1f;
                if (cameraName.Equals("MainCameraFP"))
                    score -= 5f;

                if (score > bestWorldCameraScore)
                {
                    bestWorldCameraScore = score;
                    worldCameraResult.scene = reportedScene;
                    worldCameraResult.camera = cam;
                }
            }

            // A transient cinematic can overlap MainCameraFP while it renders a
            // short shot (for example, the attacker after the helicopter crash).
            // Prefer that active cinematic until it is disabled.
            if (inventoryCameraResult.camera != null)
                return inventoryCameraResult;
            if (worldCameraResult.camera != null)
                return worldCameraResult;
            if (mainCameraResult.camera != null)
                return mainCameraResult;

            return result;
        }

        /// <summary>
        /// Cleans up any pre-existing VR camera rig under this object to avoid duplicates.
        /// Looks for children named "[VRCameraRig]" and destroys them.
        /// </summary>
        private void CleanupExistingRigs()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "[VRCameraRig]")
                {
                    MelonLogger.Msg("Cleaning up old VRCameraRig");
                    GameObject.Destroy(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Creates a new VR player rig as a child of this object and links it to the provided camera.
        /// Only creates one rig (guarded by RigCreated flag).
        /// </summary>
        /// <param name="usedCamera">The in-game camera to use as positional reference.</param>
        public void CreateCameraRig(Camera usedCamera)
        {
            CleanupExistingRigs();

            if (VRPlayer.Instance == null)
            {
                MelonLogger.Warning($"[VRSystem] #### CREATING NEW VR CAMERA RIG ####");
                GameObject rig = new GameObject("[VRCameraRig]");
                rig.transform.SetParent(transform, false);
                rig.AddComponent<VRPlayer>();
                RigCreated = true;
            }
        }

        /// <summary>
        /// Callback triggered when a new scene finishes loading.
        /// Currently unused but kept for future scene-specific logic or cleanup.
        /// </summary>
        private void OnSceneLoaded(int buildIndex, string sceneName)
        {
            // Intentionally empty
        }

        /// <summary>
        /// Temporarily toggles rendering of the VR eye cameras by changing their culling mask.
        /// Useful for debugging or disabling VR view in UI contexts.
        /// </summary>
        /// <param name="toggle">If true, hides all layers (mask = 0); if false, restores default visibility.</param>
        private void TogglePlayerCam(bool toggle)
        {
            if (VRPlayer.Instance == null || VRPlayer.Instance.StereoRender == null)
                return;

            int mask = toggle ? 0 : StereoRender.defaultCullingMask;
            VRPlayer.Instance.StereoRender.LeftCam.cullingMask = mask;
            VRPlayer.Instance.StereoRender.RightCam.cullingMask = mask;
        }

        /// <summary>
        /// Cleanup when the object is destroyed.
        /// Unsubscribes from events to prevent memory leaks.
        /// </summary>
        private void OnDestroy()
        {
            SonsVRMod.onSceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Called when the component is enabled.
        /// </summary>
        private void OnEnable()
        {
            MelonLogger.Msg("[VRSystem] VRSystem ENABLED");
        }

        /// <summary>
        /// Called when the component is disabled.
        /// </summary>
        private void OnDisable()
        {
            MelonLogger.Msg("[VRSystem] VRSystem DISABLED");
        }
    }
}
