using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
#if STILLWRONG_INPUTSYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
using StillWrong.Core;
using StillWrong.Oracle;
namespace StillWrong.Recording
{
    public sealed class RingFrameRecord
    {
        public int FrameIndex { get; set; }
        public double Timestamp { get; set; }
        public double DeltaTime { get; set; }
        public bool IsDroppedFrame { get; set; }
        public Vector3 CameraPosition { get; set; }
        public Quaternion CameraRotation { get; set; }
        public Vector2 PointerPosition { get; set; }
        public int MouseButtonMask { get; set; }
        public Texture2D DownscaledFrame { get; set; }
        public double UnscaledTime { get; set; }
        public float TimeScale { get; set; }
        public byte[] JpegBytes { get; set; }
        public double EncodeMs { get; set; }
        public double RegistryMs { get; set; }
        public double CaptureMs { get; set; }
        public double ReadbackMs { get; set; }
        public List<RegistryEntry> Registry { get; set; } = new List<RegistryEntry>();
        public string FrameFileName { get; set; } = string.Empty;
    }
    public sealed class UnityTemporalRingBuffer : MonoBehaviour
    {
        public const int DefaultCapacity = 300;
        public const int FrameWidth = 480;
        public const int FrameHeight = 270;
        public const int JpegQuality = 75;
        private static UnityTemporalRingBuffer _instance;
        public static UnityTemporalRingBuffer Instance => _instance;
        [SerializeField] private int _capacity = DefaultCapacity;
        [SerializeField] private Camera _recordingCamera;
        [SerializeField] private List<string> _axisNames = new List<string> { "Horizontal", "Vertical", "Mouse X", "Mouse Y", "Mouse ScrollWheel" };
        private RingFrameRecord[] _buffer;
        private int _head = 0;
        private int _count = 0;
        private bool _isFrozen = false;
        private bool _isRecording = true;
        private bool _freezePending = false;
        private long _overwrites = 0;
        private long _readbackDrops = 0;
        private long _encodeErrors = 0;
        private float _previousCaptureDelta = 0.0f;
        private string _startRandomState = string.Empty;
        private int _startFrame = 0;
        private string _sessionId = string.Empty;
        private float _lastTimeScale = 1.0f;
        private string _lastExportPath = string.Empty;
        private readonly List<SceneEventEntry> _sceneEvents = new List<SceneEventEntry>();
        private readonly List<InputEventRecord> _inputEvents = new List<InputEventRecord>();
        private readonly Stack<byte[]> _pool = new Stack<byte[]>();
        private RenderTexture _captureFull;
        private RenderTexture _captureSmallA;
        private RenderTexture _captureSmallB;
        private int _captureSmallIndex;
        private int _captureSmallInFlight = -1;
        private double _readbackRequestTime;
        private bool _readbackPending = false;
        private Coroutine _captureLoop;
        private int _framesRecorded = 0;
        private RingFrameRecord _pendingRecord;
        private static readonly int[] ButtonBits = new int[] { 1, 2, 4 };
        private Texture2D _encodeTexture;
        private bool _exportRunning = false;
        public bool IsFrozen => _isFrozen;
        public bool IsRecording
        {
            get => _isRecording;
            set => _isRecording = value;
        }
        public int Count => _count;
        public int Capacity => _capacity;
        public int DroppedFramesCount => (int)_readbackDrops;
        public long Overwrites => _overwrites;
        public long ReadbackDrops => _readbackDrops;
        public long EncodeErrors => _encodeErrors;
        public IReadOnlyList<string> AxisNames => _axisNames;
        public string SessionId => _sessionId;
        public string LastExportPath => _lastExportPath;
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            InitializeBuffer();
        }
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            if (_captureLoop != null)
            {
                StopCoroutine(_captureLoop);
                _captureLoop = null;
            }
            _isRecording = false;
            Time.captureDeltaTime = _previousCaptureDelta;
            ReleaseResources();
#if STILLWRONG_INPUTSYSTEM
            InputSystem.onEvent -= HandleInputEvent;
#endif
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }
        public void Configure(int capacity, Camera cam, List<string> axisNames)
        {
            _capacity = capacity > 0 ? capacity : DefaultCapacity;
            if (cam != null)
            {
                _recordingCamera = cam;
            }
            if (axisNames != null)
            {
                _axisNames = new List<string>(axisNames);
            }
            InitializeBuffer();
        }
        public void Initialize(int capacity = DefaultCapacity, Camera cam = null, bool captureTextures = true)
        {
            Configure(capacity, cam, null);
        }
        public void StartRecording()
        {
            if (_isRecording && _captureLoop != null)
            {
                return;
            }
            InitializeBuffer();
            _sceneEvents.Clear();
            _inputEvents.Clear();
            _overwrites = 0;
            _readbackDrops = 0;
            _encodeErrors = 0;
            _framesRecorded = 0;
            _freezePending = false;
            _exportRunning = false;
            _lastExportPath = string.Empty;
            _startRandomState = JsonUtility.ToJson(UnityEngine.Random.state);
            _startFrame = Time.frameCount;
            _sessionId = "session-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            _lastTimeScale = Time.timeScale;
            _previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1.0f / 60.0f;
            Scene active = SceneManager.GetActiveScene();
            SceneEventEntry startEntry = new SceneEventEntry();
            startEntry.Frame = Time.frameCount;
            startEntry.Kind = "start";
            startEntry.SceneName = active.name;
            startEntry.ScenePath = active.path;
            _sceneEvents.Add(startEntry);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
#if STILLWRONG_INPUTSYSTEM
            InputSystem.onEvent += HandleInputEvent;
#endif
            _isRecording = true;
            _isFrozen = false;
            if (_captureLoop == null)
            {
                _captureLoop = StartCoroutine(CaptureLoop());
            }
        }
        private void StopCapture()
        {
            if (_captureLoop != null)
            {
                StopCoroutine(_captureLoop);
                _captureLoop = null;
            }
            _isRecording = false;
        }
        private void InitializeBuffer()
        {
            _buffer = new RingFrameRecord[_capacity];
            _head = 0;
            _count = 0;
            if (_recordingCamera == null)
            {
                _recordingCamera = Camera.main;
            }
            if (_captureSmallA == null)
            {
                _captureSmallA = new RenderTexture(FrameWidth, FrameHeight, 0, RenderTextureFormat.ARGB32);
            }
            if (_captureSmallB == null)
            {
                _captureSmallB = new RenderTexture(FrameWidth, FrameHeight, 0, RenderTextureFormat.ARGB32);
            }
        }
        private void ReleaseResources()
        {
            ReleaseCaptureTarget(ref _captureFull);
            ReleaseCaptureTarget(ref _captureSmallA);
            ReleaseCaptureTarget(ref _captureSmallB);
            _captureSmallInFlight = -1;
            if (_encodeTexture != null)
            {
                Destroy(_encodeTexture);
                _encodeTexture = null;
            }
        }
        private static void ReleaseCaptureTarget(ref RenderTexture target)
        {
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
                target = null;
            }
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                ToggleFreeze();
                return;
            }
