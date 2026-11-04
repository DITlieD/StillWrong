using System.IO;
using StillWrong.Fixtures;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace StillWrong.Editor.Fixtures
{
    public static class AwakeSpawnFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/AwakeSpawn_Fixture.unity";
        public const string SpawnerPath = "Spawner";
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
            var spawner = new GameObject("Spawner");
            spawner.AddComponent<AwakeSpawnProbe>();
            for (int i = 0; i < 3; i++)
            {
                var d = new GameObject("Distractor_" + i);
            }
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
