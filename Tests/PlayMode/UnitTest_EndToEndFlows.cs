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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Whole save/load flows, driven through a PersistedSceneController whose sequencers are built from the real
    /// steps in the same order as Astral's InitialScene: new game, autosave, quit to menu, load most recent,
    /// quicksave/quickload, level transitions, region streaming, and damaged or deleted slots.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_EndToEndFlows : PersistenceSceneFixture {
        private PersistedSceneController _controller;
        private SaveStateManager _saves;
        private RecordingStep _loadStateDone;
        private RecordingStep _loadProfileDone;
        private RecordingStep _goToSceneDone;
        private RecordingStep _mainMenuDone;

        [SetUp]
        public void SetUpFlows() {
            _saves = CreateSingleton<SaveStateManager>();

            ExecutionStepSequencer preLoading = CreateSequencer("PreLoadingSubsequence",
                                                                CreateStep<UnloadScenesStep>(),
                                                                CreateStep<WaitForScenesToUnloadStep>());

            LoadSceneOrLevelStep loadSceneStep = CreateStep<LoadSceneOrLevelStep>();
            SetField(loadSceneStep, "_enableImmediately", true);
            LoadRegionsStep loadRegionsStep = CreateStep<LoadRegionsStep>();
            SetField(loadRegionsStep, "_enableImmediately", true);
            ExecutionStepSequencer loadScene = CreateSequencer("LoadSceneSubsequence",
                                                               CreateStep<UnloadScenesStep>(), loadSceneStep,
                                                               loadRegionsStep,
                                                               CreateStep<WaitForScenesToUnloadStep>(),
                                                               CreateStep<InitializeObjectsStep>());

            PassSavedSceneStep passSavedScene = CreateStep<PassSavedSceneStep>();
            SetField(passSavedScene, "_defaultScene", CreateSceneRef(TestScenes.PLevel));
            ExecutionStepSequencer postLoadState = CreateSequencer("PostLoadStateSubsequence", passSavedScene,
                                                                   External(loadScene),
                                                                   CreateStep<SetCurrentSavedSceneToLoadingSceneStep>(),
                                                                   CreateStep<SaveStep>(),
                                                                   CreateStep<AutoSaveStep>());

            _loadStateDone = CreateStep<RecordingStep>("LoadStateDone");
            ExecutionStepSequencer loadState = CreateSequencer("LoadStateSequence", External(preLoading),
                                                               CreateStep<LoadStateSaveDataStep>(),
                                                               External(postLoadState), _loadStateDone);

            _loadProfileDone = CreateStep<RecordingStep>("LoadProfileDone");
            ExecutionStepSequencer loadProfile = CreateSequencer("LoadProfileSequence", External(preLoading),
                                                                 CreateStep<LoadProfileSaveDataStep>(),
                                                                 CreateStep<LoadStateSaveDataStep>(),
                                                                 External(postLoadState), _loadProfileDone);

            _goToSceneDone = CreateStep<RecordingStep>("GoToSceneDone");
            ExecutionStepSequencer goToScene = CreateSequencer("GoToSceneSequence", External(preLoading),
                                                               External(loadScene),
                                                               CreateStep<SetCurrentSavedSceneToLoadingSceneStep>(),
                                                               CreateStep<SaveStep>(), _goToSceneDone);

            _mainMenuDone = CreateStep<RecordingStep>("MainMenuDone");
            ExecutionStepSequencer mainMenu = CreateSequencer("GoToMainMenuSequence", External(preLoading),
                                                              CreateStep<UnloadProfileSaveDataStep>(),
                                                              External(loadScene), _mainMenuDone);

            GameObject controllerObject = CreateGameObject("Scene controller", false);
            _controller = controllerObject.AddComponent<PersistedSceneController>();
            SetField(_controller, "_mainMenuScene", CreateSceneRef(TestScenes.Plain));
            SetField(_controller, "_goToSceneSequencer", goToScene);
            SetField(_controller, "_goToMainMenuSequencer", mainMenu);
            SetField(_controller, "_loadStateSequencer", loadState);
            SetField(_controller, "_loadProfileSequencer", loadProfile);
            controllerObject.SetActive(true);
            SimpleSceneController.ResetStaticState();
            Assert.AreSame(_controller, PersistedSceneController.Instance);
        }

        private ExternalSequencerStep External(ExecutionStepSequencer sequencer) {
            ExternalSequencerStep step = CreateStep<ExternalSequencerStep>($"External {sequencer.name}");
            SetField(step, "_sequencer", sequencer);
            return step;
        }

        /// <summary>
        /// Start a flow and wait until the sequence's final step has run.
        /// </summary>
        private static async UniTask Run(Action start, RecordingStep done, string description) {
            int before = done.Calls.Count;
            start();
            await WaitUntilOrFail(() => done.Calls.Count > before, description, 20);
            await Frames(2);
        }

        private UniTask NewGame() => Run(() => _controller.LoadState(null), _loadStateDone, "new game");
        private UniTask QuitToMenu() => Run(_controller.GoToMainMenu, _mainMenuDone, "quit to main menu");

        private UniTask LoadMostRecent() =>
            Run(_controller.LoadMostRecentProfile, _loadProfileDone, "load most recent profile");

        private UniTask GoToScene(string scenePath) =>
            Run(() => _controller.GoToScene(scenePath), _goToSceneDone, $"go to {scenePath}");

        private async UniTask LoadRegion(LevelManifestRegionEntry entry, string scenePath) {
            Assert.IsTrue(LevelRoot.Current.LoadRegion(entry));
            await WaitUntilOrFail(() => GetRegion(scenePath) is { ObjectsLoaded: true }, $"loading {scenePath}");
        }

        private static Vector3 PositionOf(string objectName) => GameObject.Find(objectName).transform.position;

        #region Flows

        [Test]
        public async Task NewGame_AutoSave_QuitToMenu_LoadMostRecent_RestoresProgress() {
            await NewGame();
            Assert.AreEqual("Autosave_0", Manager.LoadedStateData.StateName, "A new game autosaves once loaded.");
            Assert.IsTrue(PersistedLevelRoot.Current.ObjectsLoaded);

            Spawnable.Despawn(GameObject.Find(TestScenes.StaticDestroyable));
            GameObject.Find(TestScenes.StaticCrate).transform.position = new Vector3(5, 0, 0);
            _saves.AutoSave();

            await QuitToMenu();
            Assert.IsNull(LevelRoot.Current);
            Assert.IsNull(Manager.LoadedProfileData);
            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));

            await LoadMostRecent();

            Assert.AreEqual("Autosave_1", Manager.LoadedStateData.StateName);
            Assert.AreEqual(SceneStateType.Unloaded, StateOf(TestScenes.Plain));
            Assert.AreEqual(new Vector3(5, 0, 0), PositionOf(TestScenes.StaticCrate));
            Assert.IsNull(GameObject.Find(TestScenes.StaticDestroyable));
        }

        [Test]
        public async Task QuickLoadThenQuickSave_KeepsEarlierChangesInOtherRegions() {
            await NewGame();
            await LoadRegion(PRegionAEntry, TestScenes.PRegionA);
            await LoadRegion(PRegionBEntry, TestScenes.PRegionB);
            GameObject.Find(TestScenes.RegionACrate).transform.position = new Vector3(0, 1, 10);
            _saves.QuickSave();

            await Run(() => Assert.IsTrue(_saves.QuickLoad()), _loadStateDone, "first quickload");
            Assert.AreEqual(new Vector3(0, 1, 10), PositionOf(TestScenes.RegionACrate));

            GameObject.Find(TestScenes.RegionBCrate).transform.position = new Vector3(0, 1, 20);
            _saves.QuickSave();
            await Run(() => Assert.IsTrue(_saves.QuickLoad()), _loadStateDone, "second quickload");

            Assert.AreEqual(new Vector3(0, 1, 10), PositionOf(TestScenes.RegionACrate),
                            "The second quicksave lost region A's change from the first.");
            Assert.AreEqual(new Vector3(0, 1, 20), PositionOf(TestScenes.RegionBCrate));
        }

        [Test]
        public async Task GoToOtherLevelAndBack_KeepsSavedProgressAndDropsUnsavedChanges() {
            await NewGame();
            GameObject.Find(TestScenes.StaticCrate).transform.position = new Vector3(5, 0, 0);
            _saves.AutoSave();
            string slot = Manager.LoadedStateData.StateName;
            GameObject.Find(TestScenes.StaticCrate).transform.position = new Vector3(6, 0, 0);

            await GoToScene(TestScenes.Level2);
            Assert.AreEqual(TestScenes.Level2ID, LevelRoot.Current.LevelIndex);
            Assert.AreEqual(TestScenes.Name(TestScenes.Level2), Manager.GetStateData(slot).CurrentScene,
                            "Going to a scene saves the state's current scene.");

            await GoToScene(TestScenes.PLevel);

            Assert.AreEqual(new Vector3(5, 0, 0), PositionOf(TestScenes.StaticCrate),
                            "Saved progress should survive the round trip; unsaved changes are dropped by design.");
            Assert.AreEqual(TestScenes.Name(TestScenes.PLevel), Manager.GetStateData(slot).CurrentScene);
        }

        [Test]
        public async Task StreamedOutRegion_KeepsChangesAcrossQuitAndLoad() {
            await NewGame();
            await LoadRegion(PRegionAEntry, TestScenes.PRegionA);
            GameObject.Find(TestScenes.RegionACrate).transform.position = new Vector3(0, 3, 10);
            await AwaitOrFail(LevelRoot.Current.UnloadRegion(PRegionAEntry).WaitForCompletionTask(),
                              "unloading region A");
            _saves.AutoSave();

            await QuitToMenu();
            await LoadMostRecent();

            Assert.IsNull(GetRegion(TestScenes.PRegionA), "Region A was unloaded when the game was saved.");
            await LoadRegion(PRegionAEntry, TestScenes.PRegionA);
            Assert.AreEqual(new Vector3(0, 3, 10), PositionOf(TestScenes.RegionACrate));
        }

        [Test]
        public async Task SavedRegions_AreReloadedWithTheLevel() {
            await NewGame();
            await LoadRegion(PRegionBEntry, TestScenes.PRegionB);
            _saves.AutoSave();

            await QuitToMenu();
            await LoadMostRecent();

            Assert.IsNotNull(GetRegion(TestScenes.PRegionB), "Region B was loaded when the game was saved.");
            Assert.IsNull(GetRegion(TestScenes.PRegionA));
        }

        [Test]
        public async Task DeletedSlot_LoadMostRecentFallsBackToPreviousSave() {
            await NewGame();
            string previousSlot = Manager.LoadedStateData.StateName;
            Spawnable.Despawn(GameObject.Find(TestScenes.StaticDestroyable));
            _saves.AutoSave();
            string slot = Manager.LoadedStateData.StateName;

            Manager.DeleteStateData(state: slot);
            CollectionAssert.DoesNotContain(Manager.GetAvailableStates().ToList(), slot);

            await QuitToMenu();
            await LoadMostRecent();

            Assert.AreEqual(previousSlot, Manager.LoadedStateData.StateName,
                            "Load most recent should fall back to the newest remaining save.");
            Assert.IsNotNull(GameObject.Find(TestScenes.StaticDestroyable),
                             "Progress from the deleted slot came back.");
        }

        [Test]
        public async Task CorruptSlot_LoadsDefaultsInsteadOfHanging() {
            await NewGame();
            Spawnable.Despawn(GameObject.Find(TestScenes.StaticDestroyable));
            _saves.AutoSave();
            string slot = Manager.LoadedStateData.StateName;
            await QuitToMenu();

            FileSaveDataHandler handler = Manager.GetComponent<FileSaveDataHandler>();
            File.WriteAllText(handler.GetStateDataFile(Profile, slot), "{ corrupt");
            File.WriteAllText(handler.GetLevelDataFile(Profile, slot, TestScenes.PLevelID), "{ corrupt");

            LogAssert.ignoreFailingMessages = true;
            await LoadMostRecent();

            Assert.IsNotNull(PersistedLevelRoot.Current, "The default level should load.");
            Assert.IsTrue(PersistedLevelRoot.Current.ObjectsLoaded);
            Assert.IsNotNull(GameObject.Find(TestScenes.StaticDestroyable), "Corrupt level data loads as defaults.");
        }

        #endregion
    }
}
