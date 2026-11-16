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
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Change notification and custom data on the base save data type.
    /// </summary>
    public class UnitTest_PersistedData {
        #region Change notification

        [Test]
        public void Initialized_NotifiesOnlyOnChange() {
            PersistedData data = new PersistedData();
            using StateChangeCounter counter = new StateChangeCounter(data);

            data.Initialized = true;
            Assert.AreEqual(1, counter.Count);

            data.Initialized = true;
            Assert.AreEqual(1, counter.Count);

            data.Initialized = false;
            Assert.AreEqual(2, counter.Count);
        }

        [Test]
        public void NotifyStateChanged_WithoutListeners_DoesNothing() {
            PersistedData data = new PersistedData();
            Assert.DoesNotThrow(data.NotifyStateChanged);
            Assert.IsFalse(data.HasStateChangedListeners);
        }

        [Test]
        public void HasStateChangedListeners_TracksAddAndRemove() {
            PersistedData data = new PersistedData();
            StateChangeCounter first = new StateChangeCounter(data);
            StateChangeCounter second = new StateChangeCounter(data);
            Assert.IsTrue(data.HasStateChangedListeners);

            first.Dispose();
            Assert.IsTrue(data.HasStateChangedListeners);

            second.Dispose();
            Assert.IsFalse(data.HasStateChangedListeners);
        }

        #endregion

        #region Custom data

        [Test]
        public void GetCustomData_Missing_ReturnsNull() {
            PersistedData data = new PersistedData();
            Assert.IsNull(data.GetCustomData("missing"));
        }

        [Test]
        public void SetCustomData_ThenGet_ReturnsValue() {
            PersistedData data = new PersistedData();
            object value = new object();
            data.SetCustomData("key", value);
            Assert.AreSame(value, data.GetCustomData("key"));
            Assert.AreSame(value, data.CustomData["key"]);
        }

        [Test]
        public void NestedCustomData_ChangePropagatesToParent() {
            PersistedData parent = new PersistedData();
            PersistedData child = new PersistedData();
            using StateChangeCounter counter = new StateChangeCounter(parent);

            parent.SetCustomData("child", child);
            child.NotifyStateChanged();

            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void NestedCustomData_SetBeforeParentHasListeners_NotifiesParentOnce() {
            // Suspected bug #12: SetCustomData subscribes to the child even when the parent has no listeners,
            // and the parent subscribes it again when it gets one.
            PersistedData parent = new PersistedData();
            PersistedData child = new PersistedData();
            parent.SetCustomData("child", child);

            using StateChangeCounter counter = new StateChangeCounter(parent);
            child.NotifyStateChanged();

            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void NestedCustomData_ParentWithoutListeners_DoesNotListenToChild() {
            // Suspected bug #12: an unobserved parent shouldn't keep handlers on its children.
            PersistedData parent = new PersistedData();
            PersistedData child = new PersistedData();
            parent.SetCustomData("child", child);
            Assert.IsFalse(child.HasStateChangedListeners);

            StateChangeCounter counter = new StateChangeCounter(parent);
            Assert.IsTrue(child.HasStateChangedListeners);

            counter.Dispose();
            Assert.IsFalse(child.HasStateChangedListeners);
        }

        [Test]
        public void ReplacingCustomData_UnhooksOldChild() {
            PersistedData parent = new PersistedData();
            PersistedData oldChild = new PersistedData();
            PersistedData newChild = new PersistedData();
            using StateChangeCounter counter = new StateChangeCounter(parent);

            parent.SetCustomData("child", oldChild);
            parent.SetCustomData("child", newChild);
            Assert.IsFalse(oldChild.HasStateChangedListeners);

            oldChild.NotifyStateChanged();
            Assert.AreEqual(0, counter.Count);

            newChild.NotifyStateChanged();
            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void SettingCustomDataToNull_UnhooksOldChild() {
            PersistedData parent = new PersistedData();
            PersistedData child = new PersistedData();
            using StateChangeCounter counter = new StateChangeCounter(parent);

            parent.SetCustomData("child", child);
            parent.SetCustomData("child", null);

            child.NotifyStateChanged();
            Assert.AreEqual(0, counter.Count);
            Assert.IsNull(parent.GetCustomData("child"));
        }

        [Test]
        public void CustomDataSetter_CopiesDictionary() {
            PersistedData parent = new PersistedData();
            Dictionary<string, object> source = new Dictionary<string, object> { ["a"] = 1 };
            parent.CustomData = source;

            source["b"] = 2;

            Assert.AreEqual(1, parent.CustomData.Count);
            Assert.AreEqual(1, parent.GetCustomData("a"));
        }

        [Test]
        public void CustomDataSetter_WhileObserved_RehooksChildren() {
            PersistedData parent = new PersistedData();
            PersistedData oldChild = new PersistedData();
            PersistedData newChild = new PersistedData();
            using StateChangeCounter counter = new StateChangeCounter(parent);
            parent.SetCustomData("child", oldChild);

            parent.CustomData = new Dictionary<string, object> { ["child"] = newChild };

            oldChild.NotifyStateChanged();
            Assert.AreEqual(0, counter.Count);
            newChild.NotifyStateChanged();
            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void CustomDataSetter_Null_ClearsCustomData() {
            // Suspected bug #16: the setter calls ToDictionary on null.
            PersistedData parent = new PersistedData();
            parent.SetCustomData("a", 1);

            Assert.DoesNotThrow(() => parent.CustomData = null);
            Assert.IsNull(parent.GetCustomData("a"));
        }

        #endregion

        #region Object containers

        [Test]
        public void ObjectContainer_ObjectChangePropagatesToContainer() {
            LevelSaveData level = new LevelSaveData();
            using StateChangeCounter counter = new StateChangeCounter(level);
            ObjectSaveData obj = level.Objects.RegisterExistingObject(5, false);
            counter.Reset();

            obj.Destroyed = true;

            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void ObjectContainer_ResubscribingDoesNotDuplicateCustomDataHandlers() {
            // Suspected bug #12: ObjectContainerSaveData.Unsubscribe calls base.Subscribe, so every
            // unsubscribe/subscribe cycle (PersistedLevelRoot does one per save) adds another handler.
            LevelSaveData level = new LevelSaveData();
            PersistedData child = new PersistedData();
            StateChangeCounter counter = new StateChangeCounter(level);
            level.SetCustomData("child", child);

            for (int i = 0; i < 3; i++) {
                counter.Dispose();
                counter = new StateChangeCounter(level);
            }

            child.NotifyStateChanged();
            Assert.AreEqual(1, counter.Count);

            counter.Dispose();
            Assert.IsFalse(child.HasStateChangedListeners);
        }

        [Test]
        public void ObjectContainer_AfterUnsubscribe_ObjectsNotHooked() {
            LevelSaveData level = new LevelSaveData();
            ObjectSaveData obj = level.Objects.RegisterExistingObject(5, false);
            StateChangeCounter counter = new StateChangeCounter(level);
            Assert.IsTrue(obj.HasStateChangedListeners);

            counter.Dispose();

            Assert.IsFalse(obj.HasStateChangedListeners);
            Assert.IsFalse(level.Objects.HasStateChangedListeners);
        }

        #endregion
    }
}