#if STILLWRONG_INPUTSYSTEM
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                ToggleFreeze();
            }
#endif
        }
        private IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (!_isRecording)
                {
                    continue;
                }
                if (_freezePending)
                {
                    _freezePending = false;
                    if (!_exportRunning)
                    {
                        _exportRunning = true;
                        yield return ExportSessionRoutine();
                        _exportRunning = false;
                    }
                    _isFrozen = true;
                    _isRecording = false;
                    Time.captureDeltaTime = _previousCaptureDelta;
                    continue;
                }
                if (_isFrozen)
                {
                    continue;
                }
                CaptureEndOfFrame();
            }
        }
        private void CaptureEndOfFrame()
        {
            Camera activeCam = _recordingCamera != null ? _recordingCamera : Camera.main;
            if (activeCam == null)
            {
                return;
            }
            EnsureFullCaptureTarget();
            if (_captureFull == null || _captureSmallA == null || _captureSmallB == null)
            {
                return;
            }
            RingFrameRecord record = new RingFrameRecord();
            record.FrameIndex = Time.frameCount;
            record.Timestamp = Time.realtimeSinceStartupAsDouble;
            record.DeltaTime = Time.deltaTime;
            record.UnscaledTime = Time.unscaledTime;
            record.TimeScale = Time.timeScale;
            record.IsDroppedFrame = false;
            record.CameraPosition = activeCam.transform.position;
            record.CameraRotation = activeCam.transform.rotation;
            record.PointerPosition = CurrentPointer();
            record.MouseButtonMask = CurrentButtonMask();
            record.DownscaledFrame = null;
            System.Diagnostics.Stopwatch registryClock = System.Diagnostics.Stopwatch.StartNew();
            record.Registry = CollectRegistry(activeCam);
            registryClock.Stop();
            record.RegistryMs = registryClock.Elapsed.TotalMilliseconds;
            RecordLegacyInput(record.FrameIndex);
            int smallIndex = _captureSmallIndex;
            RenderTexture smallTarget = smallIndex == 0 ? _captureSmallA : _captureSmallB;
            if (_readbackPending && _captureSmallInFlight == smallIndex)
            {
                _readbackDrops++;
                record.IsDroppedFrame = true;
                record.CaptureMs = 0.0;
                record.ReadbackMs = 0.0;
            }
            else
            {
                System.Diagnostics.Stopwatch captureClock = System.Diagnostics.Stopwatch.StartNew();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureFull);
                Graphics.Blit(_captureFull, smallTarget);
                captureClock.Stop();
                record.CaptureMs = captureClock.Elapsed.TotalMilliseconds;
                _readbackPending = true;
                _captureSmallInFlight = smallIndex;
                _captureSmallIndex ^= 1;
                _pendingRecord = record;
                _readbackRequestTime = Time.realtimeSinceStartupAsDouble;
                AsyncGPUReadback.Request(smallTarget, 0, TextureFormat.RGB24, OnReadbackComplete);
            }
            PushRecord(record);
            _framesRecorded++;
        }
        private void EnsureFullCaptureTarget()
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0)
            {
                return;
            }
            if (_captureFull != null && (_captureFull.width != w || _captureFull.height != h))
            {
                ReleaseCaptureTarget(ref _captureFull);
            }
            if (_captureFull == null)
            {
                _captureFull = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            }
        }
        private static Vector2 CurrentPointer()
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                return mouse.position.ReadValue();
            }
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
        private static bool CurrentButton(int button)
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            if (button == 0)
            {
                return mouse.leftButton.isPressed;
            }
            if (button == 1)
            {
                return mouse.rightButton.isPressed;
            }
            return mouse.middleButton.isPressed;
