using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using StillWrong.Recording;
using StillWrong.Replay;
using StillWrong.Editor.Fixtures;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class CapCompareState
    {
        public string Phase = string.Empty;
        public string RunId = string.Empty;
        public string SessionDir = string.Empty;
        public int StartAbsFrame;
        public int RecordTarget = 30;
        public double PhaseStartTime;
        public int ScreenW;
        public int ScreenH;
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class CapCompareMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    [Serializable]
    public sealed class CapCompareMeta
    {
        public string RunId = string.Empty;
        public string CaseId = "C1-ui-clip";
        public string SessionDir = string.Empty;
        public string RingFrameFile = string.Empty;
        public string MarkedFile = string.Empty;
        public int Frame;
        public string BodyPath = "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody";
        public double RectX0;
        public double RectY0;
        public double RectX1;
        public double RectY1;
        public double RingBright;
        public double RingBrightUnflipped;
        public double MarkedBright;
        public double MarkedBrightUnflipped;
        public double Threshold = 0.05;
        public bool Pass;
        public double MeanCaptureMs;
        public double MeanReadbackMs;
        public long ReadbackDrops;
        public int RecordedFrames;
        public int ScreenW;
        public int ScreenH;
    }
    public static class CaptureCompareDriver
    {
        private const string RunFolder = "Library/StillWrong/capcompare";
        private const int RecordFrames = 30;
        private const double BrightCutoff = 0.6;
        private const double PassThreshold = 0.05;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string RunDir()
        {
            return Path.Combine(ProjectRoot(), RunFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(RunDir(), "driver-state.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(RunDir());
            C1UiClipFixtureBuilder.BuildFailingScene();
            string runId = "capcompare-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            CapCompareState state = new CapCompareState();
            state.Phase = "enterplay";
            state.RunId = runId;
            state.RecordTarget = RecordFrames;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            CapCompareMarker marker = new CapCompareMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(RunDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static CapCompareState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<CapCompareState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(CapCompareState state)
        {
            try
            {
                Directory.CreateDirectory(RunDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(CapCompareState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            try
            {
                CapCompareMarker marker = new CapCompareMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(RunDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            CapCompareState state = LoadState();
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
                else if (state.Phase == "record")
                {
                    TickRecord(state);
                }
                else if (state.Phase == "freeze")
                {
                    TickFreeze(state);
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
        private static void TickEnterPlay(CapCompareState state)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            UnityTemporalRingBuffer existing = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }
            GameObject go = new GameObject("StillWrong_CompareRecorder");
            UnityTemporalRingBuffer recorder = go.AddComponent<UnityTemporalRingBuffer>();
            recorder.Configure(300, null, null);
            recorder.StartRecording();
            state.Phase = "record";
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(CapCompareState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "record-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 300.0)
            {
                Fail(state, "record-timeout");
                return;
            }
            int idx = Time.frameCount - state.StartAbsFrame;
            if (idx >= state.RecordTarget)
            {
                UnityTemporalRingBuffer recorder = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
                if (recorder == null)
                {
                    Fail(state, "record-recorder-missing");
                    return;
                }
                recorder.Freeze();
                state.Phase = "freeze";
                state.PhaseStartTime = EditorApplication.timeSinceStartup;
                SaveState(state);
            }
        }
        private static void TickFreeze(CapCompareState state)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "freeze-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "freeze-timeout");
                return;
            }
            UnityTemporalRingBuffer recorder = UnityEngine.Object.FindFirstObjectByType<UnityTemporalRingBuffer>();
            if (recorder == null)
            {
                Fail(state, "freeze-recorder-missing");
                return;
            }
            if (string.IsNullOrEmpty(recorder.LastExportPath))
            {
                return;
            }
            state.SessionDir = recorder.LastExportPath;
            state.Phase = "exitplay";
            SaveState(state);
            EditorApplication.isPlaying = false;
        }
        private static void TickExitPlay(CapCompareState state)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (string.IsNullOrEmpty(state.SessionDir))
            {
                Fail(state, "exitplay-no-session");
                return;
            }
            EditorSceneManager.OpenScene(C1UiClipFixtureBuilder.ScenePath, OpenSceneMode.Single);
            CapCompareMeta meta = BuildCompareMeta(state);
            if (meta == null)
            {
                Fail(state, "exitplay-compare-failed");
                return;
            }
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "run-meta.json"), JsonUtility.ToJson(meta, true));
            }
            catch
            {
                Fail(state, "exitplay-meta-write-failed");
                return;
            }
            CapCompareMarker marker = new CapCompareMarker();
            marker.Kind = "done";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = state.RunId;
            try
            {
                File.WriteAllText(Path.Combine(RunDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static CapCompareMeta BuildCompareMeta(CapCompareState state)
        {
            SessionBundle bundle = null;
            try
            {
                bundle = SessionBundle.Load(state.SessionDir);
            }
            catch
            {
                return null;
            }
            if (bundle == null || bundle.Frames == null || bundle.Frames.Frames == null || bundle.Frames.Frames.Count == 0)
            {
                return null;
            }
            string markedPath = Path.Combine(state.SessionDir, "marked.png");
            FileInfo markedInfo = null;
            try
            {
                markedInfo = new FileInfo(markedPath);
            }
            catch
            {
                return null;
            }
            if (markedInfo == null || !markedInfo.Exists || markedInfo.Length == 0)
            {
                return null;
            }
            List<StillWrong.Recording.FrameIndexEntry> frames = bundle.Frames.Frames;
            int pick = frames.Count / 2;
            int frameNo = -1;
            string ringName = string.Empty;
            double rx0 = 0.0;
            double ry0 = 0.0;
            double rx1 = 0.0;
            double ry1 = 0.0;
            bool found = false;
            for (int k = 0; k < frames.Count && !found; k++)
            {
                int fi = (pick + k) % frames.Count;
                StillWrong.Recording.FrameIndexEntry entry = frames[fi];
                if (entry == null || string.IsNullOrEmpty(entry.JpegFile))
                {
                    continue;
                }
                string full = Path.Combine(state.SessionDir, entry.JpegFile);
                bool exists = false;
                try
                {
                    exists = File.Exists(full);
                }
                catch
                {
                    exists = false;
                }
                if (!exists)
                {
                    continue;
                }
                if (bundle.Registry == null || bundle.Registry.Frames == null)
                {
                    continue;
                }
                for (int r = 0; r < bundle.Registry.Frames.Count; r++)
                {
                    StillWrong.Recording.RegistryFrame rf = bundle.Registry.Frames[r];
                    if (rf == null || rf.Frame != entry.Frame || rf.Elements == null)
                    {
                        continue;
                    }
                    for (int e = 0; e < rf.Elements.Count; e++)
                    {
                        StillWrong.Recording.RegistryEntry element = rf.Elements[e];
                        if (element == null || string.IsNullOrEmpty(element.Path))
                        {
                            continue;
                        }
                        if (string.Equals(element.Path, "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody", StringComparison.Ordinal))
                        {
                            frameNo = entry.Frame;
                            ringName = entry.JpegFile;
                            rx0 = element.RectMinX;
                            ry0 = element.RectMinY;
                            rx1 = element.RectMaxX;
                            ry1 = element.RectMaxY;
                            found = true;
                            break;
                        }
                    }
                    if (found)
                    {
                        break;
                    }
                }
            }
            if (!found)
            {
                return null;
            }
            string ringPath = Path.Combine(state.SessionDir, ringName);
            Texture2D ringTex = LoadTexture(ringPath);
            Texture2D markedTex = LoadTexture(markedPath);
            if (ringTex == null || markedTex == null)
            {
                DestroyTexture(ringTex);
                DestroyTexture(markedTex);
                return null;
            }
            double ringBright = BrightFraction(ringTex, rx0, ry0, rx1, ry1, ringTex.width, ringTex.height);
            double ringBrightUnflipped = BrightFractionUnflipped(ringTex, rx0, ry0, rx1, ry1);
            double markedBright = BrightFraction(markedTex, rx0, ry0, rx1, ry1, markedTex.width, markedTex.height);
            double markedBrightUnflipped = BrightFractionUnflipped(markedTex, rx0, ry0, rx1, ry1);
            string ringCopy = Path.Combine(RunDir(), "ring-frame.jpg");
            string markedCopy = Path.Combine(RunDir(), "marked-frame.png");
            try
            {
                File.Copy(ringPath, ringCopy, true);
                File.Copy(markedPath, markedCopy, true);
            }
            catch
            {
                DestroyTexture(ringTex);
                DestroyTexture(markedTex);
                return null;
            }
            DestroyTexture(ringTex);
            DestroyTexture(markedTex);
            CapCompareMeta meta = new CapCompareMeta();
            meta.RunId = state.RunId;
            meta.SessionDir = state.SessionDir;
            meta.RingFrameFile = ringCopy;
            meta.MarkedFile = markedCopy;
            meta.Frame = frameNo;
            meta.RectX0 = rx0;
            meta.RectY0 = ry0;
            meta.RectX1 = rx1;
            meta.RectY1 = ry1;
            meta.RingBright = ringBright;
                meta.RingBrightUnflipped = ringBrightUnflipped;
                meta.MarkedBright = markedBright;
                meta.MarkedBrightUnflipped = markedBrightUnflipped;
            meta.Threshold = PassThreshold;
            meta.Pass = ringBright > PassThreshold && markedBright > PassThreshold;
            meta.MeanCaptureMs = bundle.Manifest != null ? bundle.Manifest.MeanCaptureMs : 0.0;
            meta.MeanReadbackMs = bundle.Manifest != null ? bundle.Manifest.MeanReadbackMs : 0.0;
            meta.ReadbackDrops = bundle.Manifest != null ? bundle.Manifest.ReadbackDrops : 0;
            meta.RecordedFrames = bundle.Frames.Frames.Count;
            meta.ScreenW = state.ScreenW;
            meta.ScreenH = state.ScreenH;
            return meta;
        }
        private static Texture2D LoadTexture(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }
                Texture2D tex = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
                return tex;
            }
            catch
            {
                return null;
            }
        }
        private static void DestroyTexture(Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            try
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            catch
            {
            }
        }
        private static double BrightFraction(Texture2D tex, double x0, double y0, double x1, double y1, int texW, int texH)
        {
            Color32[] pixels = null;
            try
            {
                pixels = tex.GetPixels32();
            }
            catch
            {
                return 0.0;
            }
            if (pixels == null || pixels.Length != texW * texH || texW <= 0 || texH <= 0)
            {
                return 0.0;
            }
            double sx = texW / 1920.0;
            double sy = texH / 1080.0;
            int ix0 = Math.Max(0, (int)Math.Floor(x0 * sx));
            int ix1 = Math.Min(texW - 1, (int)Math.Ceiling(x1 * sx) - 1);
            int iyTop = Math.Max(0, (int)Math.Floor(y0 * sy));
            int iyBottom = Math.Min(texH - 1, (int)Math.Ceiling(y1 * sy) - 1);
            return BrightFractionRows(pixels, texW, texH, ix0, ix1, texH - 1 - iyBottom, texH - 1 - iyTop);
        }
        private static double BrightFractionUnflipped(Texture2D tex, double x0, double y0, double x1, double y1)
        {
            Color32[] pixels = null;
            try
            {
                pixels = tex.GetPixels32();
            }
            catch
            {
                return 0.0;
            }
            if (pixels == null || tex.width <= 0 || tex.height <= 0)
            {
                return 0.0;
            }
            double sx = tex.width / 1920.0;
            double sy = tex.height / 1080.0;
            int ix0 = Math.Max(0, (int)Math.Floor(x0 * sx));
            int ix1 = Math.Min(tex.width - 1, (int)Math.Ceiling(x1 * sx) - 1);
            int iy0 = Math.Max(0, (int)Math.Floor(y0 * sy));
            int iy1 = Math.Min(tex.height - 1, (int)Math.Ceiling(y1 * sy) - 1);
            return BrightFractionRows(pixels, tex.width, tex.height, ix0, ix1, iy0, iy1);
        }
        private static double BrightFractionRows(Color32[] pixels, int texW, int texH, int x0, int x1, int row0, int row1)
        {
            if (x1 < x0 || row1 < row0)
            {
                return 0.0;
            }
            long bright = 0;
            long total = 0;
            for (int y = row0; y <= row1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Color32 px = pixels[y * texW + x];
                    total++;
                    if (px.r >= 153 && px.g >= 153 && px.b >= 153)
                    {
                        bright++;
                    }
                }
            }
            if (total == 0)
            {
                return 0.0;
            }
            return (double)bright / (double)total;
        }
    }
}

