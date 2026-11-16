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
    /// Dynamic (spawned) persisted objects, static-to-dynamic conversion, and moving objects between regions.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_DynamicObjects : PersistenceSceneFixture {
        private const string MoverKey = TestScenes.MoverPrefabKey;
        private const string TogglesKey = "c0ffee00c0ffee00c0ffee00c0ff0003";
        private const string UnregisteredKey = "c0ffee00c0ffee00c0ffee00c0ff0099";

        #region Spawned objects

        [Test]
        public async Task SpawnedObject_IsSavedAndRespawnedWithSameIDAndState() {
            RegisterMoverPrefab();
            PersistedLevelRoot level = await LoadPersistedLevel();
            PersistedGameObject spawned = await SpawnInLevel(MoverKey, new Vector3(7, 0, 0));
            await UniTask.Yield();

            Assert.IsTrue(spawned.Initialized);
            Assert.IsTrue(spawned.IsDynamicInstance);
            Assert.AreNotEqual(0ul, spawned.InstanceID);
            Assert.AreEqual(MoverKey, spawned.SaveData.DynamicPrefabID);
            Assert.IsTrue(level.SaveData.Objects.Objects.Contains(spawned.SaveData));
            ulong id = spawned.InstanceID;
            spawned.transform.position = new Vector3(8, 0, 0);

            level = await SaveAndReload("Slot");
            await UniTask.Yield();

            PersistedGameObject respawned = DynamicInstancesOf(MoverKey).Single();
            Assert.AreEqual(id, respawned.InstanceID);
            Assert.AreEqual(new Vector3(8, 0, 0), respawned.transform.position);
            Assert.AreEqual(level.gameObject.scene, respawned.gameObject.scene);
            Assert.AreSame(level.DynamicObjectRoot, respawned.transform.parent);
        }

        [Test]
        public async Task DespawnedObject_IsGoneAfterReload() {
            RegisterMoverPrefab();
            await LoadPersistedLevel();
            PersistedGameObject spawned = await SpawnInLevel(MoverKey, Vector3.zero);
            await UniTask.Yield();
            ulong id = spawned.InstanceID;

            Spawnable.Despawn(spawned.gameObject);

            Assert.IsFalse(PersistedGameObject.TryGetObjectWithID(id, out _));
            PersistedLevelRoot level = await SaveAndReload("Slot");
            await UniTask.Yield();
            CollectionAssert.IsEmpty(DynamicInstancesOf(MoverKey));
            Assert.IsFalse(level.SaveData.Objects.Objects.Any(o => o.InstanceID == id));
        }

        [Test]
        public async Task PooledInstance_RespawnsAsNewObject() {
            RegisterMoverPrefab();
            await LoadPersistedLevel();
            PersistedGameObject first = await SpawnInLevel(MoverKey, Vector3.zero);
            await UniTask.Yield();
            ulong firstID = first.InstanceID;
            Spawnable.Despawn(first.gameObject);

            PersistedGameObject second = await SpawnInLevel(MoverKey, Vector3.one);
            await UniTask.Yield();

            Assert.IsTrue(second.Initialized);
            Assert.IsTrue(second.IsDynamicInstance);
            Assert.AreNotEqual(firstID, second.InstanceID);
            Assert.IsFalse(PersistedGameObject.TryGetObjectWithID(firstID, out _));
            Assert.AreSame(second, PersistedGameObject.GetObjectWithID(second.InstanceID));
        }

        [Test]
        public async Task MissingPrefabAtLoad_LoadStillCompletes() {
            PersistedLevelRoot level = await LoadPersistedLevel();
            ObjectSaveData orphan = level.SaveData.Objects.RegisterNewDynamicObject(UnregisteredKey);
            orphan.Initialized = true;

            LogAssert.ignoreFailingMessages = true;
            level = await SaveAndReload("Slot");

            Assert.IsTrue(level.ObjectsLoaded);
            Assert.IsTrue(level.SaveData.Objects.Objects.Any(o => o.InstanceID == orphan.InstanceID),
                          "The orphaned entry is kept (documents current behavior).");
            CollectionAssert.IsEmpty(DynamicInstancesOf(UnregisteredKey));
        }

        [Test]
        public async Task TwoPersistedComponentsOfSameType_KeepSeparateState() {
            // Suspected bug #21: both components get the same ComponentID, so they share one state entry.
            RegisterPersistedPrefab(TogglesKey, prefab => {
                for (int i = 0; i < 2; i++) {
                    TestToggle toggle = prefab.AddComponent<TestToggle>();
                    SetField(prefab.AddComponent<PersistedComponentEnabled>(), "_target", toggle);
                }
            });
            await LoadPersistedLevel();
            PersistedGameObject spawned = await SpawnInLevel(TogglesKey, Vector3.zero);
            await UniTask.Yield();
            TestToggle[] toggles = spawned.GetComponents<TestToggle>();
            toggles[0].enabled = false;
            toggles[1].enabled = true;

            await SaveAndReload("Slot");
            await UniTask.Yield();

            toggles = DynamicInstancesOf(TogglesKey).Single().GetComponents<TestToggle>();
            Assert.IsFalse(toggles[0].enabled, "The first toggle's state was lost.");
            Assert.IsTrue(toggles[1].enabled);
        }

        #endregion

        #region Static-to-dynamic conversion

        [Test]
        public async Task ImmediateConvert_BecomesDynamicAndRespawnsOnce() {
            RegisterMoverPrefab(TestScenes.ConverterPrefabKey);
            PersistedLevelRoot level = await LoadPersistedLevelActivating(TestScenes.GoodConverter);
            PersistedGameObject converter = Find<PersistedGameObject>(TestScenes.GoodConverter);
            await UniTask.Yield();

            Assert.AreNotEqual(7006ul, converter.InstanceID);
            Assert.IsTrue(converter.SaveData.IsDynamicInstance);
            Assert.AreEqual(TestScenes.ConverterPrefabKey, converter.SaveData.DynamicPrefabID);
            Assert.IsTrue(level.SaveData.Objects.Objects.Single(o => o.InstanceID == 7006).Destroyed);
            ulong id = converter.InstanceID;
            converter.transform.position = new Vector3(60, 0, 0);

            await SaveAndReload("Slot");
            await UniTask.Yield();

            PersistedGameObject respawned = DynamicInstancesOf(TestScenes.ConverterPrefabKey).Single();
            Assert.AreEqual(id, respawned.InstanceID);
            Assert.AreEqual(new Vector3(60, 0, 0), respawned.transform.position);
        }

        [Test]
        public async Task ImmediateConvert_MarksObjectDynamic() {
            // Suspected bug #28: ConvertToDynamicInstance switches to dynamic save data but leaves
            // IsDynamicInstance false.
            RegisterMoverPrefab(TestScenes.ConverterPrefabKey);
            await LoadPersistedLevelActivating(TestScenes.GoodConverter);
            PersistedGameObject converter = Find<PersistedGameObject>(TestScenes.GoodConverter);
            await UniTask.Yield();

            Assert.IsTrue(converter.IsDynamicInstance);
        }

        [Test]
        public async Task ImmediateConvert_WithoutPrefab_KeepsStaticObject() {
            // Suspected bug #9: conversion doesn't check the prefab reference, so the object vanishes on reload.
            LogAssert.ignoreFailingMessages = true;
            await LoadPersistedLevelActivating(TestScenes.BadConverter);
            PersistedGameObject converter = Find<PersistedGameObject>(TestScenes.BadConverter);
            await UniTask.Yield();
            Assert.AreEqual(7005ul, converter.InstanceID, "An object with no prefab can't become dynamic.");

            SaveAs("Slot");
            await UnloadAll();
            Manager = ReloadPersistenceManager(Manager, Profile, "Slot");
            await LoadPersistedLevelActivating(TestScenes.BadConverter);
            await Frames(2);

            Assert.IsNotNull(GameObject.Find(TestScenes.BadConverter), "The object vanished after reloading.");
        }

        #endregion

        #region Moving between regions

        private async UniTask<(CanMoveBetweenRegions mover, PersistedGameObject persisted)> LoadMover() {
            RegisterMoverPrefab();
            await LoadPersistedLevel(TestScenes.PRegionA, TestScenes.PRegionB);
            CanMoveBetweenRegions mover = Find<CanMoveBetweenRegions>(TestScenes.Mover);
            await UniTask.Yield();
            return (mover, mover.GetComponent<PersistedGameObject>());
        }

        [Test]
        public async Task CanTransitionTo_OnlyOtherPersistedRegions() {
            (CanMoveBetweenRegions mover, _) = await LoadMover();

            Assert.IsFalse(mover.CanTransitionTo(GetRegion(TestScenes.PRegionA)), "Already in region A.");
            Assert.IsTrue(mover.CanTransitionTo(GetRegion(TestScenes.PRegionB)));
        }

        [Test]
        public async Task MoveToOtherRegion_MovesSceneAndSaveData() {
            (CanMoveBetweenRegions mover, PersistedGameObject persisted) = await LoadMover();
            PersistedRegionRoot regionA = GetRegion(TestScenes.PRegionA);
            PersistedRegionRoot regionB = GetRegion(TestScenes.PRegionB);
            ulong id = persisted.InstanceID;
            RegionRoot oldRegion = null, newRegion = null;
            mover.CurrentRegionChanged += (o, n) => (oldRegion, newRegion) = (o, n);

            mover.CurrentRegion = regionB;

            Assert.AreSame(regionB, persisted.Region);
            Assert.AreEqual(regionB.gameObject.scene, persisted.gameObject.scene);
            Assert.AreSame(regionB.DynamicObjectRoot, persisted.transform.parent);
            Assert.AreSame(regionA, oldRegion);
            Assert.AreSame(regionB, newRegion);
            Assert.IsTrue(persisted.IsDynamicInstance);
            Assert.IsTrue(regionB.SaveData.Objects.Objects.Contains(persisted.SaveData));
            Assert.IsTrue(regionA.SaveData.Objects.Objects.Single(o => o.InstanceID == id).Destroyed);
            Assert.AreSame(persisted, PersistedGameObject.GetObjectWithID(persisted.InstanceID));
        }

        [Test]
        public async Task MovedObject_ReloadsInNewRegionOnly() {
            (CanMoveBetweenRegions mover, PersistedGameObject persisted) = await LoadMover();
            mover.CurrentRegion = GetRegion(TestScenes.PRegionB);
            persisted.transform.position = new Vector3(0, 0, 25);

            await SaveAndReload("Slot", TestScenes.PRegionA, TestScenes.PRegionB);
            await Frames(2);

            PersistedGameObject moved = DynamicInstancesOf(MoverKey).Single();
            Assert.AreEqual(GetRegion(TestScenes.PRegionB).gameObject.scene, moved.gameObject.scene);
            Assert.AreEqual(new Vector3(0, 0, 25), moved.transform.position);
            Assert.IsNull(GameObject.Find(TestScenes.Mover), "The static copy in region A should stay destroyed.");
        }

        [Test]
        public async Task OldRegionReloaded_DoesNotTakeOverMovedObjectsID() {
            // Suspected bug #8: the moved object keeps its static ID, which region A's destroyed copy reuses.
            (CanMoveBetweenRegions mover, PersistedGameObject persisted) = await LoadMover();
            mover.CurrentRegion = GetRegion(TestScenes.PRegionB);
            ulong id = persisted.InstanceID;

            await UnloadAndWait(TestScenes.PRegionA);
            await LoadPersistedRegion(TestScenes.PRegionA);
            await Frames(2);

            Assert.AreSame(persisted, PersistedGameObject.GetObjectWithID(id),
                           "Region A's destroyed copy took over the moved object's ID.");
        }

        [Test]
        public async Task SetCurrentRegionNull_DoesNotThrow() {
            // Suspected bug #19.
            (CanMoveBetweenRegions mover, _) = await LoadMover();
            Assert.DoesNotThrow(() => mover.CurrentRegion = null);
        }

        #endregion
    }
}