#else
            return Input.GetMouseButton(button);
#endif
        }
        private static bool CurrentButtonDown(int button)
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            if (button == 0)
            {
                return mouse.leftButton.wasPressedThisFrame;
            }
            if (button == 1)
            {
                return mouse.rightButton.wasPressedThisFrame;
            }
            return mouse.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }
        private int CurrentButtonMask()
        {
            int mask = 0;
            for (int b = 0; b < 3; b++)
            {
                if (CurrentButton(b))
                {
                    mask |= ButtonBits[b];
                }
            }
            return mask;
        }
        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            _captureSmallInFlight = -1;
            if (_pendingRecord != null)
            {
                _pendingRecord.ReadbackMs = Math.Max(0.0, Time.realtimeSinceStartupAsDouble - _readbackRequestTime);
            }
            if (request.hasError || _pendingRecord == null)
            {
                _encodeErrors++;
                _pendingRecord = null;
                return;
            }
            var data = request.GetData<byte>();
            int expected = FrameWidth * FrameHeight * 3;
            if (data.Length != expected)
            {
                _encodeErrors++;
                _pendingRecord = null;
                return;
            }
            byte[] buffer = RentBuffer(expected);
            data.CopyTo(buffer);
            System.Diagnostics.Stopwatch encodeClock = System.Diagnostics.Stopwatch.StartNew();
            byte[] jpeg = null;
            try
            {
                if (_encodeTexture == null)
                {
                    _encodeTexture = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, false);
                }
                _encodeTexture.LoadRawTextureData(buffer);
                _encodeTexture.Apply();
                jpeg = ImageConversion.EncodeToJPG(_encodeTexture, JpegQuality);
            }
            catch (Exception)
            {
                jpeg = null;
            }
            encodeClock.Stop();
            ReturnBuffer(buffer);
            if (jpeg == null || jpeg.Length == 0)
            {
                _encodeErrors++;
            }
            else
            {
                _pendingRecord.JpegBytes = jpeg;
                _pendingRecord.EncodeMs = encodeClock.Elapsed.TotalMilliseconds;
            }
            _pendingRecord = null;
        }
        private byte[] RentBuffer(int size)
        {
            while (_pool.Count > 0)
            {
                byte[] candidate = _pool.Pop();
                if (candidate != null && candidate.Length == size)
                {
                    return candidate;
                }
            }
            return new byte[size];
        }
        private void ReturnBuffer(byte[] buffer)
        {
            if (buffer != null && _pool.Count < 8)
            {
                _pool.Push(buffer);
            }
        }
        private static bool CurrentButtonUp(int button)
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            if (button == 0)
            {
                return mouse.leftButton.wasReleasedThisFrame;
            }
            if (button == 1)
            {
                return mouse.rightButton.wasReleasedThisFrame;
            }
            return mouse.middleButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(button);
