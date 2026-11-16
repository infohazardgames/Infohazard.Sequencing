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
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Static PersistedGameObjects and PersistedLevelRoot/PersistedRegionRoot bookkeeping.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_PersistedObjects : PersistenceSceneFixture {
        #region Registration

        [Test]
        public async Task StaticObjects_RegisterInLevelOrRegionContainer() {
            PersistedLevelRoot level = await LoadPersistedLevel(TestScenes.PRegionA);
            PersistedRegionRoot regionA = GetRegion(TestScenes.PRegionA);

            PersistedGameObject crate = Find<PersistedGameObject>(TestScenes.StaticCrate);
            PersistedGameObject regionCrate = Find<PersistedGameObject>(TestScenes.RegionACrate);

            Assert.IsTrue(crate.Initialized);
            Assert.AreSame(level, crate.Level);
            Assert.IsNull(crate.Region);
            Assert.IsTrue(level.SaveData.Objects.Objects.Contains(crate.SaveData));

            Assert.IsTrue(regionCrate.Initialized);
            Assert.AreSame(regionA, regionCrate.Region);
            Assert.IsTrue(regionA.SaveData.Objects.Objects.Contains(regionCrate.SaveData));
            Assert.IsFalse(level.SaveData.Objects.Objects.Contains(regionCrate.SaveData));

            Assert.AreSame(crate, PersistedGameObject.GetObjectWithID(crate.InstanceID));
            Assert.IsTrue(PersistedGameObject.TryGetObjectWithID(regionCrate.InstanceID, out PersistedGameObject found));
            Assert.AreSame(regionCrate, found);
            Assert.IsTrue(level.ObjectsLoaded);
            Assert.IsTrue(regionA.ObjectsLoaded);
        }

        [Test]
        public async Task GetObjectsWithIDs_SkipsUnknownIDs() {
            await LoadPersistedLevel();
            PersistedGameObject crate = Find<PersistedGameObject>(TestScenes.StaticCrate);

            CollectionAssert.AreEqual(new[] { crate },
                                      PersistedGameObject.GetObjectsWithIDs(new[] { crate.InstanceID, 12345ul }));
            CollectionAssert.IsEmpty(PersistedGameObject.GetObjectsWithIDs(null));
        }

        [Test]
        public async Task RegionLoadedLater_InitializesItsObjects() {
            await LoadPersistedLevel();

            PersistedRegionRoot regionB = await LoadPersistedRegion(TestScenes.PRegionB);

            PersistedGameObject crate = Find<PersistedGameObject>(TestScenes.RegionBCrate);
            Assert.IsTrue(crate.Initialized);
            Assert.AreSame(regionB, crate.Region);
        }

        [Test]
        public async Task UnloadingScenes_UnregistersObjects() {
            await LoadPersistedLevel(TestScenes.PRegionA);
            Assert.That(PersistedGameObject.Objects, Is.Not.Empty);

            await UnloadAll();
        }

        #endregion

        #region Destroyed objects

        [Test]
        public async Task DestroyedStaticObject_StaysDestroyedAfterReload() {
            await LoadPersistedLevel();
            GameObject destroyable = GameObject.Find(TestScenes.StaticDestroyable);
            ulong id = destroyable.GetComponent<PersistedGameObject>().InstanceID;

            Spawnable.Despawn(destroyable);
            await UniTask.Yield();
            Assert.IsFalse(PersistedGameObject.TryGetObjectWithID(id, out _));

            PersistedLevelRoot level = await SaveAndReload("Slot");
            await Frames(2);

            Assert.IsNull(GameObject.Find(TestScenes.StaticDestroyable));
            Assert.IsTrue(level.SaveData.Objects.Objects.Single(o => o.InstanceID == id).Destroyed);
        }

        [Test]
        public async Task UndestroyedStaticObject_IsKeptAfterReload() {
            await LoadPersistedLevel();
            await SaveAndReload("Slot");
            await Frames(2);
            Assert.IsNotNull(GameObject.Find(TestScenes.StaticDestroyable));
        }

        #endregion

        #region PersistedLevelRoot

        [Test]
        public async Task RegionData_LoadedFlagTracksRegionScenes() {
            PersistedLevelRoot level = await LoadPersistedLevel(TestScenes.PRegionA, TestScenes.PRegionB);
            Assert.IsTrue(level.GetOrLoadRegionData(PRegionAEntry).Loaded);
            Assert.IsTrue(level.GetOrLoadRegionData(PRegionBEntry).Loaded);

            await UnloadAndWait(TestScenes.PRegionB);

            Assert.IsTrue(level.GetOrLoadRegionData(PRegionAEntry).Loaded);
            Assert.IsFalse(level.GetOrLoadRegionData(PRegionBEntry).Loaded);
        }

        [Test]
        public async Task Save_RecordsWhichRegionsWereLoaded() {
            await LoadPersistedLevel(TestScenes.PRegionA, TestScenes.PRegionB);
            await UnloadAndWait(TestScenes.PRegionB);

            SaveAs("Slot");
            await UnloadAll();
            Manager = ReloadPersistenceManager(Manager, Profile, "Slot");

            Assert.IsTrue(Manager.GetRegionData(TestScenes.PLevelID, TestScenes.PRegionAID).Loaded);
            Assert.IsFalse(Manager.GetRegionData(TestScenes.PLevelID, TestScenes.PRegionBID).Loaded);
        }

        [Test]
        public async Task Save_WritesObjectStateFromWillSave() {
            PersistedLevelRoot level = await LoadPersistedLevel();
            PersistedGameObject crate = Find<PersistedGameObject>(TestScenes.StaticCrate);
            int willSave = 0;
            level.WillSave += () => willSave++;
            crate.transform.position = new Vector3(50, 0, 0);

            SaveAs("Slot");

            Assert.AreEqual(1, willSave);
            LevelSaveData saved = Manager.GetLevelData(TestScenes.PLevelID);
            ObjectSaveData crateData = saved.Objects.Objects.Single(o => o.InstanceID == crate.InstanceID);
            object intermediate = crateData.CustomData.Values.Single(v => v.ToString().Contains("_position"));
            Assert.IsTrue(Manager.GetComponent<Serializer>()
                                 .IntermediateToObject(intermediate, out PersistedTransform.StateInfo state));
            Assert.AreEqual(new Vector3(50, 0, 0), state.Position);
        }

        [Test]
        public async Task UnloadedRegion_KeepsObjectStateUntilSaved() {
            await LoadPersistedLevel(TestScenes.PRegionA);
            GameObject crate = GameObject.Find(TestScenes.RegionACrate);
            crate.transform.position = new Vector3(0, 5, 10);

            // The region unloads before the save; its objects write their state as it unloads.
            await UnloadAndWait(TestScenes.PRegionA);
            SaveAs("Slot");
            await UnloadAll();
            Manager = ReloadPersistenceManager(Manager, Profile, "Slot");
            await LoadPersistedLevel(TestScenes.PRegionA);

            Assert.AreEqual(new Vector3(0, 5, 10), GameObject.Find(TestScenes.RegionACrate).transform.position);
        }

        [Test]
        public async Task UnchangedLevel_DoesNotRewriteLevelData() {
            await LoadPersistedLevel();
            SaveAs("Slot");
            await UnloadAll();
            Manager = ReloadPersistenceManager(Manager, Profile, "Slot");
            await LoadPersistedLevel();
            FileSaveDataHandler handler = Manager.GetComponent<FileSaveDataHandler>();
            string levelFile = handler.GetLevelDataFile(Profile, "Slot", TestScenes.PLevelID);
            System.DateTime written = System.IO.File.GetLastWriteTimeUtc(levelFile);
            await Frames(2);

            Manager.SaveStateDataAs("Slot");
            PersistedLevelRoot.Current.Save();

            Assert.AreEqual(written, System.IO.File.GetLastWriteTimeUtc(levelFile),
                            "Level data that didn't change shouldn't be rewritten.");
        }

        #endregion
    }
}
