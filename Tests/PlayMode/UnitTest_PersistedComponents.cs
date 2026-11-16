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

using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// The built-in persisted components, saved and reloaded through the persisted test scenes.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_PersistedComponents : PersistenceSceneFixture {
        #region PersistedTransform

        [Test]
        public async Task PersistedTransform_RestoresPositionAndRotation() {
            await LoadPersistedLevel();
            Transform crate = GameObject.Find(TestScenes.StaticCrate).transform;
            crate.SetPositionAndRotation(new Vector3(10, 2, 3), Quaternion.Euler(0, 45, 0));

            await SaveAndReload("Slot");

            crate = GameObject.Find(TestScenes.StaticCrate).transform;
            Assert.AreEqual(new Vector3(10, 2, 3), crate.position);
            Assert.That(Quaternion.Angle(Quaternion.Euler(0, 45, 0), crate.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public async Task PersistedTransform_RestoresScale() {
            // Suspected bug #5: LoadState assigns the transform's current scale back to itself.
            await LoadPersistedLevel();
            GameObject.Find(TestScenes.StaticCrate).transform.localScale = new Vector3(2, 3, 4);

            await SaveAndReload("Slot");

            Assert.AreEqual(new Vector3(2, 3, 4), GameObject.Find(TestScenes.StaticCrate).transform.localScale);
        }

        [Test]
        public async Task PersistedTransform_UnmovedObject_KeepsAuthoredPose() {
            await LoadPersistedLevel();
            await SaveAndReload("Slot");
            Assert.AreEqual(new Vector3(1, 0, 0), GameObject.Find(TestScenes.StaticCrate).transform.position);
        }

        [Test]
        public async Task PersistedTransform_OverrideSavePosition_IsSavedInstead() {
            await LoadPersistedLevel();
            PersistedTransform persistedTransform = Find<PersistedTransform>(TestScenes.StaticCrate);
            persistedTransform.OverrideSavePosition = new Vector3(0, 9, 0);
            persistedTransform.transform.position = new Vector3(30, 0, 0);

            await SaveAndReload("Slot");

            Assert.AreEqual(new Vector3(0, 9, 0), GameObject.Find(TestScenes.StaticCrate).transform.position);
        }

        #endregion

        #region PersistedRigidbody and PersistedComponentEnabled

        [Test]
        public async Task PersistedRigidbody_RestoresVelocities() {
            await LoadPersistedLevel();
            Rigidbody body = Find<Rigidbody>(TestScenes.StaticBall);
            body.SetLinearVelocity(new Vector3(3, 0, 0));
            body.angularVelocity = new Vector3(0, 2, 0);

            await SaveAndReload("Slot");

            body = Find<Rigidbody>(TestScenes.StaticBall);
            Assert.That(Vector3.Distance(new Vector3(3, 0, 0), body.GetLinearVelocity()), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(new Vector3(0, 2, 0), body.angularVelocity), Is.LessThan(0.01f));
        }

        [Test]
        public async Task PersistedComponentEnabled_RestoresDisabledTarget() {
            await LoadPersistedLevel();
            Find<TestToggle>(TestScenes.StaticCrate).enabled = false;

            await SaveAndReload("Slot");

            Assert.IsFalse(Find<TestToggle>(TestScenes.StaticCrate).enabled);
        }

        [Test]
        public async Task PersistedComponentEnabled_EnabledTargetStaysEnabled() {
            await LoadPersistedLevel();
            await SaveAndReload("Slot");
            Assert.IsTrue(Find<TestToggle>(TestScenes.StaticCrate).enabled);
        }

        #endregion

        #region RememberCurrentRegion

        [Test]
        public async Task RememberCurrentRegion_StartsInInitialRegion() {
            await LoadPersistedLevel(TestScenes.PRegionA);
            Assert.AreSame(GetRegion(TestScenes.PRegionA), Find<RememberCurrentRegion>(TestScenes.Rememberer).CurrentRegion);
        }

        [Test]
        public async Task RememberCurrentRegion_InitialRegionNotLoaded_HasNoRegion() {
            await LoadPersistedLevel();
            Assert.IsNull(Find<RememberCurrentRegion>(TestScenes.Rememberer).CurrentRegion);
        }

        [Test]
        public async Task RememberCurrentRegion_RestoresSavedRegion() {
            await LoadPersistedLevel(TestScenes.PRegionA, TestScenes.PRegionB);
            RememberCurrentRegion rememberer = Find<RememberCurrentRegion>(TestScenes.Rememberer);
            RegionRoot oldRegion = null, newRegion = null;
            rememberer.CurrentRegionChanged += (o, n) => (oldRegion, newRegion) = (o, n);

            rememberer.CurrentRegion = GetRegion(TestScenes.PRegionB);
            Assert.AreSame(GetRegion(TestScenes.PRegionA), oldRegion);
            Assert.AreSame(GetRegion(TestScenes.PRegionB), newRegion);

            await SaveAndReload("Slot", TestScenes.PRegionA, TestScenes.PRegionB);

            Assert.AreSame(GetRegion(TestScenes.PRegionB),
                           Find<RememberCurrentRegion>(TestScenes.Rememberer).CurrentRegion);
        }

        [Test]
        public async Task RememberCurrentRegion_SetNull_DoesNotThrow() {
            // Suspected bug #19: the setter reads the new region's index without checking for null.
            await LoadPersistedLevel(TestScenes.PRegionA);
            RememberCurrentRegion rememberer = Find<RememberCurrentRegion>(TestScenes.Rememberer);
            Assert.DoesNotThrow(() => rememberer.CurrentRegion = null);
            Assert.IsNull(rememberer.CurrentRegion);
        }

        #endregion

        #region PersistedGameObjectManual

        [Test]
        public async Task PersistedGameObjectManual_InitializesComponentsFromGivenData() {
            GameObject obj = CreateGameObject("Manual", false);
            TestToggle toggle = obj.AddComponent<TestToggle>();
            PersistedComponentEnabled enabled = obj.AddComponent<PersistedComponentEnabled>();
            SetField(enabled, "_target", toggle);
            PersistedGameObjectManual manual = obj.AddComponent<PersistedGameObjectManual>();
            obj.SetActive(true);
            int loaded = 0;
            manual.LoadCompleted += () => loaded++;
            ObjectSaveData data = new ObjectSaveData(77, false, null);

            await AwaitOrFail(manual.Initialize(data), "initializing");

            Assert.IsTrue(manual.Initialized);
            Assert.AreEqual(77ul, manual.InstanceID);
            Assert.AreEqual(1, loaded);
            Assert.IsTrue(enabled.Initialized);
            Assert.IsNotEmpty(data.CustomData, "The component's state should be stored in the given data.");

            toggle.enabled = false;
            manual.WriteState();
            TestToggle reloadedToggle = CreateGameObject("Manual 2", false).AddComponent<TestToggle>();
            PersistedComponentEnabled reloadedEnabled = reloadedToggle.gameObject.AddComponent<PersistedComponentEnabled>();
            SetField(reloadedEnabled, "_target", reloadedToggle);
            PersistedGameObjectManual reloaded = reloadedToggle.gameObject.AddComponent<PersistedGameObjectManual>();
            reloadedToggle.gameObject.SetActive(true);
            await AwaitOrFail(reloaded.Initialize(data), "initializing again");
            Assert.IsFalse(reloadedToggle.enabled, "State written by the first object should load into the second.");
        }

        #endregion
    }
}
