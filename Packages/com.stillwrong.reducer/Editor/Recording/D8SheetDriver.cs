using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Inspection;
using StillWrong.Oracle;
using StillWrong.Recording;
using StillWrong.Editor.Fixtures;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8SheetState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public string FrameChunk = string.Empty;
        public string FrameFile = string.Empty;
        public string CandidateName = string.Empty;
        public string CandidatePath = string.Empty;
        public float ClickX;
        public float ClickY;
        public bool UsedSam;
        public bool UsedId;
        public string DegradedReason = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8SheetResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-sheet";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public string SessionDir = string.Empty;
        public string FrameChunk = string.Empty;
        public string FrameFile = string.Empty;
        public string FrameIndexShown = string.Empty;
        public int ThumbCount;
        public string CandidateName = string.Empty;
        public string CandidatePath = string.Empty;
        public string Mode = "Bug";
        public string Sentence = string.Empty;
        public string ClickKind = "scripted-click";
        public float ClickX;
        public float ClickY;
        public bool GroundingUsedSam;
        public bool GroundingUsedId;
        public string GroundingReason = string.Empty;
        public bool SheetOpened;
        public string ShotFile = "sheet-frame.png";
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8SheetMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D8SheetDriver
    {
        private const string SheetFolder = "Library/StillWrong/d8sheet";
        private const int MeasureFrame = 6;
        private const double RecordSeconds = 12.0;
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
        public static string SheetDir()
        {
            return Path.Combine(ProjectRoot(), SheetFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(SheetDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(SheetDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(SheetDir());
            _captureTex = null;
            _captureDone = false;
            _captureHost = null;
            _graphicsSaved = null;
            _renderersSaved = null;
            _pathToId = null;
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8sheet-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8SheetState state = new D8SheetState();
            state.Phase = "enterplay-record";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8SheetMarker marker = new D8SheetMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(SheetDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static D8SheetState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8SheetState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8SheetState state)
        {
            try
            {
                Directory.CreateDirectory(SheetDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8SheetState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D8SheetMarker marker = new D8SheetMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(SheetDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            D8SheetState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay-record")
                {
                    TickEnterPlayRecord(state);
                }
                else if (state.Phase == "window-record")
                {
                    TickWindowRecord(state);
                }
                else if (state.Phase == "exitplay-record")
                {
                    TickExitPlay(state, "readsession");
                }
                else if (state.Phase == "readsession")
                {
                    TickReadSession(state);
                }
                else if (state.Phase == "enterplay-ground")
                {
                    TickEnterPlayGround(state);
                }
                else if (state.Phase == "measure-ground")
                {
                    TickMeasureGround(state);
                }
                else if (state.Phase == "capture-ground")
                {
                    TickCaptureGround(state);
                }
                else if (state.Phase == "exitplay-ground")
                {
                    TickExitPlay(state, "opensheet");
                }
                else if (state.Phase == "opensheet")
                {
                    TickOpenSheet(state);
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlayRecord(D8SheetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            GameObject host = new GameObject("StillWrong_D8SheetRecorder");
            RollingCaptureRecorder recorder = host.AddComponent<RollingCaptureRecorder>();
            recorder.Begin("d8sheet");
            if (!recorder.IsCapturing())
            {
                Fail(state, "recorder-would-not-capture");
                return;
            }
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.Phase = "window-record";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickWindowRecord(D8SheetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "window-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "window-timeout");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime < RecordSeconds)
            {
                return;
            }
            RollingCaptureRecorder recorder = UnityEngine.Object.FindAnyObjectByType<RollingCaptureRecorder>();
            if (recorder == null)
            {
                Fail(state, "window-recorder-missing");
                return;
            }
            recorder.FreezeCapture();
            state.Phase = "exitplay-record";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static void TickExitPlay(D8SheetState state, string next)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            state.Phase = next;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickReadSession(D8SheetState state)
        {
            if (EditorApplication.isPlaying)
            {
                Fail(state, "readsession-entered-while-playing");
                return;
            }
            string sessionDir = ReadSessionDir();
            if (string.IsNullOrEmpty(sessionDir))
            {
                Fail(state, "readsession-no-session");
                return;
            }
            string chunkName = string.Empty;
            string fileName = string.Empty;
            try
            {
                string[] dirs = Directory.GetDirectories(sessionDir, "chunk-*");
                System.Array.Sort(dirs, System.StringComparer.Ordinal);
                for (int d = dirs.Length - 1; d >= 0; d--)
                {
                    if (!File.Exists(Path.Combine(dirs[d], "manifest.json")))
                    {
                        continue;
                    }
                    string framesDir = Path.Combine(dirs[d], "frames");
                    if (!Directory.Exists(framesDir))
                    {
                        continue;
                    }
                    string[] jpgs = Directory.GetFiles(framesDir, "f-*.jpg");
                    System.Array.Sort(jpgs, System.StringComparer.Ordinal);
                    if (jpgs.Length == 0)
                    {
                        continue;
                    }
                    chunkName = Path.GetFileName(dirs[d]);
                    fileName = Path.GetFileName(jpgs[jpgs.Length - 1]);
                    break;
                }
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(chunkName) || string.IsNullOrEmpty(fileName))
            {
                Fail(state, "readsession-no-closed-frame");
                return;
            }
            state.SessionDir = sessionDir;
            state.FrameChunk = chunkName;
            state.FrameFile = fileName;
            state.Phase = "enterplay-ground";
            SaveState(state);
        }
        private static string ReadSessionDir()
        {
            try
            {
                string path = Path.Combine(RollingCaptureRecorder.RecorderDir(), "status.json");
                if (!File.Exists(path))
                {
                    return string.Empty;
                }
                RollingStatusFile status = JsonUtility.FromJson<RollingStatusFile>(File.ReadAllText(path));
                if (status == null || string.IsNullOrEmpty(status.SessionId))
                {
                    return string.Empty;
                }
                string dir = Path.Combine(RollingCaptureRecorder.SessionsRoot(), status.SessionId);
                if (!Directory.Exists(dir))
                {
                    return string.Empty;
                }
                return dir;
            }
            catch
            {
                return string.Empty;
            }
        }
        private static void TickEnterPlayGround(D8SheetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                DemoSceneFixtureBuilder.BuildScene();
                state.Phase = "measure-ground";
                state.StartAbsFrame = Time.frameCount;
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
                EditorApplication.isPlaying = true;
                return;
            }
            state.Phase = "measure-ground";
            state.StartAbsFrame = Time.frameCount;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickMeasureGround(D8SheetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "ground-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "ground-timeout");
                return;
            }
            if (Time.frameCount - state.StartAbsFrame < MeasureFrame)
            {
                return;
            }
            List<string> paths = CollectLiveRegistryPaths();
            if (paths.Count == 0)
            {
                Fail(state, "ground-empty-registry");
                return;
            }
            int markW = Screen.width;
            int markH = Screen.height;
            if (markW <= 0 || markH <= 0)
            {
                Fail(state, "ground-bad-screen-size");
                return;
            }
            _pathToId = StillWrongIdBuffer.AssignIds(paths);
            StillWrongIdBuffer.SetRegistryMap(_pathToId);
            _graphicsSaved = StillWrongIdBuffer.BeginGraphicIds(StillWrongIdBuffer.BuildGraphicAssignments(_pathToId));
            _renderersSaved = StillWrongIdBuffer.BeginRendererIds(StillWrongIdBuffer.BuildRendererAssignments(_pathToId));
            _captureDone = false;
            _captureTex = null;
            _captureW = markW;
            _captureH = markH;
            GameObject host = new GameObject("StillWrong_D8SheetCapture");
            D8SheetCaptureHost runner = host.AddComponent<D8SheetCaptureHost>();
            runner.Width = markW;
            runner.Height = markH;
            runner.StartRun();
            _captureHost = host;
            state.Phase = "capture-ground";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickCaptureGround(D8SheetState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "capture-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "capture-timeout");
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
                Fail(state, "capture-failed");
                return;
            }
            string failure = GroundAtBody(state);
            if (!string.IsNullOrEmpty(failure))
            {
                Fail(state, failure);
                return;
            }
            state.Phase = "exitplay-ground";
            SaveState(state);
            try
            {
                EditorApplication.isPlaying = false;
            }
            catch
            {
            }
        }
        private static string GroundAtBody(D8SheetState state)
        {
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
                RestoreOverrides();
                return "ground-decode-failed";
            }
            StillWrongIdBuffer.SetIdMap(map);
            Rect2D bodyRect = LookupRect(DemoSceneFixtureBuilder.TargetHierarchyPath);
            if (bodyRect.Width <= 0f || bodyRect.Height <= 0f)
            {
                RestoreOverrides();
                return "ground-body-rect-empty";
            }
            state.ClickX = bodyRect.X + bodyRect.Width * 0.5f;
            state.ClickY = bodyRect.Y + bodyRect.Height * 0.5f;
            int liveW = Screen.width > 0 ? Screen.width : state.ScreenW;
            int liveH = Screen.height > 0 ? Screen.height : state.ScreenH;
            float texSx = liveW > 0 ? (float)map.Width / liveW : 1f;
            float texSy = liveH > 0 ? (float)map.Height / liveH : 1f;
            float yTop = liveH - (bodyRect.Y + bodyRect.Height);
            Rect scope = Rect.MinMaxRect(bodyRect.X * texSx, ((float)map.Height - yTop * texSy) - bodyRect.Height * texSy, (bodyRect.X + bodyRect.Width) * texSx, (float)map.Height - yTop * texSy);
            StillWrongGroundingRequest request = new StillWrongGroundingRequest();
            request.HasBox = true;
            request.Box = scope;
            request.Sentence = string.Empty;
            StillWrongGroundingResult result = StillWrongGrounding.ActiveService.Ground(request);
            RestoreOverrides();
            if (result == null || result.Candidates == null || result.Candidates.Count == 0)
            {
                return "ground-no-candidates";
            }
            StillWrongGroundingCandidate winner = null;
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                StillWrongGroundingCandidate candidate = result.Candidates[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.ElementPath))
                {
                    continue;
                }
                if (winner == null || candidate.MaskCoverage > winner.MaskCoverage)
                {
                    winner = candidate;
                }
            }
            if (winner == null)
            {
                return "ground-no-named-candidate";
            }
            state.CandidatePath = winner.ElementPath;
            state.CandidateName = LeafName(winner.ElementPath);
            state.UsedSam = result.UsedSamMask;
            state.UsedId = result.UsedIdBuffer;
            state.DegradedReason = result.DegradedReason;
            return string.Empty;
        }
        private static void TickOpenSheet(D8SheetState state)
        {
            if (EditorApplication.isPlaying)
            {
                Fail(state, "opensheet-entered-while-playing");
                return;
            }
            if (string.IsNullOrEmpty(state.SessionDir) || string.IsNullOrEmpty(state.FrameChunk) || string.IsNullOrEmpty(state.FrameFile))
            {
                Fail(state, "opensheet-no-frame");
                return;
            }
            StillWrongBugSheet.OpenWithSession(state.SessionDir, state.FrameChunk, state.FrameFile, state.CandidateName, state.CandidatePath, "scripted-click", state.ScreenW, state.ScreenH);
            D8SheetResult result = new D8SheetResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.SessionDir = state.SessionDir;
            result.FrameChunk = state.FrameChunk;
            result.FrameFile = state.FrameFile;
            result.CandidateName = state.CandidateName;
            result.CandidatePath = state.CandidatePath;
            result.ClickX = state.ClickX;
            result.ClickY = state.ClickY;
            result.GroundingUsedSam = state.UsedSam;
            result.GroundingUsedId = state.UsedId;
            result.GroundingReason = state.DegradedReason;
            StillWrongBugSheet sheet = EditorWindow.GetWindow<StillWrongBugSheet>();
            if (sheet != null)
            {
                result.ThumbCount = sheet.SheetThumbCount();
                result.FrameIndexShown = sheet.SheetSelectedLabel();
                result.SheetOpened = sheet.SheetThumbCount() > 0;
            }
            string framePath = Path.Combine(state.SessionDir, state.FrameChunk, "frames", state.FrameFile);
            if (File.Exists(framePath))
            {
                try
                {
                    byte[] jpg = File.ReadAllBytes(framePath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(jpg))
                    {
                        byte[] png = tex.EncodeToPNG();
                        if (png != null && png.Length > 0)
                        {
                            File.WriteAllBytes(Path.Combine(SheetDir(), "sheet-frame.png"), png);
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                catch
                {
                }
            }
            WriteResult(state, result, string.Empty);
            try
            {
                DemoSceneFixtureBuilder.BuildScene();
            }
            catch
            {
            }
            try
            {
                D8SheetMarker marker = new D8SheetMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(SheetDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
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
        private static Rect2D LookupRect(string path)
        {
            GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(path);
            if (go == null)
            {
                return Rect2D.Zero;
            }
            return UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
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
        private sealed class D8SheetCaptureHost : MonoBehaviour
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
                    D8SheetDriver.FinishCapture(null);
                    yield break;
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                }
                catch
                {
                    UnityEngine.Object.Destroy(target);
                    D8SheetDriver.FinishCapture(null);
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
                D8SheetDriver.FinishCapture(tex);
            }
        }
        private static void FinishCapture(Texture2D tex)
        {
            _captureTex = tex;
            _captureDone = true;
        }
        private static void WriteResult(D8SheetState state, D8SheetResult result, string failure)
        {
            try
            {
                D8SheetResult dto = result != null ? result : new D8SheetResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(SheetDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
