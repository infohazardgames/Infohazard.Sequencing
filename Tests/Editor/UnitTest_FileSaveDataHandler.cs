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
using System.Text.RegularExpressions;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// File-handler behavior that isn't part of the shared handler contract: on-disk layout, damaged files and
    /// stray files.
    /// </summary>
    public class UnitTest_FileSaveDataHandler : SequencingTestFixture {
        private const string Profile = "Profile";
        private const string State = "State";

        private Serializer _serializer;
        private FileSaveDataHandler _handler;

        [SetUp]
        public void SetUp() {
            GameObject obj = CreateGameObject("Handler");
            _serializer = CreateSerializer(obj);
            _handler = (FileSaveDataHandler) CreateHandler(obj, TestHandlerType.File);
        }

        private void SaveAllLevels() {
            _handler.SetGlobalSaveData(_serializer, new GlobalSaveData());
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData { ProfileName = Profile });
            _handler.SetStateSaveData(_serializer, Profile, State, new StateSaveData { StateName = State });
            _handler.SetLevelSaveData(_serializer, Profile, State, 1, new LevelSaveData { LevelIndex = 1 });
            _handler.SetRegionSaveData(_serializer, Profile, State, 1, 2, new RegionSaveData { RegionIndex = 2 });
        }

        [Test]
        public void Layout_MatchesDocumentedPaths() {
            SaveAllLevels();

            string root = _handler.GlobalDataFolder;
            Assert.IsTrue(File.Exists(Path.Combine(root, "Global.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Profile_Profile", "Profile.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Profile_Profile", "State_State", "State.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Profile_Profile", "State_State", "Level_1", "Level.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Profile_Profile", "State_State", "Level_1",
                                                   "Region_2.json")));
        }

        [Test]
        public void PathHelpers_AgreeWithLayout() {
            SaveAllLevels();
            Assert.IsTrue(File.Exists(_handler.GlobalDataFile));
            Assert.IsTrue(File.Exists(_handler.GetProfileDataFile(Profile)));
            Assert.IsTrue(File.Exists(_handler.GetStateDataFile(Profile, State)));
            Assert.IsTrue(File.Exists(_handler.GetLevelDataFile(Profile, State, 1)));
            Assert.IsTrue(File.Exists(_handler.GetRegionDataFile(Profile, State, 1, 2)));
        }

        [Test]
        public void CorruptFile_LoadsDefaultsAndLogs() {
            SaveAllLevels();
            File.WriteAllText(_handler.GetStateDataFile(Profile, State), "{ \"_stateName\": ");

            LogAssert.Expect(LogType.Exception, new Regex("Json"));
            StateSaveData state = _handler.GetStateSaveData(_serializer, Profile, State);

            Assert.IsNotNull(state);
            Assert.AreEqual(State, state.StateName);
        }

        [Test]
        public void EmptyFile_LoadsDefaults() {
            SaveAllLevels();
            File.WriteAllText(_handler.GetLevelDataFile(Profile, State, 1), "");

            LevelSaveData level = _handler.GetLevelSaveData(_serializer, Profile, State, 1);

            Assert.IsNotNull(level);
            Assert.AreEqual(1, level.LevelIndex);
        }

        [Test]
        public void LockedFile_LoadsDefaultsAndLogs() {
            SaveAllLevels();
            _handler.SetProfileSaveData(_serializer, Profile, new ProfileSaveData {
                ProfileName = Profile, MostRecentState = State,
            });

            using (new FileStream(_handler.GetProfileDataFile(Profile), FileMode.Open, FileAccess.Read,
                                  FileShare.None)) {
                LogAssert.Expect(LogType.Exception, new Regex("IOException"));
                ProfileSaveData profile = _handler.GetProfileSaveData(_serializer, Profile);
                Assert.IsNotNull(profile);
                Assert.IsNull(profile.MostRecentState);
            }

            Assert.AreEqual(State, _handler.GetProfileSaveData(_serializer, Profile).MostRecentState,
                            "The data should be readable again once the file is unlocked.");
        }

        [Test]
        public void GetAvailableRegions_IgnoresLevelFileAndStrayFiles() {
            SaveAllLevels();
            string levelFolder = _handler.GetLevelDataFolder(Profile, State, 1);
            File.WriteAllText(Path.Combine(levelFolder, "notes.txt"), "");
            File.WriteAllText(Path.Combine(levelFolder, "Region_x.json"), "");
            File.WriteAllText(Path.Combine(levelFolder, "Region_3.json.bak"), "");
            File.WriteAllText(Path.Combine(levelFolder, "Region_.json"), "");

            CollectionAssert.AreEquivalent(new[] { 2 }, _handler.GetAvailableRegions(Profile, State, 1));
        }

        [Test]
        public void GetAvailableStates_IgnoresFoldersWithoutStateFile() {
            SaveAllLevels();
            Directory.CreateDirectory(Path.Combine(_handler.GetProfileDataFolder(Profile), "State_Empty"));
            Directory.CreateDirectory(Path.Combine(_handler.GetProfileDataFolder(Profile), "Junk"));

            CollectionAssert.AreEquivalent(new[] { State }, _handler.GetAvailableStates(Profile));
        }

        [Test]
        public void GetAvailableLevels_IgnoresNonNumericFolders() {
            SaveAllLevels();
            string stray = Path.Combine(_handler.GetStateDataFolder(Profile, State), "Level_abc");
            Directory.CreateDirectory(stray);
            File.WriteAllText(Path.Combine(stray, "Level.json"), "");

            CollectionAssert.AreEquivalent(new[] { 1 }, _handler.GetAvailableLevels(Profile, State));
        }
    }
}
