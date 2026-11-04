using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Inspection;
using StillWrong.Oracle;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D3GateState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public int StartAbsFrame;
        public int BeginFrame = -1;
        public int OverridesBeginFrame = -1;
        public int CaptureFrame = -1;
        public int OverridesEndFrame = -1;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D3GateBox
    {
        public double X1;
        public double Y1;
        public double X2Inclusive;
        public double Y2Inclusive;
        public double W;
        public double H;
        public double RefW = 1920.0;
        public double RefH = 1080.0;
    }
    [Serializable]
    public sealed class D3GateBody
    {
        public string Path = string.Empty;
        public int Id;
        public double Pixels;
        public double Fraction;
        public string Missing = string.Empty;
    }
    [Serializable]
    public sealed class D3GateCoverageEntry
    {
        public string Path = string.Empty;
        public int Id;
        public int PixelsInScope;
        public int ScopePixels;
        public double MaskCoverage;
        public double ElementCoverage;
        public bool IsAnalytic;
        public string Warning = string.Empty;
    }
    [Serializable]
    public sealed class D3GateResult
    {
        public string RunId = string.Empty;
        public string CaseId = "C1-ui-clip";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public int MeasureFrame = 6;
        public int IdElements;
        public int IdPixelTotal;
        public int BackgroundPixels;
        public D3GateBox BoxRef = new D3GateBox();
        public D3GateBox BoxScaled = new D3GateBox();
        public D3GateBody Body = new D3GateBody();
        public List<D3GateCoverageEntry> Coverages = new List<D3GateCoverageEntry>();
        public List<D3GateCoverageEntry> MaskCoveragesFlipped = new List<D3GateCoverageEntry>();
        public bool Ambiguous;
        public bool MaskAvailable;
        public string MaskReason = string.Empty;
        public int BeginFrame = -1;
        public int EndFrame = -1;
        public int FramesPresentedWithIds;
        public int TexW;
        public int TexH;
        public int CaptureW;
        public int CaptureH;
        public bool GameViewMatched;
        public int OverridesBeginFrame = -1;
        public int CaptureFrame = -1;
        public int OverridesEndFrame = -1;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D3GateMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D3GateDriver
    {
        private const string GateFolder = "Library/StillWrong/d3gate";
        private const int MeasureFrame = 6;
        private const double BoxX1 = 799.0;
        private const double BoxTop = 483.0;
        private const double BoxX2Inclusive = 1077.0;
        private const double BoxBottomInclusive = 591.0;
        private const double BoxRefW = 1920.0;
        private const double BoxRefH = 1080.0;
        private static bool _ticked;
        private static Texture2D _captureTex;
        private static bool _captureDone;
        private static GameObject _captureHost;
        private static int _captureW;
        private static int _captureH;
        private static List<StillWrongGraphicOverride> _graphicsSaved;
        private static List<StillWrongRendererOverride> _renderersSaved;
        private static Dictionary<string, int> _pathToId;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string GateDir()
        {
            return Path.Combine(ProjectRoot(), GateFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(GateDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(GateDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(GateDir());
            _captureTex = null;
            _captureDone = false;
            _captureHost = null;
            _captureW = 0;
            _captureH = 0;
            _graphicsSaved = null;
            _renderersSaved = null;
            _pathToId = null;
            C1UiClipFixtureBuilder.BuildFailingScene();
            string runId = "d3gate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D3GateState state = new D3GateState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D3GateMarker marker = new D3GateMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(GateDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
            EditorApplication.isPlaying = true;
            return runId;
        }
        [InitializeOnLoadMethod]
        private static void Register()
        {
            if (_ticked)
            {
                return;
            }
            _ticked = true;
            EditorApplication.update += Tick;
        }
        private static D3GateState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D3GateState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D3GateState state)
        {
            try
            {
                Directory.CreateDirectory(GateDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D3GateState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D3GateMarker marker = new D3GateMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(GateDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            RestoreOverrides();
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }
            }
            catch
            {
            }
        }
        private static void ArmTick()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            D3GateState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay")
                {
                    TickEnterPlay(state);
                }
                else if (state.Phase == "measure")
                {
                    TickMeasure(state);
                }
                else if (state.Phase == "idcapture")
                {
                    TickIdCapture(state);
                }
                else if (state.Phase == "iddecode")
                {
                    TickIdDecode(state);
                }
                else if (state.Phase == "exitplay")
                {
                    TickExitPlay(state);
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(D3GateState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            state.Phase = "measure";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickMeasure(D3GateState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "measure-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "measure-timeout");
                return;
            }
            if (Time.frameCount - state.StartAbsFrame < MeasureFrame)
            {
                return;
            }
            state.Phase = "idcapture";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickIdCapture(D3GateState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "idcapture-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "idcapture-timeout");
                return;
            }
            List<string> paths = CollectLiveRegistryPaths();
            if (paths.Count == 0)
            {
                Fail(state, "idcapture-empty-registry");
                return;
            }
            int markW = Screen.width;
            int markH = Screen.height;
            if (markW <= 0 || markH <= 0)
            {
                Fail(state, "idcapture-bad-screen-size");
                return;
            }
            _pathToId = StillWrongIdBuffer.AssignIds(paths);
            state.OverridesBeginFrame = Time.frameCount;
            StillWrongIdBuffer.SetRegistryMap(_pathToId);
            _graphicsSaved = StillWrongIdBuffer.BeginGraphicIds(StillWrongIdBuffer.BuildGraphicAssignments(_pathToId));
            _renderersSaved = StillWrongIdBuffer.BeginRendererIds(StillWrongIdBuffer.BuildRendererAssignments(_pathToId));
            _captureDone = false;
            _captureTex = null;
            _captureW = markW;
            _captureH = markH;
            GameObject host = new GameObject("StillWrong_D3GateCapture");
            D3GateCaptureHost runner = host.AddComponent<D3GateCaptureHost>();
            runner.Width = markW;
            runner.Height = markH;
            runner.StartRun();
            _captureHost = host;
            state.BeginFrame = Time.frameCount;
            state.CaptureFrame = Time.frameCount;
            state.Phase = "iddecode";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickIdDecode(D3GateState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "iddecode-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "iddecode-timeout");
                return;
            }
            if (!_captureDone)
            {
                return;
            }
            if (_captureHost != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_captureHost);
                }
                catch
                {
                }
                _captureHost = null;
            }
            if (_captureTex == null)
            {
                Fail(state, "iddecode-capture-failed");
                return;
            }
            state.OverridesEndFrame = Time.frameCount;
            D3GateResult result = BuildResult(state);
            WriteResult(state, result, string.Empty);
            RestoreOverrides();
            state.Phase = "exitplay";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void TickExitPlay(D3GateState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(C1UiClipFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            try
            {
                D3GateMarker marker = new D3GateMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(GateDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static List<string> CollectLiveRegistryPaths()
        {
            List<string> paths = new List<string>();
            try
            {
                List<SpatialHitElement> elements = UnitySpatialHitProvider.CollectAllSpatialElements();
                for (int i = 0; i < elements.Count; i++)
                {
                    if (elements[i] == null || string.IsNullOrEmpty(elements[i].HierarchyPath))
                    {
                        continue;
                    }
                    if (!paths.Contains(elements[i].HierarchyPath))
                    {
                        paths.Add(elements[i].HierarchyPath);
                    }
                }
            }
            catch
            {
            }
            try
            {
                Dictionary<string, Rect2D> docs = StillWrongIdBuffer.CollectUidocumentRects(Camera.main);
                foreach (string path in docs.Keys)
                {
                    if (!paths.Contains(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            catch
            {
            }
            return paths;
        }
        private static void RestoreOverrides()
        {
            try
            {
                StillWrongIdBuffer.EndGraphicIds(_graphicsSaved);
            }
            catch
            {
            }
            try
            {
                StillWrongIdBuffer.EndRendererIds(_renderersSaved);
            }
            catch
            {
            }
            _graphicsSaved = null;
            _renderersSaved = null;
        }
        private static D3GateResult BuildResult(D3GateState state)
        {
            D3GateResult result = new D3GateResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.MeasureFrame = MeasureFrame;
            result.BeginFrame = state.BeginFrame;
            result.EndFrame = Time.frameCount;
            result.FramesPresentedWithIds = Math.Max(0, result.EndFrame - result.BeginFrame);
            result.BoxRef = ScaledBox(BoxX1, BoxTop, BoxX2Inclusive, BoxBottomInclusive, BoxRefW, BoxRefH);
            double sx = state.ScreenW > 0 ? state.ScreenW / BoxRefW : 1.0;
            double sy = state.ScreenH > 0 ? state.ScreenH / BoxRefH : 1.0;
            result.BoxScaled = ScaledBox(BoxX1 * sx, BoxTop * sy, BoxX2Inclusive * sx, BoxBottomInclusive * sy, state.ScreenW, state.ScreenH);
            if (_captureTex == null || _pathToId == null)
            {
                result.Failure = "missing-capture-or-registry";
                return result;
            }
            StillWrongIdMap map = null;
            try
            {
                map = StillWrongIdBuffer.DecodeFrame(_captureTex);
            }
            catch
            {
                map = null;
            }
            if (map == null || map.Ids == null || map.Ids.Length == 0)
            {
                result.Failure = "decode-failed";
                return result;
            }
            StillWrongIdBuffer.SetIdMap(map);
            result.IdPixelTotal = map.Ids.Length;
            result.TexW = map.Width;
            result.TexH = map.Height;
            result.CaptureW = _captureW;
            result.CaptureH = _captureH;
            result.GameViewMatched = _captureW == state.ScreenW && _captureH == state.ScreenH && _captureW > 0;
            result.OverridesBeginFrame = state.OverridesBeginFrame;
            result.CaptureFrame = state.CaptureFrame;
            result.OverridesEndFrame = state.OverridesEndFrame;
            WriteIdMapPng(map);
            int background = 0;
            map.Counts.TryGetValue(0, out background);
            result.BackgroundPixels = background;
            result.IdElements = _pathToId.Count;
            OracleSpec spec = FixtureSpecs.C1();
            UnityProbeSource source = new UnityProbeSource(spec);
            string bodyPath = BodyPath(spec);
            result.Body.Path = bodyPath;
            int bodyId = 0;
            if (!string.IsNullOrEmpty(bodyPath) && _pathToId.TryGetValue(bodyPath, out bodyId) && bodyId != 0)
            {
                result.Body.Id = bodyId;
                MeasuredValue pixels = null;
                MeasuredValue fraction = null;
                bool hasPixels = source.TryGetValue(Probes.VisiblePixels("body"), 0, out pixels) && pixels != null && !pixels.IsMissing && pixels.Number.HasValue;
                bool hasFraction = source.TryGetValue(Probes.VisibleFraction("body"), 0, out fraction) && fraction != null && !fraction.IsMissing && fraction.Number.HasValue;
                if (hasPixels && hasFraction)
                {
                    result.Body.Pixels = pixels.Number.Value;
                    result.Body.Fraction = fraction.Number.Value;
                }
                else
                {
                    result.Body.Missing = DescribeMissing(pixels, fraction);
                }
            }
            else
            {
                result.Body.Missing = "no-id-for-body";
            }
            Rect boxRect = BoxToTextureRect(result.BoxScaled, map.Width, map.Height);
            Dictionary<int, string> idToPath = new Dictionary<int, string>();
            foreach (KeyValuePair<string, int> kv in _pathToId)
            {
                if (kv.Value != 0 && !idToPath.ContainsKey(kv.Value))
                {
                    idToPath.Add(kv.Value, kv.Key);
                }
            }
            HashSet<string> analytic = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                Dictionary<string, Rect2D> docs = StillWrongIdBuffer.CollectUidocumentRects(Camera.main);
                foreach (string path in docs.Keys)
                {
                    analytic.Add(path);
                }
            }
            catch
            {
            }
            List<SamCoverageResult> coverage = new List<SamCoverageResult>();
            try
            {
                coverage = SamCoverage.FromBox(boxRect, map, idToPath, LookupRect, analytic);
            }
            catch
            {
                coverage = new List<SamCoverageResult>();
            }
            for (int i = 0; i < coverage.Count; i++)
            {
                SamCoverageResult entry = coverage[i];
                if (entry == null || entry.ElementId == 0)
                {
                    continue;
                }
                D3GateCoverageEntry dto = new D3GateCoverageEntry();
                dto.Path = entry.ElementPath;
                dto.Id = entry.ElementId;
                dto.PixelsInScope = entry.PixelsInScope;
                dto.ScopePixels = entry.ScopePixels;
                dto.MaskCoverage = entry.MaskCoverage;
                dto.ElementCoverage = entry.ElementCoverage;
                dto.IsAnalytic = entry.IsAnalytic;
                dto.Warning = entry.Warning;
                result.Coverages.Add(dto);
            }
            result.Ambiguous = SamCoverage.IsAmbiguous(coverage);
            bool[,] mask = LoadMaskSidecar(map.Width, map.Height);
            if (mask != null)
            {
                result.MaskAvailable = true;
                result.MaskReason = "mask-sidecar-decoded";
                List<SamCoverageResult> maskCoverage = new List<SamCoverageResult>();
                try
                {
                    maskCoverage = SamCoverage.FromMask(mask, map, idToPath);
                }
                catch
                {
                    maskCoverage = new List<SamCoverageResult>();
                }
                for (int i = 0; i < maskCoverage.Count; i++)
                {
                    SamCoverageResult entry = maskCoverage[i];
                    if (entry == null || entry.ElementId == 0)
                    {
                        continue;
                    }
                    D3GateCoverageEntry dto = new D3GateCoverageEntry();
                    dto.Path = entry.ElementPath + " [mask]";
                    dto.Id = entry.ElementId;
                    dto.PixelsInScope = entry.PixelsInScope;
                    dto.ScopePixels = entry.ScopePixels;
                    dto.MaskCoverage = entry.MaskCoverage;
                    dto.ElementCoverage = entry.ElementCoverage;
                    dto.IsAnalytic = entry.IsAnalytic;
                    dto.Warning = entry.Warning;
                    result.Coverages.Add(dto);
                }
            }
            else
            {
                result.MaskAvailable = false;
                result.MaskReason = "no-mask-sidecar";
            }
            if (mask != null)
            {
                bool[,] flipped = FlipMaskVertical(mask);
                List<SamCoverageResult> flippedCoverage = new List<SamCoverageResult>();
                try
                {
                    flippedCoverage = SamCoverage.FromMask(flipped, map, idToPath);
                }
                catch
                {
                    flippedCoverage = new List<SamCoverageResult>();
                }
                for (int i = 0; i < flippedCoverage.Count; i++)
                {
                    SamCoverageResult entry = flippedCoverage[i];
                    if (entry == null || entry.ElementId == 0)
                    {
                        continue;
                    }
                    D3GateCoverageEntry dto = new D3GateCoverageEntry();
                    dto.Path = entry.ElementPath + " [mask-flipped]";
                    dto.Id = entry.ElementId;
                    dto.PixelsInScope = entry.PixelsInScope;
                    dto.ScopePixels = entry.ScopePixels;
                    dto.MaskCoverage = entry.MaskCoverage;
                    dto.ElementCoverage = entry.ElementCoverage;
                    dto.IsAnalytic = entry.IsAnalytic;
                    dto.Warning = entry.Warning;
                    result.MaskCoveragesFlipped.Add(dto);
                }
            }
            return result;
        }
        private static string BodyPath(OracleSpec spec)
        {
            if (spec == null || spec.Targets == null)
            {
                return string.Empty;
            }
            for (int i = 0; i < spec.Targets.Count; i++)
            {
                if (spec.Targets[i] != null && spec.Targets[i].Name == "body")
                {
                    return spec.Targets[i].ElementId;
                }
            }
            return string.Empty;
        }
        private static string DescribeMissing(MeasuredValue pixels, MeasuredValue fraction)
        {
            string a = pixels != null ? pixels.MissingReason : "no-value";
            string b = fraction != null ? fraction.MissingReason : "no-value";
            return "pixels:" + a + " fraction:" + b;
        }
        private static Rect2D LookupRect(string path)
        {
            GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(path);
            if (go == null)
            {
                return Rect2D.Zero;
            }
            return UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
        }
        private static D3GateBox ScaledBox(double x1, double yTop, double x2Inclusive, double yBottomInclusive, double refW, double refH)
        {
            D3GateBox box = new D3GateBox();
            box.X1 = x1;
            box.Y1 = yTop;
            box.X2Inclusive = x2Inclusive;
            box.Y2Inclusive = yBottomInclusive;
            box.W = Math.Max(0.0, x2Inclusive - x1 + 1.0);
            box.H = Math.Max(0.0, yBottomInclusive - yTop + 1.0);
            box.RefW = refW;
            box.RefH = refH;
            return box;
        }
        private static Rect BoxToTextureRect(D3GateBox scaled, int texW, int texH)
        {
            double texSx = scaled.RefW > 0.0 ? texW / scaled.RefW : 1.0;
            double texSy = scaled.RefH > 0.0 ? texH / scaled.RefH : 1.0;
            double xMin = scaled.X1 * texSx;
            double xMaxExcl = (scaled.X2Inclusive + 1.0) * texSx;
            double hImg = scaled.Y2Inclusive + 1.0 - scaled.Y1;
            double yMaxExcl = texH - scaled.Y1 * texSy;
            double yMin = yMaxExcl - hImg * texSy;
            return Rect.MinMaxRect((float)xMin, (float)yMin, (float)xMaxExcl, (float)yMaxExcl);
        }
        private static bool[,] LoadMaskSidecar(int texW, int texH)
        {
            try
            {
                string path = Path.Combine(GateDir(), "mask.json");
                if (!File.Exists(path))
                {
                    return null;
                }
                MaskSidecar sidecar = JsonUtility.FromJson<MaskSidecar>(File.ReadAllText(path));
                if (sidecar == null || sidecar.rows == null || sidecar.width <= 0 || sidecar.height <= 0)
                {
                    return null;
                }
                if (sidecar.width != texW || sidecar.height != texH)
                {
                    return null;
                }
                if (sidecar.rows.Count != sidecar.height)
                {
                    return null;
                }
                bool[,] mask = new bool[sidecar.height, sidecar.width];
                for (int y = 0; y < sidecar.height; y++)
                {
                    string row = sidecar.rows[y];
                    if (row == null || row.Length != sidecar.width)
                    {
                        return null;
                    }
                    for (int x = 0; x < sidecar.width; x++)
                    {
                        mask[y, x] = row[x] == 49;
                    }
                }
                return mask;
            }
            catch
            {
                return null;
            }
        }
        [Serializable]
        private sealed class MaskSidecar
        {
            public int width;
            public int height;
            public List<string> rows = new List<string>();
        }
        private static bool[,] FlipMaskVertical(bool[,] mask)
        {
            int h = mask.GetLength(0);
            int w = mask.GetLength(1);
            bool[,] flipped = new bool[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    flipped[y, x] = mask[h - 1 - y, x];
                }
            }
            return flipped;
        }
        private sealed class D3GateCaptureHost : MonoBehaviour
        {
            public int Width;
            public int Height;
            public void StartRun()
            {
                StartCoroutine(Capture());
            }
            private System.Collections.IEnumerator Capture()
            {
                yield return new WaitForEndOfFrame();
                RenderTexture target = null;
                try
                {
                    target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
                }
                catch
                {
                    target = null;
                }
                if (target == null)
                {
                    D3GateDriver.FinishCapture(null);
                    yield break;
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                }
                catch
                {
                    UnityEngine.Object.Destroy(target);
                    D3GateDriver.FinishCapture(null);
                    yield break;
                }
                bool done = false;
                bool error = false;
                byte[] raw = null;
                int expected = Width * Height * 3;
                AsyncGPUReadback.Request(target, 0, TextureFormat.RGB24, (AsyncGPUReadbackRequest req) =>
                {
                    if (req.hasError)
                    {
                        error = true;
                        done = true;
                        return;
                    }
                    var data = req.GetData<byte>();
                    if (data.Length != expected)
                    {
                        error = true;
                        done = true;
                        return;
                    }
                    raw = new byte[expected];
                    data.CopyTo(raw);
                    done = true;
                });
                float waitStart = Time.realtimeSinceStartup;
                while (!done && Time.realtimeSinceStartup - waitStart < 5.0f)
                {
                    yield return null;
                }
                UnityEngine.Object.Destroy(target);
                Texture2D tex = null;
                if (!error && raw != null)
                {
                    try
                    {
                        tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                        tex.LoadRawTextureData(raw);
                        tex.Apply();
                    }
                    catch
                    {
                        tex = null;
                    }
                }
                D3GateDriver.FinishCapture(tex);
            }
        }
        private static void FinishCapture(Texture2D tex)
        {
            _captureTex = tex;
            _captureDone = true;
        }
        private static void WriteIdMapPng(StillWrongIdMap map)
        {
            if (map == null || map.Ids == null || map.Ids.Length == 0)
            {
                return;
            }
            try
            {
                Texture2D tex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[map.Ids.Length];
                for (int i = 0; i < map.Ids.Length; i++)
                {
                    int id = map.Ids[i] & 16777215;
                    pixels[i] = new Color32((byte)(id & 255), (byte)((id >> 8) & 255), (byte)((id >> 16) & 255), 255);
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                byte[] png = tex.EncodeToPNG();
                if (png != null && png.Length > 0)
                {
                    File.WriteAllBytes(Path.Combine(GateDir(), "idmap.png"), png);
                }
                UnityEngine.Object.Destroy(tex);
            }
            catch
            {
            }
        }
        private static void WriteResult(D3GateState state, D3GateResult result, string failure)
        {
            try
            {
                D3GateResult dto = result != null ? result : new D3GateResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(GateDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}

