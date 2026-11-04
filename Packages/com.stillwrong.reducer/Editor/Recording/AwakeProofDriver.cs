using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Fixtures;
using StillWrong.Editor.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StillWrong.Editor.Recording
{
    [Serializable]
    public sealed class AwakeProofState
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
    public sealed class AwakeProofTrial
    {
        public string Name = string.Empty;
        public List<string> Removals = new List<string>();
        public int AwakeCount;
        public int SpawnedChildren;
        public bool ChildFound;
        public int MeasureFrame;
        public string Missing = string.Empty;
    }
    [Serializable]
    public sealed class AwakeProofResult
    {
        public string RunId = string.Empty;
        public string CaseId = "awake-spawn-proof";
        public string UnityVersion = string.Empty;
        public int ScreenW;
        public int ScreenH;
        public AwakeProofTrial Baseline = new AwakeProofTrial();
        public AwakeProofTrial Removal = new AwakeProofTrial();
        public string Failure = string.Empty;
    }
    [Serializable]
    public sealed class AwakeProofMarker
    {
        public string Kind = string.Empty;
        public string At = string.Empty;
        public string Detail = string.Empty;
    }
    public static class AwakeProofDriver
    {
        private const string ProofFolder = "Library/StillWrong/awakeproof";
        private const int MeasureFrame = 5;
        private static bool _ticked;
        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string ProofDir()
        {
            return Path.Combine(ProjectRoot(), ProofFolder);
        }
        private static string StatePath()
        {
            return Path.Combine(ProofDir(), "driver-state.json");
        }
        public static string ResultPath()
        {
            return Path.Combine(ProofDir(), "result.json");
        }
        public static string StartRun()
        {
            Directory.CreateDirectory(ProofDir());
            AwakeSpawnProbe.ResetCounts();
            AwakeSpawnFixtureBuilder.BuildScene();
            string runId = "awakeproof-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            AwakeProofState state = new AwakeProofState();
            state.Phase = "enterplay-baseline";
            state.RunId = runId;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            ArmTick();
            AwakeProofMarker marker = new AwakeProofMarker();
            marker.Kind = "start";
            marker.At = DateTime.UtcNow.ToString("o");
            marker.Detail = runId;
            File.WriteAllText(Path.Combine(ProofDir(), "marker-start.json"), JsonUtility.ToJson(marker, true));
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
        private static AwakeProofState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<AwakeProofState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void SaveState(AwakeProofState state)
        {
            try
            {
                Directory.CreateDirectory(ProofDir());
                File.WriteAllText(StatePath(), JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }
        private static void Fail(AwakeProofState state, string reason)
        {
            state.Failure = reason;
            state.Phase = "done";
            SaveState(state);
            WriteResult(state, null, "failed: " + reason);
            try
            {
                AwakeProofMarker marker = new AwakeProofMarker();
                marker.Kind = "failed";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = reason;
                File.WriteAllText(Path.Combine(ProofDir(), "marker-failed.json"), JsonUtility.ToJson(marker, true));
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
            AwakeProofState state = LoadState();
            if (state == null || string.IsNullOrEmpty(state.Phase) || state.Phase == "done")
            {
                EditorApplication.update -= Tick;
                return;
            }
            try
            {
                if (state.Phase == "enterplay-baseline")
                {
                    TickEnterPlay(state, "record-baseline");
                }
                else if (state.Phase == "record-baseline")
                {
                    TickRecord(state, "baseline");
                }
                else if (state.Phase == "exitplay-baseline")
                {
                    TickExitPlay(state, "mutate-remove");
                }
                else if (state.Phase == "mutate-remove")
                {
                    TickMutate(state);
                }
                else if (state.Phase == "enterplay-trial")
                {
                    TickEnterPlay(state, "record-trial");
                }
                else if (state.Phase == "record-trial")
                {
                    TickRecord(state, "trial");
                }
                else if (state.Phase == "exitplay-trial")
                {
                    TickExitPlay(state, "finish");
                }
            }
            catch
            {
            }
        }
        private static void TickEnterPlay(AwakeProofState state, string next)
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            state.Phase = next;
            state.StartAbsFrame = Time.frameCount;
            state.ScreenW = Screen.width;
            state.ScreenH = Screen.height;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickRecord(AwakeProofState state, string kind)
        {
            if (!EditorApplication.isPlaying)
            {
                Fail(state, "record-play-exited-early");
                return;
            }
            if (EditorApplication.timeSinceStartup - state.PhaseStartTime > 120.0)
            {
                Fail(state, "record-timeout");
                return;
            }
            if (Time.frameCount - state.StartAbsFrame < MeasureFrame)
            {
                return;
            }
            AwakeProofTrial trial = Snapshot(kind);
            WriteTrial(state, trial);
            state.Phase = kind == "baseline" ? "exitplay-baseline" : "exitplay-trial";
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
        private static void TickExitPlay(AwakeProofState state, string next)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (next == "finish")
            {
                Finish(state);
                return;
            }
            state.Phase = next;
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
        }
        private static void TickMutate(AwakeProofState state)
        {
            if (EditorApplication.isPlaying)
            {
                Fail(state, "mutate-entered-while-playing");
                return;
            }
            try
            {
                EditorSceneManager.OpenScene(AwakeSpawnFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            GameObject spawner = GameObject.Find(AwakeSpawnFixtureBuilder.SpawnerPath);
            if (spawner == null)
            {
                Fail(state, "mutate-spawner-missing");
                return;
            }
            try
            {
                UnityEngine.Object.DestroyImmediate(spawner);
            }
            catch
            {
                Fail(state, "mutate-destroy-failed");
                return;
            }
            AwakeSpawnProbe.ResetCounts();
            state.Phase = "enterplay-trial";
            state.PhaseStartTime = EditorApplication.timeSinceStartup;
            SaveState(state);
            EditorApplication.isPlaying = true;
        }
        private static AwakeProofTrial Snapshot(string kind)
        {
            AwakeProofTrial trial = new AwakeProofTrial();
            trial.Name = kind;
            trial.MeasureFrame = MeasureFrame;
            if (kind == "trial")
            {
                trial.Removals.Add(AwakeSpawnFixtureBuilder.SpawnerPath);
            }
            trial.AwakeCount = AwakeSpawnProbe.AwakeCount;
            GameObject spawner = GameObject.Find(AwakeSpawnFixtureBuilder.SpawnerPath);
            if (spawner == null)
            {
                trial.Missing = kind == "trial" ? string.Empty : "spawner-not-found";
                return trial;
            }
            trial.SpawnedChildren = spawner.transform.childCount;
            Transform child = spawner.transform.Find(spawner.name + AwakeSpawnProbe.ChildSuffix);
            trial.ChildFound = child != null;
            return trial;
        }
        private static void WriteTrial(AwakeProofState state, AwakeProofTrial trial)
        {
            try
            {
                string name = trial.Name == "baseline" ? "trial-baseline.json" : "trial-removal.json";
                File.WriteAllText(Path.Combine(ProofDir(), name), JsonUtility.ToJson(trial, true));
            }
            catch
            {
            }
        }
        private static AwakeProofTrial ReadTrial(string kind)
        {
            try
            {
                string name = kind == "baseline" ? "trial-baseline.json" : "trial-removal.json";
                string path = Path.Combine(ProofDir(), name);
                if (!File.Exists(path))
                {
                    return null;
                }
                return JsonUtility.FromJson<AwakeProofTrial>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        private static void Finish(AwakeProofState state)
        {
            AwakeProofResult result = new AwakeProofResult();
            result.RunId = state.RunId;
            result.UnityVersion = Application.unityVersion;
            result.ScreenW = state.ScreenW;
            result.ScreenH = state.ScreenH;
            AwakeProofTrial baseline = ReadTrial("baseline");
            AwakeProofTrial removal = ReadTrial("trial");
            if (baseline == null || removal == null)
            {
                Fail(state, "finish-missing-trials");
                return;
            }
            result.Baseline = baseline;
            result.Removal = removal;
            WriteResult(state, result, string.Empty);
            try
            {
                EditorSceneManager.OpenScene(AwakeSpawnFixtureBuilder.ScenePath, OpenSceneMode.Single);
            }
            catch
            {
            }
            try
            {
                AwakeProofMarker marker = new AwakeProofMarker();
                marker.Kind = "done";
                marker.At = DateTime.UtcNow.ToString("o");
                marker.Detail = state.RunId;
                File.WriteAllText(Path.Combine(ProofDir(), "marker-done.json"), JsonUtility.ToJson(marker, true));
            }
            catch
            {
            }
            state.Phase = "done";
            SaveState(state);
        }
        private static void WriteResult(AwakeProofState state, AwakeProofResult result, string failure)
        {
            try
            {
                AwakeProofResult dto = result != null ? result : new AwakeProofResult();
                dto.RunId = state.RunId;
                dto.ScreenW = state.ScreenW;
                dto.ScreenH = state.ScreenH;
                if (!string.IsNullOrEmpty(failure) && string.IsNullOrEmpty(dto.Failure))
                {
                    dto.Failure = failure;
                }
                Directory.CreateDirectory(ProofDir());
                File.WriteAllText(ResultPath(), JsonUtility.ToJson(dto, true));
            }
            catch
            {
            }
        }
    }
}
