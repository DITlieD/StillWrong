using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace StillWrong.Editor.Fixtures
{
    public static class C3TransientDisappearFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/C3_TransientDisappear_Fixture.unity";
        public const string TargetHierarchyPath = "GuidanceHud/QuestTracker/ObjectiveBadgeIcon";
        public const string ParentPanelHierarchyPath = "GuidanceHud/QuestTracker";
        public const int DistractorCount = 42;

        public static OracleSpec CreateFailureConditionSpec()
        {
            return FixtureSpecs.C3();
        }

        public static InMemoryMeasurementSource CreateMeasurementSource(string mode)
        {
            string norm = (mode ?? string.Empty).ToLowerInvariant().Trim();
            var source = new InMemoryMeasurementSource();
            bool bad = norm == "failing" || norm == "known_bad" || norm == "stillwrong";
            bool healthy = norm == "healthy" || norm == "healthy_pass" || norm == "healthypass";
            if (bad || healthy)
            {
                double[] alphas;
                if (bad)
                {
                    alphas = new double[] { 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0, 0.0, 0.85 };
                }
                else
                {
                    alphas = new double[] { 0.80, 0.78, 0.76, 0.74, 0.72, 0.70, 0.68, 0.66, 0.64 };
                }
                for (int f = 10; f <= 18; f++)
                {
                    source.AddExistingTargets(f, new[] { "badge", "panel" });
                    source.AddValue(f, Probes.Alpha("badge"), MeasuredValue.FromNumber(alphas[f - 10]));
                }
                source.AddValue(18, Probes.Active("badge"), MeasuredValue.FromBool(true));
                source.AddValue(18, Probes.Active("panel"), MeasuredValue.FromBool(true));
                return source;
            }
            for (int f = 10; f <= 18; f++)
            {
                source.AddExistingTargets(f, new[] { "panel" });
            }
            source.AddValue(18, Probes.Active("panel"), MeasuredValue.FromBool(true));
            return source;
        }


        public static void BuildFailingScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            var hudCanvasGo = new GameObject("GuidanceHud");
            var canvas = hudCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvasGo.AddComponent<GraphicRaycaster>();

            var toggleGo = new GameObject("CollapseToggle");
            toggleGo.transform.SetParent(hudCanvasGo.transform, false);
            toggleGo.AddComponent<Button>();
            toggleGo.AddComponent<Image>();

            var trackerGo = new GameObject("QuestTracker");
            trackerGo.transform.SetParent(hudCanvasGo.transform, false);
            trackerGo.AddComponent<CanvasGroup>();

            var iconGo = new GameObject("ObjectiveBadgeIcon");
            iconGo.transform.SetParent(trackerGo.transform, false);
            iconGo.AddComponent<CanvasGroup>();
            iconGo.AddComponent<Image>();
            iconGo.AddComponent<StillWrong.Fixtures.C3TransientDriver>();

            var titleGo = new GameObject("QuestTitleText");
            titleGo.transform.SetParent(trackerGo.transform, false);
            var titleText = titleGo.AddComponent<Text>();
            titleText.text = "Active Quest";

            var descGo = new GameObject("QuestDescriptionText");
            descGo.transform.SetParent(trackerGo.transform, false);
            var descText = descGo.AddComponent<Text>();
            descText.text = "Infiltrate the bandit camp";

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        public static void BuildHealthyScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            var hudCanvasGo = new GameObject("GuidanceHud");
            var canvas = hudCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvasGo.AddComponent<GraphicRaycaster>();

            var toggleGo = new GameObject("CollapseToggle");
            toggleGo.transform.SetParent(hudCanvasGo.transform, false);
            toggleGo.AddComponent<Button>();
            toggleGo.AddComponent<Image>();

            var trackerGo = new GameObject("QuestTracker");
            trackerGo.transform.SetParent(hudCanvasGo.transform, false);
            trackerGo.AddComponent<CanvasGroup>();

            var iconGo = new GameObject("ObjectiveBadgeIcon");
            iconGo.transform.SetParent(trackerGo.transform, false);
            iconGo.AddComponent<CanvasGroup>();
            iconGo.AddComponent<Image>();

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        public static void BuildInvalidScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

            var hudCanvasGo = new GameObject("GuidanceHud");
            var canvas = hudCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }



        public static List<ReductionNode> CreateReductionNodes()
        {
            var list = new List<ReductionNode>();
            list.Add(new ReductionNode("GuidanceHud", null, new[] { "GuidanceHud/CollapseToggle", ParentPanelHierarchyPath }));
            list.Add(new ReductionNode("GuidanceHud/CollapseToggle", "GuidanceHud"));
            list.Add(new ReductionNode(ParentPanelHierarchyPath, "GuidanceHud", new[] { TargetHierarchyPath, "GuidanceHud/QuestTracker/QuestTitleText", "GuidanceHud/QuestTracker/QuestDescriptionText" }));
            list.Add(new ReductionNode(TargetHierarchyPath, ParentPanelHierarchyPath));
            list.Add(new ReductionNode("GuidanceHud/QuestTracker/QuestTitleText", ParentPanelHierarchyPath));
            list.Add(new ReductionNode("GuidanceHud/QuestTracker/QuestDescriptionText", ParentPanelHierarchyPath));
            list.Add(new ReductionNode("MainCamera"));
            list.Add(new ReductionNode("EventSystem"));

            list.Add(new ReductionNode("Distractors", null));
            for (int i = 0; i < 15; i++) list.Add(new ReductionNode("EnvProp_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 8; i++) list.Add(new ReductionNode("FoliageSprite_" + i, "Distractors", priorityScore: 1.0));
            list.Add(new ReductionNode("SunDirectionalLight", "Distractors", priorityScore: 1.0));
            list.Add(new ReductionNode("WaterSurfaceRenderer", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 4; i++) list.Add(new ReductionNode("WaterWave_" + i, "WaterSurfaceRenderer", priorityScore: 1.0));
            list.Add(new ReductionNode("DialogueAudioSource", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 12; i++) list.Add(new ReductionNode("AudioTrigger_" + i, "DialogueAudioSource", priorityScore: 1.0));

            return list;
        }

        private static void CreateDistractorHierarchy()
        {
            var root = new GameObject("Distractors");
            for (int i = 0; i < 15; i++)
            {
                var d = new GameObject("EnvProp_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 8; i++)
            {
                var d = new GameObject("FoliageSprite_" + i);
                d.transform.SetParent(root.transform, false);
            }
            var sun = new GameObject("SunDirectionalLight");
            sun.transform.SetParent(root.transform, false);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;

            var water = new GameObject("WaterSurfaceRenderer");
            water.transform.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("WaterWave_" + i);
                w.transform.SetParent(water.transform, false);
            }

            var audio = new GameObject("DialogueAudioSource");
            audio.transform.SetParent(root.transform, false);
            audio.AddComponent<AudioSource>();
            for (int i = 0; i < 12; i++)
            {
                var a = new GameObject("AudioTrigger_" + i);
                a.transform.SetParent(audio.transform, false);
            }
        }

        private static void EnsureDirectory(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
