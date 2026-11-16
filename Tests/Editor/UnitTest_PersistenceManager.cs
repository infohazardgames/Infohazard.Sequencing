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

using System.IO;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Loading, unloading, dirty tracking and custom data in PersistenceManager.
    /// Save slot semantics are in <see cref="UnitTest_SaveSlots"/>.
    /// </summary>
    public class UnitTest_PersistenceManager : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;
        private FileSaveDataHandler Handler => _manager.GetComponent<FileSaveDataHandler>();

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
        }

        #region Load and unload

        [Test]
        public void LoadGlobalData_LoadsInitializedDataAndFiresEvent() {
            int loaded = 0;
            _manager.GlobalDataLoaded += () => loaded++;

            _manager.LoadGlobalData();

            Assert.IsNotNull(_manager.LoadedGlobalData);
            Assert.IsTrue(_manager.LoadedGlobalData.Initialized);
            Assert.AreEqual(1, loaded);
        }

        [Test]
        public void LoadProfileData_WithoutGlobalData_LogsAndLoadsNothing() {
            LogAssert.Expect(LogType.Error, "Trying to load profile data when global data is not loaded.");
            _manager.LoadProfileData(Profile);
            Assert.IsNull(_manager.LoadedProfileData);
        }

        [Test]
        public void LoadProfileData_LoadsNamedInitializedProfile() {
            int loaded = 0;
            _manager.ProfileDataLoaded += () => loaded++;
            _manager.LoadGlobalData();

            _manager.LoadProfileData(Profile);

            Assert.AreEqual(Profile, _manager.LoadedProfileData.ProfileName);
            Assert.IsTrue(_manager.LoadedProfileData.Initialized);
            Assert.AreEqual(1, loaded);
        }

        [Test]
        public void LoadProfileData_DifferentProfile_UnloadsPreviousProfilesState() {
            // Suspected bug #25: the old profile's state stays loaded, so the next state save writes it into the
            // new profile.
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadStateData("State");

            _manager.LoadProfileData("Other");

            Assert.IsNull(_manager.LoadedStateData);
        }

        [Test]
        public void LoadStateData_WithoutProfile_LogsAndLoadsNothing() {
            _manager.LoadGlobalData();
            LogAssert.Expect(LogType.Error, "Trying to load state data when no profile data is loaded.");
            _manager.LoadStateData("State");
            Assert.IsNull(_manager.LoadedStateData);
        }

        [Test]
        public void LoadStateData_LoadsNamedInitializedState() {
            int loaded = 0;
            _manager.StateDataLoaded += () => loaded++;
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);

            _manager.LoadStateData("State");

            Assert.AreEqual("State", _manager.LoadedStateData.StateName);
            Assert.IsTrue(_manager.LoadedStateData.Initialized);
            Assert.AreEqual(1, loaded);
        }

        [Test]
        public void LoadEmptyStateData_LoadsUnnamedInitializedState() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);

            _manager.LoadEmptyStateData();

            Assert.IsNull(_manager.LoadedStateData.StateName);
            Assert.IsTrue(_manager.LoadedStateData.Initialized);
        }

        [Test]
        public void UnloadGlobalData_UnloadsEverythingAndFiresEvents() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();
            int global = 0, profile = 0, state = 0;
            _manager.GlobalDataLoaded += () => global++;
            _manager.ProfileDataLoaded += () => profile++;
            _manager.StateDataLoaded += () => state++;

            _manager.UnloadGlobalData();

            Assert.IsNull(_manager.LoadedGlobalData);
            Assert.IsNull(_manager.LoadedProfileData);
            Assert.IsNull(_manager.LoadedStateData);
            Assert.AreEqual((1, 1, 1), (global, profile, state));
        }

        [Test]
        public void UnloadProfileData_UnloadsProfileAndStateOnly() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();

            _manager.UnloadProfileData();

            Assert.IsNotNull(_manager.LoadedGlobalData);
            Assert.IsNull(_manager.LoadedProfileData);
            Assert.IsNull(_manager.LoadedStateData);
        }

        [Test]
        public void UnloadStateData_UnloadsStateOnly() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();

            _manager.UnloadStateData();

            Assert.IsNotNull(_manager.LoadedProfileData);
            Assert.IsNull(_manager.LoadedStateData);
        }

        [Test]
        public void UnloadedData_ChangesNoLongerMarkManagerDirty() {
            _manager.LoadGlobalData();
            GlobalSaveData old = _manager.LoadedGlobalData;
            _manager.UnloadGlobalData();

            Assert.IsFalse(old.HasStateChangedListeners);
        }

        #endregion

        #region Dirty tracking

        [Test]
        public void NewGlobalData_IsSavedOnFirstSave() {
            _manager.LoadGlobalData();
            _manager.SaveGlobalData();
            Assert.IsTrue(File.Exists(Handler.GlobalDataFile), "Newly initialized data should be saved.");
        }

        [Test]
        public void UnchangedData_SaveIsNoOp() {
            _manager.LoadGlobalData();
            _manager.SaveGlobalData();
            _manager = ReloadPersistenceManager(_manager, null, null);

            File.Delete(Handler.GlobalDataFile);
            _manager.SaveGlobalData();

            Assert.IsFalse(File.Exists(Handler.GlobalDataFile), "Saving unchanged data shouldn't write.");
        }

        [Test]
        public void ChangedData_IsSaved() {
            _manager.LoadGlobalData();
            _manager.SaveGlobalData();
            _manager = ReloadPersistenceManager(_manager, null, null);

            _manager.LoadedGlobalData.MostRecentProfile = "Changed";
            _manager.SaveGlobalData();

            _manager = ReloadPersistenceManager(_manager, null, null);
            Assert.AreEqual("Changed", _manager.LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void NestedCustomDataChange_MarksDirty() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.SaveProfileData();
            _manager = ReloadPersistenceManager(_manager, Profile, null);

            Assert.IsTrue(_manager.GetCustomData(_manager.LoadedProfileData, "custom", out TestSingletonState state));
            state.Value = 7;
            _manager.SaveProfileData();

            _manager = ReloadPersistenceManager(_manager, Profile, null);
            Assert.IsTrue(_manager.GetCustomData(_manager.LoadedProfileData, "custom", out state));
            Assert.AreEqual(7, state.Value);
        }

        [Test]
        public void SaveProfileData_RecordsMostRecentProfile() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);

            _manager.SaveProfileData();
            _manager.SaveGlobalData();

            _manager = ReloadPersistenceManager(_manager, null, null);
            Assert.AreEqual(Profile, _manager.LoadedGlobalData.MostRecentProfile);
            CollectionAssert.AreEquivalent(new[] { Profile }, _manager.GetAvailableProfiles());
        }

        [Test]
        public void SetStateData_SetsSaveTime() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            StateSaveData state = new StateSaveData { StateName = "State" };

            System.DateTime before = System.DateTime.Now;
            _manager.SetStateData("State", state);

            Assert.That(state.SaveTime, Is.GreaterThanOrEqualTo(before));
            Assert.That(_manager.GetStateData("State").SaveTime, Is.EqualTo(state.SaveTime));
        }

        #endregion

        #region Custom data

        [Test]
        public void GetCustomData_Missing_CreatesAndStores() {
            PersistedData parent = new PersistedData();

            Assert.IsTrue(_manager.GetCustomData(parent, "key", out TestSingletonState state));

            Assert.IsNotNull(state);
            Assert.AreSame(state, parent.GetCustomData("key"));
        }

        [Test]
        public void GetCustomData_ExactType_ReturnsSameInstance() {
            PersistedData parent = new PersistedData();
            TestSingletonState existing = new TestSingletonState();
            parent.SetCustomData("key", existing);

            Assert.IsTrue(_manager.GetCustomData(parent, "key", out TestSingletonState state));
            Assert.AreSame(existing, state);
        }

        [Test]
        public void GetCustomData_LoadedData_ConvertsAndReplacesIntermediate() {
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.GetCustomData(_manager.LoadedProfileData, "key", out TestSingletonState state);
            state.Value = 3;
            _manager.SaveProfileData();
            _manager = ReloadPersistenceManager(_manager, Profile, null);

            Assert.IsTrue(_manager.GetCustomData(_manager.LoadedProfileData, "key", out state));

            Assert.AreEqual(3, state.Value);
            Assert.AreSame(state, _manager.LoadedProfileData.GetCustomData("key"),
                           "The converted object should replace the loaded intermediate, so changes are saved.");
        }

        [Test]
        public void GetCustomData_IncompatibleData_ReturnsFalse() {
            PersistedData parent = new PersistedData();
            parent.SetCustomData("key", "not a state");

            Assert.IsFalse(_manager.GetCustomData(parent, "key", out TestSingletonState state));
            Assert.IsNull(state);
            Assert.AreEqual("not a state", parent.GetCustomData("key"), "Incompatible data shouldn't be replaced.");
        }

        #endregion
    }
}
