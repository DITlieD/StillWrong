using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace StillWrong.Editor.Reduction
{
    public sealed class UnitySceneTrialExecutor : ITrialExecutor
    {
        private readonly string _baselineScenePath;
        private readonly OracleSpec _spec;
        private readonly Action _replayCallback;
        private readonly Func<OracleSpec, OracleVerdict> _oracleEvaluator;
        private readonly IMeasurementSource _measurementSource;

        public UnitySceneTrialExecutor(
            string baselineScenePath,
            OracleSpec spec,
            Action replayCallback = null,
            Func<OracleSpec, OracleVerdict> oracleEvaluator = null,
            IMeasurementSource measurementSource = null)
        {
            _baselineScenePath = baselineScenePath;
            _spec = spec;
            _replayCallback = replayCallback;
            _oracleEvaluator = oracleEvaluator;
            _measurementSource = measurementSource;
        }

        public OracleVerdict ExecuteTrial(ISet<string> candidateRemovals)
        {
            if (string.IsNullOrEmpty(_baselineScenePath) || !File.Exists(_baselineScenePath))
            {
                throw new FileNotFoundException("Baseline scene not found at: " + _baselineScenePath);
            }

            EditorSceneManager.OpenScene(_baselineScenePath, OpenSceneMode.Single);
            UnitySceneCopyReducer.ApplyRemovals(candidateRemovals);

            try
            {
                if (_replayCallback != null)
                {
                    _replayCallback.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Replay error during reduction trial: " + ex.Message);
                return OracleVerdict.Inconclusive;
            }

            IMeasurementSource source = _measurementSource;
            if (source == null)
            {
                source = new UnityProbeSource(_spec);
            }

            if (_oracleEvaluator != null)
            {
                return _oracleEvaluator.Invoke(_spec);
            }

            return UnitySymptomOracleRunner.Evaluate(_spec, source, new EvaluationContext()).Verdict;
        }
    }

    public static class UnitySceneCopyReducer
    {
        public static (ReductionResult Result, string ReducedScenePath) ReduceScene(
            string sourceScenePath,
            OracleSpec spec,
            string outputDirectory = null,
            Action replayCallback = null,
            Func<OracleSpec, OracleVerdict> customOracle = null,
            IEnumerable<string> additionalProtectedIds = null)
        {
            if (string.IsNullOrEmpty(sourceScenePath) || !File.Exists(sourceScenePath))
            {
                throw new FileNotFoundException("Source scene not found at: " + sourceScenePath);
            }

            string sourceDir = Path.GetDirectoryName(sourceScenePath);
            string sceneName = Path.GetFileNameWithoutExtension(sourceScenePath);
            string targetDir = !string.IsNullOrEmpty(outputDirectory) ? outputDirectory : sourceDir;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string reducedScenePath = Path.Combine(targetDir, sceneName + "_Reduced.unity").Replace("\\", "/");
            string baselineScenePath = Path.Combine(targetDir, sceneName + "_Baseline.unity").Replace("\\", "/");

            AssetDatabase.CopyAsset(sourceScenePath, baselineScenePath);
            AssetDatabase.Refresh();

            Scene workingScene = EditorSceneManager.OpenScene(baselineScenePath, OpenSceneMode.Single);

            ReductionContext context = BuildContext(workingScene, spec, additionalProtectedIds);

            var executor = new UnitySceneTrialExecutor(
                baselineScenePath,
                spec,
                replayCallback,
                customOracle);

            AssetDatabase.DisallowAutoRefresh();
            ReductionResult reductionResult;
            try
            {
                reductionResult = DeltaDebuggingReducer.Reduce(context, executor);
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            Scene reducedScene = EditorSceneManager.OpenScene(baselineScenePath, OpenSceneMode.Single);
            ApplyRemovals(reductionResult.RemovedNodeIds);
            EditorSceneManager.SaveScene(reducedScene, reducedScenePath, true);
            EditorSceneManager.OpenScene(reducedScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveOpenScenes();

            if (File.Exists(baselineScenePath))
            {
                AssetDatabase.DeleteAsset(baselineScenePath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return (reductionResult, reducedScenePath);
        }


        public static void ApplyRemovals(IEnumerable<string> candidateRemovals)
        {
            if (candidateRemovals == null)
            {
                return;
            }

            ISet<string> removalSet = candidateRemovals as ISet<string>;
            if (removalSet == null)
            {
                removalSet = new HashSet<string>(candidateRemovals, StringComparer.Ordinal);
            }

            if (removalSet.Count == 0)
            {
                return;
            }

            GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
            var toDestroy = new List<GameObject>();
            for (int i = 0; i < allObjects.Length; i++)
            {
                GameObject go = allObjects[i];
                if (go == null)
                {
                    continue;
                }

                if (removalSet.Contains(GetHierarchyPath(go)))
                {
                    toDestroy.Add(go);
                }
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (toDestroy[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(toDestroy[i]);
                }
            }
        }

        public static ReductionContext BuildContext(
            Scene scene,
            OracleSpec spec,
            IEnumerable<string> additionalProtectedIds = null)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            var allNodes = new List<ReductionNode>();
            var nodeMap = new Dictionary<string, ReductionNode>(StringComparer.Ordinal);
            var priorityScores = new Dictionary<string, double>(StringComparer.Ordinal);

            for (int r = 0; r < rootObjects.Length; r++)
            {
                TraverseHierarchy(rootObjects[r], null, allNodes, nodeMap, priorityScores);
            }

            var specTargets = new List<string>();
            if (spec != null && spec.Targets != null)
            {
                for (int i = 0; i < spec.Targets.Count; i++)
                {
                    if (!string.IsNullOrEmpty(spec.Targets[i].ElementId))
                    {
                        specTargets.Add(spec.Targets[i].ElementId);
                    }
                }
            }

            string targetId = specTargets.Count > 0 ? specTargets[0] : string.Empty;

            string cameraNodeId = string.Empty;
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                cameraNodeId = GetHierarchyPath(mainCam.gameObject);
            }

            string eventSystemNodeId = string.Empty;
            EventSystem es = EventSystem.current;
            if (es != null)
            {
                eventSystemNodeId = GetHierarchyPath(es.gameObject);
            }

            var protectedList = new List<string>(specTargets);

            if (additionalProtectedIds != null)
            {
                protectedList.AddRange(additionalProtectedIds);
            }

            return new ReductionContext(
                allNodes,
                targetId,
                cameraNodeId,
                eventSystemNodeId,
                protectedList,
                priorityScores);
        }
        private static void TraverseHierarchy(
            GameObject go,
            string parentPath,
            List<ReductionNode> allNodes,
            Dictionary<string, ReductionNode> nodeMap,
            Dictionary<string, double> priorityScores)
        {
            if (go == null) return;

            string path = GetHierarchyPath(go);
            var childrenPaths = new List<string>();
            for (int c = 0; c < go.transform.childCount; c++)
            {
                Transform child = go.transform.GetChild(c);
                childrenPaths.Add(GetHierarchyPath(child.gameObject));
            }

            var dependencies = new List<string>();

            double priority = CalculateRemovalPriority(go);
            priorityScores[path] = priority;

            var node = new ReductionNode(path, parentPath, childrenPaths, dependencies, priority);
            allNodes.Add(node);
            nodeMap[path] = node;

            for (int c = 0; c < go.transform.childCount; c++)
            {
                TraverseHierarchy(go.transform.GetChild(c).gameObject, path, allNodes, nodeMap, priorityScores);
            }
        }

        private static double CalculateRemovalPriority(GameObject go)
        {
            string name = go.name.ToLowerInvariant();
            if (name.Contains("audio") || name.Contains("music") || name.Contains("sfx")) return 90.0;
            if (name.Contains("particle") || name.Contains("vfx") || name.Contains("effect")) return 80.0;
            if (name.Contains("terrain") || name.Contains("prop") || name.Contains("decor")) return 70.0;
            if (name.Contains("light") || name.Contains("shadow")) return 60.0;
            if (name.Contains("background") || name.Contains("backdrop")) return 50.0;
            if (name.Contains("enemy") || name.Contains("npc")) return 40.0;
            return 10.0;
        }

        public static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;
            string path = go.name;
            Transform p = go.transform.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }
    }
}
