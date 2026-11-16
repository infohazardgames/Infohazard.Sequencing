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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Sequencing's custom inspectors, edit-mode instance ID generation, and the step that passes the editing scene
    /// into the boot sequence.
    /// </summary>
    public class UnitTest_EditorTooling : SequencingTestFixture {
        private readonly List<Scene> _openedScenes = new();
        private readonly Dictionary<string, object> _savedPrefs = new();
        private string _tempFolder;

        [TearDown]
        public void TearDownEditorTooling() {
            foreach (Scene scene in _openedScenes) {
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }

            _openedScenes.Clear();

            foreach ((string key, object value) in _savedPrefs) {
                switch (value) {
                    case null:
                        EditorPrefs.DeleteKey(key);
                        break;
                    case int i:
                        EditorPrefs.SetInt(key, i);
                        break;
                    case string s:
                        EditorPrefs.SetString(key, s);
                        break;
                    case bool b:
                        EditorPrefs.SetBool(key, b);
                        break;
                }
            }

            _savedPrefs.Clear();

            if (_tempFolder != null) AssetDatabase.DeleteAsset(_tempFolder);
            _tempFolder = null;
        }

        /// <summary>
        /// Open a scene additively for the test (closed without saving afterwards).
        /// </summary>
        private Scene OpenScene(string path) {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            _openedScenes.Add(scene);
            return scene;
        }

        private static T FindInScene<T>(Scene scene, string objectName) where T : Component {
            return scene.GetRootGameObjects().Single(o => o.name == objectName).GetComponent<T>();
        }

        /// <summary>
        /// Set an EditorPrefs value for the test, restoring (or deleting) the user's value afterwards.
        /// </summary>
        private void SetPref(string key, object value) {
            if (!_savedPrefs.ContainsKey(key)) {
                _savedPrefs[key] = !EditorPrefs.HasKey(key) ? null :
                    value switch {
                        int => EditorPrefs.GetInt(key),
                        bool => EditorPrefs.GetBool(key),
                        _ => EditorPrefs.GetString(key),
                    };
            }

            switch (value) {
                case int i:
                    EditorPrefs.SetInt(key, i);
                    break;
                case bool b:
                    EditorPrefs.SetBool(key, b);
                    break;
                case string s:
                    EditorPrefs.SetString(key, s);
                    break;
            }
        }

        #region PersistedGameObjectEditor

        [Test]
        public void PersistedGameObjectEditor_DrawsWithoutErrors() {
            var persisted = CreateGameObject("Persisted").AddComponent<PersistedGameObject>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(persisted);
            try {
                Assert.AreEqual("PersistedGameObjectEditor", editor.GetType().Name);
                ImGuiHarness.Run(() => editor.OnInspectorGUI());
            } finally {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void PersistedGameObjectEditor_PrefabAsset_ClearsInstanceID() {
            string guid = AssetDatabase.CreateFolder("Assets", $"SeqTestTemp_{Guid.NewGuid():N}");
            _tempFolder = AssetDatabase.GUIDToAssetPath(guid);
            GameObject source = CreateGameObject("PrefabSource");
            var sourcePersisted = source.AddComponent<PersistedGameObject>();
            sourcePersisted.SetInstanceIDEditMode(123);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{_tempFolder}/Persisted.prefab");
            var persisted = prefab.GetComponent<PersistedGameObject>();
            Assert.AreEqual(123ul, persisted.InstanceID);

            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(persisted);
            try {
                ImGuiHarness.Run(() => editor.OnInspectorGUI());
            } finally {
                Object.DestroyImmediate(editor);
            }

            Assert.AreEqual(0ul, persisted.InstanceID, "Prefab assets shouldn't carry an instance ID.");
        }

        #endregion

        #region Edit-mode instance IDs

        private static void RunEditModeUpdate(PersistedGameObject persisted) {
            FieldInfo checkedField = typeof(PersistedGameObject).GetField("_hasCheckedId",
                                                                          BindingFlags.Instance |
                                                                          BindingFlags.NonPublic);
            checkedField!.SetValue(persisted, false);
            InvokeLifecycleInEditMode(persisted, "Update");
        }

        [Test]
        public void EditModeUpdate_SceneObjects_GetStableUniqueIDs() {
            Scene scene = OpenScene(TestScenes.PLevel);
            var crate = FindInScene<PersistedGameObject>(scene, TestScenes.StaticCrate);
            var ball = FindInScene<PersistedGameObject>(scene, TestScenes.StaticBall);

            RunEditModeUpdate(crate);
            RunEditModeUpdate(ball);
            ulong crateID = crate.InstanceID;

            Assert.AreNotEqual(0ul, crateID);
            Assert.AreNotEqual(0ul, ball.InstanceID);
            Assert.AreNotEqual(crateID, ball.InstanceID);

            RunEditModeUpdate(crate);
            Assert.AreEqual(crateID, crate.InstanceID, "The ID should be stable for the same object.");
        }

        [Test]
        public void EditModeUpdate_ZeroID_IsAssigned() {
            Scene scene = OpenScene(TestScenes.PLevel);
            var crate = FindInScene<PersistedGameObject>(scene, TestScenes.StaticCrate);
            crate.SetInstanceIDEditMode(0);

            InvokeLifecycleInEditMode(crate, "Update");

            Assert.AreNotEqual(0ul, crate.InstanceID);
        }

        #endregion

        #region LevelManifestEditor

        [Test]
        public void LevelManifestEditor_FixesRegionLevelIDsAndClearsDuplicates() {
            LevelManifestRegionEntry wrongLevel = CreateRegionEntry(1, 999, "Assets/Test/A.unity");
            LevelManifestRegionEntry shared = CreateRegionEntry(2, 10, "Assets/Test/B.unity");
            LevelManifestLevelEntry level1 = CreateLevelEntry(10, "Assets/Test/L10.unity", wrongLevel, shared);
            LevelManifestLevelEntry level2 = CreateLevelEntry(20, "Assets/Test/L20.unity", shared);
            LevelManifest manifest = CreateManifest(level1, level2, level1);

            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(manifest);
            try {
                Assert.AreEqual("LevelManifestEditor", editor.GetType().Name);
                ImGuiHarness.Run(() => editor.OnInspectorGUI());
            } finally {
                Object.DestroyImmediate(editor);
            }

            Assert.AreEqual(10, wrongLevel.LevelID, "A region's level ID should match the level that lists it.");
            CollectionAssert.AreEqual(new[] { level1, level2, null }, manifest.Levels,
                                      "The duplicate level entry should be cleared.");
            CollectionAssert.AreEqual(new LevelManifestRegionEntry[] { null }, level2.Regions,
                                      "A region already listed by another level should be cleared.");
        }

        #endregion

        #region PassEditingSceneStep

        [Test]
        public void PassEditingSceneStep_OpenLevel_PassesLevelAndOpenRegions() {
            LevelManifestRegionEntry open = CreateRegionEntry(98761, 98760, "Assets/Test/Open.unity");
            LevelManifestRegionEntry closed = CreateRegionEntry(98762, 98760, "Assets/Test/Closed.unity");
            CreateManifest(CreateLevelEntry(98760, "Assets/Test/EditedLevel.unity", open, closed));
            SetPref("OpenLevel", 98760);
            SetPref("OpenLevelRegions[98761].loaded", true);
            SetPref("OpenLevelRegions[98762].loaded", false);
            var args = new ExecutionStepArguments();

            CreateStep<PassEditingSceneStep>().Execute(args);

            Assert.AreEqual("Assets/Test/EditedLevel.unity",
                            LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));
            CollectionAssert.AreEqual(new[] { 98761 }, LoadRegionsStep.ParamRegionsToLoad.GetOrDefault(args, null));
        }

        [Test]
        public void PassEditingSceneStep_NoLevel_PassesActiveSceneIfInBuildSettings() {
            if (EditorBuildSettings.scenes.Length < 2) Assert.Ignore("Needs at least two scenes in the build settings.");
            CreateManifest();
            SetPref("OpenLevel", -1);

            string sceneInBuild = EditorBuildSettings.scenes[1].path;
            SetPref("ActiveScene", sceneInBuild);
            var args = new ExecutionStepArguments();
            CreateStep<PassEditingSceneStep>().Execute(args);
            Assert.AreEqual(sceneInBuild, LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));

            SetPref("ActiveScene", EditorBuildSettings.scenes[0].path);
            args = new ExecutionStepArguments();
            CreateStep<PassEditingSceneStep>().Execute(args);
            Assert.IsFalse(LoadSceneOrLevelStep.ParamSceneToLoad.Get(args, out _),
                           "The boot scene itself isn't passed on.");
        }

        #endregion
    }
}
