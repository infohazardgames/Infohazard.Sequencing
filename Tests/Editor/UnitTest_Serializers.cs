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
using System.IO;
using System.Linq;
using System.Text;
using Infohazard.Sequencing.Tests.PlayMode;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Stream round trips and intermediate-object conversion for the JSON serializer.
    /// </summary>
    public class UnitTest_Serializers : SequencingTestFixture {
        private const string PrefabID = "prefab-guid";

        private Serializer Create() => CreateSerializer(CreateGameObject("Serializer"));

        private static T RoundTrip<T>(Serializer serializer, T data) {
            using MemoryStream stream = new MemoryStream();
            serializer.Write(new NonClosingStream(stream), data);
            stream.Position = 0;
            Assert.IsTrue(serializer.Read(stream, out T result), "Read failed.");
            return result;
        }

        /// <summary>
        /// A region with a static destroyed object, a dynamic object, and plain custom data
        /// (Unity math types are covered by Json_BuiltInComponentStates_RoundTrip).
        /// </summary>
        private static RegionSaveData CreateRegion(out ulong staticID, out ulong dynamicID) {
            RegionSaveData region = new RegionSaveData { RegionIndex = 3, Loaded = true, Initialized = true };
            ObjectSaveData staticObj = region.Objects.RegisterExistingObject(77, false);
            staticObj.Destroyed = true;
            staticObj.Initialized = true;
            ObjectSaveData dynamicObj = region.Objects.RegisterNewDynamicObject(PrefabID);
            dynamicObj.Initialized = true;
            dynamicObj.SetCustomData("component", new RememberCurrentRegion.StateInfo { RegionID = 5 });
            staticID = staticObj.InstanceID;
            dynamicID = dynamicObj.InstanceID;
            return region;
        }

        private static void AssertRegion(Serializer serializer, RegionSaveData region, ulong staticID,
                                         ulong dynamicID) {
            Assert.AreEqual(3, region.RegionIndex);
            Assert.IsTrue(region.Loaded);
            Assert.IsTrue(region.Initialized);
            Assert.AreEqual(2, region.Objects.Objects.Count());

            ObjectSaveData staticObj = region.Objects.Objects.Single(o => o.InstanceID == staticID);
            Assert.IsTrue(staticObj.Destroyed);
            Assert.IsFalse(staticObj.IsDynamicInstance);
            Assert.IsTrue(staticObj.Initialized);

            ObjectSaveData dynamicObj = region.Objects.Objects.Single(o => o.InstanceID == dynamicID);
            Assert.IsTrue(dynamicObj.IsDynamicInstance);
            Assert.AreEqual(PrefabID, dynamicObj.DynamicPrefabID);
            Assert.IsFalse(dynamicObj.Destroyed);
            Assert.IsTrue(serializer.IntermediateToObject(dynamicObj.GetCustomData("component"),
                                                          out RememberCurrentRegion.StateInfo state));
            Assert.AreEqual(5, state.RegionID);
        }

        #region Round trips and conversion

        [Test]
        public void RegionHierarchy_RoundTrips() {
            Serializer serializer = Create();
            RegionSaveData region = CreateRegion(out ulong staticID, out ulong dynamicID);

            AssertRegion(serializer, RoundTrip(serializer, region), staticID, dynamicID);
        }

        [Test]
        public void TopLevelData_RoundTrips() {
            Serializer serializer = Create();
            DateTime saveTime = new DateTime(2026, 11, 15, 12, 30, 45, 123);

            GlobalSaveData global = RoundTrip(serializer, new GlobalSaveData { MostRecentProfile = "P" });
            ProfileSaveData profile = RoundTrip(serializer, new ProfileSaveData {
                ProfileName = "P", MostRecentState = "S",
            });
            StateSaveData state = RoundTrip(serializer, new StateSaveData {
                StateName = "S", CurrentScene = "Level1", SaveTime = saveTime,
            });
            LevelSaveData level = RoundTrip(serializer, new LevelSaveData { LevelIndex = 4 });

            Assert.AreEqual("P", global.MostRecentProfile);
            Assert.AreEqual("P", profile.ProfileName);
            Assert.AreEqual("S", profile.MostRecentState);
            Assert.AreEqual("S", state.StateName);
            Assert.AreEqual("Level1", state.CurrentScene);
            Assert.AreEqual(saveTime, state.SaveTime);
            Assert.AreEqual(4, level.LevelIndex);
        }

        [Test]
        public void IntermediateToObject_ExactType_ReturnsSameInstance() {
            Serializer serializer = Create();
            RememberCurrentRegion.StateInfo state = new RememberCurrentRegion.StateInfo();

            Assert.IsTrue(serializer.IntermediateToObject(state, out RememberCurrentRegion.StateInfo result));
            Assert.AreSame(state, result);
        }

        [Test]
        public void IntermediateToObject_WrongTypeOrNull_ReturnsFalse() {
            Serializer serializer = Create();

            Assert.IsFalse(serializer.IntermediateToObject("text", out RememberCurrentRegion.StateInfo result));
            Assert.IsNull(result);
            Assert.IsFalse(serializer.IntermediateToObject(null, out result));
        }

        [Test]
        public void ObjectToIntermediate_ReturnsInput() {
            Serializer serializer = Create();
            RememberCurrentRegion.StateInfo state = new RememberCurrentRegion.StateInfo();
            Assert.AreSame(state, serializer.ObjectToIntermediate(state));
        }

        #endregion

        #region JSON

        [Test]
        public void Json_CustomDataReadsBackAsJObjectAndConverts() {
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            data.SetCustomData("state", new RememberCurrentRegion.StateInfo { RegionID = 9 });

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsInstanceOf<JObject>(result.GetCustomData("state"));
            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("state"),
                                                          out RememberCurrentRegion.StateInfo state));
            Assert.AreEqual(9, state.RegionID);
        }

        [Test]
        public void Json_BuiltInComponentStates_RoundTrip() {
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            data.SetCustomData("transform", new PersistedTransform.StateInfo {
                Position = new Vector3(1, 2, 3), Rotation = Quaternion.Euler(0, 90, 0), Scale = new Vector3(2, 2, 2),
            });
            data.SetCustomData("rigidbody", new PersistedRigidbody.StateInfo {
                Velocity = new Vector3(4, 5, 6), AngularVelocity = new Vector3(0, 1, 0),
            });
            data.SetCustomData("enabled", new PersistedComponentEnabled.StateInfo { Enabled = true });
            data.SetCustomData("region", new RememberCurrentRegion.StateInfo { RegionID = 2 });
            data.SetCustomData("saves", new SaveStateManager.StateInfo {
                LastAutoSave = 1, NextAutoSave = 2, HasQuickSave = true,
            });

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("transform"),
                                                          out PersistedTransform.StateInfo transform));
            Assert.AreEqual(new Vector3(1, 2, 3), transform.Position);
            Assert.That(Quaternion.Angle(Quaternion.Euler(0, 90, 0), transform.Rotation), Is.LessThan(0.01f));
            Assert.AreEqual(new Vector3(2, 2, 2), transform.Scale);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("rigidbody"),
                                                          out PersistedRigidbody.StateInfo rigidbody));
            Assert.AreEqual(new Vector3(4, 5, 6), rigidbody.Velocity);
            Assert.AreEqual(new Vector3(0, 1, 0), rigidbody.AngularVelocity);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("enabled"),
                                                          out PersistedComponentEnabled.StateInfo enabled));
            Assert.IsTrue(enabled.Enabled);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("region"),
                                                          out RememberCurrentRegion.StateInfo region));
            Assert.AreEqual(2, region.RegionID);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("saves"),
                                                          out SaveStateManager.StateInfo saves));
            Assert.AreEqual(1, saves.LastAutoSave);
            Assert.AreEqual(2, saves.NextAutoSave);
            Assert.IsTrue(saves.HasQuickSave);
        }

        [Test]
        public void Json_NestedCustomData_RoundTrips() {
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            RememberCurrentRegion.StateInfo outer = new RememberCurrentRegion.StateInfo { RegionID = 1 };
            outer.SetCustomData("inner", new RememberCurrentRegion.StateInfo { RegionID = 2 });
            data.SetCustomData("outer", outer);

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("outer"),
                                                          out RememberCurrentRegion.StateInfo outerResult));
            Assert.AreEqual(1, outerResult.RegionID);
            Assert.IsTrue(serializer.IntermediateToObject(outerResult.GetCustomData("inner"),
                                                          out RememberCurrentRegion.StateInfo innerResult));
            Assert.AreEqual(2, innerResult.RegionID);
        }

        [Test]
        public void Json_StringCustomData_RoundTrips() {
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            data.SetCustomData("name", "value");

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("name"), out string value));
            Assert.AreEqual("value", value);
        }

        [Test]
        public void Json_IntCustomData_RoundTrips() {
            // Suspected bug #16: JSON reads integers back as long, which isn't int or a JContainer.
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            data.SetCustomData("count", 5);

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("count"), out int value));
            Assert.AreEqual(5, value);
        }

        [Test]
        public void Json_FloatCustomData_RoundTrips() {
            // Suspected bug #16: JSON reads floats back as double.
            Serializer serializer = Create();
            ObjectSaveData data = new ObjectSaveData(1, false, null);
            data.SetCustomData("amount", 1.5f);

            ObjectSaveData result = RoundTrip(serializer, data);

            Assert.IsTrue(serializer.IntermediateToObject(result.GetCustomData("amount"), out float value));
            Assert.AreEqual(1.5f, value);
        }

        [Test]
        public void Json_ReadInvalidData_ReturnsFalseAndLogs() {
            Serializer serializer = Create();
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("{ not json"));

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Json"));
            Assert.IsFalse(serializer.Read(stream, out GlobalSaveData data));
            Assert.IsNull(data);
        }

        #endregion
    }
}
