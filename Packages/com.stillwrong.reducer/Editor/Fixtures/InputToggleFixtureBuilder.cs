using System.IO;
using StillWrong.Fixtures;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace StillWrong.Editor.Fixtures
{
    public static class InputToggleFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/InputToggle_Fixture.unity";
        public const string ButtonPath = "ToggleCanvas/ToggleButton";
        public const string TargetPath = "ToggleTarget";
        public static void BuildScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            var targetGo = new GameObject("ToggleTarget");
            var canvasGo = new GameObject("ToggleCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGo.AddComponent<GraphicRaycaster>();
            var buttonGo = new GameObject("ToggleButton");
            buttonGo.transform.SetParent(canvasGo.transform, false);
            var btnRt = buttonGo.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(220f, 90f);
            btnRt.anchoredPosition = new Vector2(0f, -100f);
            var btnImg = buttonGo.AddComponent<Image>();
            btnImg.raycastTarget = true;
            buttonGo.AddComponent<Button>();
            var toggle = buttonGo.AddComponent<ClickToggle>();
            toggle.Target = targetGo;
            EditorSceneManager.SaveScene(scene, ScenePath);
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
