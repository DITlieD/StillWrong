using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StillWrong.Editor.Recording;
using StillWrong.Recording;
namespace StillWrong.Editor.UI
{
    public sealed partial class StillWrongBugSheet : EditorWindow
    {
        [NonSerialized]
        public Texture2D FrameTexture;
        [SerializeField]
        private string _sentence = string.Empty;
        private string _groundNote = string.Empty;
        [NonSerialized]
        private bool[,] _maskOverlay;
        [NonSerialized]
        private Texture2D _maskOverlayTex;
        private string _maskOverlayPath = string.Empty;
        private float _scrub;
        private int _selectedFrame = 2;
        private Vector2 _scrollPos;
        private float _frameCap;
        private Vector2 _reviewScroll;
        private GUIStyle _dotStyle;
        private GUIStyle _frameBoxStyle;
        private GUIStyle _pickedBoxStyle;
        private GUIStyle _dotPausedStyle;
        private GUIStyle _dotBadStyle;
        private GUIStyle _dotOffStyle;
        private double _statusCacheReal;
        private bool _statusCacheOk;
        private string _statusCacheState = string.Empty;
        private double _statusCacheRetained;
        private int _statusCacheChunks;
        private long _statusCacheBytes;
        private long _statusCacheDropped;
        [SerializeField]
        private string _checkLine = string.Empty;
        private string _returnScene = string.Empty;
        private string _sessionDir = string.Empty;
        [SerializeField]
        private List<string> _thumbFiles = new List<string>();
        [SerializeField]
        private List<double> _thumbTimes = new List<double>();
        [SerializeField]
        private List<string> _thumbLabels = new List<string>();
        [NonSerialized]
        private List<Texture2D> _thumbTextures = new List<Texture2D>();
        [SerializeField]
        private int _selectedThumb;
        [SerializeField]
        private double _retainedTotal;
        [SerializeField]
        private int _sheetScreenW;
        [SerializeField]
        private int _sheetScreenH;
        [NonSerialized]
        private List<StillWrongGroundingCandidate> _candidates = new List<StillWrongGroundingCandidate>();
        [SerializeField]
        private int _candidateIndex;
        [SerializeField]
        private string _confirmedName = string.Empty;
        [SerializeField]
        private string _confirmedPath = string.Empty;
        [SerializeField]
        private string _progressLine = string.Empty;
        private Vector2 _dragStart;
        private bool _dragging;
        [MenuItem("Tools/Still Wrong/I found a bug")]
        public static void ShowWindow()
        {
            EnsureChip();
            ShowReview();
        }
        [InitializeOnLoadMethod]
        private static void KeepChip()
        {
            EditorApplication.delayCall += EnsureChip;
        }
        public static void OpenWithFrame(Texture2D frame)
        {
            StillWrongBugSheet window = Acquire();
            window.FrameTexture = frame;
            PresentReview(window);
        }
        public static void ShowReview()
        {
            StillWrongBugSheet window = Acquire();
            PresentReview(window);
        }
        public static void ShowChange(string path)
        {
            StillWrongBugSheet window = Acquire();
            window._changePath = path ?? string.Empty;
            window._changeName = LeafName(window._changePath);
            PresentReview(window);
        }
        private static void PresentReview(StillWrongBugSheet window)
        {
            if (window == null)
            {
                return;
            }
            window.ExpandReview();
            window.Show();
            window.Repaint();
            EditorApplication.delayCall += window.FillAndRaiseChip;
        }
        private static StillWrongBugSheet Acquire()
        {
            EnsureChip();
            StillWrongBugSheet review = FindRole(false);
            if (review != null)
            {
                return review;
            }
            StillWrongBugSheet created = CreateInstance<StillWrongBugSheet>();
            created._isChip = false;
            created.titleContent = new GUIContent("Still Wrong Review");
            return created;
        }
        public static void EnsureChip()
        {
            StillWrongBugSheet chip = FindRole(true);
            if (chip != null)
            {
                chip.FitChip();
                chip.Repaint();
                HookPlayFocus();
                HideCoveringOverlays();
                return;
            }
            StillWrongBugSheet created = CreateInstance<StillWrongBugSheet>();
            created._isChip = true;
            created._expanded = false;
            created.titleContent = new GUIContent("Still Wrong");
            created.Show();
            created.FitChip();
            HookPlayFocus();
            HideCoveringOverlays();
        }
        private static StillWrongBugSheet FindRole(bool chip)
        {
            StillWrongBugSheet[] open = Resources.FindObjectsOfTypeAll<StillWrongBugSheet>();
            if (open == null)
            {
                return null;
            }
            for (int i = 0; i < open.Length; i++)
            {
                if (open[i] != null && open[i]._isChip == chip && open[i].HasNativeView())
                {
                    return open[i];
                }
            }
            return null;
        }
        private bool HasNativeView()
        {
            try
            {
                System.Reflection.FieldInfo field = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null)
                {
                    return false;
                }
                return field.GetValue(this) != null;
            }
            catch
            {
                return false;
            }
        }
        public static void RepaintAll()
        {
            StillWrongBugSheet[] open = Resources.FindObjectsOfTypeAll<StillWrongBugSheet>();
            if (open == null)
            {
                return;
            }
            for (int i = 0; i < open.Length; i++)
            {
                if (open[i] != null)
                {
                    open[i].Repaint();
                }
            }
        }
        public static void ReloadOpenReview()
        {
            StillWrongBugSheet review = FindRole(false);
            if (review == null)
            {
                return;
            }
            review.ReloadFromDisk();
        }
        private void OnEnable()
        {
            titleContent = new GUIContent(_isChip ? "Still Wrong" : "Still Wrong Review");
            if (!_isChip)
            {
                _expanded = true;
            }
            if (_isChip)
            {
                HookPlayFocus();
                EditorApplication.delayCall += HideCoveringOverlays;
                EditorApplication.delayCall += FitChipWhenReady;
            }
        }
        private void FitChipWhenReady()
        {
            if (this == null || !_isChip || !HasNativeView())
            {
                return;
            }
            FitChip();
        }
        private const string FirstRunKey = "StillWrong.FirstRunDismissed";
        private static bool FirstRunDismissed()
        {
            try
            {
                return EditorPrefs.GetBool(FirstRunKey, false);
            }
            catch
            {
                return false;
            }
        }
        private void FitChip()
        {
            if (!_isChip)
            {
                return;
            }
            PlaceCorner();
        }
        private void DrawFirstRun()
        {
            EditorGUILayout.LabelField("Still Wrong", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Click the circle so it turns red, then press Play. Red means recording is on.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2. Click I found a bug. Pick the frame. Click the broken thing. Type one sentence.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. Send sends that to the agent. The agent fixes a copy. This scene stays as it is.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("4. Agent is Muse until you change it in Tools / Still Wrong / Settings. SAM reads MODEL_API_KEY from the environment. Without that key, click selection still works and the mask step is skipped.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Install in Packages/manifest.json: \"com.stillwrong.reducer\": \"file:com.stillwrong.reducer\"", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Got it"))
            {
                try
                {
                    EditorPrefs.SetBool(FirstRunKey, true);
                }
                catch
                {
                }
                StillWrongBugSheet chip = FindRole(true);
                if (chip != null)
                {
                    chip.PlaceCorner();
                }
            }
        }
        private static Texture2D _circleTex;
        private static Texture2D CircleTex()
        {
            if (_circleTex != null)
            {
                return _circleTex;
            }
            int n = 64;
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float center = (n - 1) * 0.5f;
            float radius = center - 1f;
            Color[] pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * n + x] = dist <= radius ? Color.white : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _circleTex = tex;
            return tex;
        }
        private void PlaceCorner()
        {
            if (!HasNativeView() || docked)
            {
                return;
            }
            _expanded = false;
            _zoom = 1f;
            _pan = Vector2.zero;
            minSize = new Vector2(160, 28);
            maxSize = new Vector2(200, 40);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            position = new Rect(main.xMin + 8, main.yMax - 52, 176, 34);
        }
        private void ExpandReview()
        {
            _expanded = true;
            if (!HasNativeView())
            {
                Show();
            }
            if (!HasNativeView())
            {
                return;
            }
            minSize = new Vector2(960, 640);
            maxSize = new Vector2(10000, 10000);
            position = EditorGUIUtility.GetMainWindowPosition();
            StillWrongChangePopup[] popups = Resources.FindObjectsOfTypeAll<StillWrongChangePopup>();
            if (popups == null)
            {
                return;
            }
            for (int i = 0; i < popups.Length; i++)
            {
                if (popups[i] != null)
                {
                    popups[i].Close();
                }
            }
        }
        private void FillAndRaiseChip()
        {
            if (this == null || _isChip)
            {
                return;
            }
            if (!HasNativeView())
            {
                Show();
            }
            if (!HasNativeView())
            {
                return;
            }
            minSize = new Vector2(960, 640);
            maxSize = new Vector2(10000, 10000);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            float height = main.height - 56f;
            if (height < 640f)
            {
                height = 640f;
            }
            position = new Rect(main.x, main.y, main.width, height);
            Repaint();
            EnsureChip();
            EditorApplication.delayCall += RaiseChipOnly;
        }
        private void RaiseChipOnly()
        {
            StillWrongBugSheet chip = FindRole(true);
            if (chip == null)
            {
                return;
            }
            chip.Show();
            chip.FitChip();
            chip.Repaint();
        }
        public int SheetThumbCount()
        {
            return _thumbTextures != null ? _thumbTextures.Count : 0;
        }
        public string SheetSelectedLabel()
        {
            if (_thumbLabels != null && _selectedThumb >= 0 && _selectedThumb < _thumbLabels.Count)
            {
                return _thumbLabels[_selectedThumb];
            }
            return string.Empty;
        }
        public string SheetSelectedTime()
        {
            if (_thumbTimes != null && _selectedThumb >= 0 && _selectedThumb < _thumbTimes.Count)
            {
                return _thumbTimes[_selectedThumb].ToString("F1");
            }
            return string.Empty;
        }
        private void EnsureState()
        {
            if (_thumbFiles == null)
            {
                _thumbFiles = new List<string>();
            }
            if (_thumbTimes == null)
            {
                _thumbTimes = new List<double>();
            }
            if (_thumbLabels == null)
            {
                _thumbLabels = new List<string>();
            }
            if (_thumbTextures == null)
            {
                _thumbTextures = new List<Texture2D>();
            }
            if (_candidates == null)
            {
                _candidates = new List<StillWrongGroundingCandidate>();
            }
            if (_selectedThumb < 0)
            {
                _selectedThumb = 0;
            }
        }
        private void EnsureThumbsLoaded()
        {
            if (_thumbTextures == null)
            {
                _thumbTextures = new List<Texture2D>();
            }
            if (_fullFrame == null && _thumbFiles != null && _thumbFiles.Count > 0)
            {
                SelectedFullFrame();
            }
            if (_thumbTextures.Count == 0 && _thumbFiles != null && _thumbFiles.Count > 0 && !_thumbBatchQueued)
            {
                _thumbBatchQueued = true;
                EditorApplication.delayCall += LoadThumbBatch;
            }
            if (VisibleThumbsMissing())
            {
                LoadThumbBatch();
            }
            if (_candidates.Count == 0 && !string.IsNullOrEmpty(_confirmedPath))
            {
                SetCandidate(_confirmedName, _confirmedPath);
            }
        }
        private void ThumbSpan(int count, out int start, out int window)
        {
            int fit = Mathf.FloorToInt((position.width - 24f) / 186f);
            if (fit < 1)
            {
                fit = 1;
            }
            if (fit > 8)
            {
                fit = 8;
            }
            window = count < fit ? count : fit;
            start = _selectedThumb - window / 2;
            if (start < 0)
            {
                start = 0;
            }
            if (start + window > count)
            {
                start = count - window;
            }
            if (start < 0)
            {
                start = 0;
            }
        }
        private bool VisibleThumbsMissing()
        {
            if (_thumbFiles == null || _thumbFiles.Count == 0)
            {
                return false;
            }
            if (_thumbTextures == null)
            {
                return true;
            }
            int count = _thumbFiles.Count;
            int start;
            int window;
            ThumbSpan(count, out start, out window);
            for (int k = 0; k < window; k++)
            {
                int i = start + k;
                if (i >= _thumbTextures.Count || _thumbTextures[i] == null)
                {
                    return true;
                }
            }
            return false;
        }
        private void LoadThumbBatch()
        {
            _thumbBatchQueued = false;
            if (_thumbFiles == null)
            {
                return;
            }
            while (_thumbTextures.Count < _thumbFiles.Count)
            {
                _thumbTextures.Add(null);
            }
            int center = _selectedThumb;
            if (center < 0 || center >= _thumbFiles.Count)
            {
                center = _thumbFiles.Count - 1;
            }
            int added = 0;
            int spanStart;
            int spanCount;
            ThumbSpan(_thumbFiles.Count, out spanStart, out spanCount);
            for (int dist = 0; dist < _thumbFiles.Count && added < spanCount; dist++)
            {
                int slot = center + (dist % 2 == 0 ? dist / 2 : -((dist / 2) + 1));
                if (slot < 0 || slot >= _thumbFiles.Count || _thumbTextures[slot] != null)
                {
                    continue;
                }
                _thumbTextures[slot] = LoadTextureFile(_thumbFiles[slot]);
                added++;
            }
            int windowStart = center - 4;
            if (windowStart < 0)
            {
                windowStart = 0;
            }
            int windowEnd = windowStart + 8;
            if (windowEnd > _thumbFiles.Count)
            {
                windowEnd = _thumbFiles.Count;
            }
            bool pending = false;
            for (int slot = windowStart; slot < windowEnd; slot++)
            {
                if (_thumbTextures[slot] == null)
                {
                    pending = true;
                    break;
                }
            }
            if (pending)
            {
                _thumbBatchQueued = true;
                EditorApplication.delayCall += LoadThumbBatch;
            }
            Repaint();
        }
        private static void HookPlayFocus()
        {
            if (_focusHooked)
            {
                return;
            }
            _focusHooked = true;
            EditorApplication.playModeStateChanged += OnPlayFocus;
            _editorContext = SynchronizationContext.Current;
            if (_circleTimer == null)
            {
                _circleTimer = new Timer(QueueCirclePoll, null, 50, 50);
            }
        }
        private static void QueueCirclePoll(object state)
        {
            if (_circlePollQueued)
            {
                return;
            }
            _circlePollQueued = true;
            SynchronizationContext ctx = _editorContext;
            if (ctx != null)
            {
                ctx.Post(RunCirclePoll, null);
                return;
            }
            EditorApplication.delayCall += RunCirclePollOnDelay;
        }
        private static void RunCirclePollOnDelay()
        {
            RunCirclePoll(null);
        }
        private static void RunCirclePoll(object state)
        {
            _circlePollQueued = false;
            PollCircleClick();
        }
        private static void OnPlayFocus(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }
            _overlaysSettled = false;
            StillWrongBugSheet chip = FindRole(true);
            if (chip != null)
            {
                chip.Focus();
            }
        }
        private static void HideCoveringOverlays()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (_overlaysSettled)
            {
                return;
            }
            StillWrongOverlayHider.Hide(StillWrongSettings.HiddenOverlays());
            _overlaysSettled = true;
        }
        private void OnFocus()
        {
        }
        [DllImport("libX11")]
        private static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport("libX11")]
        private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11")]
        private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        private static bool CursorRoot(out int x, out int y, out uint mask)
        {
            x = 0;
            y = 0;
            mask = 0;
            if (_xDisplay == IntPtr.Zero)
            {
                _xDisplay = XOpenDisplay(IntPtr.Zero);
            }
            if (_xDisplay == IntPtr.Zero)
            {
                return false;
            }
            IntPtr root;
            IntPtr child;
            int winX;
            int winY;
            return XQueryPointer(_xDisplay, XDefaultRootWindow(_xDisplay), out root, out child, out x, out y, out winX, out winY, out mask);
        }
        private static void PollCircleClick()
        {
            if (!EditorApplication.isPlaying)
            {
                HideCoveringOverlays();
            }
            else
            {
                if (!_overlaysSettled)
                {
                    StillWrongOverlayHider.Hide(StillWrongSettings.HiddenOverlays());
                    _overlaysSettled = true;
                }
            }
            int x;
            int y;
            uint mask;
            if (!CursorRoot(out x, out y, out mask))
            {
                return;
            }
            bool down = (mask & 256u) != 0;
            bool was = (_cursorMask & 256) != 0;
            _cursorMask = (int)mask;
            if (!down || was)
            {
                return;
            }
            StillWrongBugSheet chip = FindRole(true);
            if (chip == null || chip._chipDotScreen.width <= 0f)
            {
                return;
            }
            Rect hit = chip._chipDotScreen;
            hit.x -= 6f;
            hit.y -= 6f;
            hit.width += 12f;
            hit.height += 12f;
            if (!hit.Contains(new Vector2(x, y)))
            {
                return;
            }
            chip.ToggleFromPointer();
        }
        private void ToggleFromPointer()
        {
            if (EditorApplication.timeSinceStartup - _chipToggleAt < 0.25)
            {
                return;
            }
            _chipToggleAt = EditorApplication.timeSinceStartup;
            RollingRecorderControl.ToggleRecord();
            _statusCacheReal = 0.0;
            RepaintAll();
        }
        private static Texture2D LoadTextureFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch
            {
                return null;
            }
        }
        private void OnDestroy()
        {
            try
            {
                if (_isChip && RollingRecorderControl.IsEnabled() && !RollingRecorderControl.WasStopped())
                {
                    RollingRecorderControl.ResumeRecording();
                }
            }
            catch
            {
            }
            try
            {
                if (_thumbTextures != null)
                {
                    for (int i = 0; i < _thumbTextures.Count; i++)
                    {
                        if (_thumbTextures[i] != null)
                        {
                            UnityEngine.Object.DestroyImmediate(_thumbTextures[i]);
                        }
                    }
                    _thumbTextures.Clear();
                }
                if (FrameTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(FrameTexture);
                    FrameTexture = null;
                }
            }
            catch
            {
            }
        }
        public static void OpenWithFrameFile(string pngPath)
        {
            StillWrongBugSheet window = Acquire();
            window.ClearSession();
            window._frameFilePath = pngPath ?? string.Empty;
            window.FrameTexture = LoadTextureFile(window._frameFilePath);
            PresentReview(window);
        }
        public static void OpenWithSession(string sessionDir, string wantChunk, string wantFile, string candidateName, string candidatePath, string clickKind, int screenW, int screenH)
        {
            StillWrongBugSheet window = Acquire();
            window.ClearSession();
            window._sessionDir = sessionDir ?? string.Empty;
            window._sheetScreenW = screenW;
            window._sheetScreenH = screenH;
            window._clickKind = clickKind ?? string.Empty;
            window.LoadSessionThumbs(wantChunk, wantFile);
            window.SetCandidate(candidateName, candidatePath);
            PresentReview(window);
        }
        [SerializeField]
        private bool _expanded;
        [SerializeField]
        private bool _isChip;
        private float _zoom = 1f;
        private Vector2 _pan;
        [SerializeField]
        private string _changePath = string.Empty;
        [SerializeField]
        private string _changeName = string.Empty;
        [SerializeField]
        private string _clickKind = string.Empty;
        [SerializeField]
        private string _frameFilePath = string.Empty;
        private string _fullFramePath = string.Empty;
        private Texture2D _fullFrame;
        private long _seenCaptured = -1;
        private bool _thumbBatchQueued;
        private Rect _chipDot;
        private Rect _chipDotScreen;
        private static int _cursorMask;
        private double _chipToggleAt;
        private static bool _focusHooked;
        private static bool _overlaysSettled;
        private static IntPtr _xDisplay;
        private static Timer _circleTimer;
        private static bool _circlePollQueued;
        private static SynchronizationContext _editorContext;
        private void ReloadFromDisk()
        {
            string dir = RollingRecorderControl.LastSessionDir();
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            _sessionDir = dir;
            if (_thumbTextures != null)
            {
                for (int i = 0; i < _thumbTextures.Count; i++)
                {
                    if (_thumbTextures[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_thumbTextures[i]);
                    }
                }
                _thumbTextures.Clear();
            }
            if (_thumbFiles != null)
            {
                _thumbFiles.Clear();
            }
            if (_thumbTimes != null)
            {
                _thumbTimes.Clear();
            }
            if (_thumbLabels != null)
            {
                _thumbLabels.Clear();
            }
            if (_fullFrame != null)
            {
                UnityEngine.Object.DestroyImmediate(_fullFrame);
                _fullFrame = null;
            }
            _fullFramePath = string.Empty;
            LoadSessionThumbs(null, null);
            _thumbBatchQueued = false;
            LoadThumbBatch();
            _seenCaptured = RollingRecorderControl.CapturedFrameCount();
            Repaint();
        }
        private void ClearSession()
        {
            _sessionDir = string.Empty;
            _thumbFiles.Clear();
            _thumbTimes.Clear();
            _thumbLabels.Clear();
            _thumbTextures.Clear();
            _selectedThumb = 0;
            _retainedTotal = 0.0;
            _candidates.Clear();
            _candidateIndex = 0;
            _confirmedName = string.Empty;
            _confirmedPath = string.Empty;
            _progressLine = string.Empty;
            _clickKind = string.Empty;
            FrameTexture = null;
        }
        private void LoadSessionThumbs(string wantChunk, string wantFile)
        {
            try
            {
                if (string.IsNullOrEmpty(_sessionDir) || !Directory.Exists(_sessionDir))
                {
                    return;
                }
                string[] dirs = Directory.GetDirectories(_sessionDir, "chunk-*");
                System.Array.Sort(dirs, System.StringComparer.Ordinal);
                List<string> files = new List<string>();
                List<double> times = new List<double>();
                List<string> labels = new List<string>();
                double spanEnd = 0.0;
                for (int d = 0; d < dirs.Length; d++)
                {
                    string manifestPath = Path.Combine(dirs[d], "manifest.json");
                    string chunkName = Path.GetFileName(dirs[d]);
                    string framesDir = Path.Combine(dirs[d], "frames");
                    if (!Directory.Exists(framesDir))
                    {
                        continue;
                    }
                    double chunkStart = 0.0;
                    double chunkEnd = 0.0;
                    try
                    {
                        RollingChunkManifest man = JsonUtility.FromJson<RollingChunkManifest>(File.ReadAllText(manifestPath));
                        if (man != null)
                        {
                            chunkStart = man.StartTimeSeconds;
                            chunkEnd = man.EndTimeSeconds;
                        }
                    }
                    catch
                    {
                    }
                    if (chunkEnd > spanEnd)
                    {
                        spanEnd = chunkEnd;
                    }
                    Dictionary<int, double> clockByFrame = new Dictionary<int, double>();
                    try
                    {
                        string inputsPath = Path.Combine(dirs[d], "inputs.json");
                        if (File.Exists(inputsPath))
                        {
                            RollingInputsFile inputs = JsonUtility.FromJson<RollingInputsFile>(File.ReadAllText(inputsPath));
                            if (inputs != null && inputs.Events != null)
                            {
                                for (int i = 0; i < inputs.Events.Count; i++)
                                {
                                    RollingInputRecord rec = inputs.Events[i];
                                    if (rec != null && !clockByFrame.ContainsKey(rec.FrameNumber))
                                    {
                                        clockByFrame.Add(rec.FrameNumber, rec.ClockSeconds);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                    string[] jpgs = Directory.GetFiles(framesDir, "f-*.jpg");
                    System.Array.Sort(jpgs, System.StringComparer.Ordinal);
                    for (int i = 0; i < jpgs.Length; i++)
                    {
                        int frameNumber = ParseFrameNumber(Path.GetFileNameWithoutExtension(jpgs[i]));
                        if (frameNumber < 0)
                        {
                            continue;
                        }
                        double stamp = chunkStart;
                        double recorded;
                        if (clockByFrame.TryGetValue(frameNumber, out recorded))
                        {
                            stamp = recorded;
                        }
                        files.Add(jpgs[i]);
                        times.Add(stamp);
                        labels.Add(chunkName + " f-" + frameNumber.ToString("D6"));
                    }
                }
                if (files.Count == 0)
                {
                    return;
                }
                int stride = 1;
                while ((files.Count + stride - 1) / stride > 32)
                {
                    stride++;
                }
                for (int i = 0; i < files.Count; i += stride)
                {
                    _thumbFiles.Add(files[i]);
                    _thumbTimes.Add(times[i]);
                    _thumbLabels.Add(labels[i]);
                }
                _retainedTotal = spanEnd;
                SelectThumb(wantChunk, wantFile);
                _seenCaptured = RollingRecorderControl.CapturedFrameCount();
                SelectedFullFrame();
                _thumbBatchQueued = false;
            }
            catch
            {
            }
        }
        private static int ParseFrameNumber(string stem)
        {
            try
            {
                if (string.IsNullOrEmpty(stem) || stem.Length < 3)
                {
                    return -1;
                }
                return int.Parse(stem.Substring(2));
            }
            catch
            {
                return -1;
            }
        }
        private void SelectThumb(string wantChunk, string wantFile)
        {
            _selectedThumb = 0;
            _scrub = 0f;
            if (_thumbLabels.Count == 0)
            {
                return;
            }
            if (string.IsNullOrEmpty(wantChunk))
            {
                _selectedThumb = _thumbLabels.Count - 1;
                _scrub = _selectedThumb;
                return;
            }
            string wantStem = wantFile ?? string.Empty;
            if (wantStem.EndsWith(".jpg"))
            {
                wantStem = wantStem.Substring(0, wantStem.Length - 4);
            }
            for (int i = 0; i < _thumbLabels.Count; i++)
            {
                if (_thumbLabels[i].StartsWith(wantChunk) && _thumbLabels[i].EndsWith(wantStem))
                {
                    _selectedThumb = i;
                    _scrub = i;
                    return;
                }
            }
            _selectedThumb = _thumbLabels.Count - 1;
            _scrub = _selectedThumb;
        }
        private void SetCandidate(string name, string path)
        {
            _candidates.Clear();
            _candidateIndex = 0;
            _confirmedName = name ?? string.Empty;
            _confirmedPath = path ?? string.Empty;
            if (!string.IsNullOrEmpty(path))
            {
                StillWrongGroundingCandidate candidate = new StillWrongGroundingCandidate();
                candidate.ElementPath = path;
                _candidates.Add(candidate);
            }
        }
        private Texture2D SelectedFullFrame()
        {
            if (_thumbFiles == null || _selectedThumb < 0 || _selectedThumb >= _thumbFiles.Count)
            {
                return FrameTexture;
            }
            string path = _thumbFiles[_selectedThumb];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return FrameTexture;
            }
            if (_fullFrame != null && _fullFramePath == path)
            {
                return _fullFrame;
            }
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return FrameTexture;
            }
            tex.filterMode = tex.width >= 1280 ? FilterMode.Bilinear : FilterMode.Point;
            if (_fullFrame != null)
            {
                UnityEngine.Object.DestroyImmediate(_fullFrame);
            }
            _fullFrame = tex;
            _fullFramePath = path;
            return _fullFrame;
        }
        private Texture2D CurrentThumbTexture()
        {
            if (_thumbTextures != null && _selectedThumb >= 0 && _selectedThumb < _thumbTextures.Count)
            {
                Texture2D tex = _thumbTextures[_selectedThumb];
                if (tex != null)
                {
                    return tex;
                }
            }
            return FrameTexture;
        }
        private string FrameCaption()
        {
            if (_selectedThumb >= 0 && _selectedThumb < _thumbLabels.Count && _selectedThumb < _thumbTimes.Count)
            {
                return _thumbLabels[_selectedThumb] + " at " + _thumbTimes[_selectedThumb].ToString("F1") + "s of " + _retainedTotal.ToString("F1") + "s kept";
            }
            if (FrameTexture != null)
            {
                return "assigned frame";
            }
            return "no frame loaded";
        }
        private StillWrongGroundingCandidate CurrentCandidate()
        {
            if (_candidateIndex >= 0 && _candidateIndex < _candidates.Count)
            {
                return _candidates[_candidateIndex];
            }
            return null;
        }
        private static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            int slash = path.LastIndexOf("/");
            if (slash >= 0 && slash + 1 < path.Length)
            {
                return path.Substring(slash + 1);
            }
            return path;
        }
        private void ConfirmCurrent()
        {
            StillWrongGroundingCandidate current = CurrentCandidate();
            if (current == null || string.IsNullOrEmpty(current.ElementPath))
            {
                return;
            }
            _confirmedName = LeafName(current.ElementPath);
            _confirmedPath = current.ElementPath;
        }
        private void CycleCandidate()
        {
            if (_candidates.Count < 2)
            {
                return;
            }
            _candidateIndex = (_candidateIndex + 1) % _candidates.Count;
        }
        private void DrawSamNote()
        {
            if (string.IsNullOrEmpty(_groundNote))
            {
                return;
            }
            EditorGUILayout.LabelField(ShortNote(_groundNote), EditorStyles.wordWrappedLabel);
        }
        private static string ShortNote(string note)
        {
            int at = note.IndexOf(" Debug: ", StringComparison.Ordinal);
            if (at < 0)
            {
                return note;
            }
            string dir = note.Substring(at + 8).Trim();
            return note.Substring(0, at) + " Debug: " + System.IO.Path.GetFileName(dir.TrimEnd('/'));
        }
        private void OnGUI()
        {
            EnsureStyles();
            EnsureState();
            if (_isChip && !_expanded)
            {
                DrawStatusRow();
                return;
            }
            if (!FirstRunDismissed())
            {
                DrawFirstRun();
            }
            EnsureThumbsLoaded();
            long captured = RollingRecorderControl.CapturedFrameCount();
            if (captured != _seenCaptured)
            {
                ReloadFromDisk();
            }
            DrawReviewCorner();
            float scrollHeight = Mathf.Max(120f, position.height * 0.55f + 22f);
            _frameCap = scrollHeight - 22f;
            _reviewScroll = EditorGUILayout.BeginScrollView(_reviewScroll);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUIStyle.none, GUIStyle.none, GUILayout.Height(scrollHeight));
            DrawFrameArea();
            EditorGUILayout.EndScrollView();
            DrawTimeline();
            DrawFoundCard();
            DrawSamNote();
            DrawChangeSection();
            DrawSentence();
            DrawPrimary();
            EditorGUILayout.EndScrollView();
        }
        private void EnsureStyles()
        {
            if (_dotStyle == null)
            {
                _dotStyle = new GUIStyle(GUI.skin.box);
                _dotStyle.normal.background = MakeColorTexture(new Color(0.2f, 0.8f, 0.3f, 1f));
            }
            if (_frameBoxStyle == null)
            {
                _frameBoxStyle = new GUIStyle(GUI.skin.box);
                _frameBoxStyle.normal.background = MakeColorTexture(new Color(0.16f, 0.17f, 0.22f, 1f));
            }
            if (_pickedBoxStyle == null)
            {
                _pickedBoxStyle = new GUIStyle(GUI.skin.box);
                _pickedBoxStyle.normal.background = MakeColorTexture(new Color(0.30f, 0.55f, 0.95f, 1f));
            }
            if (_dotPausedStyle == null)
            {
                _dotPausedStyle = new GUIStyle(GUI.skin.box);
                _dotPausedStyle.normal.background = MakeColorTexture(new Color(0.95f, 0.75f, 0.20f, 1f));
            }
            if (_dotBadStyle == null)
            {
                _dotBadStyle = new GUIStyle(GUI.skin.box);
                _dotBadStyle.normal.background = MakeColorTexture(new Color(0.90f, 0.25f, 0.25f, 1f));
            }
            if (_dotOffStyle == null)
            {
                _dotOffStyle = new GUIStyle(GUI.skin.box);
                _dotOffStyle.normal.background = MakeColorTexture(new Color(0.35f, 0.37f, 0.42f, 1f));
            }
        }
        private static Texture2D MakeColorTexture(Color color)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
        private void DrawStatusRow()
        {
            if (_statusCacheReal == 0.0 || EditorApplication.timeSinceStartup - _statusCacheReal > 0.5)
            {
                _statusCacheReal = EditorApplication.timeSinceStartup;
                _statusCacheOk = RollingRecorderControl.TryReadStatus(out _statusCacheState, out _statusCacheRetained, out _statusCacheChunks, out _statusCacheBytes, out _statusCacheDropped);
            }
            Color mark = new Color(0.15f, 1f, 0.2f, 1f);
            if (RollingRecorderControl.CircleIsHot())
            {
                mark = new Color(1f, 0.05f, 0.05f, 1f);
            }
            Rect row = GUILayoutUtility.GetRect(168f, 28f, GUILayout.Width(168f), GUILayout.Height(28f));
            Rect dot = new Rect(row.x + 2f, row.y + 2f, 24f, 24f);
            Color prev = GUI.color;
            GUI.color = mark;
            GUI.DrawTexture(dot, CircleTex(), ScaleMode.ScaleToFit, true);
            GUI.color = prev;
            _chipDot = dot;
            Vector2 dotScreen = GUIUtility.GUIToScreenPoint(new Vector2(dot.x, dot.y));
            _chipDotScreen = new Rect(position.x + dot.x, position.y + dot.y, dot.width, dot.height);
            if (dotScreen.x > 1f)
            {
                _chipDotScreen = new Rect(dotScreen.x, dotScreen.y, dot.width, dot.height);
            }
            Event click = Event.current;
            if (click != null && click.type == EventType.MouseDown && click.button == 0 && dot.Contains(click.mousePosition))
            {
                ToggleFromPointer();
                click.Use();
            }
            Rect bug = new Rect(dot.xMax + 6f, row.y + 2f, 118f, 24f);
            if (GUI.Button(bug, "I found a bug"))
            {
                RollingRecorderControl.FreezeAndOpenSheet();
            }
        }
        private void DrawReviewCorner()
        {
            Rect row = GUILayoutUtility.GetRect(80f, 28f, GUILayout.Width(80f), GUILayout.Height(28f));
            if (GUI.Button(new Rect(row.x, row.y + 2f, 72f, 24f), "corner"))
            {
                Close();
                EnsureChip();
            }
        }
        private void DrawChangeSection()
        {
            if (string.IsNullOrEmpty(_changePath))
            {
                return;
            }
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Pointed at", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_changeName) ? LeafName(_changePath) : _changeName);
            EditorGUILayout.LabelField(_changePath, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        private void DrawTimeline()
        {
            EditorGUILayout.BeginVertical("box");
            int count = _thumbFiles != null ? _thumbFiles.Count : 0;
            if (count > 0)
            {
                int start;
                int window;
                ThumbSpan(count, out start, out window);
                EditorGUILayout.BeginHorizontal();
                for (int k = 0; k < window; k++)
                {
                    int i = start + k;
                    Texture2D thumb = _thumbTextures != null && i < _thumbTextures.Count ? _thumbTextures[i] : null;
                    GUIStyle style = i == _selectedThumb ? _pickedBoxStyle : _frameBoxStyle;
                    GUIContent content = thumb != null ? new GUIContent(thumb) : GUIContent.none;
                    if (GUILayout.Button(content, style, GUILayout.Width(184), GUILayout.Height(104)))
                    {
                        _selectedThumb = i;
                        _scrub = i;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(40)))
            {
                StepThumb(-1);
            }
            _scrub = GUILayout.HorizontalSlider(_scrub, 0f, count > 0 ? (float)(count - 1) : 0f);
            if (count > 0)
            {
                _selectedThumb = Mathf.Clamp(Mathf.RoundToInt(_scrub), 0, count - 1);
            }
            if (GUILayout.Button(">", GUILayout.Width(40)))
            {
                StepThumb(1);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(TimelineLabel(), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        private void StepThumb(int direction)
        {
            int count = _thumbFiles != null ? _thumbFiles.Count : 0;
            if (count == 0)
            {
                return;
            }
            _selectedThumb = (_selectedThumb + direction + count) % count;
            _scrub = _selectedThumb;
        }
        private string TimelineLabel()
        {
            int count = _thumbFiles != null ? _thumbFiles.Count : 0;
            if (count == 0)
            {
                return "no timeline loaded";
            }
            double current = 0.0;
            if (_selectedThumb >= 0 && _selectedThumb < _thumbTimes.Count)
            {
                current = _thumbTimes[_selectedThumb];
            }
            return "frame " + (_selectedThumb + 1) + " of " + count + " at " + current.ToString("F1") + "s of " + _retainedTotal.ToString("F1") + "s kept";
        }
        private void DrawFoundCard()
        {
            EditorGUILayout.BeginVertical("box");
            StillWrongGroundingCandidate current = CurrentCandidate();
            if (current == null || string.IsNullOrEmpty(current.ElementPath))
            {
                EditorGUILayout.LabelField("Click the thing that is wrong", EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField(LeafName(current.ElementPath), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(current.ElementPath, EditorStyles.miniLabel);
                if (_candidates != null && _candidates.Count > 1)
                {
                    if (GUILayout.Button("wrong object? show next", EditorStyles.linkLabel))
                    {
                    CycleCandidate();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }
        private void DrawSentence()
        {
            EditorGUILayout.LabelField("What is wrong, or what should change?");
            _sentence = EditorGUILayout.TextArea(_sentence, GUILayout.MinHeight(36));
        }
        private void DrawPrimary()
        {
            using (new EditorGUI.DisabledScope(_dispatchBusy))
            {
                if (GUILayout.Button(_dispatchBusy ? "Sending..." : "Send this bug to be fixed", GUILayout.Height(28)))
                {
                    ConfirmCurrent();
                    _progressLine = "Running verified dispatch.";
                    Repaint();
                    DispatchBugNow();
                }
            }
            EditorGUILayout.LabelField("Sends a copy of this scene to the agent. This scene stays as it is.", EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(_progressLine))
            {
                EditorGUILayout.LabelField(_progressLine);
            }
            DrawCopyCheck();
            string agent = StillWrongSettings.PreferredAgent();
            EditorGUILayout.LabelField("Agent: " + agent + ". Settings changes the agent. SAM uses MODEL_API_KEY.", EditorStyles.wordWrappedLabel);
        }
        private void DrawCopyCheck()
        {
            string worktree = RollingRecorderControl.FindCheckWorktree();
            if (string.IsNullOrEmpty(worktree))
            {
                return;
            }
            if (GUILayout.Button("Check the copy", GUILayout.Height(28)))
            {
                CheckTheCopy();
            }
            if (!string.IsNullOrEmpty(_checkLine))
            {
                EditorGUILayout.LabelField(_checkLine, EditorStyles.wordWrappedLabel);
                if (_checkLine.StartsWith("Fixed."))
                {
                    if (GUILayout.Button("Put the fix in my scene", GUILayout.Height(28)))
                    {
                        PutTheFixInMyScene();
                    }
                }
            }
            if (!string.IsNullOrEmpty(_returnScene))
            {
                if (GUILayout.Button("Back to my scene", GUILayout.Height(28)))
                {
                    BackToMyScene();
                }
            }
        }
        public string SheetDiagnostics()
        {
            string frame = SheetSelectedLabel();
            if (string.IsNullOrEmpty(frame))
            {
                frame = "none";
            }
            string candidate = _confirmedPath;
            if (string.IsNullOrEmpty(candidate))
            {
                candidate = "none";
            }
            return "mode=Bug frame=" + frame + " thumbs=" + SheetThumbCount() + " candidate=" + candidate + " sentence-chars=" + _sentence.Length;
        }
    }
}