#endif
        }
        private static Vector2 CurrentScroll()
        {
#if STILLWRONG_INPUTSYSTEM
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return Vector2.zero;
            }
            return mouse.scroll.ReadValue();
#else
            return Input.mouseScrollDelta;
#endif
        }
        private void RecordLegacyInput(int frame)
        {
            InputEventRecord record = new InputEventRecord();
            record.Frame = frame;
            record.Path = "legacy";
            Vector2 pointer = CurrentPointer();
            record.PointerX = pointer.x;
            record.PointerY = pointer.y;
            int down = 0;
            int up = 0;
            for (int b = 0; b < 3; b++)
            {
                if (CurrentButtonDown(b))
                {
                    down |= ButtonBits[b];
                }
                if (CurrentButtonUp(b))
                {
                    up |= ButtonBits[b];
                }
            }
            record.ButtonDownMask = down;
            record.ButtonUpMask = up;
            Vector2 scroll = CurrentScroll();
            record.ScrollX = scroll.x;
            record.ScrollY = scroll.y;
            for (int i = 0; i < _axisNames.Count; i++)
            {
                try
                {
                    record.AxisNames.Add(_axisNames[i]);
                    #if STILLWRONG_INPUTSYSTEM
                    record.AxisValues.Add(0f);
#else
                    record.AxisValues.Add(Input.GetAxisRaw(_axisNames[i]));
#endif
                }
                catch (Exception)
                {
                }
            }
            _inputEvents.Add(record);
        }
        private List<RegistryEntry> CollectRegistry(Camera activeCam)
        {
            List<RegistryEntry> entries = new List<RegistryEntry>(256);
            UnityEngine.Object[] foundGraphics = UnityEngine.Object.FindObjectsByType<Graphic>();
            for (int i = 0; i < foundGraphics.Length; i++)
            {
                Graphic g = foundGraphics[i] as Graphic;
                if (g == null)
                {
                    continue;
                }
                GameObject go = g.gameObject;
                if (go == null || !go.activeInHierarchy || !g.enabled)
                {
                    continue;
                }
                Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(go, activeCam);
                float alpha = g.color.a;
                CanvasGroup[] groups = g.GetComponentsInParent<CanvasGroup>();
                for (int c = 0; c < groups.Length; c++)
                {
                    if (groups[c] != null)
                    {
                        alpha *= groups[c].alpha;
                    }
                }
                string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
                int sort = UnitySymptomOracleRunner.GetSortingOrder(go);
                string goId = string.Empty;
#if UNITY_EDITOR
                goId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(g).ToString();
#endif
                RegistryEntry e = new RegistryEntry();
                e.Path = path;
                e.GoId = goId;
                e.InstanceId = g.GetEntityId().GetHashCode();
                e.Type = g.GetType().Name;
                e.Space = "graphic";
                e.RectMinX = rect.XMin;
                e.RectMinY = rect.YMin;
                e.RectMaxX = rect.XMax;
                e.RectMaxY = rect.YMax;
                e.Alpha = alpha;
                e.SortOrder = sort;
                e.SiblingIndex = go.transform.GetSiblingIndex();
                e.Layer = go.layer;
                e.Active = go.activeInHierarchy;
                entries.Add(e);
            }
            UnityEngine.Object[] foundRenderers = UnityEngine.Object.FindObjectsByType<Renderer>();
            for (int i = 0; i < foundRenderers.Length; i++)
            {
                Renderer rend = foundRenderers[i] as Renderer;
                if (rend == null)
                {
                    continue;
                }
                GameObject go = rend.gameObject;
                if (go == null || !go.activeInHierarchy || !rend.enabled)
                {
                    continue;
                }
                Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(go, activeCam);
                string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
                string goId = string.Empty;
#if UNITY_EDITOR
                goId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(rend).ToString();
#endif
                RegistryEntry e = new RegistryEntry();
                e.Path = path;
                e.GoId = goId;
                e.InstanceId = rend.GetEntityId().GetHashCode();
                e.Type = rend.GetType().Name;
                e.Space = "renderer";
                e.RectMinX = rect.XMin;
                e.RectMinY = rect.YMin;
                e.RectMaxX = rect.XMax;
                e.RectMaxY = rect.YMax;
                e.Alpha = 1.0f;
                e.SortOrder = rend.sortingOrder;
                e.SiblingIndex = go.transform.GetSiblingIndex();
                e.Layer = go.layer;
                e.Active = go.activeInHierarchy;
                entries.Add(e);
            }
            UnityEngine.Object[] foundColliders = UnityEngine.Object.FindObjectsByType<Collider>();
            for (int i = 0; i < foundColliders.Length; i++)
            {
                Collider col = foundColliders[i] as Collider;
                if (col == null)
                {
                    continue;
                }
                GameObject go = col.gameObject;
                if (go == null || !go.activeInHierarchy || !col.enabled)
                {
                    continue;
                }
                Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(go, activeCam);
                string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
                string goId = string.Empty;
#if UNITY_EDITOR
                goId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(col).ToString();
#endif
                RegistryEntry e = new RegistryEntry();
                e.Path = path;
                e.GoId = goId;
                e.InstanceId = col.GetEntityId().GetHashCode();
                e.Type = col.GetType().Name;
                e.Space = "collider";
                e.RectMinX = rect.XMin;
                e.RectMinY = rect.YMin;
                e.RectMaxX = rect.XMax;
                e.RectMaxY = rect.YMax;
                e.Alpha = 1.0f;
                e.SortOrder = UnitySymptomOracleRunner.GetSortingOrder(go);
                e.SiblingIndex = go.transform.GetSiblingIndex();
                e.Layer = go.layer;
                e.Active = go.activeInHierarchy;
                entries.Add(e);
            }
            UnityEngine.Object[] foundDocs = UnityEngine.Object.FindObjectsByType<UIDocument>();
            for (int i = 0; i < foundDocs.Length; i++)
            {
                UIDocument doc = foundDocs[i] as UIDocument;
                if (doc == null)
                {
                    continue;
                }
                GameObject go = doc.gameObject;
                if (go == null || !go.activeInHierarchy || !doc.enabled)
                {
                    continue;
                }
                VisualElement root = doc.rootVisualElement;
                if (root == null)
                {
                    continue;
                }
                List<VisualElement> ves = root.Query<VisualElement>().ToList();
                string docPath = UnitySymptomOracleRunner.GetHierarchyPath(go);
                for (int v = 0; v < ves.Count; v++)
                {
                    VisualElement ve = ves[v];
                    if (ve == null || !ve.visible)
                    {
                        continue;
                    }
                    Rect wb = ve.worldBound;
                    if (wb.width <= 0.0f || wb.height <= 0.0f)
                    {
                        continue;
                    }
                    string goId = string.Empty;
#if UNITY_EDITOR
                    goId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(doc).ToString();
#endif
                    RegistryEntry e = new RegistryEntry();
                    e.Path = docPath + "/" + ve.GetType().Name + ":" + ve.name;
                    e.GoId = goId;
                    e.InstanceId = go.GetEntityId().GetHashCode();
                    e.Type = ve.GetType().Name;
                    e.Space = "uitoolkit";
                    e.RectMinX = wb.xMin;
                    e.RectMinY = wb.yMin;
                    e.RectMaxX = wb.xMax;
                    e.RectMaxY = wb.yMax;
                    e.Alpha = 1.0f;
                    e.SortOrder = (int)doc.sortingOrder;
                    e.SiblingIndex = v;
                    e.Layer = go.layer;
                    e.Active = go.activeInHierarchy;
                    entries.Add(e);
                }
            }
            return entries;
        }
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneEventEntry e = new SceneEventEntry();
            e.Frame = Time.frameCount;
            e.Kind = "load";
            e.SceneName = scene.name;
            e.ScenePath = scene.path;
            _sceneEvents.Add(e);
        }
        private void HandleSceneUnloaded(Scene scene)
        {
            SceneEventEntry e = new SceneEventEntry();
            e.Frame = Time.frameCount;
            e.Kind = "unload";
            e.SceneName = scene.name;
            e.ScenePath = scene.path;
            _sceneEvents.Add(e);
        }
