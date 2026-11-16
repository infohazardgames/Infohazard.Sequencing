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

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Checks that <see cref="SequencingTestFixture"/> isolates tests from real save data and static state.
    /// </summary>
    public class UnitTest_TestInfrastructure : SequencingTestFixture {
        [Test]
        public void SaveFolder_IsUniqueTempFolderOutsidePersistentData() {
            string fullSaveFolder = Path.GetFullPath(SaveFolder);
            Assert.That(fullSaveFolder, Does.StartWith(Path.GetFullPath(Path.GetTempPath())));
            Assert.That(fullSaveFolder, Does.Not.StartWith(Path.GetFullPath(Application.persistentDataPath)));
            Assert.IsFalse(Directory.Exists(SaveFolder), "Each test should start with no save data.");
        }

        [Test]
        public void FileHandler_WritesOnlyUnderSaveFolder() {
            PersistenceManager manager = CreatePersistenceManager();
            FileSaveDataHandler handler = manager.GetComponent<FileSaveDataHandler>();
            Assert.That(Path.GetFullPath(handler.GlobalDataFile), Does.StartWith(Path.GetFullPath(SaveFolder)));

            manager.LoadGlobalData();
            manager.LoadedGlobalData.MostRecentProfile = "Profile";
            manager.SaveGlobalData();

            Assert.IsTrue(File.Exists(handler.GlobalDataFile));
        }

        [Test]
        public void PlayerPrefsHandler_UsesTestOnlyKey() {
            PersistenceManager manager = CreatePersistenceManager(TestHandlerType.PlayerPrefs);
            PlayerPrefsSaveDataHandler handler = manager.GetComponent<PlayerPrefsSaveDataHandler>();
            string key = GetField<string>(handler, "_dataPrefName");
            Assert.That(key, Does.StartWith("Infohazard.Sequencing.Tests."));
            Assert.That(key, Does.Contain(Path.GetFileName(SaveFolder)));
        }

        [Test]
        public void CreatePersistenceManager_BecomesInstance() {
            PersistenceManager first = CreatePersistenceManager(storageName: "First");
            Assert.AreSame(first, PersistenceManager.Instance);
            PersistenceManager second = ReloadPersistenceManager(first, null, null, storageName: "Second");
            Assert.AreSame(second, PersistenceManager.Instance);
        }

        [Test]
        [TestCase(TestHandlerType.File)]
        [TestCase(TestHandlerType.PlayerPrefs)]
        public void ReloadPersistenceManager_ReadsWhatWasSaved(TestHandlerType handlerType) {
            PersistenceManager manager = CreatePersistenceManager(handlerType);
            manager.LoadGlobalData();
            manager.LoadedGlobalData.MostRecentProfile = "Saved";
            manager.SaveGlobalData();

            manager = ReloadPersistenceManager(manager, null, null, handlerType);
            Assert.AreEqual("Saved", manager.LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void StorageNames_AreIsolated() {
            PersistenceManager manager = CreatePersistenceManager(storageName: "A");
            manager.LoadGlobalData();
            manager.LoadedGlobalData.MostRecentProfile = "InA";
            manager.SaveGlobalData();

            manager = ReloadPersistenceManager(manager, null, null, storageName: "B");
            Assert.IsNull(manager.LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public void CreateSerializer_IsUsableInEditMode() {
            Serializer serializer = CreateSerializer(CreateGameObject("Serializer"));
            GlobalSaveData data = new GlobalSaveData { MostRecentProfile = "Profile" };

            using MemoryStream stream = new MemoryStream();
            serializer.Write(new NonClosingStream(stream), data);
            stream.Position = 0;

            Assert.IsTrue(serializer.Read(stream, out GlobalSaveData read));
            Assert.AreEqual("Profile", read.MostRecentProfile);
        }

        [Test]
        public void CreateManifest_InstallsInstanceAndLookupsWork() {
            LevelManifestRegionEntry region = CreateRegionEntry(3, 1, "Assets/Test/Region3.unity");
            LevelManifestLevelEntry level = CreateLevelEntry(1, "Assets/Test/Level1.unity", region);
            LevelManifest manifest = CreateManifest(level);

            Assert.AreSame(manifest, LevelManifest.Instance);
            Assert.AreSame(level, LevelManifest.Instance.GetLevelWithID(1));
            Assert.AreSame(level, LevelManifest.Instance.GetLevelWithSceneNameOrPath("Level1"));
            Assert.AreSame(level, LevelManifest.Instance.GetLevelWithSceneNameOrPath("Assets/Test/Level1.unity"));
            Assert.AreSame(region, level.GetRegionWithID(3));
            Assert.AreSame(region, level.GetRegionWithSceneName("Region3"));
            Assert.AreEqual(1, region.LevelID);
        }

        [Test]
        public void ResetAllStaticState_ClearsCurrentLevelAndManifest() {
            LevelManifest manifest = CreateManifest(CreateLevelEntry(1, "Assets/Test/Level1.unity"));
            LevelRoot root = CreateLevelRoot(manifest.Levels[0]);
            root.Initialize();
            Assert.AreSame(root, LevelRoot.Current);

            ResetAllStaticState();

            Assert.IsNull(LevelRoot.Current);
            // Installing a new manifest afterwards works, so the old one was forgotten.
            LevelManifest second = CreateManifest();
            Assert.AreSame(second, LevelManifest.Instance);
        }

        [Test]
        public void GetStaticStateLeaks_ReportsCurrentLevel() {
            LevelManifest manifest = CreateManifest(CreateLevelEntry(1, "Assets/Test/Level1.unity"));
            LevelRoot root = CreateLevelRoot(manifest.Levels[0]);
            root.Initialize();

            Assert.That(GetStaticStateLeaks(), Has.Some.Contains("LevelRoot.Current"));

            root.Cleanup();
            Assert.That(GetStaticStateLeaks(), Is.Empty);
        }

        private LevelRoot CreateLevelRoot(LevelManifestLevelEntry entry) {
            LevelRoot root = CreateGameObject("Level root").AddComponent<LevelRoot>();
            SetField(root, "_manifestEntry", entry);
            return root;
        }
    }
}
