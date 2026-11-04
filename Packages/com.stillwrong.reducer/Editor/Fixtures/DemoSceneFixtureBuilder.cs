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
    [Serializable]
    public sealed class DemoSceneBuildResult
    {
        public string ScenePath = string.Empty;
        public int ObjectCount;
        public int CandidateCount;
        public List<string> CandidateIds = new List<string>();
    }
    public static class DemoSceneFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/DemoScene_Fixture.unity";
        public const string TargetHierarchyPath = "Distractors/CharacterModel_1";
        public const string MaskHierarchyPath = "BattleCanvas/ContextFlyout/HotbarSlotCard";
        private static int _made;
        [MenuItem("Tools/Still Wrong/Build Demo Scene")]
        public static void BuildDemoSceneFromMenu()
        {
            BuildScene();
        }
        public static List<string> CreateCandidateIds()
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < 12; i++) ids.Add("TerrainTile_" + i);
            for (int i = 0; i < 3; i++) ids.Add("CharacterModel_" + i);
            for (int i = 0; i < 2; i++) ids.Add("PointLight_" + i);
            ids.Add("TurnOrderCanvas");
            for (int i = 0; i < 4; i++) ids.Add("TurnSlot_" + i);
            ids.Add("MinimapPanel");
            for (int i = 0; i < 3; i++) ids.Add("MinimapElem_" + i);
            ids.Add("GroundPlane");
            ids.Add("HotbarBackPanel");
            ids.Add("CardBackPanel");
            ids.Add("HeaderTitle");
            return ids;
        }
        public static DemoSceneBuildResult BuildScene()
        {
            EnsureDirectory(ScenePath);
            _made = 0;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = Mk("MainCamera", null);
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 4.5f, -9f);
            camGo.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            var esGo = Mk("EventSystem", null);
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            var canvasGo = Mk("BattleCanvas", null);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            canvas.sortingOrder = 40;
            var hotbarBack = Mk("HotbarBackPanel", canvasGo.transform);
            var hotbarBackRt = hotbarBack.AddComponent<RectTransform>();
            hotbarBackRt.sizeDelta = new Vector2(560f, 220f);
            var hotbarBackImg = hotbarBack.AddComponent<Image>();
            hotbarBackImg.color = new Color(0.10f, 0.12f, 0.20f, 1f);
            var flyoutGo = Mk("ContextFlyout", canvasGo.transform);
            var flyoutRt = flyoutGo.AddComponent<RectTransform>();
            flyoutRt.sizeDelta = new Vector2(400f, 300f);
            var cardBack = Mk("CardBackPanel", flyoutGo.transform);
            var cardBackRt = cardBack.AddComponent<RectTransform>();
            cardBackRt.sizeDelta = new Vector2(360f, 160f);
            var cardBackImg = cardBack.AddComponent<Image>();
            cardBackImg.color = new Color(0.16f, 0.18f, 0.30f, 1f);
            var cardGo = Mk("HotbarSlotCard", flyoutGo.transform);
            var cardRt = cardGo.AddComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(320f, 120f);
            var cardCsf = cardGo.AddComponent<ContentSizeFitter>();
            cardCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            cardGo.AddComponent<VerticalLayoutGroup>();
            var bodyGo = Mk("SlotCardBody", cardGo.transform);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.sizeDelta = new Vector2(320f, 180f);
            var textComp = bodyGo.AddComponent<Text>();
            textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComp.fontSize = 26;
            textComp.color = new Color(1f, 0.95f, 0.1f, 1f);
            textComp.alignment = TextAnchor.UpperLeft;
            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.verticalOverflow = VerticalWrapMode.Overflow;
            textComp.text = "Strikes adjacent target for 120 slashing damage.";
            cardRt.sizeDelta = new Vector2(520f, 80f);
            bodyRt.sizeDelta = new Vector2(500f, 70f);
            cardBackImg.color = new Color(0.02f, 0.02f, 0.02f, 1f);
            cardBackRt.sizeDelta = new Vector2(540f, 90f);
            flyoutRt.anchoredPosition = new Vector2(0f, -20f);
            flyoutRt.sizeDelta = new Vector2(560f, 130f);
            var headerGo = Mk("HeaderTitle", canvasGo.transform);
            var headerRt = headerGo.AddComponent<RectTransform>();
            headerRt.sizeDelta = new Vector2(900f, 60f);
            headerRt.anchoredPosition = new Vector2(0f, 460f);
            var headerText = headerGo.AddComponent<Text>();
            headerText.text = "STILLWRONG - Live Reduction Demo";
            var distractRoot = Mk("Distractors", null);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "GroundPlane";
            ground.transform.SetParent(distractRoot.transform, false);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.transform.position = new Vector3(0f, -1.5f, 6f);
            ground.GetComponent<Renderer>().material.color = new Color(0.13f, 0.30f, 0.16f, 1f);
            _made++;
            for (int i = 0; i < 12; i++)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "TerrainTile_" + i;
                tile.transform.SetParent(distractRoot.transform, false);
                int col = i % 4;
                int row = i / 4;
                tile.transform.position = new Vector3(-3.3f + col * 2.2f, -1.4f, 5f + row * 2.2f);
                tile.transform.localScale = new Vector3(2f, 0.15f, 2f);
                float g = (i % 2 == 0) ? 0.38f : 0.30f;
                tile.GetComponent<Renderer>().material.color = new Color(0.16f, g, 0.18f, 1f);
                _made++;
            }
            Vector3[] heroPos = new Vector3[3];
            heroPos[0] = new Vector3(-4f, -0.5f, 2.5f);
            heroPos[1] = new Vector3(0f, -0.5f, 2.5f);
            heroPos[2] = new Vector3(4f, -0.5f, 2.5f);
            Color[] heroCol = new Color[3];
            heroCol[0] = new Color(0.75f, 0.20f, 0.20f, 1f);
            heroCol[1] = new Color(0.20f, 0.40f, 0.75f, 1f);
            heroCol[2] = new Color(0.80f, 0.65f, 0.20f, 1f);
            for (int i = 0; i < 3; i++)
            {
                var hero = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                hero.name = "CharacterModel_" + i;
                hero.transform.SetParent(distractRoot.transform, false);
                hero.transform.position = heroPos[i];
                if (i == 1)
                {
                    hero.transform.localScale = new Vector3(2.2f, 4.2f, 2.2f);
                    hero.transform.position = new Vector3(0f, 1.6f, 2.5f);
                    hero.GetComponent<Renderer>().material.color = new Color(1f, 0.05f, 0.85f, 1f);
                }
                else
                {
                    hero.GetComponent<Renderer>().material.color = heroCol[i];
                }
                _made++;
            }
            for (int i = 0; i < 2; i++)
            {
                var lamp = Mk("PointLight_" + i, distractRoot.transform);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.intensity = 2f;
                light.range = 12f;
                lamp.transform.position = new Vector3(i == 0 ? -3f : 3f, 3f, 5f);
                light.color = i == 0 ? new Color(1f, 0.85f, 0.65f, 1f) : new Color(0.65f, 0.80f, 1f, 1f);
            }
            var turnCanvasGo = Mk("TurnOrderCanvas", null);
            var turnCanvas = turnCanvasGo.AddComponent<Canvas>();
            turnCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            turnCanvas.sortingOrder = 6;
            turnCanvasGo.AddComponent<GraphicRaycaster>();
            string[] turnNames = new string[4];
            turnNames[0] = "Turn 1 - Ally";
            turnNames[1] = "Turn 2 - Foe";
            turnNames[2] = "Turn 3 - Ally";
            turnNames[3] = "Turn 4 - Foe";
            for (int i = 0; i < 4; i++)
            {
                var turnSlot = Mk("TurnSlot_" + i, turnCanvasGo.transform);
                var turnRt = turnSlot.AddComponent<RectTransform>();
                turnRt.sizeDelta = new Vector2(200f, 40f);
                turnRt.anchoredPosition = new Vector2(-830f, 380f - i * 50f);
                var turnText = turnSlot.AddComponent<Text>();
                turnText.text = turnNames[i];
            }
            var minimapGo = Mk("MinimapPanel", turnCanvasGo.transform);
            var minimapRt = minimapGo.AddComponent<RectTransform>();
            minimapRt.sizeDelta = new Vector2(220f, 220f);
            minimapRt.anchoredPosition = new Vector2(830f, 320f);
            var minimapImg = minimapGo.AddComponent<Image>();
            minimapImg.color = new Color(0.06f, 0.08f, 0.14f, 1f);
            Color[] blockCol = new Color[3];
            blockCol[0] = new Color(0.75f, 0.20f, 0.20f, 1f);
            blockCol[1] = new Color(0.20f, 0.65f, 0.30f, 1f);
            blockCol[2] = new Color(0.25f, 0.45f, 0.85f, 1f);
            for (int i = 0; i < 3; i++)
            {
                var elem = Mk("MinimapElem_" + i, minimapGo.transform);
                var elemRt = elem.AddComponent<RectTransform>();
                elemRt.sizeDelta = new Vector2(48f, 48f);
                elemRt.anchoredPosition = new Vector2(-60f + i * 60f, 40f - i * 55f);
                var elemImg = elem.AddComponent<Image>();
                elemImg.color = blockCol[i];
            }
            var hudCanvasGo = Mk("GuidanceHud", null);
            var hudCanvas = hudCanvasGo.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 5;
            hudCanvasGo.AddComponent<GraphicRaycaster>();
            var trackerGo = Mk("QuestTracker", hudCanvasGo.transform);
            trackerGo.AddComponent<CanvasGroup>();
            var panelGo = Mk("BadgePanelBackdrop", trackerGo.transform);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(200f, 200f);
            panelRt.anchoredPosition = new Vector2(830f, 90f);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.25f, 1f);
            var iconGo = Mk("ObjectiveBadgeIcon", trackerGo.transform);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.sizeDelta = new Vector2(160f, 160f);
            iconRt.anchoredPosition = new Vector2(830f, 90f);
            iconGo.AddComponent<CanvasGroup>();
            Image iconImg = iconGo.AddComponent<Image>();
            iconImg.color = new Color(1f, 1f, 1f, 0f);
            iconImg.raycastTarget = false;
            iconGo.SetActive(false);
            iconGo.AddComponent<StillWrong.Fixtures.C3TransientDriver>();
            var tavernCanvasGo = Mk("TavernPromptCanvas", null);
            var tavernCanvas = tavernCanvasGo.AddComponent<Canvas>();
            tavernCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tavernCanvas.sortingOrder = 10;
            tavernCanvasGo.AddComponent<GraphicRaycaster>();
            var tavernButtonGo = Mk("OpenTavernButton", tavernCanvasGo.transform);
            var tavernBtnRt = tavernButtonGo.AddComponent<RectTransform>();
            tavernBtnRt.sizeDelta = new Vector2(120f, 60f);
            tavernBtnRt.anchoredPosition = new Vector2(0f, -120f);
            var tavernBtnImg = tavernButtonGo.AddComponent<Image>();
            tavernBtnImg.raycastTarget = true;
            tavernBtnImg.color = new Color(1f, 1f, 1f, 0f);
            tavernBtnImg.raycastTarget = false;
            tavernButtonGo.AddComponent<Button>();
            tavernButtonGo.SetActive(false);
            var modalCanvasGo = Mk("ModalBackdropCanvas", null);
            var modalCanvas = modalCanvasGo.AddComponent<Canvas>();
            modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modalCanvas.sortingOrder = 15;
            modalCanvasGo.AddComponent<GraphicRaycaster>();
            var dimmerGo = Mk("FullscreenDimmer", modalCanvasGo.transform);
            var dimmerRt = dimmerGo.AddComponent<RectTransform>();
            dimmerRt.sizeDelta = new Vector2(1920f, 1080f);
            var dimmerImg = dimmerGo.AddComponent<Image>();
            dimmerImg.color = new Color(0f, 0f, 0f, 0.75f);
            dimmerImg.raycastTarget = true;
            modalCanvasGo.SetActive(false);
            EditorSceneManager.SaveScene(scene, ScenePath);
            DemoSceneBuildResult result = new DemoSceneBuildResult();
            result.ScenePath = ScenePath;
            result.ObjectCount = _made;
            result.CandidateIds = CreateCandidateIds();
            result.CandidateCount = result.CandidateIds.Count;
            return result;
        }
        private static GameObject Mk(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            _made++;
            return go;
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
