using System;
using System.Collections.Generic;
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
using UnityEngine.UI;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class C2GateState
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
    public sealed class C2GateTarget
    {
        public string Name = string.Empty;
        public string Path = string.Empty;
        public int Id;
        public double Pixels;
        public double Fraction;
        public string Missing = string.Empty;
    }
    [Serializable]
    public sealed class C2GateScopeBox
    {
        public string SourcePath = string.Empty;
        public float XMin;
        public float YMin;
        public float XMaxExcl;
        public float YMaxExcl;
        public int ScreenW;
        public int ScreenH;
        public int TexW;
        public int TexH;
        public string Missing = string.Empty;
    }
    [Serializable]
    public sealed class C2GateCandidate
    {
        public string Path = string.Empty;
        public int Id;
        public int PixelsInScope;
        public int ScopePixels;
        public double MaskCoverage;
        public double ElementCoverage;
        public bool IsAnalytic;
        public string Warning = string.Empty;
        public string Type = string.Empty;
        public string Parent = string.Empty;
        public float RectX;
        public float RectY;
        public float RectW;
        public float RectH;
        public string RectMissing = string.Empty;
        public float Alpha = 1.0f;
        public string AlphaMissing = string.Empty;
        public int SortOrder = -1;
    }
    [Serializable]
    public sealed class C2GateResult
    {
        public string RunId = string.Empty;
        public string CaseId = "C2-sorting-occlusion";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public int MeasureFrame = 3;
        public int IdElements;
        public int IdPixelTotal;
        public int BackgroundPixels;
        public C2GateScopeBox ScopeBox = new C2GateScopeBox();
        public List<C2GateTarget> Targets = new List<C2GateTarget>();
        public List<C2GateCandidate> Candidates = new List<C2GateCandidate>();
        public List<C2GateCandidate> MaskCandidates = new List<C2GateCandidate>();
        public List<C2GateCandidate> MaskCandidatesFlipped = new List<C2GateCandidate>();
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
    public sealed class C2GateMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class C2GateDriver
    {
        private const string GateFolder = "Library/StillWrong/c2gate";
        private const int MeasureFrame = 3;
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
            C2SortingOcclusionFixtureBuilder.BuildFailingScene();
            string runId = "c2gate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            C2GateState state = new C2GateState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            C2GateMarker marker = new C2GateMarker();
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
        private static C2GateState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<C2GateState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(C2GateState state)
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
        private static void Fail(C2GateState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                C2GateMarker marker = new C2GateMarker();
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
            C2GateState state = LoadState();
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
        private static void TickEnterPlay(C2GateState state)
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
        private static void TickMeasure(C2GateState state)
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
        private static void TickIdCapture(C2GateState state)
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
            GameObject host = new GameObject("StillWrong_C2GateCapture");
            C2GateCaptureHost runner = host.AddComponent<C2GateCaptureHost>();
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
        private static void TickIdDecode(C2GateState state)
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
            C2GateResult result = BuildResult(state);
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
        private static void TickExitPlay(C2GateState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(C2SortingOcclusionFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            try
            {
                C2GateMarker marker = new C2GateMarker();
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
        private static C2GateResult BuildResult(C2GateState state)
        {
            C2GateResult result = new C2GateResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.MeasureFrame = MeasureFrame;
            result.BeginFrame = state.BeginFrame;
            result.EndFrame = Time.frameCount;
            result.FramesPresentedWithIds = Math.Max(0, result.EndFrame - result.BeginFrame);
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
            OracleSpec spec = FixtureSpecs.C2();
            UnityProbeSource source = new UnityProbeSource(spec);
            MeasureTarget(source, result, "button", C2SortingOcclusionFixtureBuilder.TargetHierarchyPath);
            MeasureTarget(source, result, "dimmer", C2SortingOcclusionFixtureBuilder.OccluderHierarchyPath);
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
            Rect scopeRect;
            bool scopeValid = TryScopeRect(C2SortingOcclusionFixtureBuilder.TargetHierarchyPath, state.ScreenW, state.ScreenH, map.Width, map.Height, result.ScopeBox, out scopeRect);
            List<SamCoverageResult> coverage = new List<SamCoverageResult>();
            if (scopeValid)
            {
                try
                {
                    coverage = SamCoverage.FromBox(scopeRect, map, idToPath, LookupRect, analytic);
                }
                catch
                {
                    coverage = new List<SamCoverageResult>();
                }
            }
            result.Ambiguous = SamCoverage.IsAmbiguous(coverage);
            Dictionary<int, SamCoverageResult> coverageById = new Dictionary<int, SamCoverageResult>();
            for (int i = 0; i < coverage.Count; i++)
            {
                SamCoverageResult entry = coverage[i];
                if (entry == null || entry.ElementId == 0)
                {
                    continue;
                }
                if (!coverageById.ContainsKey(entry.ElementId))
                {
                    coverageById.Add(entry.ElementId, entry);
                }
            }
            foreach (KeyValuePair<int, string> kv in idToPath)
            {
                SamCoverageResult hit = null;
                coverageById.TryGetValue(kv.Key, out hit);
                result.Candidates.Add(BuildCandidate(kv.Value, kv.Key, hit, analytic));
            }
            bool[,] mask = LoadMaskSidecar(map.Width, map.Height);
            if (mask != null)
            {
                result.MaskAvailable = true;
                result.MaskReason = "mask-sidecar-decoded";
                FillMaskCandidates(mask, map, idToPath, analytic, result.MaskCandidates);
                bool[,] flipped = FlipMaskVertical(mask);
                FillMaskCandidates(flipped, map, idToPath, analytic, result.MaskCandidatesFlipped);
            }
            else
            {
                result.MaskAvailable = false;
                result.MaskReason = "no-mask-sidecar";
            }
            return result;
        }
        private static void MeasureTarget(UnityProbeSource source, C2GateResult result, string targetName, string path)
        {
            C2GateTarget dto = new C2GateTarget();
            dto.Name = targetName;
            dto.Path = path;
            int id = 0;
            if (!string.IsNullOrEmpty(path) && _pathToId != null && _pathToId.TryGetValue(path, out id) && id != 0)
            {
                dto.Id = id;
            }
            MeasuredValue pixels = null;
            MeasuredValue fraction = null;
            bool hasPixels = source.TryGetValue(Probes.VisiblePixels(targetName), 0, out pixels) && pixels != null && !pixels.IsMissing && pixels.Number.HasValue;
            bool hasFraction = source.TryGetValue(Probes.VisibleFraction(targetName), 0, out fraction) && fraction != null && !fraction.IsMissing && fraction.Number.HasValue;
            if (hasPixels && hasFraction)
            {
                dto.Pixels = pixels.Number.Value;
                dto.Fraction = fraction.Number.Value;
            }
            else
            {
                dto.Missing = "pixels:" + (pixels != null ? pixels.MissingReason : "no-value") + " fraction:" + (fraction != null ? fraction.MissingReason : "no-value");
            }
            if (dto.Id == 0 && string.IsNullOrEmpty(dto.Missing))
            {
                dto.Missing = "no-id-for-target";
            }
            result.Targets.Add(dto);
        }
        private static bool TryScopeRect(string targetPath, int screenW, int screenH, int texW, int texH, C2GateScopeBox dto, out Rect outRect)
        {
            outRect = new Rect(0f, 0f, 0f, 0f);
            dto.SourcePath = targetPath;
            dto.ScreenW = screenW;
            dto.ScreenH = screenH;
            dto.TexW = texW;
            dto.TexH = texH;
            if (screenW <= 0 || screenH <= 0 || texW <= 0 || texH <= 0)
            {
                dto.Missing = "bad-screen-or-texture-size";
                return false;
            }
            Rect2D r = LookupRect(targetPath);
            if (r.Width <= 0f || r.Height <= 0f)
            {
                dto.Missing = "empty-registry-rect";
                return false;
            }
            float texSx = (float)texW / (float)screenW;
            float texSy = (float)texH / (float)screenH;
            float xMin = r.X * texSx;
            float xMaxExcl = (r.X + r.Width) * texSx;
            float yTop = (float)screenH - (r.Y + r.Height);
            float yMaxExcl = (float)texH - yTop * texSy;
            float yMin = yMaxExcl - r.Height * texSy;
            outRect = Rect.MinMaxRect(xMin, yMin, xMaxExcl, yMaxExcl);
            dto.XMin = outRect.xMin;
            dto.YMin = outRect.yMin;
            dto.XMaxExcl = outRect.xMax;
            dto.YMaxExcl = outRect.yMax;
            return true;
        }
        private static C2GateCandidate BuildCandidate(string path, int id, SamCoverageResult hit, HashSet<string> analytic)
        {
            C2GateCandidate dto = new C2GateCandidate();
            dto.Path = path;
            dto.Id = id;
            if (hit != null)
            {
                dto.PixelsInScope = hit.PixelsInScope;
                dto.ScopePixels = hit.ScopePixels;
                dto.MaskCoverage = hit.MaskCoverage;
                dto.ElementCoverage = hit.ElementCoverage;
                dto.IsAnalytic = hit.IsAnalytic;
                dto.Warning = hit.Warning;
            }
            else
            {
                dto.Warning = "outside-scope";
            }
            if (analytic != null && analytic.Contains(path))
            {
                dto.IsAnalytic = true;
            }
            GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(path);
            if (go == null)
            {
                dto.RectMissing = "gameobject-not-found";
                dto.AlphaMissing = "gameobject-not-found";
                dto.Type = "missing";
                return dto;
            }
            dto.Type = DescribeType(go);
            dto.Parent = ParentPath(go);
            Rect2D rect = Rect2D.Zero;
            try
            {
                rect = UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
            }
            catch
            {
            }
            if (rect.Width > 0f && rect.Height > 0f)
            {
                dto.RectX = rect.X;
                dto.RectY = rect.Y;
                dto.RectW = rect.Width;
                dto.RectH = rect.Height;
            }
            else
            {
                dto.RectMissing = "empty-screen-rect";
            }
            Graphic graphic = go.GetComponent<Graphic>();
            if (graphic != null)
            {
                dto.Alpha = graphic.color.a;
            }
            else
            {
                dto.AlphaMissing = "no-graphic";
            }
            Canvas canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                dto.SortOrder = canvas.sortingOrder;
            }
            return dto;
        }
        private static string DescribeType(GameObject go)
        {
            if (go.GetComponent<Button>() != null)
            {
                return "Button";
            }
            if (go.GetComponent<Image>() != null)
            {
                return "Image";
            }
            if (go.GetComponent<Text>() != null)
            {
                return "Text";
            }
            if (go.GetComponent<RawImage>() != null)
            {
                return "RawImage";
            }
            if (go.GetComponent<Canvas>() != null)
            {
                return "Canvas";
            }
            if (go.GetComponent<Camera>() != null)
            {
                return "Camera";
            }
            if (go.GetComponent<RectTransform>() != null)
            {
                return "RectTransform";
            }
            return "GameObject";
        }
        private static string ParentPath(GameObject go)
        {
            try
            {
                Transform parent = go.transform.parent;
                if (parent == null)
                {
                    return string.Empty;
                }
                List<string> names = new List<string>();
                Transform cursor = parent;
                while (cursor != null)
                {
                    names.Add(cursor.name);
                    cursor = cursor.parent;
                }
                names.Reverse();
                return string.Join("/", names.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }
        private static void FillMaskCandidates(bool[,] mask, StillWrongIdMap map, Dictionary<int, string> idToPath, HashSet<string> analytic, List<C2GateCandidate> target)
        {
            List<SamCoverageResult> rows = new List<SamCoverageResult>();
            try
            {
                rows = SamCoverage.FromMask(mask, map, idToPath);
            }
            catch
            {
                rows = new List<SamCoverageResult>();
            }
            Dictionary<int, SamCoverageResult> byId = new Dictionary<int, SamCoverageResult>();
            for (int i = 0; i < rows.Count; i++)
            {
                SamCoverageResult entry = rows[i];
                if (entry == null || entry.ElementId == 0)
                {
                    continue;
                }
                if (!byId.ContainsKey(entry.ElementId))
                {
                    byId.Add(entry.ElementId, entry);
                }
            }
            foreach (KeyValuePair<int, string> kv in idToPath)
            {
                SamCoverageResult hit = null;
                byId.TryGetValue(kv.Key, out hit);
                C2GateCandidate dto = BuildCandidate(kv.Value, kv.Key, hit, analytic);
                target.Add(dto);
            }
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
        private sealed class C2GateCaptureHost : MonoBehaviour
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
                    C2GateDriver.FinishCapture(null);
                    yield break;
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                }
                catch
                {
                    UnityEngine.Object.Destroy(target);
                    C2GateDriver.FinishCapture(null);
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
                C2GateDriver.FinishCapture(tex);
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
        private static void WriteResult(C2GateState state, C2GateResult result, string failure)
        {
            try
            {
                C2GateResult dto = result != null ? result : new C2GateResult();
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
