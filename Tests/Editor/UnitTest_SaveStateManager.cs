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

using System.Linq;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Autosave rotation, quicksave and the load shortcuts in SaveStateManager.
    /// </summary>
    public class UnitTest_SaveStateManager : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;
        private SaveStateManager _saves;
        private RecordingStep _loadState;

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();
            _saves = CreateSingleton<SaveStateManager>();
            CreateRecordingSceneController(out _loadState, out _, out _);
        }

        private string LoadedStateName(ExecutionStepArguments args) =>
            LoadStateSaveDataStep.ParamStateName.GetOrDefault(args, null);

        /// <summary>
        /// Simulate relaunching the game into the same profile, with a new SaveStateManager.
        /// </summary>
        private void Relaunch() {
            Object.DestroyImmediate(_saves.gameObject);
            _manager = ReloadPersistenceManager(_manager, Profile, null);
            _saves = CreateSingleton<SaveStateManager>();
        }

        #region Autosave

        [Test]
        public void AutoSave_RotatesThroughSlots() {
            for (int i = 0; i < 4; i++) {
                _manager.LoadedStateData.CurrentScene = $"Scene{i}";
                _saves.AutoSave();
            }

            CollectionAssert.AreEquivalent(new[] { "Autosave_0", "Autosave_1", "Autosave_2" },
                                           _manager.GetAvailableStates());
            Assert.AreEqual("Autosave_0", _manager.LoadedStateData.StateName);
            Assert.AreEqual(0, _saves.State.LastAutoSave);
            Assert.AreEqual(1, _saves.State.NextAutoSave);
            Assert.AreEqual("Scene3", _manager.GetStateData("Autosave_0").CurrentScene);
            Assert.AreEqual("Scene1", _manager.GetStateData("Autosave_1").CurrentScene);
        }

        [Test]
        public void AutoSave_SavesProfileAndGlobalData() {
            _saves.AutoSave();

            _manager = ReloadPersistenceManager(_manager, null, null);
            Assert.AreEqual(Profile, _manager.LoadedGlobalData.MostRecentProfile);
            _manager.LoadProfileData(Profile);
            Assert.AreEqual("Autosave_0", _manager.LoadedProfileData.MostRecentState);
        }

        [Test]
        public void AutoSave_RotationSurvivesRelaunch() {
            _saves.AutoSave();
            _saves.AutoSave();

            Relaunch();

            Assert.AreEqual(1, _saves.State.LastAutoSave);
            Assert.AreEqual(2, _saves.State.NextAutoSave);
        }

        [Test]
        public void LoadLastAutoSave_NoAutoSave_ReturnsFalse() {
            Assert.IsFalse(_saves.LoadLastAutoSave());
            CollectionAssert.IsEmpty(_loadState.Calls);
        }

        [Test]
        public void LoadLastAutoSave_LoadsMostRecentAutoSave() {
            _saves.AutoSave();
            _saves.AutoSave();

            Assert.IsTrue(_saves.LoadLastAutoSave());

            Assert.AreEqual("Autosave_1", LoadedStateName(_loadState.Calls.Single()));
        }

        #endregion

        #region Quicksave

        [Test]
        public void QuickSave_SavesQuicksaveSlot() {
            _manager.LoadedStateData.CurrentScene = "Scene";

            _saves.QuickSave();

            Assert.IsTrue(_saves.State.HasQuickSave);
            Assert.AreEqual("Quicksave", _manager.LoadedStateData.StateName);
            Assert.AreEqual("Scene", _manager.GetStateData("Quicksave").CurrentScene);
        }

        [Test]
        public void QuickLoad_NoQuickSave_ReturnsFalse() {
            Assert.IsFalse(_saves.QuickLoad());
            CollectionAssert.IsEmpty(_loadState.Calls);
        }

        [Test]
        public void QuickLoad_AfterQuickSave_LoadsQuicksave() {
            _saves.QuickSave();

            Assert.IsTrue(_saves.QuickLoad());

            Assert.AreEqual("Quicksave", LoadedStateName(_loadState.Calls.Single()));
        }

        [Test]
        public void QuickSaveFlag_SurvivesRelaunch() {
            _saves.QuickSave();
            Relaunch();
            Assert.IsTrue(_saves.State.HasQuickSave);
        }

        [Test]
        public void QuickSaveTwice_KeepsDataWrittenByFirstQuickSave() {
            // Suspected bug #1, end to end: quickload, change something, quicksave again.
            _saves.QuickSave();
            LevelSaveData level = new LevelSaveData { LevelIndex = 1, Initialized = true };
            level.Objects.RegisterExistingObject(5, false).Destroyed = true;
            _manager.SetLevelData(1, level);

            _manager.LoadStateData("Quicksave");
            _manager.LoadedStateData.CurrentScene = "Changed";
            _saves.QuickSave();

            Assert.AreEqual(5ul, _manager.GetLevelData(1).Objects.Objects.SingleOrDefault()?.InstanceID);
        }

        #endregion

        #region No profile loaded

        [Test]
        public void NoProfileLoaded_SaveAndLoadLogErrors() {
            _manager.UnloadProfileData();
            Assert.IsNull(_saves.State, "The profile-scoped state should clear when the profile unloads.");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Cannot quick save"));
            _saves.QuickSave();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Cannot quick load"));
            Assert.IsFalse(_saves.QuickLoad());
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Cannot auto save"));
            _saves.AutoSave();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Cannot auto load"));
            Assert.IsFalse(_saves.LoadLastAutoSave());
        }

        #endregion
    }
}
