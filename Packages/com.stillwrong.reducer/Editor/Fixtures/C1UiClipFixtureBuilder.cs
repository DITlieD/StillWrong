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
    public static class C1UiClipFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/C1_UiClip_Fixture.unity";
        public const string TargetHierarchyPath = "BattleCanvas/ContextFlyout/HotbarSlotCard/SlotCardBody";
        public const string MaskHierarchyPath = "BattleCanvas/ContextFlyout/HotbarSlotCard";
        public const int DistractorCount = 48;

        public static OracleSpec CreateFailureConditionSpec()
        {
            return FixtureSpecs.C1();
        }

        public static InMemoryMeasurementSource CreateMeasurementSource(string mode)
        {
            string norm = (mode ?? string.Empty).ToLowerInvariant().Trim();
            var source = new InMemoryMeasurementSource();
            bool bad = norm == "failing" || norm == "known_bad" || norm == "stillwrong";
            bool healthy = norm == "healthy" || norm == "healthy_pass" || norm == "healthypass";
            if (bad || healthy)
            {
                source.AddExistingTargets(6, new[] { "body", "mask" });
                source.AddValue(6, Probes.OverflowPx("body", "mask"), MeasuredValue.FromNumber(bad ? 40.0 : 0.0));
                source.AddValue(6, Probes.Inside("body", "mask"), MeasuredValue.FromBool(!bad));
                source.AddValue(6, Probes.Active("body"), MeasuredValue.FromBool(true));
                source.AddValue(6, Probes.Active("mask"), MeasuredValue.FromBool(true));
                return source;
            }
            source.AddExistingTargets(6, new[] { "mask" });
            source.AddValue(6, Probes.Active("mask"), MeasuredValue.FromBool(true));
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

            var canvasGo = new GameObject("BattleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var flyoutGo = new GameObject("ContextFlyout");
            flyoutGo.transform.SetParent(canvasGo.transform, false);
            var flyoutRt = flyoutGo.AddComponent<RectTransform>();
            flyoutRt.sizeDelta = new Vector2(400f, 300f);

            var cardGo = new GameObject("HotbarSlotCard");
            cardGo.transform.SetParent(flyoutGo.transform, false);
            var cardRt = cardGo.AddComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(320f, 120f);
            cardGo.AddComponent<RectMask2D>();
            var cardCsf = cardGo.AddComponent<ContentSizeFitter>();
            cardCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            cardGo.AddComponent<VerticalLayoutGroup>();

            var bodyGo = new GameObject("SlotCardBody");
            bodyGo.transform.SetParent(cardGo.transform, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.sizeDelta = new Vector2(320f, 180f);
            var textComp = bodyGo.AddComponent<Text>();
            textComp.text = "Strikes adjacent target for 120 slashing damage.\nApplies Stun status for 2 turns.\nRequires 14 Tactical Points.";

            var slotsGo = new GameObject("HotbarSlots");
            slotsGo.transform.SetParent(canvasGo.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var slot = new GameObject("Slot_" + i);
                slot.transform.SetParent(slotsGo.transform, false);
                slot.AddComponent<Button>();
                slot.AddComponent<Image>();
            }

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

            var canvasGo = new GameObject("BattleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var flyoutGo = new GameObject("ContextFlyout");
            flyoutGo.transform.SetParent(canvasGo.transform, false);
            var flyoutRt = flyoutGo.AddComponent<RectTransform>();
            flyoutRt.sizeDelta = new Vector2(400f, 300f);

            var cardGo = new GameObject("HotbarSlotCard");
            cardGo.transform.SetParent(flyoutGo.transform, false);
            var cardRt = cardGo.AddComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(320f, 220f);
            cardGo.AddComponent<RectMask2D>();
            var cardCsf = cardGo.AddComponent<ContentSizeFitter>();
            cardCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cardGo.AddComponent<VerticalLayoutGroup>();

            var bodyGo = new GameObject("SlotCardBody");
            bodyGo.transform.SetParent(cardGo.transform, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.sizeDelta = new Vector2(320f, 180f);
            var textComp = bodyGo.AddComponent<Text>();
            textComp.text = "Strikes adjacent target for 120 slashing damage.\nApplies Stun status for 2 turns.\nRequires 14 Tactical Points.";

            var slotsGo = new GameObject("HotbarSlots");
            slotsGo.transform.SetParent(canvasGo.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var slot = new GameObject("Slot_" + i);
                slot.transform.SetParent(slotsGo.transform, false);
                slot.AddComponent<Button>();
                slot.AddComponent<Image>();
            }

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

            var canvasGo = new GameObject("BattleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }



        public static List<ReductionNode> CreateReductionNodes()
        {
            var list = new List<ReductionNode>();
            list.Add(new ReductionNode("BattleCanvas", null, new[] { "BattleCanvas/ContextFlyout", "BattleCanvas/HotbarSlots" }));
            list.Add(new ReductionNode("BattleCanvas/ContextFlyout", "BattleCanvas", new[] { MaskHierarchyPath }));
            list.Add(new ReductionNode(MaskHierarchyPath, "BattleCanvas/ContextFlyout", new[] { TargetHierarchyPath }));
            list.Add(new ReductionNode(TargetHierarchyPath, MaskHierarchyPath));
            list.Add(new ReductionNode("BattleCanvas/HotbarSlots", "BattleCanvas"));
            list.Add(new ReductionNode("MainCamera"));
            list.Add(new ReductionNode("EventSystem"));

            list.Add(new ReductionNode("Distractors", null));
            for (int i = 0; i < 12; i++) list.Add(new ReductionNode("TerrainTile_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 8; i++) list.Add(new ReductionNode("CharacterModel_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 6; i++) list.Add(new ReductionNode("PointLight_" + i, "Distractors", priorityScore: 1.0));
            list.Add(new ReductionNode("TurnOrderCanvas", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 4; i++) list.Add(new ReductionNode("TurnSlot_" + i, "TurnOrderCanvas", priorityScore: 1.0));
            list.Add(new ReductionNode("MinimapPanel", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 3; i++) list.Add(new ReductionNode("MinimapElem_" + i, "MinimapPanel", priorityScore: 1.0));
            list.Add(new ReductionNode("FloatingCombatTextCanvas", "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 2; i++) list.Add(new ReductionNode("CombatText_" + i, "FloatingCombatTextCanvas", priorityScore: 1.0));
            for (int i = 0; i < 4; i++) list.Add(new ReductionNode("AudioEmitter_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 6; i++) list.Add(new ReductionNode("ParticleProp_" + i, "Distractors", priorityScore: 1.0));

            return list;
        }

        private static void CreateDistractorHierarchy()
        {
            var root = new GameObject("Distractors");
            for (int i = 0; i < 12; i++)
            {
                var d = new GameObject("TerrainTile_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 8; i++)
            {
                var d = new GameObject("CharacterModel_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 6; i++)
            {
                var d = new GameObject("PointLight_" + i);
                d.transform.SetParent(root.transform, false);
            }
            var turnOrder = new GameObject("TurnOrderCanvas");
            turnOrder.transform.SetParent(root.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var slot = new GameObject("TurnSlot_" + i);
                slot.transform.SetParent(turnOrder.transform, false);
            }
            var minimap = new GameObject("MinimapPanel");
            minimap.transform.SetParent(root.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var elem = new GameObject("MinimapElem_" + i);
                elem.transform.SetParent(minimap.transform, false);
            }
            var fct = new GameObject("FloatingCombatTextCanvas");
            fct.transform.SetParent(root.transform, false);
            for (int i = 0; i < 2; i++)
            {
                var ct = new GameObject("CombatText_" + i);
                ct.transform.SetParent(fct.transform, false);
            }
            for (int i = 0; i < 4; i++)
            {
                var d = new GameObject("AudioEmitter_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 6; i++)
            {
                var d = new GameObject("ParticleProp_" + i);
                d.transform.SetParent(root.transform, false);
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
