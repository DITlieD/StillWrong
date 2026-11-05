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
    public sealed class StillWrongBugSheet : EditorWindow
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
            GameObject tavern = GameObject.Find("OpenTavernButton");
            GameObject badge = GameObject.Find("ObjectiveBadgeIcon");
            if (tavern != null && tavern.activeSelf)
            {
                tavern.SetActive(false);
            }
            if (badge != null && badge.activeSelf)
            {
                badge.SetActive(false);
            }
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
                    GameObject tavernPlay = GameObject.Find("OpenTavernButton");
                    if (tavernPlay != null)
                    {
                        tavernPlay.SetActive(false);
                    }
                    GameObject badgePlay = GameObject.Find("ObjectiveBadgeIcon");
                    if (badgePlay != null)
                    {
                        badgePlay.SetActive(false);
                    }
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
        private void HandleFramePointer(Rect rect, Texture2D tex)
        {
            Event evt = Event.current;
            if (evt == null || tex == null)
            {
                return;
            }
            if (evt.button == 1)
            {
                return;
            }
            if (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag && evt.type != EventType.MouseUp)
            {
                return;
            }
            if (!rect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.MouseUp && _dragging)
                {
                    _dragging = false;
                    evt.Use();
                }
                return;
            }
            float fx;
            float fy;
            if (!TexturePoint(rect, evt.mousePosition, tex, out fx, out fy))
            {
                if (evt.type == EventType.MouseUp)
                {
                    _dragging = false;
                }
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                _dragStart = new Vector2(fx, fy);
                _dragging = true;
                if (evt.shift)
                {
                    ResolvePoint(fx, fy, true, tex);
                    _dragging = false;
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                float dx = fx - _dragStart.x;
                float dy = fy - _dragStart.y;
                if (dx * dx + dy * dy > 16.0f)
                {
                    ResolveBox(_dragStart.x, _dragStart.y, fx, fy, tex);
                }
                else
                {
                    ResolvePoint(fx, fy, false, tex);
                }
                evt.Use();
            }
        }
        private void ToScreenPoint(float fx, float fy, Texture2D tex, out float sx, out float sy)
        {
            sx = fx;
            sy = tex.height - fy;
        }
        private bool TexturePoint(Rect view, Vector2 mouse, Texture2D tex, out float fx, out float fy)
        {
            fx = 0f;
            fy = 0f;
            if (tex == null || tex.width < 1 || tex.height < 1)
            {
                return false;
            }
            Rect local = FrameDrawRect(new Rect(0f, 0f, view.width, view.height), tex);
            Rect drawn = new Rect(view.x + local.x, view.y + local.y, local.width, local.height);
            Rect visible = Rect.MinMaxRect(
                Mathf.Max(drawn.xMin, view.xMin),
                Mathf.Max(drawn.yMin, view.yMin),
                Mathf.Min(drawn.xMax, view.xMax),
                Mathf.Min(drawn.yMax, view.yMax));
            if (visible.width < 1f || visible.height < 1f || !visible.Contains(mouse))
            {
                return false;
            }
            fx = (mouse.x - drawn.x) / drawn.width * tex.width;
            fy = (mouse.y - drawn.y) / drawn.height * tex.height;
            if (fx < 0f || fy < 0f || fx > tex.width || fy > tex.height)
            {
                return false;
            }
            return true;
        }
        private void ResolvePoint(float fx, float fy, bool negative, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            float sx;
            float sy;
            ToScreenPoint(fx, fy, tex, out sx, out sy);
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.Sentence = _sentence;
            if (negative)
            {
                request.NegativePoints.Add(new Vector2(sx, sy));
            }
            else
            {
                request.PositivePoints.Add(new Vector2(sx, sy));
            }
            FinishGround(request, tex, false, fx, fy, fx, fy, negative);
        }
        private void ResolveBox(float x1, float y1, float x2, float y2, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            float ax;
            float ay;
            ToScreenPoint(x1, y1, tex, out ax, out ay);
            float bx;
            float by;
            ToScreenPoint(x2, y2, tex, out bx, out by);
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.Sentence = _sentence;
            request.HasBox = true;
            request.Box = Rect.MinMaxRect(Mathf.Min(ax, bx), Mathf.Min(ay, by), Mathf.Max(ax, bx), Mathf.Max(ay, by));
            FinishGround(request, tex, true, x1, y1, x2, y2, false);
        }
        private void FinishGround(StillWrongGroundingRequest request, Texture2D tex, bool hasBox, float x1, float y1, float x2, float y2, bool pointNegative)
        {
            string framePath = (_thumbFiles != null && _selectedThumb >= 0 && _selectedThumb < _thumbFiles.Count) ? _thumbFiles[_selectedThumb] : string.Empty;
            StillWrong.Core.FrameObjectsSnapshot snapshot = StillWrong.Core.FrameObjects.ForFrameFile(framePath, 15);
            if (snapshot != null && snapshot.Elements.Count > 0 && !pointNegative)
            {
                StillWrongFrameGroundingOutcome outcome = StillWrongFrameGrounding.Run(tex, snapshot, hasBox, x1, y1, x2, y2);
                _groundNote = outcome.Note;
                SetMaskOverlay(outcome.Mask, framePath);
                if (outcome.Candidates.Count > 0)
                {
                    _candidates = outcome.Candidates;
                    _candidateIndex = 0;
                }
                StillWrongSamClick.SetReportMask(outcome.Mask);
                StillWrongSamClick.WriteReport(tex, outcome.AsResult(), outcome.ClickOnly);
                return;
            }
            StillWrongSamClick.Attach(request, tex, hasBox, x1, y1, x2, y2, pointNegative);
            bool[,] mask = request.SamMask;
            request.SamMask = null;
            StillWrongGroundingResult without = StillWrongGrounding.ActiveService.Ground(request);
            request.SamMask = mask;
            StillWrongGroundingResult result = mask == null ? without : StillWrongGrounding.ActiveService.Ground(request);
            StillWrongSamClick.WriteReport(tex, result, without);
            _groundNote = StillWrongSamClick.LastNote + (snapshot == null ? " No object snapshot for this frame, ranked against the live scene." : string.Empty);
            SetMaskOverlay(mask, framePath);
            if (result != null && result.UsedSamMask && result.Candidates != null)
            {
                result.Candidates.Sort(CompareCandidateCoverage);
            }
            if (result != null && result.Candidates != null && result.Candidates.Count > 0)
            {
                _candidates = result.Candidates;
                _candidateIndex = 0;
            }
        }
        private static int CompareCandidateCoverage(StillWrongGroundingCandidate a, StillWrongGroundingCandidate b)
        {
            double left = a == null ? 0d : a.MaskCoverage;
            double right = b == null ? 0d : b.MaskCoverage;
            return right.CompareTo(left);
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
        private void DrawFrameArea()
        {
            float width = EditorGUIUtility.currentViewWidth - 24f;
            float height = _frameCap > 1f ? _frameCap : 160f;
            Texture2D shown = SelectedFullFrame();
            Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(rect, new Color(0.05f, 0.06f, 0.08f, 1f));
            if (shown == null)
            {
                GUI.Label(rect, "Frozen frame", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            shown.filterMode = FilterMode.Bilinear;
            shown.wrapMode = TextureWrapMode.Clamp;
            HandleFrameZoom(rect);
            DrawZoomedFrame(rect, shown);
            HandleFramePointer(rect, shown);
            EditorGUILayout.LabelField(FrameCaption() + "   scroll to zoom, right-drag to move", EditorStyles.miniLabel);
        }
        private void DrawZoomedFrame(Rect view, Texture2D tex)
        {
            Rect fit = AspectFit(view, tex);
            if (fit.width <= 1f || fit.height <= 1f)
            {
                return;
            }
            Texture2D overlay = MaskOverlayTexture();
            if (_zoom <= 1.001f)
            {
                GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit, false);
                if (overlay != null)
                {
                    GUI.DrawTexture(fit, overlay, ScaleMode.StretchToFill, true);
                }
                return;
            }
            float uvW = 1f / _zoom;
            float uvH = 1f / _zoom;
            float u = 0.5f - uvW * 0.5f - _pan.x / fit.width;
            float vTop = 0.5f - uvH * 0.5f - _pan.y / fit.height;
            u = Mathf.Clamp(u, 0f, 1f - uvW);
            vTop = Mathf.Clamp(vTop, 0f, 1f - uvH);
            Rect coords = new Rect(u, 1f - (vTop + uvH), uvW, uvH);
            GUI.DrawTextureWithTexCoords(fit, tex, coords, false);
            if (overlay != null)
            {
                GUI.DrawTextureWithTexCoords(fit, overlay, coords, true);
            }
        }
        // The SAM mask of the last click, tinted cyan, only while the same frame is shown.
        private Texture2D MaskOverlayTexture()
        {
            string framePath = (_thumbFiles != null && _selectedThumb >= 0 && _selectedThumb < _thumbFiles.Count) ? _thumbFiles[_selectedThumb] : string.Empty;
            if (_maskOverlay == null || _maskOverlayPath != framePath)
            {
                return null;
            }
            if (_maskOverlayTex != null)
            {
                return _maskOverlayTex;
            }
            int h = _maskOverlay.GetLength(0);
            int w = _maskOverlay.GetLength(1);
            Color32[] pixels = new Color32[w * h];
            Color32 on = new Color32(25, 227, 255, 110);
            Color32 off = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = _maskOverlay[y, x] ? on : off;
                }
            }
            _maskOverlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _maskOverlayTex.filterMode = FilterMode.Point;
            _maskOverlayTex.SetPixels32(pixels);
            _maskOverlayTex.Apply(false);
            return _maskOverlayTex;
        }
        private void SetMaskOverlay(bool[,] mask, string framePath)
        {
            if (_maskOverlayTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_maskOverlayTex);
                _maskOverlayTex = null;
            }
            _maskOverlay = mask;
            _maskOverlayPath = framePath ?? string.Empty;
        }
        private static Rect AspectFit(Rect view, Texture2D tex)
        {
            float aspect = tex.height > 0 ? (float)tex.width / (float)tex.height : 16f / 9f;
            float fitW = view.width;
            float fitH = fitW / aspect;
            if (fitH > view.height)
            {
                fitH = view.height;
                fitW = fitH * aspect;
            }
            float x = view.x + (view.width - fitW) * 0.5f;
            float y = view.y + (view.height - fitH) * 0.5f;
            return new Rect(x, y, fitW, fitH);
        }
        private void HandleFrameZoom(Rect rect)
        {
            Event evt = Event.current;
            if (evt == null || !rect.Contains(evt.mousePosition))
            {
                return;
            }
            if (evt.type == EventType.ScrollWheel)
            {
                float step = evt.delta.y < 0f ? 1.15f : 0.87f;
                _zoom = Mathf.Clamp(_zoom * step, 1f, 8f);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 1)
            {
                _pan += evt.delta;
                evt.Use();
                Repaint();
            }
        }
        private Rect FrameDrawRect(Rect view, Texture2D tex)
        {
            float aspect = tex.height > 0 ? (float)tex.width / (float)tex.height : 16f / 9f;
            float fitW = view.width;
            float fitH = fitW / aspect;
            if (fitH > view.height)
            {
                fitH = view.height;
                fitW = fitH * aspect;
            }
            float x = view.x + (view.width - fitW) * 0.5f;
            float y = view.y + (view.height - fitH) * 0.5f;
            return new Rect(x, y, fitW, fitH);
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
            if (GUILayout.Button("Send this bug to be fixed", GUILayout.Height(28)))
            {
                ConfirmCurrent();
                _progressLine = "Running verified dispatch.";
                Repaint();
                DispatchBugNow();
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
        private void PutTheFixInMyScene()
        {
            UnityEngine.SceneManagement.Scene open = EditorSceneManager.GetActiveScene();
            string openPath = open.path ?? string.Empty;
            if (openPath.IndexOf("StillWrongDispatch/FixedCopy", StringComparison.Ordinal) < 0)
            {
                _checkLine = "Open the copy first. Your scene was not changed.";
                return;
            }
            if (string.IsNullOrEmpty(_returnScene))
            {
                _checkLine = "No scene to update. Your scene was not changed.";
                return;
            }
            if (_returnScene.IndexOf("StillWrongDispatch", StringComparison.Ordinal) >= 0)
            {
                _checkLine = "The return scene is a dispatch copy. Your scene was not changed.";
                return;
            }
            string project = Directory.GetParent(Application.dataPath).FullName;
            string destAbs = SceneAbs(project, _returnScene);
            if (!File.Exists(destAbs))
            {
                _checkLine = "No scene to update. Your scene was not changed.";
                return;
            }
            string openAbs = SceneAbs(project, openPath);
            if (!File.Exists(openAbs))
            {
                _checkLine = "Open the copy first. Your scene was not changed.";
                return;
            }
            StillWrong.Dispatch.DispatchPacket packet = ReadCheckPacket();
            string target = packet != null && packet.Target != null ? packet.Target.Name : string.Empty;
            if (string.IsNullOrEmpty(target))
            {
                _checkLine = "Target name is missing or not unique. Your scene was not changed.";
                return;
            }
            string sourceText = File.ReadAllText(openAbs);
            string destText = File.ReadAllText(destAbs);
            if (CountExactName(sourceText, target) != 1 || CountExactName(destText, target) != 1)
            {
                _checkLine = "Target name is missing or not unique. Your scene was not changed.";
                return;
            }
            string updated;
            if (!TryApplyObject(sourceText, destText, target, out updated))
            {
                _checkLine = "The component set does not match. Your scene was not changed.";
                return;
            }
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(project, "Library", "StillWrong", "backups", stamp);
            Directory.CreateDirectory(backupDir);
            File.Copy(destAbs, Path.Combine(backupDir, Path.GetFileName(destAbs)), false);
            File.WriteAllText(destAbs, updated);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(_returnScene, OpenSceneMode.Single);
            _checkLine = "The fix is in your scene.";
        }
        private static string SceneAbs(string project, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return string.Empty;
            }
            if (scenePath.StartsWith("Assets/"))
            {
                return Path.Combine(project, scenePath);
            }
            return scenePath;
        }
        private static StillWrong.Dispatch.DispatchPacket ReadCheckPacket()
        {
            string worktree = RollingRecorderControl.FindCheckWorktree();
            if (string.IsNullOrEmpty(worktree) || !Directory.Exists(worktree))
            {
                return null;
            }
            string[] packets = Directory.GetFiles(worktree, "packet-*.json");
            if (packets == null || packets.Length == 0)
            {
                return null;
            }
            string packetFile = packets[0];
            for (int i = 1; i < packets.Length; i++)
            {
                if (string.CompareOrdinal(packets[i], packetFile) > 0)
                {
                    packetFile = packets[i];
                }
            }
            try
            {
                return JsonUtility.FromJson<StillWrong.Dispatch.DispatchPacket>(File.ReadAllText(packetFile));
            }
            catch
            {
                return null;
            }
        }
        private static int CountExactName(string text, string targetName)
        {
            string[] rows = SplitScene(text);
            string nameLine = "m_Name: " + targetName;
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == nameLine)
                {
                    count++;
                }
            }
            return count;
        }
        public static string CaptureTargetBlocks(string scenePath, string targetName)
        {
            string abs = scenePath ?? string.Empty;
            if (abs.StartsWith("Assets/"))
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                abs = Path.Combine(project, abs);
            }
            if (string.IsNullOrEmpty(targetName) || !File.Exists(abs))
            {
                return string.Empty;
            }
            string text = File.ReadAllText(abs);
            if (CountExactName(text, targetName) != 1)
            {
                return string.Empty;
            }
            string canon;
            if (!TryCanonicalBlocks(text, targetName, out canon))
            {
                return string.Empty;
            }
            return canon;
        }
        private void FinishBlockLine(string sceneFile, string targetName, StillWrong.Dispatch.DispatchPacket packet)
        {
            string nowBlocks;
            if (!TryCanonicalBlocks(File.ReadAllText(sceneFile), targetName, out nowBlocks))
            {
                nowBlocks = string.Empty;
            }
            string thenBlocks = packet != null ? packet.TargetBlocks ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(thenBlocks) || string.IsNullOrEmpty(nowBlocks))
            {
                _checkLine = "Opened the copy. Your scene was not changed.";
                return;
            }
            if (nowBlocks == thenBlocks)
            {
                _checkLine = "Still wrong. This is the copy. Your scene was not changed.";
                return;
            }
            _checkLine = "Fixed. This is the copy. Your scene was not changed.";
        }
        private static bool TryCanonicalBlocks(string text, string targetName, out string canon)
        {
            canon = string.Empty;
            List<string[]> blocks;
            if (!TryObjectBlocks(text, targetName, out blocks))
            {
                return false;
            }
            List<string> parts = new List<string>();
            for (int i = 0; i < blocks.Count; i++)
            {
                parts.Add(BlockBody(blocks[i]));
            }
            string[] rows = SplitScene(text);
            List<string> matIds = EmbeddedMaterialIds(rows, blocks);
            for (int i = 0; i < matIds.Count; i++)
            {
                int start;
                int end;
                if (!TryDocSpan(rows, matIds[i], out start, out end))
                {
                    return false;
                }
                string[] slice = new string[end - start];
                for (int j = start; j < end; j++)
                {
                    slice[j - start] = rows[j];
                }
                parts.Add(BlockBody(slice));
            }
            canon = string.Join("\n--block--\n", parts);
            return canon.Length > 0;
        }
        private static List<string> EmbeddedMaterialIds(string[] rows, List<string[]> blocks)
        {
            List<string> ids = new List<string>();
            if (rows == null || blocks == null)
            {
                return ids;
            }
            for (int b = 0; b < blocks.Count; b++)
            {
                string[] block = blocks[b];
                bool inMats = false;
                for (int i = 0; i < block.Length; i++)
                {
                    string trimmed = block[i].Trim();
                    if (trimmed.StartsWith("m_Materials:"))
                    {
                        inMats = true;
                        continue;
                    }
                    if (!inMats)
                    {
                        continue;
                    }
                    if (!trimmed.StartsWith("-"))
                    {
                        inMats = false;
                        continue;
                    }
                    if (trimmed.IndexOf("guid:", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }
                    string id = FileIdToken(trimmed);
                    if (string.IsNullOrEmpty(id) || id == "0")
                    {
                        continue;
                    }
                    if (!IsLocalMaterial(rows, id))
                    {
                        continue;
                    }
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids;
        }
        private static string FileIdToken(string line)
        {
            int key = line.IndexOf("fileID:", StringComparison.Ordinal);
            if (key < 0)
            {
                return string.Empty;
            }
            string rest = line.Substring(key + 7).Trim();
            int n = 0;
            while (n < rest.Length && char.IsDigit(rest[n]))
            {
                n++;
            }
            if (n == 0)
            {
                return string.Empty;
            }
            return rest.Substring(0, n);
        }
        private static bool IsLocalMaterial(string[] rows, string fileId)
        {
            string header = "--- !u!21 &" + fileId;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == header)
                {
                    return true;
                }
            }
            return false;
        }
        private static string BlockBody(string[] rows)
        {
            List<string> kept = new List<string>();
            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    continue;
                }
                kept.Add(rows[i]);
            }
            return string.Join("\n", kept);
        }
        private static bool TryApplyObject(string sourceText, string destText, string targetName, out string updated)
        {
            updated = destText;
            List<string[]> sourceBlocks;
            List<string[]> destBlocks;
            if (!TryObjectBlocks(sourceText, targetName, out sourceBlocks) || !TryObjectBlocks(destText, targetName, out destBlocks))
            {
                return false;
            }
            if (sourceBlocks.Count != destBlocks.Count || sourceBlocks.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < sourceBlocks.Count; i++)
            {
                if (BlockSignature(sourceBlocks[i]) != BlockSignature(destBlocks[i]))
                {
                    return false;
                }
            }
            string destId;
            if (!TryGameObjectFileId(destText, targetName, out destId))
            {
                return false;
            }
            string[] destRows = SplitScene(destText);
            List<string> ids = ComponentIds(destRows, destId);
            if (ids.Count != sourceBlocks.Count)
            {
                return false;
            }
            int[] starts = new int[ids.Count];
            int[] ends = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                if (!TryDocSpan(destRows, ids[i], out starts[i], out ends[i]))
                {
                    return false;
                }
            }
            string[] sourceRows = SplitScene(sourceText);
            List<string> sourceMats = EmbeddedMaterialIds(sourceRows, sourceBlocks);
            List<string> destMats = EmbeddedMaterialIds(destRows, destBlocks);
            if (sourceMats.Count != destMats.Count)
            {
                return false;
            }
            int[] matSrc = PairMaterials(sourceMats, destMats);
            if (matSrc == null)
            {
                return false;
            }
            int total = ids.Count + destMats.Count;
            int[] allStart = new int[total];
            int[] allEnd = new int[total];
            string[][] pieces = new string[total][];
            for (int i = 0; i < ids.Count; i++)
            {
                allStart[i] = starts[i];
                allEnd[i] = ends[i];
                pieces[i] = RebuildBlock(sourceBlocks[i], destBlocks[i]);
                if (pieces[i] == null)
                {
                    return false;
                }
            }
            for (int m = 0; m < destMats.Count; m++)
            {
                int destStart;
                int destEnd;
                int sourceStart;
                int sourceEnd;
                if (!TryDocSpan(destRows, destMats[m], out destStart, out destEnd) || !TryDocSpan(sourceRows, sourceMats[matSrc[m]], out sourceStart, out sourceEnd))
                {
                    return false;
                }
                string[] sourceSlice = new string[sourceEnd - sourceStart];
                for (int j = sourceStart; j < sourceEnd; j++)
                {
                    sourceSlice[j - sourceStart] = sourceRows[j];
                }
                string[] destSlice = new string[destEnd - destStart];
                for (int j = destStart; j < destEnd; j++)
                {
                    destSlice[j - destStart] = destRows[j];
                }
                int slot = ids.Count + m;
                allStart[slot] = destStart;
                allEnd[slot] = destEnd;
                pieces[slot] = RebuildMaterial(sourceSlice, destSlice);
                if (pieces[slot] == null)
                {
                    return false;
                }
            }
            int[] order = new int[total];
            for (int i = 0; i < total; i++)
            {
                order[i] = i;
            }
            for (int a = 0; a < order.Length; a++)
            {
                for (int b = a + 1; b < order.Length; b++)
                {
                    if (allStart[order[b]] < allStart[order[a]])
                    {
                        int swap = order[a];
                        order[a] = order[b];
                        order[b] = swap;
                    }
                }
            }
            for (int n = 1; n < order.Length; n++)
            {
                if (allStart[order[n]] < allEnd[order[n - 1]])
                {
                    return false;
                }
            }
            List<string> merged = new List<string>();
            int cursor = 0;
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                for (int j = cursor; j < allStart[i]; j++)
                {
                    merged.Add(destRows[j]);
                }
                for (int j = 0; j < pieces[i].Length; j++)
                {
                    merged.Add(pieces[i][j]);
                }
                cursor = allEnd[i];
            }
            for (int j = cursor; j < destRows.Length; j++)
            {
                merged.Add(destRows[j]);
            }
            updated = JoinScene(destText, merged.ToArray());
            for (int i = 0; i < ids.Count; i++)
            {
                if (CountDocFileId(updated, ids[i]) != 1)
                {
                    updated = destText;
                    return false;
                }
            }
            for (int i = 0; i < destMats.Count; i++)
            {
                if (CountDocFileId(updated, destMats[i]) != 1)
                {
                    updated = destText;
                    return false;
                }
            }
            return true;
        }
        private static int[] PairMaterials(List<string> sourceIds, List<string> destIds)
        {
            int[] pair = new int[destIds.Count];
            bool[] used = new bool[sourceIds.Count];
            for (int d = 0; d < destIds.Count; d++)
            {
                pair[d] = -1;
                for (int s = 0; s < sourceIds.Count; s++)
                {
                    if (used[s] || sourceIds[s] != destIds[d])
                    {
                        continue;
                    }
                    pair[d] = s;
                    used[s] = true;
                    break;
                }
            }
            int cursor = 0;
            for (int d = 0; d < destIds.Count; d++)
            {
                if (pair[d] >= 0)
                {
                    continue;
                }
                while (cursor < used.Length && used[cursor])
                {
                    cursor++;
                }
                if (cursor >= used.Length)
                {
                    return null;
                }
                pair[d] = cursor;
                used[cursor] = true;
                cursor++;
            }
            return pair;
        }
        private static string[] RebuildMaterial(string[] source, string[] dest)
        {
            if (source == null || dest == null || source.Length == 0 || dest.Length == 0)
            {
                return null;
            }
            List<string> rows = new List<string>();
            rows.Add(dest[0]);
            for (int i = 1; i < source.Length; i++)
            {
                rows.Add(source[i]);
            }
            return rows.ToArray();
        }
        private static int CountDocFileId(string text, string fileId)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fileId))
            {
                return 0;
            }
            string[] rows = SplitScene(text);
            int count = 0;
            string tail = "&" + fileId;
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("--- !u!"))
                {
                    continue;
                }
                int amp = trimmed.LastIndexOf('&');
                if (amp >= 0 && trimmed.Substring(amp).Trim() == tail)
                {
                    count++;
                }
            }
            return count;
        }
        private static string[] RebuildBlock(string[] source, string[] dest)
        {
            string destGo = null;
            for (int i = 0; i < dest.Length; i++)
            {
                if (dest[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    destGo = dest[i];
                    break;
                }
            }
            if (destGo == null)
            {
                return null;
            }
            bool saw = false;
            List<string> rows = new List<string>();
            rows.Add(dest[0]);
            for (int i = 1; i < source.Length; i++)
            {
                if (source[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    rows.Add(destGo);
                    saw = true;
                }
                else
                {
                    rows.Add(source[i]);
                }
            }
            if (!saw)
            {
                return null;
            }
            return rows.ToArray();
        }
        private static string BlockSignature(string[] block)
        {
            string header = block[0].Trim();
            int bang = header.IndexOf("!u!");
            int amp = header.LastIndexOf('&');
            string typeId = string.Empty;
            if (bang >= 0 && amp > bang)
            {
                typeId = header.Substring(bang, amp - bang).Trim();
            }
            string className = block.Length > 1 ? block[1].Trim() : string.Empty;
            return typeId + " " + className;
        }
        private static bool TryObjectBlocks(string text, string targetName, out List<string[]> blocks)
        {
            blocks = new List<string[]>();
            string goId;
            if (!TryGameObjectFileId(text, targetName, out goId))
            {
                return false;
            }
            string[] rows = SplitScene(text);
            List<string> ids = ComponentIds(rows, goId);
            if (ids.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < ids.Count; i++)
            {
                int start;
                int end;
                if (!TryDocSpan(rows, ids[i], out start, out end))
                {
                    return false;
                }
                string[] slice = new string[end - start];
                for (int j = start; j < end; j++)
                {
                    slice[j - start] = rows[j];
                }
                blocks.Add(slice);
            }
            return true;
        }
        private static List<string> ComponentIds(string[] rows, string goId)
        {
            List<string> ids = new List<string>();
            string header = "--- !u!1 &" + goId;
            int doc = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == header)
                {
                    doc = i;
                    break;
                }
            }
            if (doc < 0)
            {
                return ids;
            }
            for (int i = doc + 1; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (trimmed.StartsWith("--- "))
                {
                    break;
                }
                if (!trimmed.StartsWith("- component:"))
                {
                    continue;
                }
                int key = trimmed.IndexOf("fileID:");
                if (key < 0)
                {
                    continue;
                }
                string rest = trimmed.Substring(key + 7).Trim().TrimEnd('}');
                ids.Add(rest.Trim());
            }
            return ids;
        }
        private static bool TryDocSpan(string[] rows, string fileId, out int start, out int end)
        {
            start = -1;
            end = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("--- !u!"))
                {
                    continue;
                }
                int amp = trimmed.LastIndexOf('&');
                if (amp < 0 || trimmed.Substring(amp + 1).Trim() != fileId)
                {
                    continue;
                }
                int stop = rows.Length;
                for (int j = i + 1; j < rows.Length; j++)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        stop = j;
                        break;
                    }
                }
                start = i;
                end = stop;
                return true;
            }
            return false;
        }

        private static bool TryApplyTransform(string sourceText, string destText, string targetName, out string updated, out string scaleLine)
        {
            updated = destText;
            scaleLine = string.Empty;
            string sourceId;
            string destId;
            if (!TryGameObjectFileId(sourceText, targetName, out sourceId) || !TryGameObjectFileId(destText, targetName, out destId))
            {
                return false;
            }
            string[] sourceRows = SplitScene(sourceText);
            string[] destRows = SplitScene(destText);
            int sourceStart;
            int sourceEnd;
            int destStart;
            int destEnd;
            if (!TryTransformSpan(sourceRows, sourceId, out sourceStart, out sourceEnd) || !TryTransformSpan(destRows, destId, out destStart, out destEnd))
            {
                return false;
            }
            string[] keys = new string[] { "m_LocalRotation:", "m_LocalPosition:", "m_LocalScale:", "m_LocalEulerAnglesHint:" };
            for (int k = 0; k < keys.Length; k++)
            {
                int from = FindField(sourceRows, sourceStart, sourceEnd, keys[k]);
                int to = FindField(destRows, destStart, destEnd, keys[k]);
                if (from < 0 || to < 0)
                {
                    return false;
                }
                string indent = destRows[to].Substring(0, destRows[to].Length - destRows[to].TrimStart().Length);
                destRows[to] = indent + sourceRows[from].Trim();
                if (keys[k] == "m_LocalScale:")
                {
                    scaleLine = destRows[to].Trim();
                }
            }
            updated = JoinScene(destText, destRows);
            return true;
        }
        private static bool TryGameObjectFileId(string text, string targetName, out string fileId)
        {
            fileId = string.Empty;
            string[] rows = SplitScene(text);
            string nameLine = "m_Name: " + targetName;
            int at = -1;
            int hits = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == nameLine)
                {
                    hits++;
                    at = i;
                }
            }
            if (hits != 1)
            {
                return false;
            }
            for (int i = at; i >= 0; i--)
            {
                string row = rows[i].Trim();
                if (row.StartsWith("--- !u!1 &"))
                {
                    fileId = row.Substring("--- !u!1 &".Length).Trim();
                    return fileId.Length > 0;
                }
                if (row.StartsWith("--- "))
                {
                    return false;
                }
            }
            return false;
        }
        private static bool TryTransformSpan(string[] rows, string goId, out int start, out int end)
        {
            start = -1;
            end = -1;
            string needle = "m_GameObject: {fileID: " + goId + "}";
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() != needle)
                {
                    continue;
                }
                int doc = -1;
                for (int j = i; j >= 0; j--)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        if (rows[j].TrimStart().StartsWith("--- !u!4 "))
                        {
                            doc = j;
                        }
                        break;
                    }
                }
                if (doc < 0)
                {
                    continue;
                }
                int stop = rows.Length;
                for (int j = doc + 1; j < rows.Length; j++)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        stop = j;
                        break;
                    }
                }
                start = doc;
                end = stop;
                return true;
            }
            return false;
        }
        private static int FindField(string[] rows, int start, int end, string key)
        {
            int found = -1;
            for (int i = start; i < end; i++)
            {
                if (rows[i].TrimStart().StartsWith(key))
                {
                    if (found >= 0)
                    {
                        return -1;
                    }
                    found = i;
                }
            }
            return found;
        }
        private static string[] SplitScene(string text)
        {
            string norm = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            if (norm.EndsWith("\n"))
            {
                norm = norm.Substring(0, norm.Length - 1);
            }
            return norm.Split('\n');
        }
        private static string JoinScene(string original, string[] rows)
        {
            string joined = string.Join("\n", rows);
            bool trail = (original ?? string.Empty).Replace("\r\n", "\n").EndsWith("\n");
            if (trail)
            {
                joined += "\n";
            }
            if ((original ?? string.Empty).Contains("\r\n"))
            {
                joined = joined.Replace("\n", "\r\n");
            }
            return joined;
        }
        private void BackToMyScene()
        {
            if (string.IsNullOrEmpty(_returnScene))
            {
                return;
            }
            string abs = _returnScene;
            if (_returnScene.StartsWith("Assets/"))
            {
                abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, _returnScene);
            }
            if (!File.Exists(abs))
            {
                _checkLine = "Scene to return to is missing. Your scene was not changed.";
                return;
            }
            EditorSceneManager.OpenScene(_returnScene, OpenSceneMode.Single);
        }
        private void CheckTheCopy()
        {
            string worktree = RollingRecorderControl.FindCheckWorktree();
            if (string.IsNullOrEmpty(worktree) || !Directory.Exists(worktree))
            {
                _checkLine = "No copy to check. Your scene was not changed.";
                return;
            }
            string[] packets = Directory.GetFiles(worktree, "packet-*.json");
            if (packets == null || packets.Length == 0)
            {
                _checkLine = "No copy to check. Your scene was not changed.";
                return;
            }
            string packetFile = packets[0];
            for (int i = 1; i < packets.Length; i++)
            {
                if (string.CompareOrdinal(packets[i], packetFile) > 0)
                {
                    packetFile = packets[i];
                }
            }
            StillWrong.Dispatch.DispatchPacket packet = null;
            try
            {
                packet = JsonUtility.FromJson<StillWrong.Dispatch.DispatchPacket>(File.ReadAllText(packetFile));
            }
            catch
            {
                packet = null;
            }
            if (packet == null || string.IsNullOrEmpty(packet.ScenePath))
            {
                _checkLine = "Copy not found. Your scene was not changed.";
                return;
            }
            string rel = packet.ScenePath.Replace("\\", "/");
            string source = Path.Combine(worktree, rel);
            if (!File.Exists(source))
            {
                _checkLine = "Copy not found. Your scene was not changed.";
                return;
            }
            string destDir = Path.Combine(Application.dataPath, "StillWrongDispatch", "FixedCopy");
            Directory.CreateDirectory(destDir);
            string fileName = Path.GetFileName(source);
            string dest = Path.Combine(destDir, fileName);
            File.Copy(source, dest, true);
            string meta = source + ".meta";
            if (File.Exists(meta))
            {
                File.Copy(meta, dest + ".meta", true);
                AssignFreshGuid(dest + ".meta");
            }
            AssetDatabase.Refresh();
            string assetPath = "Assets/StillWrongDispatch/FixedCopy/" + fileName;
            UnityEngine.SceneManagement.Scene open = EditorSceneManager.GetActiveScene();
            string openPath = open.path ?? string.Empty;
            string sourceScene = packet.SourceScenePath ?? string.Empty;
            sourceScene = sourceScene.Replace('\\', '/');
            if (!string.IsNullOrEmpty(sourceScene) && sourceScene.IndexOf("StillWrongDispatch", StringComparison.Ordinal) < 0)
            {
                _returnScene = sourceScene;
            }
            else if (string.IsNullOrEmpty(packet.SourceScenePath) && !string.IsNullOrEmpty(openPath) && openPath.IndexOf("StillWrongDispatch", StringComparison.Ordinal) < 0)
            {
                _returnScene = openPath;
            }
            EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
            string target = packet.Target != null ? packet.Target.Name : string.Empty;
            if (!IsScalePacket(packet))
            {
                FinishBlockLine(dest, target, packet);
                return;
            }
            float x;
            float y;
            float z;
            if (!TryReadScale(dest, target, out x, out y, out z))
            {
                _checkLine = "Opened the copy. Your scene was not changed.";
                return;
            }
            float largest = Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z)));
            string numbers = x.ToString("0.###") + ", " + y.ToString("0.###") + ", " + z.ToString("0.###");
            if (largest > 1.2f)
            {
                _checkLine = "Still wrong. This is the copy. Your scene was not changed. " + numbers;
            }
            else
            {
                _checkLine = "Fixed. This is the copy. Your scene was not changed. " + numbers;
            }
        }
        private static void AssignFreshGuid(string metaPath)
        {
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
            {
                return;
            }
            string body = File.ReadAllText(metaPath);
            string[] rows = body.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("guid:"))
                {
                    continue;
                }
                string oldGuid = trimmed.Substring(5).Trim();
                string fresh = NewSceneGuid(oldGuid);
                int pad = rows[i].Length - rows[i].TrimStart().Length;
                rows[i] = rows[i].Substring(0, pad) + "guid: " + fresh;
                string joined = string.Join("\n", rows);
                if (body.Contains("\r\n"))
                {
                    joined = joined.Replace("\n", "\r\n");
                }
                File.WriteAllText(metaPath, joined);
                return;
            }
        }
        private static string NewSceneGuid(string oldGuid)
        {
            string fresh = Guid.NewGuid().ToString("N");
            int guard = 0;
            while (!string.IsNullOrEmpty(oldGuid) && fresh == oldGuid && guard < 4)
            {
                fresh = Guid.NewGuid().ToString("N");
                guard++;
            }
            return fresh;
        }
        private static bool IsScalePacket(StillWrong.Dispatch.DispatchPacket packet)
        {
            if (packet == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(packet.Sentence) && packet.Sentence.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(packet.FreshProcessVerdict) && packet.FreshProcessVerdict.StartsWith("scale"))
            {
                return true;
            }
            if (packet.FailureReasons == null)
            {
                return false;
            }
            for (int i = 0; i < packet.FailureReasons.Count; i++)
            {
                string reason = packet.FailureReasons[i];
                if (!string.IsNullOrEmpty(reason) && reason.StartsWith("scale"))
                {
                    return true;
                }
            }
            return false;
        }
        private static bool TryReadScale(string sceneFile, string targetName, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            if (string.IsNullOrEmpty(targetName) || !File.Exists(sceneFile))
            {
                return false;
            }
            string[] lines = File.ReadAllLines(sceneFile);
            string nameLine = "m_Name: " + targetName;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != nameLine)
                {
                    continue;
                }
                int end = Mathf.Min(i + 24, lines.Length);
                for (int j = i; j < end; j++)
                {
                    string row = lines[j].Trim();
                    if (!row.StartsWith("m_LocalScale:"))
                    {
                        continue;
                    }
                    return ParseScale(row, out x, out y, out z);
                }
            }
            return false;
        }
        private static bool ParseScale(string row, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            int xs = row.IndexOf("x:");
            int ys = row.IndexOf("y:");
            int zs = row.IndexOf("z:");
            if (xs < 0 || ys < 0 || zs < 0)
            {
                return false;
            }
            string xv = row.Substring(xs + 2, ys - (xs + 2)).Trim().TrimEnd(","[0]);
            string yv = row.Substring(ys + 2, zs - (ys + 2)).Trim().TrimEnd(","[0]);
            int brace = row.IndexOf("}", zs);
            string zv = brace > zs ? row.Substring(zs + 2, brace - (zs + 2)).Trim() : row.Substring(zs + 2).Trim();
            return float.TryParse(xv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(yv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && float.TryParse(zv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
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
        private void DispatchBugNow()
        {
            _checkLine = string.Empty;
            string chunk;
            string file;
            double clock;
            List<string> paths;
            GatherDispatchArgs(out chunk, out file, out clock, out paths);
            string sessionDir = _sessionDir;
            string candidatePath = _confirmedPath;
            string sentence = _sentence;
            _progressLine = "Checking this frame.";
            Repaint();
            FrozenSessionSheet.BeginAuthorSpec(sentence, candidatePath, 6, authored =>
            {
                string progress = string.Empty;
                string failure;
                try
                {
                    failure = RollingRecorderControl.RunBugDispatch(sessionDir, chunk, file, clock, candidatePath, paths, sentence, authored, out progress);
                }
                catch (Exception ex)
                {
                    failure = "failed: " + ex.GetType().Name + ": " + ex.Message;
                    progress = string.Empty;
                }
                if (!string.IsNullOrEmpty(failure))
                {
                    _progressLine = failure;
                }
                else
                {
                    _progressLine = progress;
                }
                Repaint();
            });
        }
        private void DispatchBugUnverifiedNow()
        {
            string chunk;
            string file;
            double clock;
            List<string> paths;
            GatherDispatchArgs(out chunk, out file, out clock, out paths);
            _progressLine = "Sending this frame.";
            Repaint();
            RollingRecorderControl.BeginBugDispatchUnverified(_sessionDir, chunk, file, clock, _confirmedPath, paths, _sentence, (failure, progress) =>
            {
                if (!string.IsNullOrEmpty(failure))
                {
                    _progressLine = failure;
                }
                else
                {
                    _progressLine = progress;
                }
                Repaint();
            });
        }
        private void GatherDispatchArgs(out string chunk, out string file, out double clock, out List<string> paths)
        {
            chunk = string.Empty;
            file = string.Empty;
            clock = 0.0;
            if (_selectedThumb >= 0 && _selectedThumb < _thumbLabels.Count)
            {
                string label = _thumbLabels[_selectedThumb];
                int space = label.IndexOf(" ");
                if (space > 0)
                {
                    chunk = label.Substring(0, space);
                    file = label.Substring(space + 1) + ".jpg";
                }
            }
            if (_selectedThumb >= 0 && _selectedThumb < _thumbTimes.Count)
            {
                clock = _thumbTimes[_selectedThumb];
            }
            if (string.IsNullOrEmpty(_confirmedPath) && CurrentCandidate() != null && !string.IsNullOrEmpty(CurrentCandidate().ElementPath))
            {
                ConfirmCurrent();
            }
            paths = new List<string>();
            if (_candidates != null)
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidates[i] != null && !string.IsNullOrEmpty(_candidates[i].ElementPath))
                    {
                        paths.Add(_candidates[i].ElementPath);
                    }
                }
            }
        }
        public string DispatchSelectedBug(string sentence)
        {
            if (!string.IsNullOrEmpty(sentence))
            {
                _sentence = sentence;
            }
            DispatchBugNow();
            return _progressLine;
        }
        public string SheetProgress()
        {
            return _progressLine;
        }
    }
}