#if STILLWRONG_INPUTSYSTEM
        private void HandleInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            InputEventRecord record = new InputEventRecord();
            record.Frame = Time.frameCount;
            record.Path = "inputsystem";
            record.PointerX = 0.0f;
            record.PointerY = 0.0f;
            record.ButtonDownMask = 0;
            record.ButtonUpMask = 0;
            record.ScrollX = 0.0f;
            record.ScrollY = 0.0f;
            record.DeviceId = eventPtr.deviceId;
            if (device != null)
            {
                record.Layout = device.layout;
            }
            else
            {
                record.Layout = string.Empty;
            }
            record.EventType = eventPtr.type.ToString();
            record.StateBase64 = string.Empty;
            record.StateSize = (int)eventPtr.sizeInBytes;
            _inputEvents.Add(record);
        }
#endif
        public void Freeze()
        {
            if (!_isRecording || _isFrozen || _freezePending || _exportRunning)
            {
                return;
            }
            _freezePending = true;
        }
        public void Unfreeze()
        {
            _isFrozen = false;
            _freezePending = false;
        }
        public void ToggleFreeze()
        {
            if (_isFrozen)
            {
                Unfreeze();
            }
            else if (_freezePending)
            {
                _freezePending = false;
            }
            else
            {
                Freeze();
            }
        }
        public void Clear()
        {
            if (_buffer != null)
            {
                for (int i = 0; i < _buffer.Length; i++)
                {
                    _buffer[i] = null;
                }
            }
            _head = 0;
            _count = 0;
            _overwrites = 0;
            _readbackDrops = 0;
            _encodeErrors = 0;
            _framesRecorded = 0;
            _sceneEvents.Clear();
            _inputEvents.Clear();
            _pendingRecord = null;
            _readbackPending = false;
        }
        public void PushRecord(RingFrameRecord record)
        {
            if (_buffer == null || _buffer.Length != _capacity)
            {
                InitializeBuffer();
            }
            if (record == null)
            {
                return;
            }
            if (_count == _capacity)
            {
                _overwrites++;
                _buffer[_head] = record;
                _head = (_head + 1) % _capacity;
            }
            else
            {
                int index = (_head + _count) % _capacity;
                _buffer[index] = record;
                _count++;
            }
        }
        public IReadOnlyList<RingFrameRecord> GetFrames()
        {
            List<RingFrameRecord> list = new List<RingFrameRecord>(_count);
            for (int i = 0; i < _count; i++)
            {
                int index = (_head + i) % _capacity;
                if (_buffer[index] != null)
                {
                    list.Add(_buffer[index]);
                }
            }
            return list;
        }
        public RingFrameRecord GetLatestFrame()
        {
            if (_count == 0)
            {
                return null;
            }
            int lastIndex = (_head + _count - 1) % _capacity;
            return _buffer[lastIndex];
        }
        public RingFrameRecord GetFrameAt(int chronologicalIndex)
        {
            if (chronologicalIndex < 0 || chronologicalIndex >= _count)
            {
                return null;
            }
            int index = (_head + chronologicalIndex) % _capacity;
            return _buffer[index];
        }
        private string SessionFolderPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Library", "StillWrong", "sessions", _sessionId);
        }
        private long ComputeFolderBytes(string dir)
        {
            long total = 0;
            string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo info = new FileInfo(files[i]);
                if (info.Exists)
                {
                    total += info.Length;
                }
            }
            return total;
        }
        private IEnumerator ExportSessionRoutine()
        {
            IReadOnlyList<RingFrameRecord> all = GetFrames();
            List<RingFrameRecord> kept = new List<RingFrameRecord>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].JpegBytes != null && all[i].JpegBytes.Length > 0)
                {
                    kept.Add(all[i]);
                }
            }
            string sessionDir = SessionFolderPath();
            Directory.CreateDirectory(sessionDir);
            long jpegTotal = 0;
            double encodeSum = 0.0;
            double registrySum = 0.0;
            double captureSum = 0.0;
            double readbackSum = 0.0;
            long registryTotal = 0;
            FrameIndexFile framesFile = new FrameIndexFile();
            RegistryFile registryFile = new RegistryFile();
            for (int i = 0; i < kept.Count; i++)
            {
                RingFrameRecord rec = kept[i];
                string fileName = "frame-" + rec.FrameIndex.ToString("D6") + ".jpg";
                rec.FrameFileName = fileName;
                string fullPath = Path.Combine(sessionDir, fileName);
                File.WriteAllBytes(fullPath, rec.JpegBytes);
                jpegTotal += rec.JpegBytes.Length;
                encodeSum += rec.EncodeMs;
                registrySum += rec.RegistryMs;
                captureSum += rec.CaptureMs;
                readbackSum += rec.ReadbackMs;
                int regCount = rec.Registry != null ? rec.Registry.Count : 0;
                registryTotal += regCount;
                FrameIndexEntry fe = new FrameIndexEntry();
                fe.Frame = rec.FrameIndex;
                fe.Timestamp = rec.Timestamp;
                fe.UnscaledTime = rec.UnscaledTime;
                fe.TimeScale = rec.TimeScale;
                fe.Dropped = rec.IsDroppedFrame;
                fe.JpegFile = fileName;
                fe.EncodeMs = rec.EncodeMs;
                fe.RegistryMs = rec.RegistryMs;
                fe.RegistryCount = regCount;
                framesFile.Frames.Add(fe);
                RegistryFrame rf = new RegistryFrame();
                rf.Frame = rec.FrameIndex;
                if (rec.Registry != null)
                {
                    rf.Elements = rec.Registry;
                }
                registryFile.Frames.Add(rf);
            }
            byte[] pngBytes = null;
            int markW = Screen.width;
            int markH = Screen.height;
            if (markW > 0 && markH > 0)
            {
                RenderTexture markTarget = new RenderTexture(markW, markH, 0, RenderTextureFormat.ARGB32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(markTarget);
                bool markDone = false;
                byte[] markRaw = null;
                bool markError = false;
                AsyncGPUReadback.Request(markTarget, 0, TextureFormat.RGB24, (AsyncGPUReadbackRequest req) =>
                {
                    if (req.hasError)
                    {
                        markError = true;
                        markDone = true;
                        return;
                    }
                    var data = req.GetData<byte>();
                    int expected = markW * markH * 3;
                    if (data.Length != expected)
                    {
                        markError = true;
                        markDone = true;
                        return;
                    }
                    markRaw = new byte[expected];
                    data.CopyTo(markRaw);
                    markDone = true;
                });
                float waitStart = Time.realtimeSinceStartup;
                while (!markDone && Time.realtimeSinceStartup - waitStart < 5.0f)
                {
                    yield return null;
                }
                Destroy(markTarget);
                if (!markError && markRaw != null)
                {
                    Texture2D markTex = new Texture2D(markW, markH, TextureFormat.RGB24, false);
                    markTex.LoadRawTextureData(markRaw);
                    markTex.Apply();
                    pngBytes = ImageConversion.EncodeToPNG(markTex);
                    Destroy(markTex);
                }
            }
            if (pngBytes == null)
            {
                pngBytes = new byte[0];
                _encodeErrors++;
            }
            string markedPath = Path.Combine(sessionDir, "marked.png");
            File.WriteAllBytes(markedPath, pngBytes);
            string framesJson = JsonUtility.ToJson(framesFile, true);
            File.WriteAllText(Path.Combine(sessionDir, "frames.json"), framesJson);
            string registryJson = JsonUtility.ToJson(registryFile, true);
            File.WriteAllText(Path.Combine(sessionDir, "registry.json"), registryJson);
            InputEventFile inputFile = new InputEventFile();
            inputFile.Events = _inputEvents;
            string inputJson = JsonUtility.ToJson(inputFile, true);
            File.WriteAllText(Path.Combine(sessionDir, "inputs.json"), inputJson);
            SceneEventFile sceneFile = new SceneEventFile();
            sceneFile.Events = _sceneEvents;
            string sceneJson = JsonUtility.ToJson(sceneFile, true);
            File.WriteAllText(Path.Combine(sessionDir, "scenes.json"), sceneJson);
            Scene activeScene = SceneManager.GetActiveScene();
            SessionManifest manifest = new SessionManifest();
            manifest.Id = _sessionId;
            manifest.UnityVersion = Application.unityVersion;
            manifest.SceneName = activeScene.name;
            manifest.ScenePath = activeScene.path;
            manifest.FrameWidth = FrameWidth;
            manifest.FrameHeight = FrameHeight;
            manifest.GridRate = 60;
            manifest.StartRandomState = _startRandomState;
            manifest.StartFrame = _startFrame;
            manifest.FrameCount = kept.Count;
            manifest.JpegTotalBytes = jpegTotal;
            manifest.MeanJpegBytes = kept.Count > 0 ? (double)jpegTotal / kept.Count : 0.0;
            manifest.MeanEncodeMs = kept.Count > 0 ? encodeSum / kept.Count : 0.0;
            manifest.MeanRegistryMs = kept.Count > 0 ? registrySum / kept.Count : 0.0;
            manifest.MeanCaptureMs = kept.Count > 0 ? captureSum / kept.Count : 0.0;
            manifest.MeanReadbackMs = kept.Count > 0 ? readbackSum / kept.Count : 0.0;
            manifest.ReadbackDrops = _readbackDrops;
            manifest.Overwrites = _overwrites;
            manifest.InputEventCount = _inputEvents.Count;
            manifest.RegistryElementCount = (int)registryTotal;
            manifest.MarkedPngBytes = pngBytes.Length;
            manifest.FolderBytes = 0;
            string manifestJson = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(Path.Combine(sessionDir, "session.json"), manifestJson);
            long folderBytes = ComputeFolderBytes(sessionDir);
            manifest.FolderBytes = folderBytes;
            manifestJson = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(Path.Combine(sessionDir, "session.json"), manifestJson);
            folderBytes = ComputeFolderBytes(sessionDir);
            manifest.FolderBytes = folderBytes;
            manifestJson = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(Path.Combine(sessionDir, "session.json"), manifestJson);
            _lastExportPath = sessionDir;
        }
    }
}

