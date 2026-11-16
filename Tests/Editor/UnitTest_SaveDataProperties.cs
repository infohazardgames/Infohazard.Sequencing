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
using System.Collections.Generic;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Every typed save data property notifies exactly when its value changes.
    /// </summary>
    public class UnitTest_SaveDataProperties {
        private static IEnumerable<TestCaseData> Properties() {
            yield return Case<GlobalSaveData>("Global.MostRecentProfile",
                                              d => d.MostRecentProfile = "A", d => d.MostRecentProfile = "B",
                                              d => d.MostRecentProfile);
            yield return Case<ProfileSaveData>("Profile.ProfileName",
                                               d => d.ProfileName = "A", d => d.ProfileName = "B",
                                               d => d.ProfileName);
            yield return Case<ProfileSaveData>("Profile.MostRecentState",
                                               d => d.MostRecentState = "A", d => d.MostRecentState = "B",
                                               d => d.MostRecentState);
            yield return Case<StateSaveData>("State.StateName",
                                             d => d.StateName = "A", d => d.StateName = "B", d => d.StateName);
            yield return Case<StateSaveData>("State.CurrentScene",
                                             d => d.CurrentScene = "A", d => d.CurrentScene = "B",
                                             d => d.CurrentScene);
            yield return Case<StateSaveData>("State.SaveTime",
                                             d => d.SaveTime = new DateTime(2026, 1, 1),
                                             d => d.SaveTime = new DateTime(2026, 1, 2), d => d.SaveTime);
            yield return Case<LevelSaveData>("Level.LevelIndex",
                                             d => d.LevelIndex = 1, d => d.LevelIndex = 2, d => d.LevelIndex);
            yield return Case<RegionSaveData>("Region.RegionIndex",
                                              d => d.RegionIndex = 1, d => d.RegionIndex = 2, d => d.RegionIndex);
            yield return Case<RegionSaveData>("Region.Loaded",
                                              d => d.Loaded = true, d => d.Loaded = false, d => d.Loaded);
            yield return Case<ObjectSaveData>("Object.Destroyed",
                                              d => d.Destroyed = true, d => d.Destroyed = false, d => d.Destroyed);
        }

        private static TestCaseData Case<T>(string name, Action<T> setA, Action<T> setB, Func<T, object> get)
            where T : PersistedData, new() {
            return new TestCaseData(new Func<PersistedData>(() => new T()),
                                    new Action<PersistedData>(d => setA((T) d)),
                                    new Action<PersistedData>(d => setB((T) d)),
                                    new Func<PersistedData, object>(d => get((T) d))).SetName(name);
        }

        [Test]
        [TestCaseSource(nameof(Properties))]
        public void Property_NotifiesOnlyOnChange(Func<PersistedData> create, Action<PersistedData> setA,
                                                  Action<PersistedData> setB, Func<PersistedData, object> get) {
            PersistedData data = create();
            using StateChangeCounter counter = new StateChangeCounter(data);

            setA(data);
            object a = get(data);
            Assert.AreEqual(1, counter.Count, "Setting a new value should notify.");

            setA(data);
            Assert.AreEqual(1, counter.Count, "Setting the same value should not notify.");

            setB(data);
            Assert.AreEqual(2, counter.Count, "Changing the value should notify.");
            Assert.AreNotEqual(a, get(data));
        }

        [Test]
        public void ObjectSaveData_ConstructorSetsIdentity() {
            ObjectSaveData data = new ObjectSaveData(7, true, "prefab");
            Assert.AreEqual(7ul, data.InstanceID);
            Assert.IsTrue(data.IsDynamicInstance);
            Assert.AreEqual("prefab", data.DynamicPrefabID);
            Assert.IsFalse(data.Destroyed);
            Assert.IsFalse(data.Initialized);
        }
    }
}
