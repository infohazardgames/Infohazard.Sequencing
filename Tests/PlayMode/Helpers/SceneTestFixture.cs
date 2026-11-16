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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Base fixture for play mode tests that load the test scenes (<see cref="TestScenes"/>) through a
    /// SceneLoadingManager. After each test it finishes or cancels pending loads, unloads every test scene, and
    /// only then runs the leak check.
    /// </summary>
    /// <remarks>
    /// Derived fixtures need <c>[PrebuildSetup(typeof(SequencingTestScenesSetup))]</c> and
    /// <c>[PostBuildCleanup(typeof(SequencingTestScenesSetup))]</c> so the scenes are in the build settings.
    /// </remarks>
    public abstract class SceneTestFixture : SequencingTestFixture {
        private readonly List<SceneGroup> _groups = new();

        protected override bool DefersTearDown => true;

        protected SceneLoadingManager Loader { get; private set; }
        protected SceneGroup DefaultGroup { get; private set; }

        protected LevelManifestLevelEntry LevelEntry { get; private set; }
        protected LevelManifestLevelEntry Level2Entry { get; private set; }
        protected LevelManifestRegionEntry RegionAEntry { get; private set; }
        protected LevelManifestRegionEntry RegionBEntry { get; private set; }
        protected LevelManifestRegionEntry RegionGatedEntry { get; private set; }
        protected LevelManifestLevelEntry PLevelEntry { get; private set; }
        protected LevelManifestRegionEntry PRegionAEntry { get; private set; }
        protected LevelManifestRegionEntry PRegionBEntry { get; private set; }

        [SetUp]
        public void SceneTestFixtureSetUp() {
#if UNITY_EDITOR
            LevelEntry = LoadEntry<LevelManifestLevelEntry>(TestScenes.Level);
            Level2Entry = LoadEntry<LevelManifestLevelEntry>(TestScenes.Level2);
            RegionAEntry = LoadEntry<LevelManifestRegionEntry>(TestScenes.RegionA);
            RegionBEntry = LoadEntry<LevelManifestRegionEntry>(TestScenes.RegionB);
            RegionGatedEntry = LoadEntry<LevelManifestRegionEntry>(TestScenes.RegionGated);
            PLevelEntry = LoadEntry<LevelManifestLevelEntry>(TestScenes.PLevel);
            PRegionAEntry = LoadEntry<LevelManifestRegionEntry>(TestScenes.PRegionA);
            PRegionBEntry = LoadEntry<LevelManifestRegionEntry>(TestScenes.PRegionB);
#else
            Assert.Ignore("The scene tests need the Editor to load the test manifest entries.");
#endif
            CreateManifest(LevelEntry, Level2Entry, PLevelEntry);
            TestGatedRegionRoot.ResetStaticState();

            DefaultGroup = CreateSceneGroup("Default group");
            Loader = CreateSingleton<SceneLoadingManager>(m => SetField(m, "_defaultGroup", DefaultGroup));
        }

        [UnityTearDown]
        public IEnumerator SceneTestFixtureTearDown() {
            yield return CleanUpScenesAsync().ToCoroutine();
            yield return CleanUpAfterScenesAsync().ToCoroutine();
            _groups.Clear();
            FinishTest();
        }

#if UNITY_EDITOR
        private static T LoadEntry<T>(string scenePath) where T : ScriptableObject {
            T entry = AssetDatabase.LoadAssetAtPath<T>(TestScenes.EntryPath(scenePath));
            Assert.IsNotNull(entry, $"Missing test manifest entry for {scenePath}. Run SequencingTestSceneGenerator.");
            return entry;
        }
#endif

        /// <summary>
        /// Create a scene group that cleanup knows about.
        /// </summary>
        protected SceneGroup CreateSceneGroup(string name) {
            SceneGroup group = CreateAsset<SceneGroup>(name);
            _groups.Add(group);
            return group;
        }

        /// <summary>
        /// Extra asynchronous cleanup for derived fixtures, run after the scenes are unloaded and before the leak
        /// check.
        /// </summary>
        protected virtual UniTask CleanUpAfterScenesAsync() => UniTask.CompletedTask;

        private async UniTask CleanUpScenesAsync() {
            TestGatedRegionRoot.Gate?.TrySetResult();

            if (Loader) {
                // Loads that are waiting for activation block every later scene operation.
                foreach (SceneGroup group in _groups) Loader.ActivateLoadingScenes(group);
                await WaitUntil(() => !Loader || !Loader.IsLoadingAnyScenes());

                if (Loader) {
                    foreach (SceneGroup group in _groups) Loader.UnloadScenes(group);
                    await WaitUntil(() => !Loader || !Loader.IsUnloadingAnyScenes);
                }
            }

            // Anything still loaded (for example after a test broke the manager's bookkeeping).
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!TestScenes.AllScenes.Contains(scene.path) || !scene.isLoaded) continue;
                AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                if (op != null) await op;
            }

            TestGatedRegionRoot.ResetStaticState();
        }

        #region Waiting

        /// <summary>
        /// Wait (real time) until a condition holds, returning false on timeout.
        /// </summary>
        protected static async UniTask<bool> WaitUntil(Func<bool> condition, float timeoutSeconds = 10) {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition()) {
                if (Time.realtimeSinceStartup > end) return false;
                await UniTask.Yield();
            }

            return true;
        }

        /// <summary>
        /// Wait (real time) until a condition holds, failing the test on timeout.
        /// </summary>
        protected static async UniTask WaitUntilOrFail(Func<bool> condition, string message,
                                                       float timeoutSeconds = 10) {
            if (!await WaitUntil(condition, timeoutSeconds)) Assert.Fail($"Timed out: {message}");
        }

        /// <summary>
        /// Await a task, failing the test if it doesn't finish in time.
        /// </summary>
        protected static async UniTask AwaitOrFail(UniTask task, string message, float timeoutSeconds = 10) {
            UniTask.Awaiter awaiter = task.GetAwaiter();
            await WaitUntilOrFail(() => awaiter.IsCompleted, message, timeoutSeconds);
            awaiter.GetResult();
        }

        /// <summary>
        /// Wait a few frames, for checking that something doesn't happen.
        /// </summary>
        protected static async UniTask Frames(int count = 5) {
            for (int i = 0; i < count; i++) await UniTask.Yield();
        }

        #endregion

        #region Scene helpers

        protected SceneStateType StateOf(string scenePath) =>
            Loader.GetSceneLoadedState(TestScenes.Name(scenePath), out _, out _);

        protected static int LoadedCount(string scenePath) {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == scenePath && scene.isLoaded) count++;
            }

            return count;
        }

        /// <summary>
        /// Load a scene (activating immediately) and wait until it is fully loaded and initialized.
        /// </summary>
        protected async UniTask LoadAndWait(string scenePath, SceneGroup group = null) {
            SceneLoadOperations ops = Loader.LoadScene(scenePath, true, false, group);
            Assert.IsTrue(ops.IsValid, $"Loading {scenePath} didn't start.");
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), $"loading {scenePath}");
            Assert.AreEqual(SceneStateType.Loaded, StateOf(scenePath));
        }

        /// <summary>
        /// Unload a scene and wait until it is gone.
        /// </summary>
        protected async UniTask UnloadAndWait(string scenePath) {
            IProgressSource op = Loader.UnloadScene(TestScenes.Name(scenePath));
            Assert.IsNotNull(op, $"Unloading {scenePath} didn't start.");
            await AwaitOrFail(op.WaitForCompletionTask(), $"unloading {scenePath}");
        }

        #endregion
    }
}
