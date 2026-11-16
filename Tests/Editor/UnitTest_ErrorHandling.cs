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

using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Misuse and missing-data cases that should log an error instead of throwing.
    /// </summary>
    public class UnitTest_ErrorHandling : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;
        private LevelManifestLevelEntry _levelEntry;
        private LevelManifestRegionEntry _regionEntry;

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _regionEntry = CreateRegionEntry(2, 1, "Assets/Test/Region.unity");
            _levelEntry = CreateLevelEntry(1, "Assets/Test/Level.unity", _regionEntry);
            CreateManifest(_levelEntry);
        }

        private PersistedLevelRoot CreateLevelRoot(string objectName) {
            var root = CreateGameObject(objectName).AddComponent<PersistedLevelRoot>();
            SetField(root, "_manifestEntry", _levelEntry);
            return root;
        }

        #region PersistedLevelRoot

        [Test]
        public void PersistedLevelRoot_NoStateLoaded_WorksWithUnsavedData() {
            // Suspected bug #18: Initialize dereferenced the null level data.
            PersistedLevelRoot root = CreateLevelRoot("Level");

            LogAssert.Expect(LogType.Error, "Trying to load level data when no state data is loaded.");
            Assert.DoesNotThrow(() => root.Initialize());
            Assert.IsNotNull(root.SaveData);
            Assert.AreEqual(1, root.SaveData.LevelIndex);

            LogAssert.Expect(LogType.Error, "Trying to load region data when no state data is loaded.");
            RegionSaveData region = null;
            Assert.DoesNotThrow(() => region = root.GetOrLoadRegionData(_regionEntry));
            Assert.IsNotNull(region);
            Assert.AreEqual(2, region.RegionIndex);

            root.SaveData.Objects.RegisterExistingObject(5, false);
            LogAssert.Expect(LogType.Error, "Trying to save level data when no state data is loaded.");
            Assert.DoesNotThrow(root.Save);

            Assert.DoesNotThrow(root.Cleanup);
            Assert.IsNull(LevelRoot.Current);
        }

        [Test]
        public void SecondPersistedLevel_DoesNotTakeOverSaveData() {
            // Suspected bug #18: the second level still hooked level data even though it isn't current.
            _manager.LoadEmptyStateData();
            PersistedLevelRoot first = CreateLevelRoot("First");
            first.Initialize();
            PersistedLevelRoot second = CreateLevelRoot("Second");

            LogAssert.Expect(LogType.Error, "Trying to initialize a LevelRoot when one is already active!");
            second.Initialize();

            Assert.AreSame(first, LevelRoot.Current);
            Assert.IsNull(second.SaveData);
            Assert.DoesNotThrow(second.Cleanup);
            Assert.AreSame(first, LevelRoot.Current, "Cleaning up the second level shouldn't affect the first.");
            first.Cleanup();
        }

        #endregion

        #region Saving without state

        [Test]
        public void SaveStateDataAs_NoStateLoaded_LogsInsteadOfThrowing() {
            // Suspected bug #20.
            LogAssert.Expect(LogType.Error, "Trying to save state data when no profile and state data are loaded.");
            Assert.DoesNotThrow(() => _manager.SaveStateDataAs("Slot"));
            CollectionAssert.IsEmpty(_manager.GetAvailableStates());
        }

        [Test]
        public void SaveAll_NoStateLoaded_LogsInsteadOfThrowing() {
            // Suspected bug #20.
            SaveStateManager saves = CreateSingleton<SaveStateManager>();

            LogAssert.Expect(LogType.Error, "Trying to save state data when no profile and state data are loaded.");
            Assert.DoesNotThrow(() => saves.SaveAll("Slot"));
        }

        #endregion

        #region ExecutionStepSequencer

        [Test]
        public void PlayOnAwake_InitialSceneNotLoaded_DoesNotRunSteps() {
            // Suspected bug #20: Start logged and quit, then ran the steps anyway.
            if (ExecutionStepSequencer.InitialSceneIsLoaded) {
                Assert.Ignore("The initial scene (build index 0) is open, so this case can't be checked.");
            }

            RecordingStep step = CreateStep<RecordingStep>();
            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", step);
            SetField(sequencer, "_playOnAwake", true);

            // In edit mode, SceneControl.Quit only sets EditorApplication.isPlaying to false, which does nothing.
            LogAssert.Expect(LogType.Error, "Initial scene is not loaded!");
            InvokeLifecycleInEditMode(sequencer, "Start");

            Assert.AreEqual(0, step.Calls.Count);
        }

        #endregion
    }
}
