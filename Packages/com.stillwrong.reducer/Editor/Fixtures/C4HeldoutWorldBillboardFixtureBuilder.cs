using System;
using System.Collections.Generic;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace StillWrong.Editor.Fixtures
{
    public static class C4HeldoutWorldBillboardFixtureBuilder
    {
        public const string ScenePath = "Assets/Fixtures/C4_HeldoutSecondProject_Fixture.unity";
        public const string TargetMeshHierarchyPath = "ArenaCombatants/TrainingDummy/MeshRenderer";
        public const string TargetBillboardHierarchyPath = "ArenaCombatants/TrainingDummy/StatusBarAnchor/UIDocument_HealthPlate";
        public const string DummyMeshId = "ArenaCombatants/TrainingDummy/MeshRenderer";
        public const string BillboardId = "ArenaCombatants/TrainingDummy/StatusBarAnchor/UIDocument_HealthPlate";
        public const int BillboardLayer = 10;
        public const int DistractorCount = 65;

        public static OracleSpec CreateFailureConditionSpec()
        {
            return FixtureSpecs.C4();
        }

        public static InMemoryMeasurementSource CreateMeasurementSource(string mode)
        {
            string norm = (mode ?? string.Empty).ToLowerInvariant().Trim();
            var source = new InMemoryMeasurementSource();
            bool bad = norm == "failing" || norm == "known_bad" || norm == "stillwrong";
            bool healthy = norm == "healthy" || norm == "healthy_pass" || norm == "healthypass";
            if (bad || healthy)
            {
                source.AddExistingTargets(6, new[] { "dummy", "plate", "camera" });
                source.AddValue(6, Probes.ScreenArea("dummy"), MeasuredValue.FromNumber(620.0));
                source.AddValue(6, Probes.Culled("plate", "camera"), MeasuredValue.FromBool(bad));
                source.AddValue(6, Probes.VisiblePixels("plate"), MeasuredValue.FromNumber(bad ? 0.0 : 220.0));
                source.AddValue(6, Probes.Active("dummy"), MeasuredValue.FromBool(true));
                source.AddValue(6, Probes.Active("plate"), MeasuredValue.FromBool(true));
                return source;
            }
            source.AddExistingTargets(6, new[] { "plate", "camera" });
            source.AddValue(6, Probes.Active("plate"), MeasuredValue.FromBool(true));
            return source;
        }


        public static void BuildFailingScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var playerGo = new GameObject("PlayerCombatant");
            playerGo.transform.position = Vector3.zero;

            var rigGo = new GameObject("PlayerCameraRig");
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rigGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = ~(1 << BillboardLayer);

            var arenaGo = new GameObject("ArenaCombatants");
            var dummyGo = new GameObject("TrainingDummy");
            dummyGo.transform.SetParent(arenaGo.transform, false);
            dummyGo.transform.position = new Vector3(0f, 0f, 8f);
            var mr = dummyGo.AddComponent<MeshRenderer>();
            dummyGo.AddComponent<MeshFilter>();

            var anchorGo = new GameObject("StatusBarAnchor");
            anchorGo.transform.SetParent(dummyGo.transform, false);
            anchorGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var plateGo = new GameObject("UIDocument_HealthPlate");
            plateGo.transform.SetParent(anchorGo.transform, false);
            plateGo.layer = BillboardLayer;

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        public static void BuildHealthyScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var playerGo = new GameObject("PlayerCombatant");
            playerGo.transform.position = Vector3.zero;

            var rigGo = new GameObject("PlayerCameraRig");
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rigGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = ~0;

            var arenaGo = new GameObject("ArenaCombatants");
            var dummyGo = new GameObject("TrainingDummy");
            dummyGo.transform.SetParent(arenaGo.transform, false);
            dummyGo.transform.position = new Vector3(0f, 0f, 8f);
            dummyGo.AddComponent<MeshRenderer>();
            dummyGo.AddComponent<MeshFilter>();

            var anchorGo = new GameObject("StatusBarAnchor");
            anchorGo.transform.SetParent(dummyGo.transform, false);
            anchorGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var plateGo = new GameObject("UIDocument_HealthPlate");
            plateGo.transform.SetParent(anchorGo.transform, false);
            plateGo.layer = BillboardLayer;

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        public static void BuildInvalidScene()
        {
            EnsureDirectory(ScenePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var playerGo = new GameObject("PlayerCombatant");
            var rigGo = new GameObject("PlayerCameraRig");
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rigGo.transform, false);
            camGo.AddComponent<Camera>();

            var arenaGo = new GameObject("ArenaCombatants");
            var dummyGo = new GameObject("TrainingDummy");
            dummyGo.transform.SetParent(arenaGo.transform, false);

            CreateDistractorHierarchy();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }



        public static List<ReductionNode> CreateReductionNodes()
        {
            var list = new List<ReductionNode>();
            list.Add(new ReductionNode("ArenaCombatants", null, new[] { "ArenaCombatants/TrainingDummy" }));
            list.Add(new ReductionNode("ArenaCombatants/TrainingDummy", "ArenaCombatants", new[] { DummyMeshId, "ArenaCombatants/TrainingDummy/StatusBarAnchor" }));
            list.Add(new ReductionNode(DummyMeshId, "ArenaCombatants/TrainingDummy"));
            list.Add(new ReductionNode("ArenaCombatants/TrainingDummy/StatusBarAnchor", "ArenaCombatants/TrainingDummy", new[] { BillboardId }));
            list.Add(new ReductionNode(BillboardId, "ArenaCombatants/TrainingDummy/StatusBarAnchor"));
            list.Add(new ReductionNode("PlayerCameraRig", null, new[] { "PlayerCameraRig/MainCamera" }));
            list.Add(new ReductionNode("PlayerCameraRig/MainCamera", "PlayerCameraRig"));
            list.Add(new ReductionNode("PlayerCombatant"));
            list.Add(new ReductionNode("EventSystem"));

            list.Add(new ReductionNode("Distractors", null));
            for (int i = 0; i < 18; i++) list.Add(new ReductionNode("TemplePillar_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 14; i++) list.Add(new ReductionNode("WallBlock_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 12; i++) list.Add(new ReductionNode("ArenaGroundTile_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 8; i++) list.Add(new ReductionNode("WeaponRack_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 6; i++) list.Add(new ReductionNode("DynamicSpotlight_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 3; i++) list.Add(new ReductionNode("AudioReverbZone_" + i, "Distractors", priorityScore: 1.0));
            for (int i = 0; i < 4; i++) list.Add(new ReductionNode("DustParticleSystem_" + i, "Distractors", priorityScore: 1.0));

            return list;
        }

        private static void CreateDistractorHierarchy()
        {
            var root = new GameObject("Distractors");
            for (int i = 0; i < 18; i++)
            {
                var d = new GameObject("TemplePillar_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 14; i++)
            {
                var d = new GameObject("WallBlock_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 12; i++)
            {
                var d = new GameObject("ArenaGroundTile_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 8; i++)
            {
                var d = new GameObject("WeaponRack_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 6; i++)
            {
                var d = new GameObject("DynamicSpotlight_" + i);
                d.transform.SetParent(root.transform, false);
                var l = d.AddComponent<Light>();
                l.type = LightType.Spot;
            }
            for (int i = 0; i < 3; i++)
            {
                var d = new GameObject("AudioReverbZone_" + i);
                d.transform.SetParent(root.transform, false);
            }
            for (int i = 0; i < 4; i++)
            {
                var d = new GameObject("DustParticleSystem_" + i);
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
