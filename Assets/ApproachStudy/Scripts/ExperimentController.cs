using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace ApproachStudy
{
    /// <summary>
    /// Runs the study: calibration of the participant's spot, sending characters in the chosen order, and recording.
    ///
    /// Default controls (right/left Quest controller, keyboard in the Editor):
    ///   A (Space)          Setup: calibrate & start session.  Ready: send next character.  Finished: next participant.
    ///   B (Backspace)      Running: abort & redo this trial.   Ready: press twice to end the session early.
    ///   X (C)              Recalibrate the spot (between trials).
    ///   Y (H)              Show / hide the status panel.
    ///   Right stick (arrows), Setup only: left/right = choose order, up/down = participant number.
    /// </summary>
    public class ExperimentController : MonoBehaviour
    {
        enum State { Setup, Ready, Running, InterTrial, Finished }

        struct OrderOption
        {
            public OrderMode mode;
            public int customIndex;
        }

        [Header("Study")]
        [Tooltip("The study settings (trials, orders...). Create one via Assets > Create > Approach Study > Experiment Config.")]
        public ExperimentConfig config;

        [Tooltip("Participant number for the next session. Can also be changed in the headset with the right stick (up/down).")]
        [Min(1)] public int participantNumber = 1;

        [Header("Scene references (found automatically if empty)")]
        public XROrigin xrOrigin;
        public Transform head;
        public ARPlaneManager planeManager;

        [Header("Floor")]
        [Tooltip("Use the lowest floor plane from the room scan as the floor height. Falls back to the XR Origin height.")]
        public bool useScannedFloor = true;

        [Tooltip("Fine-tune if characters float above or sink into the floor (metres, + = up).")]
        public float floorHeightAdjust = 0f;

        [Header("Passthrough")]
        [Tooltip("Turns on passthrough from this script (AR Camera Manager on + transparent camera background), " +
                 "so it doesn't depend on the template's UI / Goal Manager. Only works in a build on the headset, not in Play mode.")]
        public bool forcePassthrough = true;

        [Header("Materials (use 'Tools > Approach Study > Create & Assign Materials')")]
        [Tooltip("Lit material for the cube characters. Must be a URP material, otherwise objects render pink in the build.")]
        public Material characterMaterial;
        [Tooltip("Material for the spot marker on the floor.")]
        public Material markerMaterial;
        [Tooltip("Unlit material for the status panel background.")]
        public Material panelMaterial;

        [Header("Display")]
        public bool showStatusPanel = true;
        [Tooltip("Hide the status panel while a character is walking so the participant isn't distracted.")]
        public bool hideStatusDuringTrials = true;
        [Tooltip("Show a green disc + arrow on the floor at the calibrated spot.")]
        public bool showSpotMarker = true;
        public bool hideSpotMarkerDuringTrials = false;
        [Tooltip("Short vibrations on the controller to confirm button presses.")]
        public bool controllerHaptics = true;

        [Header("Controls (Input System binding paths)")]
        public string mainButton = "<XRController>{RightHand}/{PrimaryButton}";          // A
        public string abortButton = "<XRController>{RightHand}/{SecondaryButton}";        // B
        public string recalibrateButton = "<XRController>{LeftHand}/{PrimaryButton}";     // X
        public string togglePanelButton = "<XRController>{LeftHand}/{SecondaryButton}";   // Y
        public string setupStick = "<XRController>{RightHand}/{Primary2DAxis}";

        // ------------------------------------------------------------------ runtime state

        State _state = State.Setup;
        MotionLogger _logger;
        StatusDisplay _status;
        GameObject _spotMarker;
        ApproachCharacter _character;

        bool _calibrated;
        Vector3 _calibPoint;
        Vector3 _calibForward = Vector3.forward;
        float _floorY;
        string _floorSource = "";

        readonly List<OrderOption> _orderOptions = new List<OrderOption>();
        int _orderOptionIndex;
        int _seed;
        List<int> _order = new List<int>();
        readonly List<string> _warnings = new List<string>();
        int _seqPos;
        int[] _attempts = new int[0];

        TrialContext _ctx;
        float _trialTime, _sampleTimer, _postTimer, _interTimer;
        bool _characterDone;

        bool _panelVisible = true;
        bool _trackingChanged;
        string _flash = "";
        float _flashTimer;
        float _endConfirmTimer;
        string _lastSessionFolder = "";
        bool _stickLatched;

        ARCameraManager _cameraManager;
        Camera _camera;

        InputAction _mainAction, _abortAction, _recalAction, _panelAction, _stickAction;
        readonly List<UnityEngine.XR.XRInputSubsystem> _inputSubsystems = new List<UnityEngine.XR.XRInputSubsystem>();

        // ------------------------------------------------------------------ Unity lifecycle

        void Awake()
        {
            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
            if (head == null)
            {
                if (xrOrigin != null && xrOrigin.Camera != null) head = xrOrigin.Camera.transform;
                else if (Camera.main != null) head = Camera.main.transform;
            }
            if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (head == null) Debug.LogError("[ApproachStudy] No head/camera found. Assign 'Head' on the Experiment Controller.", this);

            CreateInput();
            EnsurePassthrough();
            _status = StatusDisplay.Create(head, panelMaterial);
            _spotMarker = CreateSpotMarker();
            _spotMarker.SetActive(false);
            EnterSetup();
        }

        /// <summary>
        /// On Quest, passthrough is on whenever the camera's AR Camera Manager is enabled and the camera clears to a
        /// fully transparent colour. Some template UI (Goal Manager / AR Feature Controller) toggles this, so we set it here.
        /// </summary>
        void EnsurePassthrough()
        {
            if (!forcePassthrough || head == null) return;

            _camera = head.GetComponent<Camera>();
            if (_camera != null)
            {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            _cameraManager = head.GetComponent<ARCameraManager>();
            if (_cameraManager == null) _cameraManager = head.gameObject.AddComponent<ARCameraManager>();
            _cameraManager.enabled = true;
        }

        /// <summary>Material for a runtime-created object: a copy of <paramref name="template"/> in the given colour.</summary>
        public static void ApplyMaterial(Renderer renderer, Material template, Color color)
        {
            if (template != null) renderer.material = new Material(template);
            renderer.material.color = color;
        }

        void OnEnable()
        {
            _mainAction?.Enable(); _abortAction?.Enable(); _recalAction?.Enable(); _panelAction?.Enable(); _stickAction?.Enable();
        }

        void OnDisable()
        {
            _mainAction?.Disable(); _abortAction?.Disable(); _recalAction?.Disable(); _panelAction?.Disable(); _stickAction?.Disable();
        }

        void Start() => SubscribeTrackingEvents();

        void OnDestroy()
        {
            foreach (var s in _inputSubsystems) if (s != null) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _logger?.EndSession();
            _mainAction?.Dispose(); _abortAction?.Dispose(); _recalAction?.Dispose(); _panelAction?.Dispose(); _stickAction?.Dispose();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            _logger?.Flush();
            if (_logger != null && _logger.SessionOpen) _logger.LogEvent(participantNumber, "app_paused", "headset removed or app sent to background");
        }

        void OnApplicationQuit() => _logger?.EndSession();

        void Update()
        {
            // Keep passthrough on even if something else (e.g. leftover template UI) switches it off.
            if (forcePassthrough && _cameraManager != null && !_cameraManager.enabled) _cameraManager.enabled = true;

            if (head == null || config == null) { UpdateVisuals(); return; }

            float dt = Time.deltaTime;
            if (_flashTimer > 0f) _flashTimer -= dt;
            if (_endConfirmTimer > 0f) _endConfirmTimer -= dt;

            State stateBefore = _state;
            HandleInput();

            // Only advance a trial/countdown that was already running before this frame's input,
            // so a trial started this frame begins cleanly at t = 0.
            if (_state == stateBefore)
            {
                switch (_state)
                {
                    case State.Running:
                        UpdateTrial(dt);
                        break;
                    case State.InterTrial:
                        _interTimer -= dt;
                        if (_interTimer <= 0f) StartTrial();
                        break;
                }
            }
            UpdateVisuals();
        }

        // ------------------------------------------------------------------ input

        void CreateInput()
        {
            _mainAction = MakeButton("Main", mainButton, "<Keyboard>/space");
            _abortAction = MakeButton("Abort", abortButton, "<Keyboard>/backspace");
            _recalAction = MakeButton("Recalibrate", recalibrateButton, "<Keyboard>/c");
            _panelAction = MakeButton("TogglePanel", togglePanelButton, "<Keyboard>/h");

            _stickAction = new InputAction(name: "SetupStick", type: InputActionType.Value, expectedControlType: "Vector2");
            if (!string.IsNullOrEmpty(setupStick)) _stickAction.AddBinding(setupStick);
            _stickAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
        }

        static InputAction MakeButton(string name, string xrPath, string keyPath)
        {
            var action = new InputAction(name: name, type: InputActionType.Button);
            if (!string.IsNullOrEmpty(xrPath)) action.AddBinding(xrPath);
            if (!string.IsNullOrEmpty(keyPath)) action.AddBinding(keyPath);
            return action;
        }

        void HandleInput()
        {
            bool main = _mainAction.WasPressedThisFrame();
            bool abort = _abortAction.WasPressedThisFrame();
            bool recal = _recalAction.WasPressedThisFrame();
            if (_panelAction.WasPressedThisFrame()) _panelVisible = !_panelVisible;

            // Thumbstick as discrete clicks
            Vector2 stick = _stickAction.ReadValue<Vector2>();
            int stickX = 0, stickY = 0;
            if (stick.magnitude < 0.3f) _stickLatched = false;
            else if (!_stickLatched && stick.magnitude > 0.7f)
            {
                _stickLatched = true;
                if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) stickX = stick.x > 0 ? 1 : -1;
                else stickY = stick.y > 0 ? 1 : -1;
            }

            switch (_state)
            {
                case State.Setup:
                    if (stickX != 0 && _orderOptions.Count > 0)
                    {
                        _orderOptionIndex = (_orderOptionIndex + stickX + _orderOptions.Count) % _orderOptions.Count;
                        RefreshOrderPreview();
                        Buzz(UnityEngine.XR.XRNode.RightHand, 0.2f, 0.03f);
                    }
                    if (stickY != 0)
                    {
                        participantNumber = Mathf.Max(1, participantNumber + stickY);
                        RefreshOrderPreview();
                        Buzz(UnityEngine.XR.XRNode.RightHand, 0.2f, 0.03f);
                    }
                    if (main) CalibrateAndStartSession();
                    break;

                case State.Ready:
                    if (main) StartTrial();
                    else if (recal) { Calibrate(); LogCalibration("recalibrate"); Flash("Recalibrated."); }
                    else if (abort)
                    {
                        if (_endConfirmTimer > 0f) { _logger?.LogEvent(participantNumber, "session_ended_early", $"after {_seqPos} of {_order.Count} trials"); FinishSession(); }
                        else { _endConfirmTimer = 3f; Flash("Press B again to END the session early."); }
                    }
                    break;

                case State.InterTrial:
                    if (main) StartTrial();
                    else if (abort) { _state = State.Ready; Flash("Paused. Press A to continue."); }
                    else if (recal) { Calibrate(); LogCalibration("recalibrate"); _state = State.Ready; Flash("Recalibrated."); }
                    break;

                case State.Running:
                    if (abort) AbortTrial();
                    break;

                case State.Finished:
                    if (main) { participantNumber++; EnterSetup(); }
                    break;
            }
        }

        // ------------------------------------------------------------------ session flow

        void EnterSetup()
        {
            DestroyCharacter();
            _state = State.Setup;
            _calibrated = false;
            _trackingChanged = false;
            BuildOrderOptions();
            _seed = config != null && config.shuffleSeed != 0 ? config.shuffleSeed : Random.Range(1, int.MaxValue);
            RefreshOrderPreview();
        }

        void BuildOrderOptions()
        {
            OrderOption previous = _orderOptions.Count > 0 ? _orderOptions[_orderOptionIndex] : default;
            bool hadPrevious = _orderOptions.Count > 0;

            _orderOptions.Clear();
            if (config == null) return;
            _orderOptions.Add(new OrderOption { mode = OrderMode.AsListed });
            _orderOptions.Add(new OrderOption { mode = OrderMode.Shuffled });
            _orderOptions.Add(new OrderOption { mode = OrderMode.LatinSquare });
            for (int i = 0; i < config.customOrders.Count; i++)
                _orderOptions.Add(new OrderOption { mode = OrderMode.Custom, customIndex = i });

            OrderOption wanted = hadPrevious ? previous : new OrderOption { mode = config.defaultOrderMode, customIndex = config.defaultCustomOrderIndex };
            _orderOptionIndex = 0;
            for (int i = 0; i < _orderOptions.Count; i++)
                if (_orderOptions[i].mode == wanted.mode && (wanted.mode != OrderMode.Custom || _orderOptions[i].customIndex == wanted.customIndex))
                    _orderOptionIndex = i;
        }

        void RefreshOrderPreview()
        {
            _warnings.Clear();
            if (config == null || _orderOptions.Count == 0) { _order = new List<int>(); return; }
            var opt = _orderOptions[_orderOptionIndex];
            _order = config.BuildOrder(opt.mode, opt.customIndex, participantNumber, _seed, _warnings);
        }

        string OrderLabel()
        {
            if (_orderOptions.Count == 0) return "-";
            var opt = _orderOptions[_orderOptionIndex];
            switch (opt.mode)
            {
                case OrderMode.AsListed: return "As listed";
                case OrderMode.Shuffled: return $"Shuffled (seed {_seed})";
                case OrderMode.LatinSquare:
                    int n = Mathf.Max(1, config.trials.Count);
                    int rows = n % 2 == 0 ? n : 2 * n;
                    return $"Latin square (row {(participantNumber - 1) % rows + 1} of {rows})";
                case OrderMode.Custom: return "Custom: " + config.customOrders[opt.customIndex].name;
            }
            return opt.mode.ToString();
        }

        string OrderIds(int maxItems = 14)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _order.Count && i < maxItems; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(config.trials[_order[i]].id);
            }
            if (_order.Count > maxItems) sb.Append($", ... (+{_order.Count - maxItems})");
            return sb.ToString();
        }

        void CalibrateAndStartSession()
        {
            RefreshOrderPreview();
            if (_order.Count == 0) { Flash("No trials to run - check the config / order."); return; }

            Calibrate();
            _seqPos = 0;
            _attempts = new int[_order.Count];

            _logger?.EndSession();
            _logger = new MotionLogger(config.europeanExcelFormat);
            try
            {
                _logger.BeginSession(participantNumber, BuildSessionInfo());
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                Flash("Could not create data files: " + e.Message);
                _logger = null;
                return;
            }
            _lastSessionFolder = _logger.SessionFolder;
            _logger.LogEvent(participantNumber, "session_start", $"order={OrderLabel()}; trials={_order.Count}");
            LogCalibration("calibrate");
            SubscribeTrackingEvents();
            _state = State.Ready;
            Debug.Log($"[ApproachStudy] Session started. Data folder: {_lastSessionFolder}");
        }

        void Calibrate()
        {
            Vector3 hp = head.position;
            _floorY = EstimateFloorHeight(hp.y, out _floorSource) + floorHeightAdjust;
            _calibPoint = new Vector3(hp.x, _floorY, hp.z);

            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) { f = head.up; f.y = 0f; }   // looking straight down
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            _calibForward = f.normalized;

            _calibrated = true;
            _trackingChanged = false;
            _spotMarker.transform.SetPositionAndRotation(_calibPoint, Quaternion.LookRotation(_calibForward, Vector3.up));
            Buzz(UnityEngine.XR.XRNode.RightHand, 0.5f, 0.12f);
        }

        float EstimateFloorHeight(float headY, out string source)
        {
            if (useScannedFloor && planeManager != null)
            {
                bool found = false;
                float lowest = float.MaxValue;
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
                    float y = plane.transform.position.y;
                    if (y > headY - 0.8f) continue;   // ignore tables, beds etc.
                    if (y < lowest) { lowest = y; found = true; }
                }
                if (found) { source = "scanned floor plane"; return lowest; }
            }
            if (xrOrigin != null) { source = "XR Origin height"; return xrOrigin.transform.position.y; }
            source = "world y = 0";
            return 0f;
        }

        void LogCalibration(string evt)
        {
            _logger?.LogEvent(participantNumber, evt, string.Format(CultureInfo.InvariantCulture,
                "spot=({0:0.000} {1:0.000} {2:0.000}) forward=({3:0.000} {4:0.000}) floor={5}",
                _calibPoint.x, _calibPoint.y, _calibPoint.z, _calibForward.x, _calibForward.z, _floorSource));
        }

        void StartTrial()
        {
            if (_seqPos >= _order.Count) { FinishSession(); return; }
            if (_logger == null) { Flash("No session open."); return; }
            DestroyCharacter();

            var def = config.trials[_order[_seqPos]];
            _attempts[_seqPos]++;

            // dir: from the participant towards where the character starts. right: participant's right when facing the character.
            Vector3 dir = Quaternion.Euler(0f, def.approachAngle, 0f) * _calibForward;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 target = _calibPoint + right * def.lateralOffset;
            Vector3 start = target + dir * def.startDistance;
            Vector3 end = def.endBehaviour == EndBehaviour.WalkPastThenVanish
                ? target - dir * def.walkPastDistance
                : target + dir * Mathf.Min(def.stopDistance, def.startDistance);

            _character = ApproachCharacter.Spawn(def, start, end, characterMaterial);

            _ctx = new TrialContext
            {
                participant = participantNumber,
                sequenceNumber = _seqPos + 1,
                sequenceLength = _order.Count,
                attempt = _attempts[_seqPos],
                def = def.Clone(),
                modelName = _character.ModelName,
                calibPoint = _calibPoint,
                calibForward = _calibForward,
                approachDir = dir,
                floorY = _floorY,
            };
            _logger.BeginTrial(_ctx);
            _logger.LogEvent(participantNumber, "trial_start", $"seq={_ctx.sequenceNumber} id={def.id} attempt={_ctx.attempt}");

            _trialTime = 0f;
            _sampleTimer = 0f;
            _characterDone = false;
            _state = State.Running;
            RecordSample();
            Buzz(UnityEngine.XR.XRNode.RightHand, 0.3f, 0.05f);
        }

        void UpdateTrial(float dt)
        {
            _trialTime += dt;
            _character.Tick(dt);

            if (!_characterDone && _character.CurrentPhase == ApproachCharacter.Phase.Finished)
            {
                _characterDone = true;
                _postTimer = config.postTrialRecordTime;
                _logger.LogEvent(participantNumber, "character_done", $"t={_trialTime.ToString("0.000", CultureInfo.InvariantCulture)}");
            }

            float interval = config.sampleRate > 0f ? 1f / config.sampleRate : 0f;
            _sampleTimer += dt;
            if (interval <= 0f || _sampleTimer >= interval)
            {
                _sampleTimer = interval > 0f ? _sampleTimer % interval : 0f;
                RecordSample();
            }

            if (_characterDone)
            {
                _postTimer -= dt;
                if (_postTimer <= 0f) CompleteTrial();
            }
        }

        void RecordSample()
        {
            _logger.Sample(_trialTime, _character.CurrentPhase.ToString(), head.position, head.rotation,
                _character.IsVisible, _character.transform.position);
        }

        void CompleteTrial()
        {
            _logger.EndTrial(aborted: false);
            _logger.LogEvent(participantNumber, "trial_end", $"seq={_ctx.sequenceNumber} id={_ctx.def.id}");
            if (_ctx.def.endBehaviour != EndBehaviour.StopAndStay) DestroyCharacter();

            _seqPos++;
            Buzz(UnityEngine.XR.XRNode.RightHand, 0.3f, 0.05f);
            if (_seqPos >= _order.Count) FinishSession();
            else if (config.autoAdvance) { _state = State.InterTrial; _interTimer = config.interTrialInterval; }
            else _state = State.Ready;
        }

        void AbortTrial()
        {
            _logger.EndTrial(aborted: true);
            _logger.LogEvent(participantNumber, "trial_aborted", $"seq={_ctx.sequenceNumber} id={_ctx.def.id} attempt={_ctx.attempt}");
            DestroyCharacter();
            _state = State.Ready;
            Flash("Trial aborted - it will be run again.");
            Buzz(UnityEngine.XR.XRNode.RightHand, 0.9f, 0.3f);
        }

        void FinishSession()
        {
            DestroyCharacter();
            if (_logger != null)
            {
                _logger.LogEvent(participantNumber, "session_end", $"completed={Mathf.Min(_seqPos, _order.Count)} of {_order.Count}");
                _logger.EndSession();
                _logger = null;
            }
            _state = State.Finished;
            Buzz(UnityEngine.XR.XRNode.RightHand, 0.6f, 0.4f);
        }

        void DestroyCharacter()
        {
            if (_character != null) Destroy(_character.gameObject);
            _character = null;
        }

        string BuildSessionInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Approach Study session");
            sb.AppendLine($"date: {System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"participant: {participantNumber}");
            sb.AppendLine($"config: {config.name}");
            sb.AppendLine($"order_mode: {OrderLabel()}");
            sb.AppendLine($"seed: {_seed}");
            sb.AppendLine($"repetitions: {config.repetitions}");
            sb.AppendLine($"order: {OrderIds(int.MaxValue)}");
            sb.AppendLine($"sample_rate: {(config.sampleRate > 0 ? config.sampleRate.ToString(CultureInfo.InvariantCulture) + " Hz" : "every frame")}");
            sb.AppendLine($"post_trial_record_time_s: {config.postTrialRecordTime.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"floor_source: {_floorSource}");
            sb.AppendLine($"device: {SystemInfo.deviceModel}");
            sb.AppendLine($"app_version: {Application.version}");
            sb.AppendLine($"unity_version: {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("Trials:");
            foreach (var t in config.trials)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: model={1} start={2}m lateral={3}m angle={4}deg speed={5}m/s stop={6}m end={7} hold={8}s walkPast={9}m scale={10} delay={11}s modelOffset={12} modelRot={13}",
                    t.id, t.ModelName, t.startDistance, t.lateralOffset, t.approachAngle, t.speed, t.stopDistance,
                    t.endBehaviour, t.holdTime, t.walkPastDistance, t.scale, t.startDelay, t.modelPositionOffset, t.modelRotationOffset));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ tracking resets

        void SubscribeTrackingEvents()
        {
            foreach (var s in _inputSubsystems) if (s != null) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _inputSubsystems.Clear();
            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (var s in _inputSubsystems) s.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        void OnTrackingOriginUpdated(UnityEngine.XR.XRInputSubsystem subsystem)
        {
            if (!_calibrated) return;
            _trackingChanged = true;
            _logger?.LogEvent(participantNumber, "tracking_origin_changed", "headset recentred or tracking reset - recalibration recommended");
        }

        // ------------------------------------------------------------------ visuals

        void Flash(string message)
        {
            _flash = message;
            _flashTimer = 4f;
        }

        void UpdateVisuals()
        {
            bool running = _state == State.Running;
            if (_status != null)
            {
                _status.SetVisible(showStatusPanel && _panelVisible && !(running && hideStatusDuringTrials));
                _status.SetText(BuildStatusText());
            }
            if (_spotMarker != null)
                _spotMarker.SetActive(showSpotMarker && _calibrated && !(running && hideSpotMarkerDuringTrials));
        }

        string BuildStatusText()
        {
            if (config == null) return "<b>No Experiment Config assigned</b>\nSelect the Experiment Controller and drag a config into 'Config'.";
            if (config.trials.Count == 0) return "<b>The Experiment Config has no trials.</b>";

            var sb = new StringBuilder();
            string p = $"P{participantNumber:000}";
            switch (_state)
            {
                case State.Setup:
                    sb.AppendLine("<b>APPROACH STUDY - SETUP</b>");
                    sb.AppendLine($"Participant: <b>{p}</b>   <size=75%>(right stick up/down)</size>");
                    sb.AppendLine($"Order: <b>{OrderLabel()}</b>   <size=75%>(right stick left/right)</size>");
                    sb.AppendLine($"<size=80%>Trials ({_order.Count}): {OrderIds()}</size>");
                    foreach (var w in _warnings) sb.AppendLine($"<color=#FFC040><size=80%>{w}</size></color>");
                    sb.AppendLine();
                    sb.AppendLine("Participant stands on their spot, looking straight ahead.");
                    sb.AppendLine("<b>A</b>: calibrate & start     <b>Y</b>: hide panel");
                    break;

                case State.Ready:
                {
                    var def = config.trials[_order[_seqPos]];
                    sb.AppendLine($"<b>{p}</b>   <size=80%>{OrderLabel()}   floor: {_floorSource}</size>");
                    sb.AppendLine($"Next: trial <b>{_seqPos + 1} of {_order.Count}</b> - <b>{def.id}</b>" +
                                  (_attempts[_seqPos] > 0 ? $" (attempt {_attempts[_seqPos] + 1})" : ""));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<size=80%>{0}  {1:0.##} m  {2:0.##} m/s  offset {3:0.##} m  angle {4:0}  scale {5:0.##}</size>",
                        def.ModelName, def.startDistance, def.speed, def.lateralOffset, def.approachAngle, def.scale));
                    sb.AppendLine();
                    sb.AppendLine("<b>A</b>: send character    <b>X</b>: recalibrate");
                    sb.AppendLine("<b>B</b> twice: end session    <b>Y</b>: hide panel");
                    break;
                }

                case State.Running:
                    sb.AppendLine($"<b>{p}</b>  Trial {_seqPos + 1} of {_order.Count} - <b>{_ctx.def.id}</b>  [{_character.CurrentPhase}]");
                    sb.AppendLine("<b>B</b>: abort & redo this trial");
                    break;

                case State.InterTrial:
                    sb.AppendLine($"<b>{p}</b>  Next trial in {Mathf.Max(0f, _interTimer):0.0} s");
                    sb.AppendLine("<b>A</b>: send now    <b>B</b>: pause    <b>X</b>: recalibrate");
                    break;

                case State.Finished:
                    sb.AppendLine($"<b>Session complete - {p}</b>");
                    sb.AppendLine("<size=70%>Data saved to:</size>");
                    sb.AppendLine($"<size=60%>{_lastSessionFolder}</size>");
                    sb.AppendLine();
                    sb.AppendLine($"<b>A</b>: set up next participant (P{participantNumber + 1:000})");
                    break;
            }

            if (_trackingChanged && _calibrated)
                sb.AppendLine("<color=#FFC040>Tracking was reset (recentred?). Press X to recalibrate.</color>");
            if (_flashTimer > 0f && !string.IsNullOrEmpty(_flash))
                sb.AppendLine($"<color=#80D0FF>{_flash}</color>");
            return sb.ToString();
        }

        GameObject CreateSpotMarker()
        {
            var root = new GameObject("CalibratedSpotMarker");
            var green = new Color(0.2f, 0.85f, 0.4f);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.003f, 0f);
            disc.transform.localScale = new Vector3(0.5f, 0.0025f, 0.5f);
            ApplyMaterial(disc.GetComponent<Renderer>(), markerMaterial, green);

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "FacingArrow";
            Destroy(arrow.GetComponent<Collider>());
            arrow.transform.SetParent(root.transform, false);
            arrow.transform.localPosition = new Vector3(0f, 0.006f, 0.4f);
            arrow.transform.localScale = new Vector3(0.05f, 0.006f, 0.3f);
            ApplyMaterial(arrow.GetComponent<Renderer>(), markerMaterial, green);
            return root;
        }

        void Buzz(UnityEngine.XR.XRNode node, float amplitude, float duration)
        {
            if (!controllerHaptics) return;
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid) device.SendHapticImpulse(0u, amplitude, duration);
        }

        void OnDrawGizmos()
        {
            if (!_calibrated) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_calibPoint, 0.25f);
            Gizmos.DrawLine(_calibPoint, _calibPoint + _calibForward);
            if (_character != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(_character.PathStart, _character.PathEnd);
            }
        }
    }
}
