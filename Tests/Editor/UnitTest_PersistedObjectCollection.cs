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
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Registration, destruction and static-to-dynamic conversion of persisted objects.
    /// </summary>
    public class UnitTest_PersistedObjectCollection {
        private const string PrefabID = "prefab-guid";

        private PersistedObjectCollection _collection;
        private StateChangeCounter _counter;

        [SetUp]
        public void SetUp() {
            _collection = new PersistedObjectCollection();
            _counter = new StateChangeCounter(_collection);
        }

        [TearDown]
        public void TearDown() {
            _counter.Dispose();
        }

        #region RegisterExistingObject

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void RegisterExistingObject_New_CreatesEntryAndNotifies(bool isDynamic) {
            ObjectSaveData data = _collection.RegisterExistingObject(42, isDynamic);

            Assert.AreEqual(42ul, data.InstanceID);
            Assert.AreEqual(isDynamic, data.IsDynamicInstance);
            Assert.IsNull(data.DynamicPrefabID);
            Assert.IsFalse(data.Destroyed);
            Assert.AreEqual(1, _counter.Count);
            CollectionAssert.AreEquivalent(new[] { data }, _collection.Objects);
        }

        [Test]
        public void RegisterExistingObject_Existing_ReturnsSameEntryWithoutNotifying() {
            ObjectSaveData first = _collection.RegisterExistingObject(42, false);
            _counter.Reset();

            ObjectSaveData second = _collection.RegisterExistingObject(42, false);

            Assert.AreSame(first, second);
            Assert.AreEqual(0, _counter.Count);
            Assert.AreEqual(1, _collection.Objects.Count());
        }

        #endregion

        #region RegisterNewDynamicObject

        [Test]
        public void RegisterNewDynamicObject_GeneratesNonZeroID() {
            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID);

            Assert.AreNotEqual(0ul, data.InstanceID);
            Assert.IsTrue(data.IsDynamicInstance);
            Assert.AreEqual(PrefabID, data.DynamicPrefabID);
            Assert.AreEqual(1, _counter.Count);
        }

        [Test]
        public void RegisterNewDynamicObject_ManyObjects_IDsAreUnique() {
            List<ulong> ids = Enumerable.Range(0, 1000)
                                        .Select(_ => _collection.RegisterNewDynamicObject(PrefabID).InstanceID)
                                        .ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.AreEqual(1000, _collection.Objects.Count());
        }

        [Test]
        public void RegisterNewDynamicObject_FreeRequestedID_UsesIt() {
            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID, 1234);
            Assert.AreEqual(1234ul, data.InstanceID);
        }

        [Test]
        public void RegisterNewDynamicObject_TakenRequestedID_GeneratesAnother() {
            ObjectSaveData existing = _collection.RegisterExistingObject(1234, false);

            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID, 1234);

            Assert.AreNotEqual(1234ul, data.InstanceID);
            Assert.AreNotEqual(0ul, data.InstanceID);
            Assert.IsFalse(existing.IsDynamicInstance, "The existing entry must not be replaced.");
            Assert.AreEqual(2, _collection.Objects.Count());
        }

        [Test]
        public void RegisterNewDynamicObject_RequestedZero_GeneratesNonZeroID() {
            // Suspected bug #23: 0 means "no ID" to PersistedGameObject, but a requested 0 is used as-is.
            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID, 0);
            Assert.AreNotEqual(0ul, data.InstanceID);
        }

        #endregion

        #region RegisterObjectDestroyed

        [Test]
        public void RegisterObjectDestroyed_Dynamic_RemovesEntry() {
            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID);
            _counter.Reset();

            _collection.RegisterObjectDestroyed(data.InstanceID);

            CollectionAssert.IsEmpty(_collection.Objects);
            Assert.AreEqual(1, _counter.Count);
            Assert.IsFalse(data.HasStateChangedListeners, "A removed entry should be unhooked.");
        }

        [Test]
        public void RegisterObjectDestroyed_Static_KeepsEntryAndMarksDestroyed() {
            ObjectSaveData data = _collection.RegisterExistingObject(42, false);
            _counter.Reset();

            _collection.RegisterObjectDestroyed(42);

            Assert.IsTrue(data.Destroyed);
            CollectionAssert.AreEquivalent(new[] { data }, _collection.Objects);
            Assert.That(_counter.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RegisterObjectDestroyed_UnknownID_LogsErrorAndChangesNothing() {
            _collection.RegisterExistingObject(42, false);
            _counter.Reset();

            LogAssert.Expect(LogType.Error, "Invalid instanceID 99 passed to RegisterObjectDestroyed.");
            _collection.RegisterObjectDestroyed(99);

            Assert.AreEqual(0, _counter.Count);
            Assert.AreEqual(1, _collection.Objects.Count());
        }

        [Test]
        public void RemovedDynamicObject_NoLongerNotifiesCollection() {
            ObjectSaveData data = _collection.RegisterNewDynamicObject(PrefabID);
            _collection.RegisterObjectDestroyed(data.InstanceID);
            _counter.Reset();

            data.Destroyed = true;

            Assert.AreEqual(0, _counter.Count);
        }

        #endregion

        #region ConvertStaticObjectToDynamic

        [Test]
        public void ConvertStaticObjectToDynamic_MarksStaticDestroyedAndCopiesState() {
            ObjectSaveData staticData = _collection.RegisterExistingObject(42, false);
            PersistedData componentState = new PersistedData();
            staticData.SetCustomData("component", componentState);
            staticData.Initialized = true;

            ObjectSaveData dynamicData = _collection.ConvertStaticObjectToDynamic(42, PrefabID);

            Assert.IsTrue(staticData.Destroyed);
            Assert.AreNotEqual(42ul, dynamicData.InstanceID);
            Assert.AreNotEqual(0ul, dynamicData.InstanceID);
            Assert.IsTrue(dynamicData.IsDynamicInstance);
            Assert.AreEqual(PrefabID, dynamicData.DynamicPrefabID);
            Assert.IsTrue(dynamicData.Initialized);
            Assert.IsFalse(dynamicData.Destroyed);
            Assert.AreSame(componentState, dynamicData.GetCustomData("component"));
            CollectionAssert.AreEquivalent(new[] { staticData, dynamicData }, _collection.Objects);
        }

        [Test]
        public void ConvertStaticObjectToDynamic_UnregisteredStatic_RegistersItAsDestroyed() {
            ObjectSaveData dynamicData = _collection.ConvertStaticObjectToDynamic(42, PrefabID);

            ObjectSaveData staticData = _collection.Objects.Single(o => o.InstanceID == 42);
            Assert.IsTrue(staticData.Destroyed);
            Assert.IsFalse(staticData.IsDynamicInstance);
            Assert.IsTrue(dynamicData.IsDynamicInstance);
        }

        [Test]
        public void ConvertStaticObjectToDynamic_NewEntryNotifiesCollection() {
            ObjectSaveData dynamicData = _collection.ConvertStaticObjectToDynamic(42, PrefabID);
            _counter.Reset();

            dynamicData.Destroyed = true;

            Assert.AreEqual(1, _counter.Count);
        }

        #endregion

        #region Subscription

        [Test]
        public void ObjectsRegisteredBeforeListener_AreHookedWhenListenerAdded() {
            _counter.Dispose();
            PersistedObjectCollection collection = new PersistedObjectCollection();
            ObjectSaveData data = collection.RegisterExistingObject(42, false);
            Assert.IsFalse(data.HasStateChangedListeners);

            using StateChangeCounter counter = new StateChangeCounter(collection);
            data.Destroyed = true;

            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void ObjectsAreUnhookedWhenLastListenerRemoved() {
            ObjectSaveData data = _collection.RegisterExistingObject(42, false);
            Assert.IsTrue(data.HasStateChangedListeners);

            _counter.Dispose();

            Assert.IsFalse(data.HasStateChangedListeners);
        }

        #endregion
    }
}
