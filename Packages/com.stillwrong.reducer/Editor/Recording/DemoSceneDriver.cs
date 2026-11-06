using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class DemoSceneState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public int BuilderObjectCount;
        public int BuilderCandidateCount;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class DemoSceneProbes
    {
        public string OverflowPx = string.Empty;
        public string Inside = string.Empty;
        public string ActiveBody = string.Empty;
        public string ActiveMask = string.Empty;
    }
    [Serializable]
    public sealed class DemoSceneResult
    {
        public string RunId = string.Empty;
        public string CaseId = "demo-scene";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public int MeasureFrame = 6;
        public int ObjectCount;
        public int BuilderObjectCount;
        public int BuilderCandidateCount;
        public int CandidateCount;
        public List<string> CandidateIds = new List<string>();
        public DemoSceneProbes Probes = new DemoSceneProbes();
        public string ShotFile = string.Empty;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class DemoSceneMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class DemoSceneDriver
    {
        private const string DemoFolder = "Library/StillWrong/demoscene";
        private const int MeasureFrame = 6;
        private static bool _ticked;
        private static Texture2D _captureTex;
        private static bool _captureDone;
        private static GameObject _captureHost;
        private static int _captureW;
        private static int _captureH;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string DemoDir()
        {
            return Path.Combine(ProjectRoot(), DemoFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(DemoDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(DemoDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(DemoDir());
            _captureTex = null;
            _captureDone = false;
            _captureHost = null;
            _captureW = 0;
            _captureH = 0;
            DemoSceneBuildResult built = DemoSceneFixtureBuilder.BuildScene();
            string runId = "demoscene-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            DemoSceneState state = new DemoSceneState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.BuilderObjectCount = built.ObjectCount;
            state.BuilderCandidateCount = built.CandidateCount;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            DemoSceneMarker marker = new DemoSceneMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(DemoDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static DemoSceneState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<DemoSceneState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(DemoSceneState state)
        {
            try
            {
                Directory.CreateDirectory(DemoDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(DemoSceneState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                DemoSceneMarker marker = new DemoSceneMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(DemoDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
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
            DemoSceneState state = LoadState();
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
                else if (state.Phase == "capture")
                {
                    TickCapture(state);
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
        private static void TickEnterPlay(DemoSceneState state)
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
        private static void TickMeasure(DemoSceneState state)
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
            int markW = Screen.width;
            int markH = Screen.height;
            if (markW <= 0 || markH <= 0)
            {
                Fail(state, "measure-bad-screen-size");
                return;
            }
            _captureDone = false;
            _captureTex = null;
            _captureW = markW;
            _captureH = markH;
            GameObject host = new GameObject("StillWrong_DemoSceneCapture");
            DemoSceneCaptureHost runner = host.AddComponent<DemoSceneCaptureHost>();
            runner.Width = markW;
            runner.Height = markH;
            runner.StartRun();
            _captureHost = host;
            state.Phase = "capture";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickCapture(DemoSceneState state)
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
            DemoSceneResult result = BuildResult(state);
            WriteResult(state, result, string.Empty);
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
        private static void TickExitPlay(DemoSceneState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(DemoSceneFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            try
            {
                DemoSceneMarker marker = new DemoSceneMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(DemoDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static string DescribeValue(MeasuredValue value)
        {
            if (value == null)
            {
                return "missing (no-source)";
            }
            return value.Describe();
        }
        private static DemoSceneResult BuildResult(DemoSceneState state)
        {
            DemoSceneResult result = new DemoSceneResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.MeasureFrame = MeasureFrame;
            result.BuilderObjectCount = state.BuilderObjectCount;
            result.BuilderCandidateCount = state.BuilderCandidateCount;
            try
            {
                GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>();
                result.ObjectCount = all != null ? all.Length : 0;
            }
            catch
            {
            }
            List<string> ids = DemoSceneFixtureBuilder.CreateCandidateIds();
            result.CandidateIds = ids;
            result.CandidateCount = ids.Count;
            OracleSpec spec = FixtureSpecs.C1();
            UnityProbeSource source = new UnityProbeSource(spec);
            MeasuredValue value;
            if (source.TryGetValue(Probes.OverflowPx("body", "mask"), 0, out value))
            {
                result.Probes.OverflowPx = DescribeValue(value);
            }
            if (source.TryGetValue(Probes.Inside("body", "mask"), 0, out value))
            {
                result.Probes.Inside = DescribeValue(value);
            }
            if (source.TryGetValue(Probes.Active("body"), 0, out value))
            {
                result.Probes.ActiveBody = DescribeValue(value);
            }
            if (source.TryGetValue(Probes.Active("mask"), 0, out value))
            {
                result.Probes.ActiveMask = DescribeValue(value);
            }
            try
            {
                byte[] png = _captureTex.EncodeToPNG();
                if (png != null && png.Length > 0)
                {
                    File.WriteAllBytes(Path.Combine(DemoDir(), "shot.png"), png);
                    result.ShotFile = "shot.png";
                }
            }
            catch
            {
            }
            return result;
        }
        private sealed class DemoSceneCaptureHost : MonoBehaviour
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
                    DemoSceneDriver.FinishCapture(null);
                    yield break;
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                }
                catch
                {
                    UnityEngine.Object.Destroy(target);
                    DemoSceneDriver.FinishCapture(null);
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
                DemoSceneDriver.FinishCapture(tex);
            }
        }
        private static void FinishCapture(Texture2D tex)
        {
            _captureTex = tex;
            _captureDone = true;
        }
        private static void WriteResult(DemoSceneState state, DemoSceneResult result, string failure)
        {
            try
            {
                DemoSceneResult dto = result != null ? result : new DemoSceneResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(DemoDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
