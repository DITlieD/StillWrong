using System;
using System.IO;
using StillWrong.Editor.Fixtures;
using StillWrong.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class D8ShellState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public int StartAbsFrame;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8ShellResult
    {
        public string RunId = string.Empty;
        public string CaseId = "d8-shell";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public int MeasureFrame = 6;
        public string ShotFile = string.Empty;
        public bool SheetOpened;
        public string TextureSource = string.Empty;
        public string Note = string.Empty;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class D8ShellMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class D8ShellDriver
    {
        private const string ShellFolder = "Library/StillWrong/d8shell";
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
        public static string ShellDir()
        {
            return Path.Combine(ProjectRoot(), ShellFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(ShellDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(ShellDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(ShellDir());
            _captureTex = null;
            _captureDone = false;
            _captureHost = null;
            _captureW = 0;
            _captureH = 0;
            DemoSceneFixtureBuilder.BuildScene();
            string runId = "d8shell-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            D8ShellState state = new D8ShellState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            D8ShellMarker marker = new D8ShellMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(ShellDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static D8ShellState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<D8ShellState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(D8ShellState state)
        {
            try
            {
                Directory.CreateDirectory(ShellDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(D8ShellState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                D8ShellMarker marker = new D8ShellMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(ShellDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            D8ShellState state = LoadState();
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
        private static void TickEnterPlay(D8ShellState state)
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
        private static void TickMeasure(D8ShellState state)
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
            GameObject host = new GameObject("StillWrong_D8ShellCapture");
            D8ShellCaptureHost runner = host.AddComponent<D8ShellCaptureHost>();
            runner.Width = markW;
            runner.Height = markH;
            runner.StartRun();
            _captureHost = host;
            state.Phase = "capture";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickCapture(D8ShellState state)
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
            try
            {
                byte[] png = _captureTex.EncodeToPNG();
                if (png != null && png.Length > 0)
                {
                    File.WriteAllBytes(Path.Combine(ShellDir(), "shot.png"), png);
                }
            }
            catch
            {
            }
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
        private static void TickExitPlay(D8ShellState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            D8ShellResult result = new D8ShellResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            result.MeasureFrame = MeasureFrame;
            result.ShotFile = "shot.png";
            result.TextureSource = "sheet-owned-file-load";
            result.Note = "sheet loads shot.png itself; no live reference crosses window instances";
            StillWrongBugSheet.OpenWithFrameFile(Path.Combine(ShellDir(), "shot.png"));
            StillWrongBugSheet sheet = EditorWindow.GetWindow<StillWrongBugSheet>();
            result.SheetOpened = sheet != null && sheet.FrameTexture != null;
            WriteResult(state, result, string.Empty);
            try
            {
                EditorSceneManager.OpenScene(DemoSceneFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            try
            {
                D8ShellMarker marker = new D8ShellMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(ShellDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static Texture2D LoadShotBack()
        {
            try
            {
                string path = Path.Combine(ShellDir(), "shot.png");
                if (!File.Exists(path))
                {
                    return null;
                }
                byte[] png = File.ReadAllBytes(path);
                if (png == null || png.Length == 0)
                {
                    return null;
                }
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(png))
                {
                    return null;
                }
                return tex;
            }
            catch
            {
                return null;
            }
        }
        private sealed class D8ShellCaptureHost : MonoBehaviour
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
                    D8ShellDriver.FinishCapture(null);
                    yield break;
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                }
                catch
                {
                    UnityEngine.Object.Destroy(target);
                    D8ShellDriver.FinishCapture(null);
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
                D8ShellDriver.FinishCapture(tex);
            }
        }
        private static void FinishCapture(Texture2D tex)
        {
            _captureTex = tex;
            _captureDone = true;
        }
        private static void WriteResult(D8ShellState state, D8ShellResult result, string failure)
        {
            try
            {
                D8ShellResult dto = result != null ? result : new D8ShellResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(ShellDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
