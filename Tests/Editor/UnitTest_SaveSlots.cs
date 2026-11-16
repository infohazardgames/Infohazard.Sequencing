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
using System.Linq;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Save slot semantics: saving the loaded state under the same or a new name, and deleting states and
    /// profiles. Level and region data is written with SetLevelData/SetRegionData, the way
    /// PersistedLevelRoot.Save writes dirty data after SaveStateDataAs.
    /// </summary>
    public class UnitTest_SaveSlots : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
        }

        private static LevelSaveData Level(int index, ulong destroyedObjectID) {
            LevelSaveData level = new LevelSaveData { LevelIndex = index, Initialized = true };
            level.Objects.RegisterExistingObject(destroyedObjectID, false).Destroyed = true;
            return level;
        }

        /// <summary>
        /// Start a new game, save it as <paramref name="state"/>, and write level 1 (object 11 destroyed),
        /// level 2 (object 22 destroyed) and region 3 of level 1 (loaded) into it.
        /// </summary>
        private void CreateSlot(string state, string currentScene = "Scene") {
            _manager.LoadEmptyStateData();
            _manager.LoadedStateData.CurrentScene = currentScene;
            _manager.SaveStateDataAs(state);
            _manager.SetLevelData(1, Level(1, 11));
            _manager.SetLevelData(2, Level(2, 22));
            _manager.SetRegionData(1, 3, new RegionSaveData { RegionIndex = 3, Loaded = true });
        }

        private void AssertSlotHasLevelData(string state) {
            _manager.LoadStateData(state);
            Assert.AreEqual(11ul, _manager.GetLevelData(1).Objects.Objects.SingleOrDefault()?.InstanceID,
                            $"Level 1 data missing from {state}.");
            Assert.AreEqual(22ul, _manager.GetLevelData(2).Objects.Objects.SingleOrDefault()?.InstanceID,
                            $"Level 2 data missing from {state}.");
            Assert.IsTrue(_manager.GetRegionData(1, 3).Loaded, $"Region data missing from {state}.");
        }

        #region Save as a new name

        [Test]
        public void SaveStateDataAs_NewName_CopiesSlotAndSwitchesToIt() {
            CreateSlot("A");
            _manager.LoadStateData("A");

            DateTime before = DateTime.Now;
            _manager.SaveStateDataAs("B");

            Assert.AreEqual("B", _manager.LoadedStateData.StateName);
            Assert.AreEqual("B", _manager.LoadedProfileData.MostRecentState);
            Assert.That(_manager.LoadedStateData.SaveTime, Is.GreaterThanOrEqualTo(before));
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, _manager.GetAvailableStates());
            Assert.AreEqual("B", _manager.GetStateData("B").StateName);
            AssertSlotHasLevelData("B");
            AssertSlotHasLevelData("A");
        }

        [Test]
        public void SaveStateDataAs_NewName_LaterWritesGoToNewSlotOnly() {
            CreateSlot("A");
            _manager.LoadStateData("A");
            _manager.SaveStateDataAs("B");

            _manager.SetLevelData(1, Level(1, 99));

            Assert.AreEqual(99ul, _manager.GetLevelData(1).Objects.Objects.Single().InstanceID);
            _manager.LoadStateData("A");
            Assert.AreEqual(11ul, _manager.GetLevelData(1).Objects.Objects.Single().InstanceID);
        }

        [Test]
        public void SaveStateDataAs_FromUnnamedState_ReplacesExistingSlot() {
            CreateSlot("A", "OldScene");

            _manager.LoadEmptyStateData();
            _manager.LoadedStateData.CurrentScene = "NewScene";
            _manager.SaveStateDataAs("A");

            StateSaveData saved = _manager.GetStateData("A");
            Assert.AreEqual("NewScene", saved.CurrentScene);
            CollectionAssert.IsEmpty(_manager.GetLevelData(1).Objects.Objects,
                                     "A new game saved over a slot shouldn't inherit its level data.");
        }

        [Test]
        public void SaveStateDataAs_FromNeverWrittenNamedState_SavesWithoutThrowing() {
            // Suspected bug #2, through PersistenceManager: "DefaultState" was loaded but never written.
            _manager.LoadStateData("DefaultState");
            _manager.LoadedStateData.CurrentScene = "Scene";

            Assert.DoesNotThrow(() => _manager.SaveStateDataAs("Autosave_0"));

            Assert.AreEqual("Scene", _manager.GetStateData("Autosave_0").CurrentScene);
            CollectionAssert.Contains(_manager.GetAvailableStates().ToList(), "Autosave_0");
        }

        [Test]
        public void SaveStateDataAs_EmptyName_LogsAndDoesNothing() {
            CreateSlot("A");
            _manager.LoadStateData("A");

            LogAssert.Expect(LogType.Error, "A state cannot be saved without a name.");
            _manager.SaveStateDataAs("");

            Assert.AreEqual("A", _manager.LoadedStateData.StateName);
            CollectionAssert.AreEquivalent(new[] { "A" }, _manager.GetAvailableStates());
        }

        #endregion

        #region Save under the same name

        [Test]
        public void SaveStateDataAs_SameName_KeepsLevelAndRegionData() {
            // Suspected bug #1: saving the loaded state under its own name clears the whole slot first.
            CreateSlot("A");
            _manager.LoadStateData("A");
            _manager.LoadedStateData.CurrentScene = "Changed";

            _manager.SaveStateDataAs("A");

            Assert.AreEqual("Changed", _manager.GetStateData("A").CurrentScene);
            AssertSlotHasLevelData("A");
        }

        [Test]
        public void SaveStateData_ChangedState_KeepsLevelAndRegionData() {
            // Suspected bug #1, through SaveStateData (what SaveStep calls after the current scene changes).
            CreateSlot("A");
            _manager.LoadStateData("A");
            _manager.LoadedStateData.CurrentScene = "NextLevel";

            _manager.SaveStateData();

            Assert.AreEqual("NextLevel", _manager.GetStateData("A").CurrentScene);
            AssertSlotHasLevelData("A");
        }

        [Test]
        public void SaveStateData_Unchanged_DoesNotWrite() {
            CreateSlot("A");
            _manager.LoadStateData("A");
            DateTime savedTime = _manager.LoadedStateData.SaveTime;

            _manager.SaveStateData();

            Assert.AreEqual(savedTime, _manager.GetStateData("A").SaveTime);
        }

        [Test]
        public void SaveStateData_UnnamedState_DoesNotWrite() {
            _manager.LoadEmptyStateData();
            _manager.LoadedStateData.CurrentScene = "Scene";

            _manager.SaveStateData();

            CollectionAssert.IsEmpty(_manager.GetAvailableStates());
        }

        #endregion

        #region Delete state

        [Test]
        public void DeleteStateData_NamedStateOfLoadedProfile_DeletesIt() {
            // Suspected bug #4: DeleteStateData never deletes anything from the handler.
            CreateSlot("A");
            CreateSlot("B");

            _manager.DeleteStateData(state: "A");

            CollectionAssert.AreEquivalent(new[] { "B" }, _manager.GetAvailableStates());
            Assert.AreEqual("B", _manager.LoadedStateData.StateName, "Deleting another state keeps the loaded one.");
        }

        [Test]
        public void DeleteStateData_NoArguments_DeletesAndUnloadsCurrentState() {
            // Suspected bug #4: with no arguments it logs an error meant for a different case.
            CreateSlot("A");
            CreateSlot("B");

            _manager.DeleteStateData();

            Assert.IsNull(_manager.LoadedStateData);
            CollectionAssert.AreEquivalent(new[] { "A" }, _manager.GetAvailableStates());
        }

        [Test]
        public void DeleteStateData_LoadedStateByName_DeletesAndUnloadsIt() {
            CreateSlot("A");

            _manager.DeleteStateData(state: "A");

            Assert.IsNull(_manager.LoadedStateData);
            CollectionAssert.IsEmpty(_manager.GetAvailableStates());
        }

        [Test]
        public void DeleteStateData_OtherProfile_DeletesThere() {
            CreateSlot("A");
            _manager.LoadProfileData("Other");
            CreateSlot("A");
            _manager.LoadProfileData(Profile);
            _manager.LoadStateData("A");

            _manager.DeleteStateData("Other", "A");

            CollectionAssert.IsEmpty(_manager.GetAvailableStates("Other"));
            CollectionAssert.AreEquivalent(new[] { "A" }, _manager.GetAvailableStates(Profile));
            Assert.IsNotNull(_manager.LoadedStateData, "A same-named state in another profile isn't the loaded one.");
        }

        [Test]
        public void DeleteStateData_ProfileWithoutState_LogsAndDeletesNothing() {
            // Suspected bug #4: "the current state" only makes sense for the loaded profile.
            CreateSlot("A");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("specified profile"));
            _manager.DeleteStateData(Profile);

            CollectionAssert.AreEquivalent(new[] { "A" }, _manager.GetAvailableStates());
            Assert.IsNotNull(_manager.LoadedStateData);
        }

        [Test]
        public void DeleteStateData_NoStateLoaded_LogsAndDeletesNothing() {
            CreateSlot("A");
            _manager.UnloadStateData();

            LogAssert.Expect(LogType.Error,
                             "Trying to delete current profile state data when no state data is loaded.");
            _manager.DeleteStateData();

            CollectionAssert.AreEquivalent(new[] { "A" }, _manager.GetAvailableStates());
        }

        #endregion

        #region Most recent state after delete

        /// <summary>
        /// Overwrite a saved state's save time (SaveStateDataAs always uses DateTime.Now, which can repeat).
        /// </summary>
        private void SetSaveTime(string profile, string state, DateTime time) {
            var handler = _manager.GetComponent<SaveDataHandler>();
            var serializer = _manager.GetComponent<Serializer>();
            StateSaveData data = handler.GetStateSaveData(serializer, profile, state);
            data.SaveTime = time;
            handler.SetStateSaveData(serializer, profile, state, data);
        }

        private void CreateSlotsAtTimes(string profile, params (string state, int day)[] slots) {
            foreach ((string state, int day) in slots) {
                CreateSlot(state);
                SetSaveTime(profile, state, new DateTime(2026, 1, day));
            }

            _manager.SaveProfileData();
        }

        [Test]
        public void DeleteMostRecentState_PointsToNewestRemainingState() {
            CreateSlotsAtTimes(Profile, ("A", 1), ("B", 3), ("C", 2));
            Assert.AreEqual("C", _manager.LoadedProfileData.MostRecentState);

            _manager.DeleteStateData();

            Assert.AreEqual("B", _manager.LoadedProfileData.MostRecentState, "B has the newest save time.");
            PersistenceManager reloaded = ReloadPersistenceManager(_manager, Profile, null);
            Assert.AreEqual("B", reloaded.LoadedProfileData.MostRecentState,
                            "The saved profile should be updated without another save.");
        }

        [Test]
        public void DeleteOtherState_KeepsMostRecentState() {
            CreateSlotsAtTimes(Profile, ("A", 1), ("B", 2));

            _manager.DeleteStateData(state: "A");

            Assert.AreEqual("B", _manager.LoadedProfileData.MostRecentState);
            Assert.AreEqual("B", ReloadPersistenceManager(_manager, Profile, null).LoadedProfileData.MostRecentState);
        }

        [Test]
        public void DeleteOnlyState_ClearsMostRecentState() {
            CreateSlotsAtTimes(Profile, ("A", 1));

            _manager.DeleteStateData();

            Assert.IsNull(_manager.LoadedProfileData.MostRecentState);
            Assert.IsNull(ReloadPersistenceManager(_manager, Profile, null).LoadedProfileData.MostRecentState);
        }

        [Test]
        public void DeleteMostRecentStateOfOtherProfile_UpdatesThatProfile() {
            _manager.LoadProfileData("Other");
            CreateSlotsAtTimes("Other", ("X", 1), ("Y", 2));
            _manager.LoadProfileData(Profile);

            _manager.DeleteStateData("Other", "Y");

            Assert.AreEqual("X", _manager.GetProfileData("Other").MostRecentState);
            Assert.IsNull(_manager.LoadedProfileData.MostRecentState, "The loaded profile isn't affected.");
        }

        #endregion

        #region Delete profile

        [Test]
        public void DeleteProfileData_LoadedProfile_DeletesAndUnloads() {
            CreateSlot("A");
            _manager.SaveProfileData();

            _manager.DeleteProfileData();

            Assert.IsNull(_manager.LoadedProfileData);
            Assert.IsNull(_manager.LoadedStateData);
            CollectionAssert.IsEmpty(_manager.GetAvailableProfiles());
            CollectionAssert.IsEmpty(_manager.GetAvailableStates(Profile));
        }

        [Test]
        public void DeleteProfileData_OtherProfile_KeepsLoadedProfile() {
            _manager.SaveProfileData();
            _manager.LoadProfileData("Other");
            _manager.SaveProfileData();
            _manager.LoadProfileData(Profile);

            _manager.DeleteProfileData("Other");

            Assert.IsNotNull(_manager.LoadedProfileData);
            CollectionAssert.AreEquivalent(new[] { Profile }, _manager.GetAvailableProfiles());
        }

        /// <summary>
        /// Load a profile, save one slot in it at the given day, and save the profile and global data.
        /// </summary>
        private void CreateProfileWithSlot(string profile, int day) {
            _manager.LoadProfileData(profile);
            CreateSlotsAtTimes(profile, ("Slot", day));
            _manager.SaveGlobalData();
        }

        [Test]
        public void DeleteMostRecentProfile_PointsToProfileWithNewestSave() {
            CreateProfileWithSlot("P1", 1);
            CreateProfileWithSlot("P2", 3);
            CreateProfileWithSlot("P3", 2);
            Assert.AreEqual("P3", _manager.LoadedGlobalData.MostRecentProfile);

            _manager.DeleteProfileData();

            Assert.AreEqual("P2", _manager.LoadedGlobalData.MostRecentProfile, "P2 has the newest save.");
            Assert.AreEqual("P2", ReloadPersistenceManager(_manager, null, null).LoadedGlobalData.MostRecentProfile,
                            "The saved global data should be updated without another save.");
        }

        [Test]
        public void DeleteMostRecentProfile_ProfilesWithoutSavesComeLast() {
            _manager.LoadProfileData("Empty");
            _manager.SaveProfileData();
            CreateProfileWithSlot("WithSave", 1);
            CreateProfileWithSlot("Latest", 2);

            _manager.DeleteProfileData("Latest");

            Assert.AreEqual("WithSave", _manager.LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void DeleteOtherProfile_KeepsMostRecentProfile() {
            CreateProfileWithSlot("P1", 1);
            CreateProfileWithSlot("P2", 2);

            _manager.DeleteProfileData("P1");

            Assert.AreEqual("P2", _manager.LoadedGlobalData.MostRecentProfile);
            Assert.AreEqual("P2", ReloadPersistenceManager(_manager, null, null).LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void DeleteOnlyProfile_ClearsMostRecentProfile() {
            CreateProfileWithSlot("Only", 1);

            _manager.DeleteProfileData();

            Assert.IsNull(_manager.LoadedGlobalData.MostRecentProfile);
            Assert.IsNull(ReloadPersistenceManager(_manager, null, null).LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void DeleteProfileData_NoProfileLoaded_LogsAndDeletesNothing() {
            _manager.SaveProfileData();
            _manager.UnloadProfileData();

            LogAssert.Expect(LogType.Error,
                             "Trying to delete current profile data when no profile data is loaded.");
            _manager.DeleteProfileData();

            CollectionAssert.AreEquivalent(new[] { Profile }, _manager.GetAvailableProfiles());
        }

        #endregion
    }
}
