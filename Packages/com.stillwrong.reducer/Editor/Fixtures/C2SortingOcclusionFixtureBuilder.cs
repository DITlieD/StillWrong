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
    public static class C2SortingOcclusionFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/C2_SortingOcclusion_Fixture.unity";
        public const string TargetHierarchyPath = "TavernPromptCanvas/OpenTavernButton";
        public const string OccluderHierarchyPath = "ModalBackdropCanvas/FullscreenDimmer";
        public const string TransparentInterceptorPath = "ScreenOverlayCanvas/TransparentBlocker";
        public const int DistractorCount = 54;

        public static OracleSpec CreateFailureConditionSpec()
        {
            return FixtureSpecs.C2();
        }

        public static InMemoryMeasurementSource CreateMeasurementSource(string mode)
        {
            string norm = (mode ?? string.Empty).ToLowerInvariant().Trim();
            var source = new InMemoryMeasurementSource();
            bool bad = norm == "failing" || norm == "known_bad" || norm == "stillwrong" || norm == "visual_occlusion" || norm == "transparent_interceptor" || norm == "raycast_block";
            bool healthy = norm == "healthy" || norm == "healthy_pass" || norm == "healthypass";
            if (bad || healthy)
            {
                source.AddExistingTargets(3, new[] { "button", "dimmer", "blocker" });
                source.AddValue(3, Probes.Inside("button", "dimmer"), MeasuredValue.FromBool(true));
                source.AddValue(3, Probes.VisibleFraction("button"), MeasuredValue.FromNumber(bad ? 0.04 : 1.0));
                source.AddValue(3, Probes.TopmostAt(960.0, 420.0), MeasuredValue.FromText(bad ? "ModalBackdropCanvas/FullscreenDimmer" : "TavernPromptCanvas/OpenTavernButton"));
                source.AddValue(3, Probes.RaycastTargetAt(960.0, 420.0), MeasuredValue.FromText(bad ? "ModalBackdropCanvas/FullscreenDimmer" : "TavernPromptCanvas/OpenTavernButton"));
                source.AddValue(3, Probes.Active("button"), MeasuredValue.FromBool(true));
                source.AddValue(3, Probes.Active("dimmer"), MeasuredValue.FromBool(true));
                return source;
            }
            source.AddExistingTargets(3, new[] { "dimmer", "blocker" });
            source.AddValue(3, Probes.Active("dimmer"), MeasuredValue.FromBool(true));
            return source;
        }


        public static void BuildFailingScene()
        {
            BuildFailingScene("visual_occlusion");
        }

        public static void BuildFailingScene(string failureSubmode)
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            var tavernCanvasGo = new GameObject("TavernPromptCanvas");
            var tavernCanvas = tavernCanvasGo.AddComponent<Canvas>();
            tavernCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tavernCanvas.sortingOrder = 10;
            tavernCanvasGo.AddComponent<GraphicRaycaster>();

            var buttonGo = new GameObject("OpenTavernButton");
            buttonGo.transform.SetParent(tavernCanvasGo.transform, false);
            var btnRt = buttonGo.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(120f, 60f);
            btnRt.anchoredPosition = new Vector2(0f, -120f);
            var btnImg = buttonGo.AddComponent<Image>();
            btnImg.raycastTarget = true;
            buttonGo.AddComponent<Button>();

            var modalCanvasGo = new GameObject("ModalBackdropCanvas");
            var modalCanvas = modalCanvasGo.AddComponent<Canvas>();
            modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modalCanvas.sortingOrder = failureSubmode == "visual_occlusion" ? 15 : 5;
            modalCanvasGo.AddComponent<GraphicRaycaster>();

            var dimmerGo = new GameObject("FullscreenDimmer");
            dimmerGo.transform.SetParent(modalCanvasGo.transform, false);
            var dimmerRt = dimmerGo.AddComponent<RectTransform>();
            dimmerRt.sizeDelta = new Vector2(1920f, 1080f);
            var dimmerImg = dimmerGo.AddComponent<Image>();
            dimmerImg.color = new Color(0f, 0f, 0f, 0.75f);
            dimmerImg.raycastTarget = true;

            var screenOverlayGo = new GameObject("ScreenOverlayCanvas");
            var overlayCanvas = screenOverlayGo.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = failureSubmode == "transparent_interceptor" ? 25 : 5;
            screenOverlayGo.AddComponent<GraphicRaycaster>();

            var blockerGo = new GameObject("TransparentBlocker");
            blockerGo.transform.SetParent(screenOverlayGo.transform, false);
            var blockerRt = blockerGo.AddComponent<RectTransform>();
            blockerRt.sizeDelta = new Vector2(1920f, 1080f);
            var blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(1f, 1f, 1f, 0.01f);
            blockerImg.raycastTarget = true;

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

            var tavernCanvasGo = new GameObject("TavernPromptCanvas");
            var tavernCanvas = tavernCanvasGo.AddComponent<Canvas>();
            tavernCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tavernCanvas.sortingOrder = 20;
            tavernCanvasGo.AddComponent<GraphicRaycaster>();

            var buttonGo = new GameObject("OpenTavernButton");
            buttonGo.transform.SetParent(tavernCanvasGo.transform, false);
            var btnRt = buttonGo.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(120f, 60f);
            var btnImg = buttonGo.AddComponent<Image>();
            btnImg.raycastTarget = true;
            buttonGo.AddComponent<Button>();

            var modalCanvasGo = new GameObject("ModalBackdropCanvas");
            var modalCanvas = modalCanvasGo.AddComponent<Canvas>();
            modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modalCanvas.sortingOrder = 5;
            modalCanvasGo.AddComponent<GraphicRaycaster>();

            var dimmerGo = new GameObject("FullscreenDimmer");
            dimmerGo.transform.SetParent(modalCanvasGo.transform, false);
            var dimmerRt = dimmerGo.AddComponent<RectTransform>();
            dimmerRt.sizeDelta = new Vector2(1920f, 1080f);
            dimmerGo.AddComponent<Image>();

            var screenOverlayGo = new GameObject("ScreenOverlayCanvas");
            var overlayCanvas = screenOverlayGo.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 5;
            screenOverlayGo.AddComponent<GraphicRaycaster>();

            var blockerGo = new GameObject("TransparentBlocker");
            blockerGo.transform.SetParent(screenOverlayGo.transform, false);
            var blockerRt = blockerGo.AddComponent<RectTransform>();
            blockerRt.sizeDelta = new Vector2(1920f, 1080f);
            var blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(1f, 1f, 1f, 0f);
            blockerImg.raycastTarget = false;

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

            var tavernCanvasGo = new GameObject("TavernPromptCanvas");
            var tavernCanvas = tavernCanvasGo.AddComponent<Canvas>();
            tavernCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }



        public static List<ReductionNode> CreateReductionNodes()
        {
            var list = new List<ReductionNode>();
            list.Add(new ReductionNode("TavernPromptCanvas", null, new[] { TargetHierarchyPath }));
            list.Add(new ReductionNode(TargetHierarchyPath, "TavernPromptCanvas"));
            list.Add(new ReductionNode("ModalBackdropCanvas", null, new[] { OccluderHierarchyPath }));
            list.Add(new ReductionNode(OccluderHierarchyPath, "ModalBackdropCanvas"));
            list.Add(new ReductionNode("ScreenOverlayCanvas", null, new[] { TransparentInterceptorPath }));
            list.Add(new ReductionNode(TransparentInterceptorPath, "ScreenOverlayCanvas"));
            list.Add(new ReductionNode("MainCamera"));
            list.Add(new ReductionNode("EventSystem"));

            list.Add(new ReductionNode("Distractors", null));
            for (int i = 0; i < 20; i++) list.Add(new ReductionNode("Building_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 10; i++) list.Add(new ReductionNode("NpcSprite_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 8; i++) list.Add(new ReductionNode("StreetLamp_" + i, "Distractors", priorityScore: 1.0));
            list.Add(new ReductionNode("AmbientDustParticles", "Distractors", priorityScore: 1.0));
            list.Add(new ReductionNode("WeatherCanvas", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 5; i++) list.Add(new ReductionNode("WeatherLayer_" + i, "WeatherCanvas", priorityScore: 1.0));
            list.Add(new ReductionNode("MinimapCamera", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 8; i++) list.Add(new ReductionNode("MinimapMarker_" + i, "MinimapCamera", priorityScore: 1.0));

            return list;
        }

        private static void CreateDistractorHierarchy()
        {
            var root = new GameObject("Distractors");
            for (int i = 0; i < 20; i++)
            {
                var d = new GameObject("Building_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 10; i++)
            {
                var d = new GameObject("NpcSprite_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 8; i++)
            {
                var d = new GameObject("StreetLamp_" + i);
                d.transform.SetParent(root.transform, false);
            }
            var dust = new GameObject("AmbientDustParticles");
            dust.transform.SetParent(root.transform, false);

            var weather = new GameObject("WeatherCanvas");
            weather.transform.SetParent(root.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var w = new GameObject("WeatherLayer_" + i);
                w.transform.SetParent(weather.transform, false);
            }

            var minimap = new GameObject("MinimapCamera");
            minimap.transform.SetParent(root.transform, false);
            for (int i = 0; i < 8; i++)
            {
                var m = new GameObject("MinimapMarker_" + i);
                m.transform.SetParent(minimap.transform, false);
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
