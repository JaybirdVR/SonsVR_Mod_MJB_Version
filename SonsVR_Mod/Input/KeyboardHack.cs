using MelonLoader;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SonsVRMod
{
    /// <summary>
    /// Emulates traditional keyboard/mouse input based on VR controller gestures.
    /// This allows VR hand movements to control in-game actions like movement, jumping, melee attacks, etc.
    /// </summary>
    public class KeyboardHack
    {
        // Jump-related state and constants
        private float _jumpCooldown = 0f;
        private const float JUMP_THRESHOLD = 1.5f;    // Sensitivity threshold for jump detection (m/s)
        private const float JUMP_DELAY = 0.8f;        // Minimum time (in seconds) between jump gestures

        // Melee attack state and constants
        private bool _isMeleeAttacking = false;
        private float _attackCooldown = 0f;
        private const float ATTACK_THRESHOLD = 3.5f;        // Hand speed required to trigger an attack (m/s)
        private const float RELEASE_THRESHOLD = 1.2f;       // Speed below which the attack button is released
        private const float MIN_ATTACK_INTERVAL = 0.2f;     // Minimum time between consecutive melee swings

        
        private bool _isGripPressed = false;
        private float _gripPressStartTime = 0f;
        private const float LongPressThreshold = 2f; // Long press duration in seconds

        public GameObject LocalPlayer;
        private bool _wasRightGripRightPressed = false;
        private bool _wasLeftGripRightPressed = false;
        private bool _wasBGripRightPressed = false;
        private Vector2 _lastUiStick = Vector2.zero;
        private bool _lastUiSubmit = false;
        private bool _lastUiCancel = false;
        private float _nextUiMoveTime = 0f;
        private Vector2 _uiPointerPosition = Vector2.zero;
        private Vector2 _lastUiPointerPosition = Vector2.zero;
        private GameObject _uiPointerTarget = null;
        private bool _uiPointerInitialized = false;
        private Component _singlePlayerMenuHelper = null;
        private MethodInfo _getActiveSelectionMethod = null;
        private MethodInfo _setNextActiveControlMethod = null;
        private float _nextSinglePlayerBridgeScanTime = 0f;
        private bool _singlePlayerBridgeWarningShown = false;
        private bool _singlePlayerBridgeOwnsUiInput = false;
        private Behaviour _suppressedSinglePlayerDialogHelper = null;
        private GameObject _singlePlayerDialog = null;
        private bool _uiStickCentered = false;
        private GameObject _highlightedUiSelection = null;
        private readonly List<Graphic> _highlightedUiText = new List<Graphic>();
        private readonly List<Color> _highlightedUiTextColors = new List<Color>();
        private bool _titleMenuSelectionInitialized = false;


        /// <summary>
        /// Main entry point called each frame to inject simulated keyboard and mouse input
        /// based on current VR controller states.
        /// </summary>
        public void InjectMovement()
        {
            // Retrieve reference to Unity's virtual keyboard device
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            
            // Create a synthetic StateEvent that mimics real keyboard input
            StateEvent.From(keyboard, out var eventPtr);
            
            
            // Read current thumbstick values from VR controllers
            UnityEngine.Vector2 lstick = NativeVRInput.GetVector2("ThumbstickLeft");
            UnityEngine.Vector2 rstick = NativeVRInput.GetVector2("ThumbstickRight");

            // Read grip and face button states
            bool leftGripPress = NativeVRInput.GetBoolean("GripLeft_Press");
            bool rightGripPress = NativeVRInput.GetBoolean("GripRight_Press");
            bool currentGripPressed = leftGripPress || rightGripPress;
            if (currentGripPressed && !_isGripPressed)
            {
                // Grip just pressed: start timer
                _isGripPressed = true;
                _gripPressStartTime = Time.time;
    
                // Emulate 'E' key press
                keyboard.eKey.WriteValueIntoEvent(1f, eventPtr);
            }
            else if (!currentGripPressed && _isGripPressed)
            {
                // Grip released
                _isGripPressed = false;
                float pressDuration = Time.time - _gripPressStartTime;

                if (pressDuration >= LongPressThreshold)
                {
                    // Long press: emulate a quick tap of 'G' (press + immediate release)
                    keyboard.gKey.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);

                    // Release 'G' immediately (simulate tap)
                    StateEvent.From(keyboard, out var releaseEvent);
                    keyboard.gKey.WriteValueIntoEvent(0f, releaseEvent);
                    InputSystem.QueueEvent(releaseEvent);
                }
                else
                {
                    // Short press: release 'E'
                    keyboard.eKey.WriteValueIntoEvent(0f, eventPtr);
                }
            }
            else if (currentGripPressed && _isGripPressed)
            {
                // Keep 'E' held down
                keyboard.eKey.WriteValueIntoEvent(1f, eventPtr);
            }
            
            bool YPress = NativeVRInput.GetBoolean("ButtonLeft_Y_Press");
            bool XPress = NativeVRInput.GetBoolean("ButtonLeft_X_Press");
            bool APress = NativeVRInput.GetBoolean("ButtonRight_A_Press");
            bool BPress = NativeVRInput.GetBoolean("ButtonRight_B_Press");
            bool rightTriggerPressed = NativeVRInput.GetBoolean("TriggerRight_Press") ||
                                       NativeVRInput.GetFloat("TriggerRight_Value") > 0.65f;
            bool leftTriggerPressed = NativeVRInput.GetBoolean("TriggerLeft_Press") ||
                                      NativeVRInput.GetFloat("TriggerLeft_Value") > 0.65f;

            VRPlayer.Instance?.UpdateInGameMenu(
                NativeVRInput.GetBoolean("ThumbstickRight_Press"),
                rightTriggerPressed);

            bool uiSubmit = APress || XPress || rightTriggerPressed;
            bool uiCancel = BPress || leftTriggerPressed;
            bool isTitleScene = VRPlayer.Instance != null
                ? VRPlayer.Instance.IsTitleUiScene
                : SceneManager.GetActiveScene().name == "SonsTitleScene";
            if (isTitleScene)
            {
                _singlePlayerBridgeOwnsUiInput = true;
                UpdateTitleUiPointer(uiSubmit, uiCancel);
            }
            else
            {
                _singlePlayerBridgeOwnsUiInput = UpdateSinglePlayerMenuBridge(
                    lstick,
                    uiSubmit,
                    uiCancel);
                if (!_singlePlayerBridgeOwnsUiInput)
                {
                    UpdateUiNavigation(lstick, uiSubmit, uiCancel);
                }
            }

            
            // Map left thumbstick to WASD movement
            keyboard.wKey.WriteValueIntoEvent(lstick.y > 0.1f ? 1f : 0f, eventPtr);
            keyboard.sKey.WriteValueIntoEvent(lstick.y < -0.1f ? 1f : 0f, eventPtr);
            keyboard.aKey.WriteValueIntoEvent(lstick.x < -0.1f ? 1f : 0f, eventPtr);
            keyboard.dKey.WriteValueIntoEvent(lstick.x > 0.1f ? 1f : 0f, eventPtr);
            
            // Map Y button (left controller) to 'I' (e.g., inventory)
            keyboard.iKey.WriteValueIntoEvent(YPress ? 1f : 0f, eventPtr);
            
            // Map B button (right controller) to 'L' (e.g., LIGHTER)
            keyboard.lKey.WriteValueIntoEvent(BPress ? 1f : 0f, eventPtr);
            
            // Map A button (right controller) to 'R' (e.g., RELOAD/ROTATE RIGHT)
            keyboard.rKey.WriteValueIntoEvent(APress ? 1f : 0f, eventPtr); // FIXED: was BPress

            
            
            // Map right thumbstick down to Left Ctrl (e.g., crouch)
            keyboard.leftCtrlKey.WriteValueIntoEvent(rstick.y < -0.3f ? 1f : 0f, eventPtr);

            // Provide keyboard navigation for menus and screen-space UI.
            // The left stick remains mapped to movement in gameplay, while
            // arrow keys allow Unity UI to navigate when a menu has focus.
            // The title menu is driven directly below; injecting held keys there
            // duplicates moves and lets one submit bleed into a newly opened dialog.
            bool injectFallbackMenuKeys = !_singlePlayerBridgeOwnsUiInput && !isTitleScene;
            keyboard.upArrowKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && lstick.y > 0.5f ? 1f : 0f,
                eventPtr);
            keyboard.downArrowKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && lstick.y < -0.5f ? 1f : 0f,
                eventPtr);
            keyboard.leftArrowKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && lstick.x < -0.5f ? 1f : 0f,
                eventPtr);
            keyboard.rightArrowKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && lstick.x > 0.5f ? 1f : 0f,
                eventPtr);
            keyboard.enterKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && APress ? 1f : 0f,
                eventPtr);
            keyboard.escapeKey.WriteValueIntoEvent(
                injectFallbackMenuKeys && BPress ? 1f : 0f,
                eventPtr);

            // Submit the synthetic event to Unity's input system
            InputSystem.QueueEvent(eventPtr);

            // Update gesture-based actions
            UpdateJumpGesture();
            UpdateMouseInjection();
            UpdateMeleeAttackGesture();
            UpdateWalkieTalkieInput();
            UpdateGpsInput();
            UpdateBookInput();
        }

        private void UpdateTitleUiPointer(bool submitPressed, bool cancelPressed)
        {
            EventSystem eventSystem = FindActiveEventSystem();
            Vector2 pointerPosition = Vector2.zero;
            bool hasPointer = VRPlayer.Instance != null &&
                              VRPlayer.Instance.TryGetTitleUiPointer(
                                  out pointerPosition);

            if (hasPointer && eventSystem != null)
            {
                if (!_uiPointerInitialized)
                {
                    _lastUiPointerPosition = pointerPosition;
                    _uiPointerInitialized = true;
                }

                _uiPointerPosition = pointerPosition;
                UpdateVirtualUiPointer(
                    eventSystem,
                    Vector2.zero,
                    submitPressed,
                    cancelPressed);
            }
            else
            {
                ClearUiPointerTarget(eventSystem);
            }

            _lastUiSubmit = submitPressed;
            _lastUiCancel = cancelPressed;
        }

        private EventSystem FindActiveEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (IsGameUiEventSystem(eventSystem))
                return eventSystem;

            EventSystem[] eventSystems = GameObject.FindObjectsOfType<EventSystem>(true);
            for (int i = 0; i < eventSystems.Length; i++)
            {
                if (IsGameUiEventSystem(eventSystems[i]))
                    return eventSystems[i];
            }

            return null;
        }

        private bool IsGameUiEventSystem(EventSystem eventSystem)
        {
            if (eventSystem == null ||
                !eventSystem.isActiveAndEnabled ||
                eventSystem.gameObject == null ||
                !eventSystem.gameObject.activeInHierarchy)
            {
                return false;
            }

            string objectName = eventSystem.gameObject.name.ToLowerInvariant();
            string sceneName = eventSystem.gameObject.scene.name.ToLowerInvariant();
            return !objectName.Contains("unityexplorer") &&
                   !sceneName.Contains("unityexplorer") &&
                   !objectName.Contains("leave disabled");
        }

        private void ClearUiPointerTarget(EventSystem eventSystem)
        {
            if (_uiPointerTarget == null || eventSystem == null)
                return;

            PointerEventData pointerEvent = new PointerEventData(eventSystem)
            {
                position = _uiPointerPosition,
                delta = Vector2.zero,
                button = PointerEventData.InputButton.Left
            };
            ExecuteEvents.ExecuteHierarchy(
                _uiPointerTarget,
                pointerEvent,
                ExecuteEvents.pointerExitHandler);
            _uiPointerTarget = null;
        }

        /// <summary>
        /// The title scene's Single Player submenu does not use the same menu
        /// selection component as Multiplayer. Keep this adapter scoped to the
        /// title scene and let the game's InputGamepadMenuHelper own selection.
        /// Multiplayer deliberately falls through to the existing path.
        /// </summary>
        private bool UpdateSinglePlayerMenuBridge(
            Vector2 stick,
            bool submitPressed,
            bool cancelPressed)
        {
            ReleaseSinglePlayerDialogHelperIfReady(submitPressed, cancelPressed);

            if (!TryRefreshSinglePlayerMenuBridge())
                return false;

            EventSystem eventSystem = EventSystem.current;
            GameObject selected = GetSinglePlayerSelection(eventSystem);

            if (selected != null)
            {
                UpdateSelectedTextHighlight(selected);
                MoveDirection direction = GetUiMoveDirection(stick);

                if (direction != MoveDirection.None && Time.unscaledTime >= _nextUiMoveTime)
                {
                    GameObject next = FindSelectableInDirection(selected, direction);
                    if (next != null && next != selected)
                        SetSinglePlayerSelection(next, eventSystem);

                    _nextUiMoveTime = Time.unscaledTime + 0.25f;
                }

                if (submitPressed && !_lastUiSubmit)
                {
                    SuppressSinglePlayerDialogHelper();
                    SubmitSinglePlayerSelection(selected, eventSystem);
                    InvalidateSinglePlayerMenuHelper();
                }

                if (cancelPressed && !_lastUiCancel && eventSystem != null)
                {
                    ExecuteEvents.ExecuteHierarchy(
                        selected,
                        new BaseEventData(eventSystem),
                        ExecuteEvents.cancelHandler);
                }
            }

            _lastUiStick = stick;
            _lastUiSubmit = submitPressed;
            _lastUiCancel = cancelPressed;

            // Keep the generic navigation path from competing with the game's
            // helper during title-menu transitions.
            return true;
        }

        private MoveDirection GetUiMoveDirection(Vector2 stick)
        {
            const float centerThreshold = 0.3f;
            const float moveThreshold = 0.7f;

            if (Mathf.Abs(stick.x) <= centerThreshold &&
                Mathf.Abs(stick.y) <= centerThreshold)
            {
                _uiStickCentered = true;
                return MoveDirection.None;
            }

            if (!_uiStickCentered ||
                Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y)) < moveThreshold)
            {
                return MoveDirection.None;
            }

            _uiStickCentered = false;
            if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
                return stick.x < 0f ? MoveDirection.Left : MoveDirection.Right;

            return stick.y < 0f ? MoveDirection.Down : MoveDirection.Up;
        }

        private bool IsButtonNamed(GameObject selected, string buttonName)
        {
            if (selected == null)
                return false;

            Button button = selected.GetComponent<Button>() ??
                            selected.GetComponentInParent<Button>();
            GameObject buttonObject = button != null ? button.gameObject : selected;
            return buttonObject.name == buttonName;
        }

        private void SuppressSinglePlayerDialogHelper()
        {
            if (_suppressedSinglePlayerDialogHelper != null)
                return;

            Component[] components = GameObject.FindObjectsOfType<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.gameObject == null ||
                    component.gameObject.name != "SinglePlayerTypeDialog" ||
                    component.GetType().Name != "InputGamepadMenuHelper")
                {
                    continue;
                }

                Behaviour helper = component as Behaviour;
                if (helper == null)
                    return;

                helper.enabled = false;
                _suppressedSinglePlayerDialogHelper = helper;
                _singlePlayerDialog = component.gameObject;
                MelonLogger.Msg(
                    "[VR UI] Holding Single Player dialog input while the dialog is open.");
                return;
            }

            WarnSinglePlayerBridge(
                "SinglePlayerTypeDialog input helper was not found for the release gate.");
        }

        private void ReleaseSinglePlayerDialogHelperIfReady(
            bool submitPressed,
            bool cancelPressed)
        {
            if (_suppressedSinglePlayerDialogHelper == null)
                return;

            bool leftTitleScene = SceneManager.GetActiveScene().name != "SonsTitleScene";
            bool dialogIsOpen = !leftTitleScene &&
                                _singlePlayerDialog != null &&
                                _singlePlayerDialog.activeInHierarchy;
            if (dialogIsOpen)
                return;

            if (!leftTitleScene && (submitPressed || cancelPressed))
                return;

            _suppressedSinglePlayerDialogHelper.enabled = true;
            _suppressedSinglePlayerDialogHelper = null;
            _singlePlayerDialog = null;
            _titleMenuSelectionInitialized = false;
            InvalidateSinglePlayerMenuHelper();
            MelonLogger.Msg("[VR UI] Single Player dialog input released.");
        }

        private void InvalidateSinglePlayerMenuHelper()
        {
            _nextSinglePlayerBridgeScanTime = 0f;
            _singlePlayerMenuHelper = null;
            _getActiveSelectionMethod = null;
            _setNextActiveControlMethod = null;
        }

        private bool TryRefreshSinglePlayerMenuBridge()
        {
            if (SceneManager.GetActiveScene().name != "SonsTitleScene")
            {
                InvalidateSinglePlayerMenuHelper();
                RestoreSelectedTextHighlight();
                return false;
            }

            if (Time.unscaledTime < _nextSinglePlayerBridgeScanTime &&
                _singlePlayerMenuHelper != null)
            {
                return true;
            }

            _nextSinglePlayerBridgeScanTime = Time.unscaledTime + 0.5f;
            _singlePlayerMenuHelper = null;
            _getActiveSelectionMethod = null;
            _setNextActiveControlMethod = null;

            Component[] components = GameObject.FindObjectsOfType<Component>(true);
            bool multiplayerMenuActive = false;
            Component activeMenuHelper = null;
            Component singlePlayerDialogHelper = null;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.gameObject == null)
                    continue;

                string typeName = component.GetType().Name;
                if (typeName == "MultiplayerMenu" && component.gameObject.activeInHierarchy)
                    multiplayerMenuActive = true;

                if (typeName != "InputGamepadMenuHelper")
                    continue;

                if (component.gameObject.name == "SinglePlayerTypeDialog")
                {
                    singlePlayerDialogHelper = component;
                    _singlePlayerDialog = component.gameObject;
                }
                else if (activeMenuHelper == null && component.gameObject.activeInHierarchy)
                {
                    activeMenuHelper = component;
                }
            }

            bool singlePlayerDialogActive =
                singlePlayerDialogHelper != null &&
                singlePlayerDialogHelper.gameObject.activeInHierarchy;
            _singlePlayerMenuHelper = singlePlayerDialogActive ||
                                      _suppressedSinglePlayerDialogHelper != null
                ? singlePlayerDialogHelper
                : activeMenuHelper;

            if (multiplayerMenuActive || _singlePlayerMenuHelper == null)
            {
                _singlePlayerMenuHelper = null;
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance |
                                        BindingFlags.Public |
                                        BindingFlags.NonPublic;
            System.Type helperType = _singlePlayerMenuHelper.GetType();
            _getActiveSelectionMethod = helperType.GetMethod("GetActiveSelection", flags);
            _setNextActiveControlMethod = helperType.GetMethod("SetNextActiveControl", flags);

            if (_getActiveSelectionMethod == null || _setNextActiveControlMethod == null)
            {
                WarnSinglePlayerBridge(
                    "InputGamepadMenuHelper methods were not found; title menu bridge is inactive.");
                _singlePlayerMenuHelper = null;
                return false;
            }

            return true;
        }

        private GameObject GetSinglePlayerSelection(EventSystem eventSystem)
        {
            GameObject selected = null;
            if (_singlePlayerMenuHelper != null && _getActiveSelectionMethod != null)
            {
                try
                {
                    object result = _getActiveSelectionMethod.Invoke(_singlePlayerMenuHelper, null);
                    selected = result as GameObject;
                    if (selected == null && result is Component component)
                        selected = component.gameObject;
                }
                catch (System.Exception exception)
                {
                    WarnSinglePlayerBridge(
                        $"Could not read the title menu selection: {exception.GetType().Name}.");
                }
            }

            if (selected == null || !selected.activeInHierarchy)
                selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null || !selected.activeInHierarchy)
                selected = eventSystem != null ? eventSystem.firstSelectedGameObject : null;
            if (selected == null || !selected.activeInHierarchy)
                selected = FindFirstTitleMenuSelectable();

            return selected;
        }

        private GameObject FindFirstTitleMenuSelectable()
        {
            Selectable[] selectables = GameObject.FindObjectsOfType<Selectable>(true);
            GameObject fallback = null;

            for (int i = 0; i < selectables.Length; i++)
            {
                Selectable selectable = selectables[i];
                if (selectable == null || !selectable.isActiveAndEnabled || !selectable.interactable)
                    continue;

                string name = selectable.gameObject.name.ToLowerInvariant();
                if (name.Contains("newgame") || name.Contains("continue") ||
                    name.Contains("singleplayer"))
                {
                    return selectable.gameObject;
                }

                fallback ??= selectable.gameObject;
            }

            return fallback;
        }

        private GameObject FindSelectableInDirection(GameObject selected, MoveDirection direction)
        {
            Selectable selectable = selected.GetComponent<Selectable>() ??
                                    selected.GetComponentInParent<Selectable>();
            if (selectable != null)
            {
                Selectable neighbor = null;
                switch (direction)
                {
                    case MoveDirection.Up:
                        neighbor = selectable.FindSelectableOnUp();
                        break;
                    case MoveDirection.Down:
                        neighbor = selectable.FindSelectableOnDown();
                        break;
                    case MoveDirection.Left:
                        neighbor = selectable.FindSelectableOnLeft();
                        break;
                    case MoveDirection.Right:
                        neighbor = selectable.FindSelectableOnRight();
                        break;
                }

                if (neighbor != null && neighbor.isActiveAndEnabled && neighbor.interactable)
                    return neighbor.gameObject;
            }

            return FindDirectionalSelectable(selected, direction);
        }

        private void SetSinglePlayerSelection(GameObject selection, EventSystem eventSystem)
        {
            if (selection == null || _singlePlayerMenuHelper == null ||
                _setNextActiveControlMethod == null)
            {
                return;
            }

            try
            {
                _setNextActiveControlMethod.Invoke(
                    _singlePlayerMenuHelper,
                    new object[] { selection });
            }
            catch (System.Exception exception)
            {
                WarnSinglePlayerBridge(
                    $"Could not set the title menu selection: {exception.GetType().Name}.");
            }

            if (eventSystem != null && eventSystem.currentSelectedGameObject != selection)
                eventSystem.SetSelectedGameObject(selection);
        }

        private void SubmitSinglePlayerSelection(GameObject selected, EventSystem eventSystem)
        {
            if (selected == null)
                return;

            GameObject handledObject = null;
            if (eventSystem != null)
            {
                handledObject = ExecuteEvents.ExecuteHierarchy(
                    selected,
                    new BaseEventData(eventSystem),
                    ExecuteEvents.submitHandler);
            }

            if (handledObject == null)
            {
                Button button = selected.GetComponentInParent<Button>();
                if (button != null && button.interactable && button.gameObject.activeInHierarchy)
                    button.onClick.Invoke();
            }
        }

        private void WarnSinglePlayerBridge(string message)
        {
            if (_singlePlayerBridgeWarningShown)
                return;

            _singlePlayerBridgeWarningShown = true;
            MelonLogger.Warning($"[VR UI] {message}");
        }

        private void UpdateUiNavigation(Vector2 stick, bool submitPressed, bool cancelPressed)
        {
            bool isTitleScene = SceneManager.GetActiveScene().name == "SonsTitleScene";
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !eventSystem.isActiveAndEnabled)
            {
                EventSystem[] eventSystems = GameObject.FindObjectsOfType<EventSystem>(true);
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i] != null && eventSystems[i].isActiveAndEnabled)
                    {
                        eventSystem = eventSystems[i];
                        break;
                    }
                }
            }

            if (eventSystem == null || !eventSystem.isActiveAndEnabled)
            {
                RestoreSelectedTextHighlight();
                return;
            }

            if (!isTitleScene)
                _titleMenuSelectionInitialized = false;

            if (isTitleScene && !_titleMenuSelectionInitialized)
            {
                GameObject firstSelection = eventSystem.firstSelectedGameObject;
                if (firstSelection != null && firstSelection.activeInHierarchy)
                    eventSystem.SetSelectedGameObject(firstSelection);

                _titleMenuSelectionInitialized = true;
                _uiStickCentered = false;
            }

            // Title UI is navigation-only. Mixing this virtual pointer with
            // EventSystem moves, synthetic mouse state, and Enter caused one
            // physical press to activate both Single Player and New Game.
            bool pointerHandledSubmit = !isTitleScene && UpdateVirtualUiPointer(
                eventSystem,
                stick,
                submitPressed,
                cancelPressed);

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy)
            {
                Selectable[] selectables = GameObject.FindObjectsOfType<Selectable>(true);
                for (int i = 0; i < selectables.Length; i++)
                {
                    Selectable selectable = selectables[i];
                    if (selectable != null && selectable.isActiveAndEnabled && selectable.interactable)
                    {
                        eventSystem.SetSelectedGameObject(selectable.gameObject);
                        selected = selectable.gameObject;
                        break;
                    }
                }
            }

            if (selected != null)
            {
                UpdateSelectedTextHighlight(selected);
                MoveDirection direction = GetUiMoveDirection(stick);
                Vector2 moveVector = Vector2.zero;

                if (direction == MoveDirection.Up)
                {
                    moveVector = Vector2.up;
                }
                else if (direction == MoveDirection.Down)
                {
                    moveVector = Vector2.down;
                }
                else if (direction == MoveDirection.Left)
                {
                    moveVector = Vector2.left;
                }
                else if (direction == MoveDirection.Right)
                {
                    moveVector = Vector2.right;
                }

                if (direction != MoveDirection.None && Time.unscaledTime >= _nextUiMoveTime)
                {
                    GameObject previousSelection = eventSystem.currentSelectedGameObject;
                    AxisEventData moveEvent = new AxisEventData(eventSystem)
                    {
                        moveDir = direction,
                        moveVector = moveVector
                    };
                    ExecuteEvents.Execute(selected, moveEvent, ExecuteEvents.moveHandler);

                    if (eventSystem.currentSelectedGameObject == previousSelection)
                    {
                        GameObject fallbackSelection = FindDirectionalSelectable(selected, direction);
                        if (fallbackSelection != null)
                            eventSystem.SetSelectedGameObject(fallbackSelection);
                    }

                    _nextUiMoveTime = Time.unscaledTime + 0.25f;
                }

                if (submitPressed && !_lastUiSubmit && !pointerHandledSubmit)
                {
                    if (isTitleScene && IsButtonNamed(selected, "SinglePlayerButton"))
                        SuppressSinglePlayerDialogHelper();

                    BaseEventData submitEvent = new BaseEventData(eventSystem);
                    GameObject handledObject = ExecuteEvents.ExecuteHierarchy(
                        selected,
                        submitEvent,
                        ExecuteEvents.submitHandler);

                    if (handledObject == null)
                    {
                        Button button = selected.GetComponentInParent<Button>();
                        if (button != null && button.interactable && button.gameObject.activeInHierarchy)
                            button.onClick.Invoke();
                    }
                }

                if (cancelPressed && !_lastUiCancel)
                {
                    ExecuteEvents.ExecuteHierarchy(
                        selected,
                        new BaseEventData(eventSystem),
                        ExecuteEvents.cancelHandler);
                }
            }

            _lastUiStick = stick;
            _lastUiSubmit = submitPressed;
            _lastUiCancel = cancelPressed;
        }

        private void UpdateSelectedTextHighlight(GameObject selected)
        {
            Selectable selectable = selected != null
                ? selected.GetComponent<Selectable>() ?? selected.GetComponentInParent<Selectable>()
                : null;
            GameObject selectionObject = selectable != null ? selectable.gameObject : selected;

            if (selectionObject != _highlightedUiSelection)
            {
                RestoreSelectedTextHighlight();
                _highlightedUiSelection = selectionObject;

                if (_highlightedUiSelection != null)
                {
                    Graphic[] graphics =
                        _highlightedUiSelection.GetComponentsInChildren<Graphic>(true);
                    for (int i = 0; i < graphics.Length; i++)
                    {
                        Graphic graphic = graphics[i];
                        if (graphic == null ||
                            !graphic.GetType().Name.ToLowerInvariant().Contains("text"))
                        {
                            continue;
                        }

                        _highlightedUiText.Add(graphic);
                        _highlightedUiTextColors.Add(graphic.color);
                    }
                }
            }

            for (int i = 0; i < _highlightedUiText.Count; i++)
            {
                Graphic graphic = _highlightedUiText[i];
                if (graphic != null)
                    graphic.color = new Color(1f, 0.08f, 0.08f, graphic.color.a);
            }
        }

        private void RestoreSelectedTextHighlight()
        {
            for (int i = 0; i < _highlightedUiText.Count; i++)
            {
                Graphic graphic = _highlightedUiText[i];
                if (graphic != null && i < _highlightedUiTextColors.Count)
                    graphic.color = _highlightedUiTextColors[i];
            }

            _highlightedUiText.Clear();
            _highlightedUiTextColors.Clear();
            _highlightedUiSelection = null;
        }

        private bool UpdateVirtualUiPointer(
            EventSystem eventSystem,
            Vector2 stick,
            bool submitPressed,
            bool cancelPressed)
        {
            if (!_uiPointerInitialized)
            {
                _uiPointerPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                _lastUiPointerPosition = _uiPointerPosition;
                _uiPointerInitialized = true;
            }

            if (stick.sqrMagnitude > 0.04f)
            {
                float pointerSpeed = Screen.height * 1.5f;
                _uiPointerPosition += stick * pointerSpeed * Time.unscaledDeltaTime;
                _uiPointerPosition.x = Mathf.Clamp(_uiPointerPosition.x, 0f, Screen.width);
                _uiPointerPosition.y = Mathf.Clamp(_uiPointerPosition.y, 0f, Screen.height);
            }

            PointerEventData pointerEvent = new PointerEventData(eventSystem)
            {
                position = _uiPointerPosition,
                delta = _uiPointerPosition - _lastUiPointerPosition,
                button = PointerEventData.InputButton.Left
            };

            Il2CppSystem.Collections.Generic.List<RaycastResult> raycastResults =
                new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointerEvent, raycastResults);
            GameObject pointerTarget = null;
            foreach (RaycastResult raycastResult in raycastResults)
            {
                pointerTarget = raycastResult.gameObject;
                break;
            }

            if (pointerTarget != _uiPointerTarget)
            {
                if (_uiPointerTarget != null)
                {
                    ExecuteEvents.ExecuteHierarchy(
                        _uiPointerTarget,
                        pointerEvent,
                        ExecuteEvents.pointerExitHandler);
                }

                if (pointerTarget != null)
                {
                    ExecuteEvents.ExecuteHierarchy(
                        pointerTarget,
                        pointerEvent,
                        ExecuteEvents.pointerEnterHandler);
                }

                _uiPointerTarget = pointerTarget;
            }

            bool handledSubmit = false;
            if (submitPressed && !_lastUiSubmit && _uiPointerTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(
                    _uiPointerTarget,
                    pointerEvent,
                    ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(
                    _uiPointerTarget,
                    pointerEvent,
                    ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(
                    _uiPointerTarget,
                    pointerEvent,
                    ExecuteEvents.pointerClickHandler);
                handledSubmit = true;
            }

            if (cancelPressed && !_lastUiCancel && _uiPointerTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(
                    _uiPointerTarget,
                    pointerEvent,
                    ExecuteEvents.pointerExitHandler);
                _uiPointerTarget = null;
            }

            _lastUiPointerPosition = _uiPointerPosition;
            return handledSubmit;
        }

        private GameObject FindDirectionalSelectable(GameObject current, MoveDirection direction)
        {
            RectTransform currentRect = current != null
                ? current.GetComponent<RectTransform>()
                : null;
            if (currentRect == null && current != null)
                currentRect = current.GetComponentInParent<RectTransform>();

            if (currentRect == null)
                return null;

            Canvas currentCanvas = current.GetComponentInParent<Canvas>();
            Camera eventCamera = currentCanvas != null ? currentCanvas.worldCamera : Camera.main;
            Vector2 currentPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, currentRect.position);

            Vector2 directionVector;
            switch (direction)
            {
                case MoveDirection.Up:
                    directionVector = Vector2.up;
                    break;
                case MoveDirection.Down:
                    directionVector = Vector2.down;
                    break;
                case MoveDirection.Left:
                    directionVector = Vector2.left;
                    break;
                case MoveDirection.Right:
                    directionVector = Vector2.right;
                    break;
                default:
                    return null;
            }

            Selectable[] selectables = GameObject.FindObjectsOfType<Selectable>(true);
            GameObject best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < selectables.Length; i++)
            {
                Selectable selectable = selectables[i];
                if (selectable == null || !selectable.isActiveAndEnabled || !selectable.interactable ||
                    selectable.gameObject == current)
                {
                    continue;
                }

                RectTransform candidateRect = selectable.GetComponent<RectTransform>();
                if (candidateRect == null)
                    continue;

                Canvas candidateCanvas = selectable.GetComponentInParent<Canvas>();
                Camera candidateCamera = candidateCanvas != null ? candidateCanvas.worldCamera : eventCamera;
                Vector2 candidatePoint = RectTransformUtility.WorldToScreenPoint(
                    candidateCamera,
                    candidateRect.position);
                Vector2 delta = candidatePoint - currentPoint;
                float forwardDistance = Vector2.Dot(delta, directionVector);
                if (forwardDistance <= 1f)
                    continue;

                float sidewaysDistance = Mathf.Abs(
                    delta.x * directionVector.y - delta.y * directionVector.x);
                float score = forwardDistance + sidewaysDistance * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = selectable.gameObject;
                }
            }

            return best;
        }

        /// <summary>
        /// Simulates mouse button presses based on VR trigger inputs.
        /// Right trigger = left mouse button (attack/fire), left trigger = right mouse button (aim/block).
        /// </summary>
        public void UpdateMouseInjection()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (VRPlayer.Instance != null &&
                VRPlayer.Instance.IsMirroredUiActive)
            {
                // Pause/title UI already receives controller events through
                // the mirrored-panel EventSystem path. Do not continuously
                // overwrite the physical desktop mouse while it is open.
                return;
            }

            // Read trigger states (digital press detection)
            bool triggerRight = NativeVRInput.GetBoolean("TriggerRight_Press") ||
                                NativeVRInput.GetFloat("TriggerRight_Value") > 0.65f; // Attack/fire
            bool triggerLeft = NativeVRInput.GetBoolean("TriggerLeft_Press") ||
                               NativeVRInput.GetFloat("TriggerLeft_Value") > 0.65f; // Aim/block
            bool allowMouseUiInput = !_singlePlayerBridgeOwnsUiInput &&
                                     SceneManager.GetActiveScene().name != "SonsTitleScene";
            bool uiSubmit = allowMouseUiInput && _uiPointerInitialized &&
                            NativeVRInput.GetBoolean("ButtonRight_A_Press");

            // Generate a synthetic mouse event
            StateEvent.From(mouse, out var eventPtr);
            bool physicalLeftPressed = mouse.leftButton.isPressed;
            bool physicalRightPressed = mouse.rightButton.isPressed;
            mouse.leftButton.WriteValueIntoEvent(
                physicalLeftPressed ||
                (allowMouseUiInput && (triggerRight || uiSubmit)) ? 1f : 0f,
                eventPtr);
            mouse.rightButton.WriteValueIntoEvent(
                physicalRightPressed ||
                (allowMouseUiInput && triggerLeft) ? 1f : 0f,
                eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }

        /// <summary>
        /// Detects a "jump" gesture when both hands move upward rapidly.
        /// Requires both hands to exceed a vertical velocity threshold simultaneously.
        /// </summary>
        public void UpdateJumpGesture()
        {
            if (_jumpCooldown > 0)
            {
                _jumpCooldown -= Time.deltaTime;
                return;
            }

            // Get vertical (Y-axis) velocity of both hands
            float leftY = NativeVRInput.GetHandVelocity("PoseLeft").y;
            float rightY = NativeVRInput.GetHandVelocity("PoseRight").y;

            // Trigger jump only if both hands exceed the upward velocity threshold
            if (leftY > JUMP_THRESHOLD && rightY > JUMP_THRESHOLD)
            {
                MelonLogger.Msg($"[GESTURE] Jump detected! LeftVel: {leftY:F2} m/s, RightVel: {rightY:F2} m/s");
                ExecuteJumpAction();
                _jumpCooldown = JUMP_DELAY; // Prevent rapid successive jumps
            }
        }

        /// <summary>
        /// Detects and simulates melee swing gestures using right-hand speed.
        /// Presses the left mouse button on fast motion, releases it when motion slows.
        /// </summary>
        public void UpdateMeleeAttackGesture()
        {
            if (_attackCooldown > 0)
                _attackCooldown -= Time.deltaTime;

            // Get full 3D velocity of the right hand
            Vector3 velocity = NativeVRInput.GetHandVelocity("PoseRight");
            float currentSpeed = velocity.magnitude;

            var mouse = Mouse.current;
            if (mouse == null) return;

            // --- ATTACK TRIGGER ---
            // If hand is moving fast enough, not already attacking, and cooldown has passed
            if (currentSpeed > ATTACK_THRESHOLD && !_isMeleeAttacking && _attackCooldown <= 0)
            {
                _isMeleeAttacking = true;
                _attackCooldown = MIN_ATTACK_INTERVAL;

                StateEvent.From(mouse, out var eventPtr);
                mouse.leftButton.WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);

                MelonLogger.Msg($"[COMBAT] Melee attack detected! Speed: {currentSpeed:F2} m/s");
            }

            // --- ATTACK RELEASE ---
            // Release the mouse button once hand slows below the release threshold
            else if (currentSpeed < RELEASE_THRESHOLD && _isMeleeAttacking)
            {
                _isMeleeAttacking = false;
                StateEvent.From(mouse, out var eventPtr);
                mouse.leftButton.WriteValueIntoEvent(0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        /// <summary>
        /// Executes a jump by simulating a momentary press of the spacebar.
        /// </summary>
        private void ExecuteJumpAction()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Press the space key
            StateEvent.From(keyboard, out var eventPtr);
            keyboard.spaceKey.WriteValueIntoEvent(1f, eventPtr);
            InputSystem.QueueEvent(eventPtr);

            // Schedule key release after a short delay
            MelonCoroutines.Start(ReleaseSpace(keyboard));
        }

        /// <summary>
        /// Coroutine that releases the spacebar after a brief duration to simulate a real key press.
        /// </summary>
        /// <param name="keyboard">Reference to the Unity virtual keyboard device.</param>
        /// <returns>An IEnumerator for use as a coroutine.</returns>
        private System.Collections.IEnumerator ReleaseSpace(Keyboard keyboard)
        {
            yield return new WaitForSeconds(0.1f);
            StateEvent.From(keyboard, out var eventPtr);
            keyboard.spaceKey.WriteValueIntoEvent(0f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
        
        public void ExecutePress_E()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Press the E key
            StateEvent.From(keyboard, out var eventPtr);
            keyboard.eKey.WriteValueIntoEvent(1f , eventPtr);
            InputSystem.QueueEvent(eventPtr);

            // Schedule key release after a short delay
            MelonCoroutines.Start(ReleaseE(keyboard));
        }

        /// <summary>
        /// Coroutine that releases the E key after a brief duration to simulate a real key press.
        /// </summary>
        /// <param name="keyboard">Reference to the Unity virtual keyboard device.</param>
        /// <returns>An IEnumerator for use as a coroutine.</returns>
        private System.Collections.IEnumerator ReleaseE(Keyboard keyboard)
        {
            yield return new WaitForSeconds(0.1f);
            StateEvent.From(keyboard, out var eventPtr);
            keyboard.eKey.WriteValueIntoEvent(0f , eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
     
        
        public void UpdateWalkieTalkieInput()
        {
            if (LocalPlayer == null) return;

            // 1. Read input
            bool rightGripPress = NativeVRInput.GetBoolean("GripRight_Press");
            Vector3 rightHandPos = NativeVRInput.GetPose("PoseRight").position;
            

            // 2. Calculate left shoulder position
            Vector3 playerPos = LocalPlayer.transform.position;
            Vector3 leftShoulderPos = playerPos 
                                      + LocalPlayer.transform.right * -0.3f 
                                      + LocalPlayer.transform.up * 0.8f;

            // 3. Check proximity
            bool isNearShoulder = Vector3.Distance(rightHandPos, leftShoulderPos) < 0.25f;

            // 4. Handle press edge
            if (rightGripPress && !isNearShoulder)
            {
                // Grip pressed but not near shoulder → reset state
                _wasRightGripRightPressed = false;
            }
            else if (rightGripPress && isNearShoulder && !_wasRightGripRightPressed)
            {
                // Grip pressed ON THE SHOULDER for the first time → emulate "T"
                _wasRightGripRightPressed = true;

                var keyboard = Keyboard.current;
        
                // Press "T"
                StateEvent.From(keyboard, out var pressEvent);
                keyboard.tKey.WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);

                // Immediate release (tap)
                StateEvent.From(keyboard, out var releaseEvent);
                keyboard.tKey.WriteValueIntoEvent(0f, releaseEvent);
                InputSystem.QueueEvent(releaseEvent);

                MelonLogger.Msg("[VRPlayer] Walkie-talkie activated!");
            }
            else if (!rightGripPress)
            {
                // Grip released → reset state
                _wasRightGripRightPressed = false;
            }
        }
        
        public void UpdateGpsInput()
        {
            if (LocalPlayer == null) return;

            // 1. Read input
            bool leftGripPress = NativeVRInput.GetBoolean("GripLeft_Press");
            Vector3 leftHandPos = NativeVRInput.GetPose("PoseLeft").position;
            

            // 2. Calculate right shoulder position
            Vector3 playerPos = LocalPlayer.transform.position;
            Vector3 rightShoulderPos = playerPos 
                                      + LocalPlayer.transform.right * 0.3f 
                                      + LocalPlayer.transform.up * 0.8f;

            // 3. Check proximity
            bool isNearShoulder = Vector3.Distance(leftHandPos, rightShoulderPos) < 0.25f;

            // 4. Handle press edge
            if (leftGripPress && !isNearShoulder)
            {
                // Grip pressed but not near shoulder → reset state
                _wasLeftGripRightPressed = false;
            }
            else if (leftGripPress && isNearShoulder && !_wasLeftGripRightPressed)
            {
                // Grip pressed ON THE SHOULDER for the first time → emulate "M"
                _wasLeftGripRightPressed = true;

                var keyboard = Keyboard.current;
        
                // Press "M"
                StateEvent.From(keyboard, out var pressEvent);
                keyboard.mKey.WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);

                // Immediate release (tap)
                StateEvent.From(keyboard, out var releaseEvent);
                keyboard.mKey.WriteValueIntoEvent(0f, releaseEvent);
                InputSystem.QueueEvent(releaseEvent);

                MelonLogger.Msg("[VRPlayer] GPS map activated!");
            }
            else if (!leftGripPress)
            {
                // Grip released → reset state
                _wasLeftGripRightPressed = false;
            }
        }
        
        public void UpdateBookInput()
        {
            if (LocalPlayer == null) return;

            // 1. Read input
            bool rightGripPress = NativeVRInput.GetBoolean("GripRight_Press");
            Vector3 rightHandPos = NativeVRInput.GetPose("PoseRight").position;
            

            // 2. Calculate right shoulder position
            Vector3 playerPos = LocalPlayer.transform.position;
            Vector3 rightShoulderPos = playerPos 
                                       + LocalPlayer.transform.right * 0.3f 
                                       + LocalPlayer.transform.up * 0.8f;

            // 3. Check proximity
            bool isNearShoulder = Vector3.Distance(rightHandPos, rightShoulderPos) < 0.25f;

            // 4. Handle press edge
            if (rightGripPress && !isNearShoulder)
            {
                // Grip pressed but not near shoulder → reset state
                _wasBGripRightPressed = false;
            }
            else if (rightGripPress && isNearShoulder && !_wasBGripRightPressed)
            {
                // Grip pressed ON THE SHOULDER for the first time → emulate "B"
                _wasBGripRightPressed = true;

                var keyboard = Keyboard.current;
        
                // Press "B"
                StateEvent.From(keyboard, out var pressEvent);
                keyboard.bKey.WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);

                // Immediate release (tap)
                StateEvent.From(keyboard, out var releaseEvent);
                keyboard.bKey.WriteValueIntoEvent(0f, releaseEvent);
                InputSystem.QueueEvent(releaseEvent);

                MelonLogger.Msg("[VRPlayer] Notebook activated!");
            }
            else if (!rightGripPress)
            {
                // Grip released → reset state
                _wasBGripRightPressed = false;
            }
        }
        
        
    }
}
