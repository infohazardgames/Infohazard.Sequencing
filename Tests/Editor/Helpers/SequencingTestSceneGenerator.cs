// The MIT License (MIT)
//
// Copyright (c) 2026-present Val Miller
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using Infohazard.Sequencing.Tests.PlayMode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Generates the play mode test scenes and their manifest entries (see <see cref="TestScenes"/>).
    /// Run it from the Editor (for example with <c>unity command script eval</c>) after changing
    /// <see cref="TestScenes"/>. It overwrites existing test assets in place, keeping their GUIDs.
    /// </summary>
    public static class SequencingTestSceneGenerator {
        public static void Generate() {
            EnsureFolder(TestScenes.Folder);
            EnsureFolder(TestScenes.EntryFolder);

            // Entries first (scenes reference them), then scenes, then the entries' scene GUIDs.
            LevelManifestRegionEntry regionA = CreateRegionEntry(TestScenes.RegionA, TestScenes.RegionAID, false);
            LevelManifestRegionEntry regionB = CreateRegionEntry(TestScenes.RegionB, TestScenes.RegionBID, true);
            LevelManifestRegionEntry regionGated =
                CreateRegionEntry(TestScenes.RegionGated, TestScenes.RegionGatedID, false);
            LevelManifestLevelEntry level = CreateLevelEntry(TestScenes.Level, TestScenes.LevelID,
                                                             regionA, regionB, regionGated);
            LevelManifestLevelEntry level2 = CreateLevelEntry(TestScenes.Level2, TestScenes.Level2ID);

            CreateRootScene<LevelRoot>(TestScenes.Level, "LevelRoot", level);
            CreateRootScene<LevelRoot>(TestScenes.Level2, "LevelRoot", level2);
            CreateRootScene<RegionRoot>(TestScenes.RegionA, "RegionRoot", regionA);
            CreateRootScene<RegionRoot>(TestScenes.RegionB, "RegionRoot", regionB);
            CreateRootScene<TestGatedRegionRoot>(TestScenes.RegionGated, "RegionRoot", regionGated);
            CreateScene(TestScenes.Plain, _ => new GameObject(TestScenes.PlainMarker));

            LevelManifestRegionEntry pRegionA =
                CreateRegionEntry(TestScenes.PRegionA, TestScenes.PRegionAID, false, TestScenes.PLevelID);
            LevelManifestRegionEntry pRegionB =
                CreateRegionEntry(TestScenes.PRegionB, TestScenes.PRegionBID, false, TestScenes.PLevelID);
            LevelManifestLevelEntry pLevel = CreateLevelEntry(TestScenes.PLevel, TestScenes.PLevelID,
                                                              pRegionA, pRegionB);

            CreateRootScene<PersistedLevelRoot>(TestScenes.PLevel, "LevelRoot", pLevel, () => {
                GameObject crate = CreatePersisted(TestScenes.StaticCrate, new Vector3(1, 0, 0), 7001);
                AddPersistedTransform(crate, saveScale: true);
                TestToggle toggle = crate.AddComponent<TestToggle>();
                SetFields(crate.AddComponent<PersistedComponentEnabled>(), ("_target", toggle));

                GameObject ball = CreatePersisted(TestScenes.StaticBall, new Vector3(2, 0, 0), 7002);
                Rigidbody body = ball.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearDamping = 0;
                body.angularDamping = 0;
                SetFields(ball.AddComponent<PersistedRigidbody>(), ("_rigidbody", body));

                CreatePersisted(TestScenes.StaticDestroyable, new Vector3(3, 0, 0), 7003);

                GameObject rememberer = CreatePersisted(TestScenes.Rememberer, new Vector3(4, 0, 0), 7004);
                SetFields(rememberer.AddComponent<RememberCurrentRegion>(), ("_initialRegion", pRegionA));

                GameObject bad = CreatePersisted(TestScenes.BadConverter, new Vector3(5, 0, 0), 7005);
                SetFields(bad.GetComponent<PersistedGameObject>(), ("_immediatelyConvert", true));

                GameObject good = CreatePersisted(TestScenes.GoodConverter, new Vector3(6, 0, 0), 7006,
                                                  TestScenes.ConverterPrefabKey);
                SetFields(good.GetComponent<PersistedGameObject>(), ("_immediatelyConvert", true));
                AddPersistedTransform(good);

                // Inactive so only the tests that want conversion get it (they activate them before LoadObjects).
                bad.SetActive(false);
                good.SetActive(false);
            });

            CreateRootScene<PersistedRegionRoot>(TestScenes.PRegionA, "RegionRoot", pRegionA, () => {
                GameObject crate = CreatePersisted(TestScenes.RegionACrate, new Vector3(0, 0, 10), 7101);
                AddPersistedTransform(crate);

                GameObject mover = CreatePersisted(TestScenes.Mover, new Vector3(0, 0, 11), 7102,
                                                   TestScenes.MoverPrefabKey);
                AddPersistedTransform(mover);
                mover.AddComponent<CanMoveBetweenRegions>();
            });

            CreateRootScene<PersistedRegionRoot>(TestScenes.PRegionB, "RegionRoot", pRegionB, () => {
                GameObject crate = CreatePersisted(TestScenes.RegionBCrate, new Vector3(0, 0, 20), 7201);
                AddPersistedTransform(crate);
            });

            foreach (Object entry in new Object[] {
                         regionA, regionB, regionGated, level, level2, pRegionA, pRegionB, pLevel,
                     }) {
                using SerializedObject serializedObject = new SerializedObject(entry);
                SerializedProperty scene = serializedObject.FindProperty("_scene");
                string path = scene.FindPropertyRelative("_path").stringValue;
                scene.FindPropertyRelative("_guid").stringValue = AssetDatabase.AssetPathToGUID(path);
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(entry);
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetScene(SerializedObject serializedObject, string scenePath) {
            SerializedProperty scene = serializedObject.FindProperty("_scene");
            scene.FindPropertyRelative("_path").stringValue = scenePath;
            scene.FindPropertyRelative("_guid").stringValue = AssetDatabase.AssetPathToGUID(scenePath);
        }

        private static LevelManifestRegionEntry CreateRegionEntry(string scenePath, int regionID,
                                                                  bool loadedByDefault,
                                                                  int levelID = TestScenes.LevelID) {
            LevelManifestRegionEntry entry = LoadOrCreate<LevelManifestRegionEntry>(TestScenes.EntryPath(scenePath));
            using SerializedObject serializedObject = new SerializedObject(entry);
            serializedObject.FindProperty("_displayName").stringValue = TestScenes.Name(scenePath);
            serializedObject.FindProperty("_regionID").intValue = regionID;
            serializedObject.FindProperty("_levelID").intValue = levelID;
            serializedObject.FindProperty("_loadedByDefault").boolValue = loadedByDefault;
            serializedObject.FindProperty("_alwaysLoaded").boolValue = false;
            SetScene(serializedObject, scenePath);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entry);
            return entry;
        }

        private static LevelManifestLevelEntry CreateLevelEntry(string scenePath, int levelID,
                                                                params LevelManifestRegionEntry[] regions) {
            LevelManifestLevelEntry entry = LoadOrCreate<LevelManifestLevelEntry>(TestScenes.EntryPath(scenePath));
            using SerializedObject serializedObject = new SerializedObject(entry);
            serializedObject.FindProperty("_displayName").stringValue = TestScenes.Name(scenePath);
            serializedObject.FindProperty("_levelID").intValue = levelID;
            SetScene(serializedObject, scenePath);
            SerializedProperty regionsProperty = serializedObject.FindProperty("_regions");
            regionsProperty.arraySize = regions.Length;
            for (int i = 0; i < regions.Length; i++) {
                regionsProperty.GetArrayElementAtIndex(i).objectReferenceValue = regions[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entry);
            return entry;
        }

        private static void CreateRootScene<T>(string scenePath, string rootName, Object entry,
                                               Action populate = null)
            where T : MonoBehaviour {
            CreateScene(scenePath, _ => {
                GameObject root = new GameObject(rootName);
                GameObject dynamicRoot = new GameObject("Dynamic Objects");
                dynamicRoot.transform.SetParent(root.transform, false);
                T component = root.AddComponent<T>();

                using SerializedObject serializedObject = new SerializedObject(component);
                serializedObject.FindProperty("_manifestEntry").objectReferenceValue = entry;
                serializedObject.FindProperty("_dynamicObjectRoot").objectReferenceValue = dynamicRoot.transform;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                populate?.Invoke();
            });
        }

        /// <summary>
        /// Create a root GameObject with a PersistedGameObject, a fixed instance ID and optionally a prefab key.
        /// </summary>
        private static GameObject CreatePersisted(string objectName, Vector3 position, ulong instanceID,
                                                  string prefabKey = null) {
            GameObject obj = new GameObject(objectName);
            obj.transform.position = position;
            PersistedGameObject persisted = obj.AddComponent<PersistedGameObject>();

            using SerializedObject serializedObject = new SerializedObject(persisted);
            serializedObject.FindProperty("_instanceID").ulongValue = instanceID;
            if (prefabKey != null) {
                serializedObject.FindProperty("_prefabReference").FindPropertyRelative("m_AssetGUID").stringValue =
                    prefabKey;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }

        private static void AddPersistedTransform(GameObject obj, bool saveScale = false) {
            SetFields(obj.AddComponent<PersistedTransform>(), ("_transform", obj.transform), ("_saveScale", saveScale));
        }

        private static void SetFields(Object target, params (string name, object value)[] fields) {
            using SerializedObject serializedObject = new SerializedObject(target);
            foreach ((string name, object value) in fields) {
                SerializedProperty property = serializedObject.FindProperty(name);
                switch (value) {
                    case Object obj:
                        property.objectReferenceValue = obj;
                        break;
                    case bool b:
                        property.boolValue = b;
                        break;
                    default:
                        throw new ArgumentException($"Unsupported value for {name}.");
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateScene(string scenePath, Action<Scene> populate) {
            Scene previousActive = SceneManager.GetActiveScene();

            // NewScene can't be additive while an untitled scene is open, but opening an existing file can, so start
            // from an empty scene file. Existing scenes are rebuilt from scratch, keeping their GUIDs.
            System.IO.File.WriteAllText(scenePath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try {
                SceneManager.SetActiveScene(scene);
                populate(scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) {
                    throw new InvalidOperationException($"Failed to save {scenePath}.");
                }
            } finally {
                if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
