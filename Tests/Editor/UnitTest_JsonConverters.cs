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

using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// JSON converters for Unity math types used in save data.
    /// </summary>
    public class UnitTest_JsonConverters {
        private static readonly JsonConverter[] Converters = {
            new Vector2Converter(), new Vector3Converter(), new QuaternionConverter(),
        };

        private class Holder {
            public Vector2 V2;
            public Vector3 V3;
            public Quaternion Q;
            public string After;
        }

        private static T RoundTrip<T>(T value) {
            string json = JsonConvert.SerializeObject(value, Converters);
            return JsonConvert.DeserializeObject<T>(json, Converters);
        }

        [Test]
        public void Vector2_RoundTrips() {
            Vector2 value = new Vector2(1.5f, -2.25f);
            Assert.AreEqual(value, RoundTrip(value));
        }

        [Test]
        public void Vector3_RoundTrips() {
            Vector3 value = new Vector3(1.5f, -2.25f, 1e-3f);
            Assert.AreEqual(value, RoundTrip(value));
        }

        [Test]
        public void Vector3_ExtremeValues_RoundTripExactly() {
            Vector3 value = new Vector3(float.MaxValue, float.Epsilon, -123456.789f);
            Vector3 result = RoundTrip(value);
            Assert.AreEqual(value.x, result.x);
            Assert.AreEqual(value.y, result.y);
            Assert.AreEqual(value.z, result.z);
        }

        [Test]
        public void Quaternion_RoundTrips() {
            Quaternion value = Quaternion.Euler(10, 20, 30);
            Quaternion result = RoundTrip(value);
            Assert.AreEqual(value.x, result.x);
            Assert.AreEqual(value.y, result.y);
            Assert.AreEqual(value.z, result.z);
            Assert.AreEqual(value.w, result.w);
        }

        [Test]
        public void Write_UsesLowercaseComponentNames() {
            string json = JsonConvert.SerializeObject(new Vector3(1, 2, 3), Converters);
            Assert.AreEqual("{\"x\":1.0,\"y\":2.0,\"z\":3.0}", json);
        }

        [Test]
        public void MissingFields_UseDefaults() {
            Assert.AreEqual(new Vector2(0, 5), JsonConvert.DeserializeObject<Vector2>("{\"y\":5}", Converters));
            Assert.AreEqual(new Vector3(1, 0, 0), JsonConvert.DeserializeObject<Vector3>("{\"x\":1}", Converters));

            Quaternion q = JsonConvert.DeserializeObject<Quaternion>("{}", Converters);
            Assert.AreEqual(new Vector4(0, 0, 0, 1), new Vector4(q.x, q.y, q.z, q.w),
                            "A missing w should default to 1, giving identity.");
        }

        [Test]
        public void ExtraFields_AreIgnored() {
            Vector3 v = JsonConvert.DeserializeObject<Vector3>("{\"x\":1,\"y\":2,\"z\":3,\"w\":4,\"m\":\"s\"}",
                                                               Converters);
            Assert.AreEqual(new Vector3(1, 2, 3), v);
        }

        [Test]
        public void NestedInObject_ReaderContinuesAfterValue() {
            Holder holder = new Holder {
                V2 = new Vector2(1, 2),
                V3 = new Vector3(3, 4, 5),
                Q = new Quaternion(0, 0, 0, 1),
                After = "after",
            };

            Holder result = RoundTrip(holder);

            Assert.AreEqual(holder.V2, result.V2);
            Assert.AreEqual(holder.V3, result.V3);
            Assert.AreEqual(holder.Q, result.Q);
            Assert.AreEqual("after", result.After);
        }
    }
}
