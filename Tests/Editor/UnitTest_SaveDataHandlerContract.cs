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
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// The behavior every <see cref="SaveDataHandler"/> must share, run against each implementation.
    /// The file handler is the reference behavior (it is the one Astral ships with); differences in the
    /// PlayerPrefs handler are suspected bug #6.
    /// "Fresh" handlers are new instances reading the same storage, which is what a relaunched game sees.
    /// </summary>
    [TestFixture(TestHandlerType.File)]
    [TestFixture(TestHandlerType.PlayerPrefs)]
    public class UnitTest_SaveDataHandlerContract : SequencingTestFixture {
        private const string Profile = "Profile";
        private const string State = "State";

        private readonly TestHandlerType _type;
        private Serializer _serializer;
        private SaveDataHandler _handler;

        public UnitTest_SaveDataHandlerContract(TestHandlerType type) {
            _type = type;
        }

        [SetUp]
        public void SetUp() {
            _serializer = CreateSerializer(CreateGameObject("Serializer"));
            _handler = Fresh();
        }

        private SaveDataHandler Fresh() => CreateHandler(CreateGameObject("Handler"), _type);

        private static LevelSaveData Level(int index, ulong destroyedObjectID) {
            LevelSaveData level = new LevelSaveData { LevelIndex = index };
            level.Objects.RegisterExistingObject(destroyedObjectID, false).Destroyed = true;
            return level;
        }

        private static RegionSaveData Region(int index, bool loaded = true) =>
            new() { RegionIndex = index, Loaded = loaded };

        private void SaveState(string profile, string state, string currentScene) {
            _handler.SetStateSaveData(_serializer, profile, state, new StateSaveData {
                StateName = state, CurrentScene = currentScene,
            });
        }

        /// <summary>
        /// Save something unrelated, which makes the PlayerPrefs handler write everything it holds.
        /// </summary>
        private void SaveUnrelated() {
            _handler.SetGlobalSaveData(_serializer, new GlobalSaveData { MostRecentProfile = "Unrelated" });
        }

        #region Missing data

        [Test]
        public void GetMissing_ReturnsDefaultsWithIdentity() {
            Assert.IsNull(_handler.GetGlobalSaveData(_serializer).MostRecentProfile);
            Assert.AreEqual(Profile, _handler.GetProfileSaveData(_serializer, Profile).ProfileName);
            Assert.AreEqual(State, _handler.GetStateSaveData(_serializer, Profile, State).StateName);
            Assert.AreEqual(4, _handler.GetLevelSaveData(_serializer, Profile, State, 4).LevelIndex);
        }

        [Test]
        public void GetMissingRegion_ReturnsDefaultWithIdentity() {
            // Suspected bug #6: the PlayerPrefs handler returns null here.
            RegionSaveData region = _handler.GetRegionSaveData(_serializer, Profile, State, 1, 7);
            Assert.IsNotNull(region);
            Assert.AreEqual(7, region.RegionIndex);
            Assert.IsFalse(region.Loaded);
        }

        [Test]
        public void GetMissingProfile_DoesNotCreateIt() {
            // Suspected bug #6: the PlayerPrefs handler adds an entry on read, and the next save persists it.
            _handler.GetProfileSaveData(_serializer, "Phantom");
            CollectionAssert.IsEmpty(_handler.GetAvailableProfiles());

            SaveUnrelated();
            CollectionAssert.IsEmpty(Fresh().GetAvailableProfiles());
        }

        [Test]
        public void GetMissingState_DoesNotCreateIt() {
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData { ProfileName = Profile });

            _handler.GetStateSaveData(_serializer, Profile, "Phantom");
            CollectionAssert.IsEmpty(_handler.GetAvailableStates(Profile));

            SaveUnrelated();
            CollectionAssert.IsEmpty(Fresh().GetAvailableStates(Profile));
        }

        [Test]
        public void GetMissingLevel_DoesNotCreateIt() {
            SaveState(Profile, State, "Scene");

            _handler.GetLevelSaveData(_serializer, Profile, State, 9);
            _handler.GetRegionSaveData(_serializer, Profile, State, 8, 1);
            CollectionAssert.IsEmpty(_handler.GetAvailableLevels(Profile, State));

            SaveUnrelated();
            CollectionAssert.IsEmpty(Fresh().GetAvailableLevels(Profile, State));
        }

        [Test]
        public void ListingsOfMissingParents_AreEmpty() {
            CollectionAssert.IsEmpty(_handler.GetAvailableProfiles());
            CollectionAssert.IsEmpty(_handler.GetAvailableStates("Nobody"));
            CollectionAssert.IsEmpty(_handler.GetAvailableLevels("Nobody", "Nothing"));
            CollectionAssert.IsEmpty(_handler.GetAvailableRegions("Nobody", "Nothing", 1));
        }

        #endregion

        #region Set and get

        [Test]
        public void SetThenGet_EveryLevel_PersistsAcrossHandlers() {
            _handler.SetGlobalSaveData(_serializer, new GlobalSaveData { MostRecentProfile = Profile });
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData {
                ProfileName = Profile, MostRecentState = State,
            });
            SaveState(Profile, State, "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, State, 2, Level(2, 55));
            _handler.SetRegionSaveData(_serializer, Profile, State, 2, 3, Region(3));

            SaveDataHandler fresh = Fresh();
            Assert.AreEqual(Profile, fresh.GetGlobalSaveData(_serializer).MostRecentProfile);
            Assert.AreEqual(State, fresh.GetProfileSaveData(_serializer, Profile).MostRecentState);
            Assert.AreEqual("Scene", fresh.GetStateSaveData(_serializer, Profile, State).CurrentScene);

            LevelSaveData level = fresh.GetLevelSaveData(_serializer, Profile, State, 2);
            Assert.AreEqual(2, level.LevelIndex);
            ObjectSaveData obj = level.Objects.Objects.Single();
            Assert.AreEqual(55ul, obj.InstanceID);
            Assert.IsTrue(obj.Destroyed);

            RegionSaveData region = fresh.GetRegionSaveData(_serializer, Profile, State, 2, 3);
            Assert.AreEqual(3, region.RegionIndex);
            Assert.IsTrue(region.Loaded);
        }

        [Test]
        public void Set_Twice_Overwrites() {
            SaveState(Profile, State, "First");
            SaveState(Profile, State, "Second");

            Assert.AreEqual("Second", Fresh().GetStateSaveData(_serializer, Profile, State).CurrentScene);
            CollectionAssert.AreEquivalent(new[] { State }, Fresh().GetAvailableStates(Profile));
        }

        [Test]
        public void MutatingDataAfterSet_DoesNotChangeSavedData() {
            // Suspected bug #6: the PlayerPrefs handler keeps the caller's object, so any later save writes
            // changes that were never saved.
            ProfileSaveData profile = new ProfileSaveData { ProfileName = Profile, MostRecentState = "Saved" };
            _handler.SetProfileSaveData(_serializer, Profile, profile);

            profile.MostRecentState = "Unsaved";
            SaveUnrelated();

            Assert.AreEqual("Saved", Fresh().GetProfileSaveData(_serializer, Profile).MostRecentState);
        }

        [Test]
        public void MutatingDataAfterGet_DoesNotChangeSavedData() {
            // Suspected bug #6: the PlayerPrefs handler returns its live object, so "reload the last save"
            // can't revert unsaved changes.
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData {
                ProfileName = Profile, MostRecentState = "Saved",
            });

            _handler.GetProfileSaveData(_serializer, Profile).MostRecentState = "Unsaved";
            SaveUnrelated();

            Assert.AreEqual("Saved", _handler.GetProfileSaveData(_serializer, Profile).MostRecentState);
            Assert.AreEqual("Saved", Fresh().GetProfileSaveData(_serializer, Profile).MostRecentState);
        }

        [Test]
        public void FailedSave_KeepsPreviousData() {
            // Suspected bug #3: the file handler truncates the file before serializing.
            _handler.SetGlobalSaveData(_serializer, new GlobalSaveData { MostRecentProfile = "Good" });
            GlobalSaveData bad = new GlobalSaveData { MostRecentProfile = "Bad" };
            bad.SetCustomData("loop", new SelfReferencingValue());

            LogAssert.ignoreFailingMessages = true;
            try {
                _handler.SetGlobalSaveData(_serializer, bad);
            } catch (Exception) {
                // Whether a failed save throws or logs is checked elsewhere; this test is about the old data.
            }

            Assert.AreEqual("Good", Fresh().GetGlobalSaveData(_serializer).MostRecentProfile);
        }

        [Test]
        public void FailedSave_DoesNotBlockLaterSaves() {
            // Suspected bug #6: the PlayerPrefs handler keeps the unserializable data, so every later save fails.
            ProfileSaveData bad = new ProfileSaveData { ProfileName = Profile };
            bad.SetCustomData("loop", new SelfReferencingValue());

            LogAssert.ignoreFailingMessages = true;
            try {
                _handler.SetProfileSaveData(_serializer, Profile, bad);
            } catch (Exception) {
                // See FailedSave_KeepsPreviousData.
            }

            Assert.DoesNotThrow(() => _handler.SetGlobalSaveData(
                                    _serializer, new GlobalSaveData { MostRecentProfile = "Later" }));
            Assert.AreEqual("Later", Fresh().GetGlobalSaveData(_serializer).MostRecentProfile);
        }

        #endregion

        #region Listings

        [Test]
        public void GetAvailableProfiles_ListsSavedProfiles() {
            _handler.SetProfileSaveData(_serializer, "A", new ProfileSaveData { ProfileName = "A" });
            _handler.SetProfileSaveData(_serializer, "My_Profile", new ProfileSaveData { ProfileName = "My_Profile" });

            CollectionAssert.AreEquivalent(new[] { "A", "My_Profile" }, Fresh().GetAvailableProfiles());
        }

        [Test]
        public void GetAvailableStates_ListsSavedStatesOfThatProfile() {
            SaveState(Profile, "Autosave_0", "Scene");
            SaveState(Profile, "Quick", "Scene");
            SaveState("Other", "OtherState", "Scene");

            CollectionAssert.AreEquivalent(new[] { "Autosave_0", "Quick" }, Fresh().GetAvailableStates(Profile));
        }

        [Test]
        public void GetAvailableLevels_ListsSavedLevels() {
            SaveState(Profile, State, "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, State, 3, Level(3, 1));
            _handler.SetLevelSaveData(_serializer, Profile, State, 10, Level(10, 1));

            CollectionAssert.AreEquivalent(new[] { 3, 10 }, Fresh().GetAvailableLevels(Profile, State));
        }

        [Test]
        public void GetAvailableRegions_ListsSavedRegionsOfThatLevel() {
            SaveState(Profile, State, "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, State, 1, Level(1, 1));
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 0, Region(0));
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 5, Region(5));
            _handler.SetLevelSaveData(_serializer, Profile, State, 2, Level(2, 1));
            _handler.SetRegionSaveData(_serializer, Profile, State, 2, 9, Region(9));

            CollectionAssert.AreEquivalent(new[] { 0, 5 }, Fresh().GetAvailableRegions(Profile, State, 1));
        }

        #endregion

        #region Clear

        [Test]
        public void ClearGlobal_RemovesEverything() {
            _handler.SetGlobalSaveData(_serializer, new GlobalSaveData { MostRecentProfile = Profile });
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData { ProfileName = Profile });
            SaveState(Profile, State, "Scene");

            _handler.ClearGlobalSaveData();

            foreach (SaveDataHandler handler in new[] { _handler, Fresh() }) {
                Assert.IsNull(handler.GetGlobalSaveData(_serializer).MostRecentProfile);
                CollectionAssert.IsEmpty(handler.GetAvailableProfiles());
            }
        }

        [Test]
        public void ClearProfile_RemovesOnlyThatProfileAndItsStates() {
            _handler.SetProfileSaveData(_serializer, "A", new ProfileSaveData { ProfileName = "A" });
            _handler.SetProfileSaveData(_serializer, "B", new ProfileSaveData { ProfileName = "B" });
            SaveState("A", State, "SceneA");
            SaveState("B", State, "SceneB");

            _handler.ClearProfileSaveData("A");

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { "B" }, fresh.GetAvailableProfiles());
            CollectionAssert.IsEmpty(fresh.GetAvailableStates("A"));
            Assert.AreEqual("SceneB", fresh.GetStateSaveData(_serializer, "B", State).CurrentScene);
        }

        [Test]
        public void ClearState_RemovesStateAndItsLevels() {
            SaveState(Profile, "A", "SceneA");
            SaveState(Profile, "B", "SceneB");
            _handler.SetLevelSaveData(_serializer, Profile, "A", 1, Level(1, 1));

            _handler.ClearStateSaveData(Profile, "A");

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { "B" }, fresh.GetAvailableStates(Profile));
            CollectionAssert.IsEmpty(fresh.GetAvailableLevels(Profile, "A"));
            Assert.IsNull(fresh.GetStateSaveData(_serializer, Profile, "A").CurrentScene);
        }

        [Test]
        public void ClearLevel_RemovesLevelAndItsRegions() {
            SaveState(Profile, State, "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, State, 1, Level(1, 1));
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 2, Region(2));
            _handler.SetLevelSaveData(_serializer, Profile, State, 2, Level(2, 1));

            _handler.ClearLevelSaveData(Profile, State, 1);

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { 2 }, fresh.GetAvailableLevels(Profile, State));
            CollectionAssert.IsEmpty(fresh.GetAvailableRegions(Profile, State, 1));
            CollectionAssert.IsEmpty(fresh.GetLevelSaveData(_serializer, Profile, State, 1).Objects.Objects);
        }

        [Test]
        public void ClearRegion_RemovesOnlyThatRegion() {
            SaveState(Profile, State, "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, State, 1, Level(1, 1));
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 2, Region(2));
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 3, Region(3));

            _handler.ClearRegionSaveData(Profile, State, 1, 2);

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { 3 }, fresh.GetAvailableRegions(Profile, State, 1));
            Assert.AreEqual(1, fresh.GetLevelSaveData(_serializer, Profile, State, 1).Objects.Objects.Count());
        }

        [Test]
        public void ClearMissing_DoesNothing() {
            Assert.DoesNotThrow(() => {
                _handler.ClearGlobalSaveData();
                _handler.ClearProfileSaveData("Nobody");
                _handler.ClearStateSaveData("Nobody", "Nothing");
                _handler.ClearLevelSaveData("Nobody", "Nothing", 1);
                _handler.ClearRegionSaveData("Nobody", "Nothing", 1, 1);
            });
        }

        #endregion

        #region Copy

        [Test]
        public void CopyState_CopiesLevelsAndRegions() {
            SaveState(Profile, "Source", "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, "Source", 1, Level(1, 42));
            _handler.SetRegionSaveData(_serializer, Profile, "Source", 1, 2, Region(2));

            _handler.CopyStateSaveData(_serializer, Profile, "Source", "Dest");

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { "Source", "Dest" }, fresh.GetAvailableStates(Profile));
            Assert.AreEqual("Scene", fresh.GetStateSaveData(_serializer, Profile, "Dest").CurrentScene);
            Assert.AreEqual(42ul, fresh.GetLevelSaveData(_serializer, Profile, "Dest", 1)
                                       .Objects.Objects.Single().InstanceID);
            CollectionAssert.AreEquivalent(new[] { 2 }, fresh.GetAvailableRegions(Profile, "Dest", 1));
        }

        [Test]
        public void CopyState_DestinationIsNamedAfterItself() {
            // Suspected bug #24: the file handler copies State.json verbatim, so the copy's StateName is the
            // source's.
            SaveState(Profile, "Source", "Scene");

            _handler.CopyStateSaveData(_serializer, Profile, "Source", "Dest");

            Assert.AreEqual("Dest", Fresh().GetStateSaveData(_serializer, Profile, "Dest").StateName);
        }

        [Test]
        public void CopyState_IsIndependentOfSource() {
            SaveState(Profile, "Source", "Scene");
            _handler.SetLevelSaveData(_serializer, Profile, "Source", 1, Level(1, 42));

            _handler.CopyStateSaveData(_serializer, Profile, "Source", "Dest");
            _handler.SetLevelSaveData(_serializer, Profile, "Dest", 1, Level(1, 99));

            Assert.AreEqual(42ul, Fresh().GetLevelSaveData(_serializer, Profile, "Source", 1)
                                         .Objects.Objects.Single().InstanceID);
        }

        [Test]
        public void CopyState_ReplacesExistingDestination() {
            SaveState(Profile, "Source", "SourceScene");
            _handler.SetLevelSaveData(_serializer, Profile, "Source", 1, Level(1, 1));
            SaveState(Profile, "Dest", "DestScene");
            _handler.SetLevelSaveData(_serializer, Profile, "Dest", 5, Level(5, 1));

            _handler.CopyStateSaveData(_serializer, Profile, "Source", "Dest");

            SaveDataHandler fresh = Fresh();
            Assert.AreEqual("SourceScene", fresh.GetStateSaveData(_serializer, Profile, "Dest").CurrentScene);
            CollectionAssert.AreEquivalent(new[] { 1 }, fresh.GetAvailableLevels(Profile, "Dest"));
        }

        [Test]
        public void CopyState_MissingSource_LeavesEmptyDestinationWithoutThrowing() {
            // Suspected bug #2: the file handler clears the destination, then throws DirectoryNotFoundException.
            SaveState(Profile, "Dest", "DestScene");

            Assert.DoesNotThrow(() => _handler.CopyStateSaveData(_serializer, Profile, "NeverSaved", "Dest"));
            CollectionAssert.IsEmpty(Fresh().GetAvailableLevels(Profile, "Dest"));
        }

        [Test]
        public void CopyProfile_CopiesStatesAndIsNamedAfterItself() {
            // Suspected bug #24: the file handler copies Profile.json verbatim, so the copy's ProfileName is the
            // source's, and PersistenceManager.SaveProfileData would write the copy back over the source.
            _handler.SetProfileSaveData(_serializer, "Source", new ProfileSaveData {
                ProfileName = "Source", MostRecentState = State,
            });
            SaveState("Source", State, "Scene");

            _handler.CopyProfileSaveData(_serializer, "Source", "Dest");

            SaveDataHandler fresh = Fresh();
            CollectionAssert.AreEquivalent(new[] { "Source", "Dest" }, fresh.GetAvailableProfiles());
            ProfileSaveData dest = fresh.GetProfileSaveData(_serializer, "Dest");
            Assert.AreEqual(State, dest.MostRecentState);
            Assert.AreEqual("Scene", fresh.GetStateSaveData(_serializer, "Dest", State).CurrentScene);
            Assert.AreEqual("Dest", dest.ProfileName);
        }

        [Test]
        public void CopyProfile_ReplacesExistingDestination() {
            _handler.SetProfileSaveData(_serializer, "Source", new ProfileSaveData { ProfileName = "Source" });
            SaveState("Source", "S1", "Scene");
            _handler.SetProfileSaveData(_serializer, "Dest", new ProfileSaveData { ProfileName = "Dest" });
            SaveState("Dest", "Old", "Scene");

            _handler.CopyProfileSaveData(_serializer, "Source", "Dest");

            CollectionAssert.AreEquivalent(new[] { "S1" }, Fresh().GetAvailableStates("Dest"));
        }

        [Test]
        public void CopyProfile_MissingSource_LeavesEmptyDestinationWithoutThrowing() {
            // Suspected bug #2.
            _handler.SetProfileSaveData(_serializer, "Dest", new ProfileSaveData { ProfileName = "Dest" });
            SaveState("Dest", "Old", "Scene");

            Assert.DoesNotThrow(() => _handler.CopyProfileSaveData(_serializer, "NeverSaved", "Dest"));
            CollectionAssert.IsEmpty(Fresh().GetAvailableStates("Dest"));
        }

        #endregion
    }
}
