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
using System.Reflection;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Scopes and load callbacks of PersistedSingleton.
    /// </summary>
    public class UnitTest_PersistedSingleton : SequencingTestFixture {
        private const string Profile = "Profile";

        private PersistenceManager _manager;

        [SetUp]
        public void SetUp() {
            _manager = CreatePersistenceManager();
            _manager.LoadGlobalData();
            _manager.LoadProfileData(Profile);
            _manager.LoadEmptyStateData();
        }

        private TestPersistedSingleton Create(string scope = "Profile") {
            return CreateSingleton<TestPersistedSingleton>(s => SetScope(s, scope));
        }

        private static void SetScope(TestPersistedSingleton singleton, string scope) {
            FieldInfo field = typeof(PersistedSingleton<TestPersistedSingleton, TestSingletonState>)
                .GetField("_scope", BindingFlags.Instance | BindingFlags.NonPublic);
            field!.SetValue(singleton, Enum.Parse(field.FieldType, scope));
        }

        [Test]
        [TestCase("Global")]
        [TestCase("Profile")]
        [TestCase("State")]
        public void State_IsStoredInScopeData(string scope) {
            TestPersistedSingleton singleton = Create(scope);

            PersistedData scopeData = scope switch {
                "Global" => _manager.LoadedGlobalData,
                "Profile" => _manager.LoadedProfileData,
                _ => _manager.LoadedStateData,
            };
            Assert.IsNotNull(singleton.State);
            Assert.AreSame(singleton.State, scopeData.GetCustomData(nameof(TestPersistedSingleton)));
        }

        [Test]
        public void FirstLoad_CallsLoadDefaultStateOnly() {
            TestPersistedSingleton singleton = Create();

            Assert.AreEqual(1, singleton.LoadDefaultStateCalls);
            Assert.AreEqual(0, singleton.LoadStateCalls);
            Assert.AreEqual(TestPersistedSingleton.DefaultValue, singleton.State.Value);
        }

        [Test]
        public void SavedState_CallsLoadStateAfterRelaunch() {
            // Suspected bug #7: Initialized is never set, so LoadDefaultState runs on every load and resets
            // the saved value.
            TestPersistedSingleton singleton = Create();
            singleton.State.Value = 42;
            _manager.SaveProfileData();

            Object.DestroyImmediate(singleton.gameObject);
            _manager = ReloadPersistenceManager(_manager, Profile, null);
            singleton = Create();

            Assert.AreEqual(1, singleton.LoadStateCalls);
            Assert.AreEqual(0, singleton.LoadDefaultStateCalls);
            Assert.AreEqual(42, singleton.State.Value);
        }

        [Test]
        public void ScopeReload_ReloadsState() {
            TestPersistedSingleton singleton = Create();
            singleton.State.Value = 42;
            TestSingletonState oldState = singleton.State;

            _manager.LoadProfileData("Other");

            Assert.AreNotSame(oldState, singleton.State);
            Assert.AreSame(singleton.State, _manager.LoadedProfileData.GetCustomData(nameof(TestPersistedSingleton)));
            Assert.AreEqual(TestPersistedSingleton.DefaultValue, singleton.State.Value);
        }

        [Test]
        public void ScopeUnload_ClearsState() {
            TestPersistedSingleton singleton = Create("State");

            _manager.UnloadStateData();

            Assert.IsNull(singleton.State);
        }

        [Test]
        public void OtherScopeReload_DoesNotReloadState() {
            TestPersistedSingleton singleton = Create("Global");
            TestSingletonState state = singleton.State;

            _manager.LoadProfileData("Other");

            Assert.AreSame(state, singleton.State);
        }

        [Test]
        public void CreatedBeforeScopeLoads_GetsStateWhenItLoads() {
            _manager.UnloadProfileData();
            TestPersistedSingleton singleton = Create();
            Assert.IsNull(singleton.State);

            _manager.LoadProfileData(Profile);

            Assert.IsNotNull(singleton.State);
        }
    }
}
