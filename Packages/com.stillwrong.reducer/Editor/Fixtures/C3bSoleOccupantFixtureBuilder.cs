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
    public static class C3bSoleOccupantFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/C3b_SoleOccupant_Fixture.unity";
        public const string TargetHierarchyPath = "GuidanceHud/QuestTracker/ObjectiveBadgeIcon";
        public const string ParentPanelHierarchyPath = "GuidanceHud/QuestTracker";
        public const string PanelHierarchyPath = "GuidanceHud/QuestTracker/BadgePanelBackdrop";
        public const int DistractorCount = 42;
        public const float BadgeSizePx = 160f;

        public static OracleSpec CreateFailureConditionSpec()
        {
            return FixtureSpecs.C3();
        }

        public static InMemoryMeasurementSource CreateMeasurementSource(string mode)
        {
            return C3TransientDisappearFixtureBuilder.CreateMeasurementSource(mode);
        }

        public static void BuildFailingScene()
        {
            BuildScene(true);
        }

        public static void BuildHealthyScene()
        {
            BuildScene(false);
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
            hudCanvasGo.AddComponent<GraphicRaycaster>();

            var trackerGo = new GameObject("QuestTracker");
            trackerGo.transform.SetParent(hudCanvasGo.transform, false);
            trackerGo.AddComponent<CanvasGroup>();

            var panelGo = new GameObject("BadgePanelBackdrop");
            panelGo.transform.SetParent(trackerGo.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(480f, 480f);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.25f, 1f);

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void BuildScene(bool animate)
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

            var trackerGo = new GameObject("QuestTracker");
            trackerGo.transform.SetParent(hudCanvasGo.transform, false);
            trackerGo.AddComponent<CanvasGroup>();

            var panelGo = new GameObject("BadgePanelBackdrop");
            panelGo.transform.SetParent(trackerGo.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(480f, 480f);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.25f, 1f);

            var iconGo = new GameObject("ObjectiveBadgeIcon");
            iconGo.transform.SetParent(trackerGo.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.sizeDelta = new Vector2(BadgeSizePx, BadgeSizePx);
            iconGo.AddComponent<CanvasGroup>();
            iconGo.AddComponent<Image>();
            if (animate)
            {
                iconGo.AddComponent<StillWrong.Fixtures.C3TransientDriver>();
            }

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        public static List<ReductionNode> CreateReductionNodes()
        {
            var list = new List<ReductionNode>();
            list.Add(new ReductionNode("GuidanceHud", null, new[] { ParentPanelHierarchyPath }));
            list.Add(new ReductionNode(ParentPanelHierarchyPath, "GuidanceHud", new[] { TargetHierarchyPath, PanelHierarchyPath }));
            list.Add(new ReductionNode(PanelHierarchyPath, ParentPanelHierarchyPath));
            list.Add(new ReductionNode(TargetHierarchyPath, ParentPanelHierarchyPath));
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

