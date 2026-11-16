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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// The execution steps that load, activate, wait for and unload scenes.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_SceneSteps : SceneTestFixture {
        private LoadSceneOrLevelStep CreateLoadSceneStep(bool enableImmediately = true, bool waitToFinish = true,
                                                          bool makeActive = false, SceneGroup group = null) {
            LoadSceneOrLevelStep step = CreateStep<LoadSceneOrLevelStep>();
            SetField(step, "_enableImmediately", enableImmediately);
            SetField(step, "_waitToFinish", waitToFinish);
            SetField(step, "_makeActiveScene", makeActive);
            SetField(step, "_sceneGroup", group);
            return step;
        }

        private LoadRegionsStep CreateLoadRegionsStep(bool enableImmediately = true, bool waitToFinish = true) {
            LoadRegionsStep step = CreateStep<LoadRegionsStep>();
            SetField(step, "_enableImmediately", enableImmediately);
            SetField(step, "_waitToFinish", waitToFinish);
            return step;
        }

        #region LoadSceneOrLevelStep

        [Test]
        public async Task LoadSceneOrLevel_Level_LoadsAndPassesLevelEntry() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Level);

            await AwaitOrFail(CreateLoadSceneStep().Execute(args), "step");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Level));
            Assert.AreSame(LevelEntry, LoadRegionsStep.ParamLoadingLevel.GetOrDefault(args, null));
        }

        [Test]
        public async Task LoadSceneOrLevel_PlainScene_PassesNullLevel() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Plain);

            await AwaitOrFail(CreateLoadSceneStep().Execute(args), "step");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
            Assert.IsTrue(LoadRegionsStep.ParamLoadingLevel.Get(args, out LevelManifestLevelEntry level));
            Assert.IsNull(level);
        }

        [Test]
        public async Task LoadSceneOrLevel_NoParameter_UsesDefaultScene() {
            LoadSceneOrLevelStep step = CreateLoadSceneStep();
            SetField(step, "_defaultSceneToLoad", CreateSceneRef(TestScenes.Plain));

            await AwaitOrFail(step.Execute(new ExecutionStepArguments()), "step");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
        }

        [Test]
        public async Task LoadSceneOrLevel_WithoutEnableImmediately_FinishesAtPartialLoad() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Plain);

            await AwaitOrFail(CreateLoadSceneStep(enableImmediately: false).Execute(args), "step");

            Assert.AreEqual(SceneStateType.Loading, StateOf(TestScenes.Plain), "The scene shouldn't be activated yet.");
        }

        [Test]
        public async Task LoadSceneOrLevel_MakeActiveScene_SetsActiveScene() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Plain);

            await AwaitOrFail(CreateLoadSceneStep(makeActive: true).Execute(args), "step");

            Assert.AreEqual(TestScenes.Plain, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        }

        [Test]
        public async Task LoadSceneOrLevel_AlreadyLoaded_LogsFailure() {
            await LoadAndWait(TestScenes.Plain);
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Plain);

            LogAssert.Expect(LogType.Error, new Regex("Failed to load scene"));
            await AwaitOrFail(CreateLoadSceneStep().Execute(args), "step");
        }

        #endregion

        #region LoadRegionsStep

        [Test]
        public async Task LoadRegions_DefaultRegions_LoadsInitialRegions() {
            await LoadAndWait(TestScenes.Level);
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadRegionsStep.ParamLoadingLevel.Set(args, LevelEntry);

            await AwaitOrFail(CreateLoadRegionsStep().Execute(args), "step");

            CollectionAssert.AreEquivalent(new[] { TestScenes.RegionBID }, LevelRoot.Current.LoadedRegions.Keys);
        }

        [Test]
        public async Task LoadRegions_PassedRegions_LoadsThoseRegions() {
            await LoadAndWait(TestScenes.Level);
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadRegionsStep.ParamLoadingLevel.Set(args, LevelEntry);
            LoadRegionsStep.ParamRegionsToLoad.Set(args, new[] { TestScenes.RegionAID, TestScenes.RegionBID });

            await AwaitOrFail(CreateLoadRegionsStep().Execute(args), "step");

            CollectionAssert.AreEquivalent(new[] { TestScenes.RegionAID, TestScenes.RegionBID },
                                           LevelRoot.Current.LoadedRegions.Keys);
        }

        [Test]
        public async Task LoadRegions_NoLevel_DoesNothing() {
            await AwaitOrFail(CreateLoadRegionsStep().Execute(new ExecutionStepArguments()), "step");
            await Frames();
            Assert.IsFalse(Loader.IsLoadingAnyScenes());
        }

        [Test]
        public async Task LoadSceneThenRegions_InOneSequence_LoadsLevelAndItsRegions() {
            ExecutionStepSequencer sequencer = CreateSequencer("Load level", CreateLoadSceneStep(),
                                                               CreateLoadRegionsStep());
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, TestScenes.Level);

            await AwaitOrFail(sequencer.Execute(args), "sequence");

            Assert.IsNotNull(LevelRoot.Current);
            CollectionAssert.AreEquivalent(new[] { TestScenes.RegionBID }, LevelRoot.Current.LoadedRegions.Keys);
        }

        #endregion

        #region Activation, waiting and unloading

        [Test]
        public async Task ActivateLoadingScenesStep_WaitToFinish_WaitsForActivatedScenes() {
            Loader.LoadScene(TestScenes.Plain, false, false, null);
            ActivateLoadingScenesStep step = CreateStep<ActivateLoadingScenesStep>();
            SetField(step, "_waitToFinish", true);

            await AwaitOrFail(step.Execute(new ExecutionStepArguments()), "step");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
            Assert.IsTrue(step.IsFinished);
        }

        [Test]
        public async Task WaitForScenesToLoad_CompletesAfterLoads() {
            Loader.LoadScene(TestScenes.Plain, true, false, null);
            WaitForScenesToLoad step = CreateStep<WaitForScenesToLoad>();

            await AwaitOrFail(step.Execute(new ExecutionStepArguments()), "step");

            Assert.AreEqual(SceneStateType.Loaded, StateOf(TestScenes.Plain));
        }

        [Test]
        public async Task UnloadScenesThenWait_UnloadsGroup() {
            await LoadAndWait(TestScenes.Plain);
            UnloadScenesStep unload = CreateStep<UnloadScenesStep>();
            SetField(unload, "_groupToUnload", DefaultGroup);
            WaitForScenesToUnloadStep wait = CreateStep<WaitForScenesToUnloadStep>();
            ExecutionStepSequencer sequencer = CreateSequencer("Unload", unload, wait);

            await AwaitOrFail(sequencer.Execute(new ExecutionStepArguments()), "sequence");

            Assert.AreEqual(SceneStateType.Unloaded, StateOf(TestScenes.Plain));
            Assert.AreEqual(0, LoadedCount(TestScenes.Plain));
        }

        #endregion
    }
}
