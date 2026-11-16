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
using Infohazard.Core;
using Infohazard.Sequencing.Persistence.Steps;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using SBR.Persistence;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Execution steps that don't load scenes: save data steps, parameter-passing steps, pause and events.
    /// </summary>
    public class UnitTest_PersistenceSteps : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
        }

        private void LoadProfileAndEmptyState() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();
        }

        #region Load and unload steps

        [Test]
        public void LoadGlobalSaveDataStep_LoadsGlobalData() {
            CreateStep<LoadGlobalSaveDataStep>().Execute(new ExecutionStepArguments());
            Assert.IsNotNull(_manager.LoadedGlobalData);
        }

        [Test]
        public void LoadProfileSaveDataStep_UsesParameterOrDefault() {
            _manager.LoadGlobalData();
            LoadProfileSaveDataStep step = CreateStep<LoadProfileSaveDataStep>();
            SetField(step, "_defaultProfileName", "DefaultProfile");

            step.Execute(new ExecutionStepArguments());
            Assert.AreEqual("DefaultProfile", _manager.LoadedProfileData.ProfileName);

            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadProfileSaveDataStep.ParamProfileName.Set(args, "Chosen");
            step.Execute(args);
            Assert.AreEqual("Chosen", _manager.LoadedProfileData.ProfileName);
        }

        [Test]
        public void LoadProfileSaveDataStep_PassesMostRecentState() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadedProfileData.MostRecentState = "Recent";
            _manager.SaveProfileData();
            LoadProfileSaveDataStep step = CreateStep<LoadProfileSaveDataStep>();
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadProfileSaveDataStep.ParamProfileName.Set(args, Profile);

            step.Execute(args);

            Assert.AreEqual("Recent", LoadStateSaveDataStep.ParamStateName.GetOrDefault(args, null));
        }

        [Test]
        public void LoadProfileSaveDataStep_PassMostRecentDisabled_DoesNotPassState() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadedProfileData.MostRecentState = "Recent";
            _manager.SaveProfileData();
            LoadProfileSaveDataStep step = CreateStep<LoadProfileSaveDataStep>();
            SetField(step, "_passMostRecentState", false);
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadProfileSaveDataStep.ParamProfileName.Set(args, Profile);

            step.Execute(args);

            Assert.IsFalse(LoadStateSaveDataStep.ParamStateName.Get(args, out _));
        }

        [Test]
        public void LoadStateSaveDataStep_NamedState_LoadsItWithoutAutoSave() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadStateSaveDataStep.ParamStateName.Set(args, "Saved");

            CreateStep<LoadStateSaveDataStep>().Execute(args);

            Assert.AreEqual("Saved", _manager.LoadedStateData.StateName);
            Assert.IsFalse(AutoSaveStep.DoAutoSave.GetOrDefault(args, true));
        }

        [Test]
        public void LoadStateSaveDataStep_NoName_LoadsEmptyStateWithAutoSave() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            ExecutionStepArguments args = new ExecutionStepArguments();

            CreateStep<LoadStateSaveDataStep>().Execute(args);

            Assert.IsNotNull(_manager.LoadedStateData);
            Assert.IsNull(_manager.LoadedStateData.StateName);
            Assert.IsTrue(AutoSaveStep.DoAutoSave.GetOrDefault(args, false));
        }

        [Test]
        public void UnloadSteps_UnloadProfileAndState() {
            LoadProfileAndEmptyState();

            CreateStep<UnloadStateSaveDataStep>().Execute(new ExecutionStepArguments());
            Assert.IsNull(_manager.LoadedStateData);
            Assert.IsNotNull(_manager.LoadedProfileData);

            CreateStep<UnloadProfileSaveDataStep>().Execute(new ExecutionStepArguments());
            Assert.IsNull(_manager.LoadedProfileData);
        }

        #endregion

        #region Save steps

        [Test]
        public void SaveStep_SavesSelectedLevels() {
            LoadProfileAndEmptyState();
            SaveStep step = CreateStep<SaveStep>();
            SetField(step, "_saveState", false);

            step.Execute(new ExecutionStepArguments());

            PersistenceManager reloaded = ReloadPersistenceManager(_manager, null, null);
            Assert.AreEqual(Profile, reloaded.LoadedGlobalData.MostRecentProfile);
            CollectionAssert.AreEquivalent(new[] { Profile }, reloaded.GetAvailableProfiles());
        }

        [Test]
        public void AutoSaveStep_InLevel_AutoSaves() {
            LoadProfileAndEmptyState();
            CreateSingleton<SaveStateManager>();
            LevelRoot level = CreateLevelRoot();
            level.Initialize();

            CreateStep<AutoSaveStep>().Execute(new ExecutionStepArguments());

            Assert.AreEqual("Autosave_0", _manager.LoadedStateData.StateName);
            level.Cleanup();
        }

        [Test]
        public void AutoSaveStep_NoLevelOrDisabled_DoesNotSave() {
            LoadProfileAndEmptyState();
            CreateSingleton<SaveStateManager>();
            AutoSaveStep step = CreateStep<AutoSaveStep>();

            step.Execute(new ExecutionStepArguments());
            Assert.IsNull(_manager.LoadedStateData.StateName, "No level loaded, so no autosave.");

            LevelRoot level = CreateLevelRoot();
            level.Initialize();
            ExecutionStepArguments args = new ExecutionStepArguments();
            AutoSaveStep.DoAutoSave.Set(args, false);
            step.Execute(args);
            Assert.IsNull(_manager.LoadedStateData.StateName, "Autosave was disabled.");
            level.Cleanup();
        }

        private LevelRoot CreateLevelRoot() {
            LevelManifest manifest = CreateManifest(CreateLevelEntry(1, "Assets/Test/Level1.unity"));
            LevelRoot root = CreateGameObject("Level root").AddComponent<LevelRoot>();
            SetField(root, "_manifestEntry", manifest.Levels[0]);
            return root;
        }

        #endregion

        #region Parameter-passing steps

        [Test]
        public void PassSceneStep_PassesScenePath() {
            PassSceneStep step = CreateStep<PassSceneStep>();
            SetField(step, "_sceneToLoad", CreateSceneRef("Assets/Test/Scene.unity"));
            ExecutionStepArguments args = new ExecutionStepArguments();

            step.Execute(args);

            Assert.AreEqual("Assets/Test/Scene.unity", LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));
        }

        [Test]
        public void PassRegionsStep_PassesLevelAndRegionIDs() {
            LevelManifestRegionEntry a = CreateRegionEntry(4, 1, "Assets/Test/A.unity");
            LevelManifestRegionEntry b = CreateRegionEntry(7, 1, "Assets/Test/B.unity");
            LevelManifestLevelEntry level = CreateLevelEntry(1, "Assets/Test/Level.unity", a, b);
            PassRegionsStep step = CreateStep<PassRegionsStep>();
            SetField(step, "_level", level);
            SetField(step, "_regionsToLoad", new[] { b });
            ExecutionStepArguments args = new ExecutionStepArguments();

            step.Execute(args);

            Assert.AreSame(level, LoadRegionsStep.ParamLoadingLevel.GetOrDefault(args, null));
            CollectionAssert.AreEqual(new[] { 7 }, LoadRegionsStep.ParamRegionsToLoad.GetOrDefault(args, null));
        }

        [Test]
        public void PassSavedSceneStep_PassesSavedSceneAndLoadedRegions() {
            LevelManifestRegionEntry a = CreateRegionEntry(4, 1, "Assets/Test/Level_A.unity");
            LevelManifestRegionEntry b = CreateRegionEntry(7, 1, "Assets/Test/Level_B.unity");
            CreateManifest(CreateLevelEntry(1, "Assets/Test/Level.unity", a, b));
            LoadProfileAndEmptyState();
            _manager.LoadedStateData.CurrentScene = "Level";
            _manager.SaveStateDataAs("Slot");
            _manager.SetRegionData(1, 7, new RegionSaveData { RegionIndex = 7, Loaded = true });
            _manager.SetRegionData(1, 4, new RegionSaveData { RegionIndex = 4, Loaded = false });
            ExecutionStepArguments args = new ExecutionStepArguments();

            CreateStep<PassSavedSceneStep>().Execute(args);

            Assert.AreEqual("Level", LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));
            CollectionAssert.AreEqual(new[] { 7 }, LoadRegionsStep.ParamRegionsToLoad.GetOrDefault(args, null));
        }

        [Test]
        public void PassSavedSceneStep_NoSavedScene_UsesDefault() {
            CreateManifest();
            LoadProfileAndEmptyState();
            PassSavedSceneStep step = CreateStep<PassSavedSceneStep>();
            SetField(step, "_defaultScene", CreateSceneRef("Assets/Test/MainMenu.unity"));
            ExecutionStepArguments args = new ExecutionStepArguments();

            step.Execute(args);

            Assert.AreEqual("MainMenu", LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));
            Assert.IsFalse(LoadRegionsStep.ParamRegionsToLoad.Get(args, out _));
        }

        [Test]
        public void SetCurrentSavedSceneStep_StoresSceneName() {
            LoadProfileAndEmptyState();
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, "Assets/Test/Level.unity");

            CreateStep<SetCurrentSavedSceneToLoadingSceneStep>().Execute(args);

            Assert.AreEqual("Level", _manager.LoadedStateData.CurrentScene);
        }

        [Test]
        public void SetCurrentSavedSceneStep_NoState_LogsError() {
            LogAssert.Expect(LogType.Error, "Trying to save current scene when no state data is loaded.");
            CreateStep<SetCurrentSavedSceneToLoadingSceneStep>().Execute(new ExecutionStepArguments());
        }

        #endregion

        #region Other steps

        [Test]
        public void PauseStep_SetsPaused() {
            PauseStep step = CreateStep<PauseStep>();
            SetField(step, "_paused", true);
            step.Execute(new ExecutionStepArguments());
            Assert.IsTrue(Pause.Paused);

            SetField(step, "_paused", false);
            step.Execute(new ExecutionStepArguments());
            Assert.IsFalse(Pause.Paused);
        }

        [Test]
        public void InstantEventStep_InvokesEvent() {
            InstantEventStep step = CreateStep<InstantEventStep>();
            int invoked = 0;
            UnityEvent onExecute = GetField<UnityEvent>(step, "_onExecute");
            if (onExecute == null) {
                onExecute = new UnityEvent();
                SetField(step, "_onExecute", onExecute);
            }

            onExecute.AddListener(() => invoked++);

            step.Execute(new ExecutionStepArguments());

            Assert.AreEqual(1, invoked);
        }

        #endregion
    }
}
