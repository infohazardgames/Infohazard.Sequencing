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

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Scene states, groups, activation, cancellation and unloading in SceneLoadingManager.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_SceneLoadingManager : SceneTestFixture {
        #region Loading

        [Test]
        public async Task LoadScene_GoesFromLoadingToLoaded() {
            Assert.AreEqual(SceneStateType.Unloaded, StateOf(TestScenes.Plain));

            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, true, false, null);

            Assert.IsTrue(ops.IsValid);
            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Plain));
            Assert.IsTrue(Loader.IsLoadingAnyScenes());

            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "loading");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
            Assert.IsFalse(Loader.IsLoadingAnyScenes());
            Assert.AreEqual(1, LoadedCount(TestScenes.Plain));
            Assert.IsNotNull(GameObject.Find(TestScenes.PlainMarker));
        }

        [Test]
        public async Task LoadScene_ByName_Works() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Name(TestScenes.Plain), true, false, null);
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "loading");
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
        }

        [Test]
        public async Task LoadScene_DetectsSceneTypeFromManifest() {
            await LoadAndWait(TestScenes.Level);
            await LoadAndWait(TestScenes.RegionA);
            await LoadAndWait(TestScenes.Plain);

            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.Level), out LevelRoot level, out RegionRoot noRegion);
            Assert.IsNotNull(level);
            Assert.IsNull(noRegion);
            Assert.AreSame(level, LevelRoot.Current);

            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.RegionA), out LevelRoot noLevel, out RegionRoot region);
            Assert.IsNull(noLevel);
            Assert.IsNotNull(region);
            Assert.AreEqual(TestScenes.RegionAID, region.RegionIndex);

            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.Plain), out noLevel, out noRegion);
            Assert.IsNull(noLevel);
            Assert.IsNull(noRegion);
        }

        [Test]
        public async Task LoadScene_AlreadyLoaded_ReturnsInvalid() {
            await LoadAndWait(TestScenes.Plain);

            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, true, false, null);

            Assert.IsFalse(ops.IsValid);
            await Frames();
            Assert.AreEqual(1, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task LoadScene_WhileLoading_RejoinsSameOperation() {
            SceneLoadOperations first = Loader.LoadScene(TestScenes.Plain, true, false, null);
            SceneLoadOperations second = Loader.LoadScene(TestScenes.Plain, true, false, null);

            Assert.AreSame(first.FullOperation, second.FullOperation);
            await AwaitOrFail(first.FullOperation.WaitForCompletionTask(), "loading");
            Assert.AreEqual(1, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task LoadScene_TwoWaiters_BothComplete() {
            // Suspected bug #26: WaitForCompletionTask returns the same single-use UniTask to every caller.
            SceneLoadOperations first = Loader.LoadScene(TestScenes.Plain, true, false, null);
            SceneLoadOperations second = Loader.LoadScene(TestScenes.Plain, true, false, null);

            UniTask a = first.FullOperation.WaitForCompletionTask();
            UniTask b = second.FullOperation.WaitForCompletionTask();

            await AwaitOrFail(UniTask.WhenAll(a, b), "both waiters");
        }

        [Test]
        public async Task LoadScene_SetActiveScene_MakesItActive() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, true, true, null);
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "loading");
            Assert.AreEqual(TestScenes.Plain, SceneManager.GetActiveScene().path);
        }

        [Test]
        public async Task LoadScene_MissingScene_ReturnsInvalid() {
            LogAssert.ignoreFailingMessages = true;
            SceneLoadOperations ops = default;
            Assert.DoesNotThrow(() => ops = Loader.LoadScene("SeqTest_DoesNotExist", true, false, null));
            Assert.IsFalse(ops.IsValid);
            await Frames();
            Assert.AreEqual(SceneStateType.Unloaded, Loader.GetSceneLoadedState("SeqTest_DoesNotExist", out _, out _));
        }

        [Test]
        public async Task AllScenesFinishedLoading_FiresWhenLastLoadCompletes() {
            int finished = 0;
            Loader.AllScenesFinishedLoading += () => finished++;

            SceneLoadOperations ops = Loader.LoadScenes(new[] { TestScenes.Plain, TestScenes.Level2 }, true);
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "loading both");
            await Frames(2);

            Assert.AreEqual(1, finished);
        }

        [Test]
        public async Task LoadScenes_CombinesProgressOfAllScenes() {
            SceneLoadOperations ops = Loader.LoadScenes(new[] { TestScenes.Plain, TestScenes.Level2 }, true);

            Assert.IsInstanceOf<MultiProgressSource>(ops.FullOperation);
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "loading both");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Level2));
            Assert.That(ops.FullOperation.Progress, Is.EqualTo(1).Within(1e-4f));
        }

        [Test]
        public void LoadScenes_NothingToLoad_ReturnsInvalid() {
            Assert.IsFalse(Loader.LoadScenes(new string[0], true).IsValid);
        }

        #endregion

        #region Activation

        [Test]
        public async Task WithoutAutoActivate_StopsAtPartialUntilActivated() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, false, false, null);

            await AwaitOrFail(ops.PartialOperation.WaitForCompletionTask(), "partial load");
            Assert.That(ops.PartialOperation.Progress, Is.GreaterThanOrEqualTo(1));
            await Frames();
            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Plain));
            Assert.IsTrue(Loader.IsLoadingAnyScenes());

            Loader.ActivateLoadingScenes();

            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "full load");
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
        }

        [Test]
        public async Task ActivateLoadingScenes_OnlyActivatesRequestedTypes() {
            // The level is queued first: a scene waiting for activation blocks the ones queued after it.
            SceneLoadOperations level = Loader.LoadScene(TestScenes.Level2, false, false, null);
            SceneLoadOperations plain = Loader.LoadScene(TestScenes.Plain, false, false, null);
            await AwaitOrFail(level.PartialOperation.WaitForCompletionTask(), "level partial load");

            Loader.ActivateLoadingScenes(typesToActivate: SceneLoadingType.Level);

            await AwaitOrFail(level.FullOperation.WaitForCompletionTask(), "level full load");
            Assert.IsFalse(Loader.IsLoadingAnyScenes(SceneLoadingType.Level));
            await Frames();
            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Plain));
            Assert.IsTrue(Loader.IsLoadingAnyScenes(SceneLoadingType.Scene));
        }

        [Test]
        public async Task ActivateLoadingScenes_OnlyActivatesRequestedGroup() {
            SceneGroup other = CreateSceneGroup("Other");
            SceneLoadOperations inDefault = Loader.LoadScene(TestScenes.Level2, false, false, null);
            Loader.LoadScene(TestScenes.Plain, false, false, other);
            await AwaitOrFail(inDefault.PartialOperation.WaitForCompletionTask(), "partial load");

            Loader.ActivateLoadingScenes(other);
            await Frames(10);

            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Level2),
                            "Activating another group shouldn't activate the default group.");
        }

        #endregion

        #region Groups

        [Test]
        public async Task LoadScene_LoadingInAnotherGroup_DoesNotLoadTwice() {
            // Suspected bug #14: only the requested group is searched for an in-progress load.
            SceneGroup other = CreateSceneGroup("Other");
            SceneLoadOperations first = Loader.LoadScene(TestScenes.Plain, false, false, null);
            SceneLoadOperations second = Loader.LoadScene(TestScenes.Plain, false, false, other);

            Loader.ActivateLoadingScenes();
            Loader.ActivateLoadingScenes(other);
            await AwaitOrFail(first.FullOperation.WaitForCompletionTask(), "first load");
            if (second.IsValid && !ReferenceEquals(second.FullOperation, first.FullOperation)) {
                await AwaitOrFail(second.FullOperation.WaitForCompletionTask(), "second load");
            }

            Assert.AreEqual(1, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task UnloadScenes_OnlyUnloadsThatGroup() {
            SceneGroup other = CreateSceneGroup("Other");
            await LoadAndWait(TestScenes.Plain, other);
            await LoadAndWait(TestScenes.Level2);

            Loader.UnloadScenes(other);
            await WaitUntilOrFail(() => !Loader.IsUnloadingAnyScenes, "unloading");

            Assert.AreEqual(SceneStateType.Unloaded, StateOf(TestScenes.Plain));
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Level2));
            Assert.AreEqual(0, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task UnloadScenes_CancelsLoadsInThatGroup() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, false, false, null);
            await AwaitOrFail(ops.PartialOperation.WaitForCompletionTask(), "partial load");

            Loader.UnloadScenes();
            Assert.AreEqual(SceneStateType.Cancelled, StateOf(TestScenes.Plain));

            Loader.ActivateLoadingScenes();
            await WaitUntilOrFail(() => StateOf(TestScenes.Plain) == SceneStateType.Unloaded, "cancelled scene unloads");
            Assert.AreEqual(0, LoadedCount(TestScenes.Plain));
        }

        #endregion

        #region Unloading and cancelling

        [Test]
        public async Task UnloadScene_Loaded_GoesThroughUnloadingToUnloaded() {
            await LoadAndWait(TestScenes.Plain);

            IProgressSource op = Loader.UnloadScene(TestScenes.Name(TestScenes.Plain));

            Assert.IsNotNull(op);
            Assert.AreEqual(SceneStateType.Unloading, StateOf(TestScenes.Plain));
            Assert.IsTrue(Loader.IsUnloadingAnyScenes);
            await AwaitOrFail(op.WaitForCompletionTask(), "unloading");
            Assert.AreEqual(SceneStateType.Unloaded, StateOf(TestScenes.Plain));
            Assert.IsFalse(Loader.IsUnloadingAnyScenes);
            Assert.AreEqual(0, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task UnloadScene_LevelScene_CleansUpLevel() {
            await LoadAndWait(TestScenes.Level2);
            Assert.IsNotNull(LevelRoot.Current);

            await UnloadAndWait(TestScenes.Level2);

            Assert.IsNull(LevelRoot.Current);
        }

        [Test]
        public void UnloadScene_NotLoaded_ReturnsNull() {
            Assert.IsNull(Loader.UnloadScene(TestScenes.Name(TestScenes.Plain)));
            Assert.IsNull(Loader.UnloadScene("SeqTest_DoesNotExist"));
        }

        [Test]
        public async Task UnloadScene_WhileUnloading_ReturnsNull() {
            await LoadAndWait(TestScenes.Plain);
            IProgressSource first = Loader.UnloadScene(TestScenes.Name(TestScenes.Plain));

            Assert.IsNull(Loader.UnloadScene(TestScenes.Name(TestScenes.Plain)));
            await AwaitOrFail(first.WaitForCompletionTask(), "unloading");
        }

        [Test]
        public async Task UnloadScene_WhileLoading_CancelsAndUnloadsWhenDone() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, true, false, null);

            IProgressSource cancel = Loader.UnloadScene(TestScenes.Name(TestScenes.Plain));

            Assert.IsNotNull(cancel);
            Assert.AreEqual(SceneStateType.Cancelled, StateOf(TestScenes.Plain));
            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "cancelled load");
            await WaitUntilOrFail(() => StateOf(TestScenes.Plain) == SceneStateType.Unloaded, "unloaded");
            Assert.AreEqual(0, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task LoadScene_AfterCancel_RevivesLoad() {
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.Plain, false, false, null);
            Loader.UnloadScene(TestScenes.Name(TestScenes.Plain));

            SceneLoadOperations revived = Loader.LoadScene(TestScenes.Plain, true, false, null);

            Assert.AreSame(ops.FullOperation, revived.FullOperation);
            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Plain));
            await AwaitOrFail(revived.FullOperation.WaitForCompletionTask(), "revived load");
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
            Assert.AreEqual(1, LoadedCount(TestScenes.Plain));
        }

        [Test]
        public async Task CancelDuringInitialization_UndoesRegionRegistration() {
            // Suspected bug #10: a scene cancelled while its root initializes is unloaded without Cleanup.
            await LoadAndWait(TestScenes.Level);
            LevelRoot level = LevelRoot.Current;
            TestGatedRegionRoot.Gate = new UniTaskCompletionSource();

            SceneLoadOperations ops = Loader.LoadScene(TestScenes.RegionGated, true, false, null);
            await WaitUntilOrFail(() => TestGatedRegionRoot.InitializeStarted, "region initialization starts");
            Loader.UnloadScene(TestScenes.Name(TestScenes.RegionGated));
            TestGatedRegionRoot.Gate.TrySetResult();

            await AwaitOrFail(ops.FullOperation.WaitForCompletionTask(), "cancelled load");
            await WaitUntilOrFail(() => StateOf(TestScenes.RegionGated) == SceneStateType.Unloaded, "unloaded");

            Assert.IsFalse(level.LoadedRegions.ContainsKey(TestScenes.RegionGatedID),
                           "The cancelled region is still registered with the level.");
        }

        #endregion

        #region Already-loaded scenes

        [Test]
        public async Task InitializeLoadedScene_RegistersSceneLoadedOutsideManager() {
            AsyncOperation op = SceneManager.LoadSceneAsync(TestScenes.Level2, LoadSceneMode.Additive);
            await op;
            Scene scene = SceneManager.GetSceneByPath(TestScenes.Level2);

            await AwaitOrFail(Loader.InitializeLoadedScene(scene, SceneLoadingType.Level), "initializing");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Level2));
            Assert.IsNotNull(LevelRoot.Current);

            Loader.UnloadScenes();
            await WaitUntilOrFail(() => !Loader.IsUnloadingAnyScenes, "unloading");
            Assert.IsNull(LevelRoot.Current);
            Assert.AreEqual(0, LoadedCount(TestScenes.Level2));
        }

        #endregion
    }
}
