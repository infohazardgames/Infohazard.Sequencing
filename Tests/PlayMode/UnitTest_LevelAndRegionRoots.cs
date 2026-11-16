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

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Infohazard.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// LevelRoot and RegionRoot registration as their scenes load and unload.
    /// </summary>
    [PrebuildSetup(typeof(SequencingTestScenesSetup))]
    [PostBuildCleanup(typeof(SequencingTestScenesSetup))]
    public class UnitTest_LevelAndRegionRoots : SceneTestFixture {
        [Test]
        public async Task LevelLoad_BecomesCurrentAndResetsTimeScale() {
            Pause.TimeScale = 0.5f;

            await LoadAndWait(TestScenes.Level);

            LevelRoot level = LevelRoot.Current;
            Assert.IsNotNull(level);
            Assert.IsTrue(level.Initialized);
            Assert.AreEqual(TestScenes.LevelID, level.LevelIndex);
            Assert.AreSame(LevelEntry, level.ManifestEntry);
            Assert.IsNotNull(level.DynamicObjectRoot);
            Assert.AreEqual(1f, Pause.TimeScale);
        }

        [Test]
        public async Task LevelUnload_ClearsCurrent() {
            await LoadAndWait(TestScenes.Level);
            LevelRoot level = LevelRoot.Current;

            await UnloadAndWait(TestScenes.Level);

            Assert.IsNull(LevelRoot.Current);
            Assert.IsFalse(level.Initialized);
        }

        [Test]
        public async Task LevelUnloading_FiresOnUnloadNotLoad() {
            // Suspected bug #18: LevelRoot.Unloading is invoked in Initialize, not Cleanup.
            await LoadAndWait(TestScenes.Level);
            int unloading = 0;
            LevelRoot.Current.Unloading += () => unloading++;

            await UnloadAndWait(TestScenes.Level);

            Assert.AreEqual(1, unloading);
        }

        [Test]
        public async Task SecondLevel_LogsErrorAndKeepsFirst() {
            await LoadAndWait(TestScenes.Level);
            LevelRoot first = LevelRoot.Current;

            LogAssert.Expect(LogType.Error, "Trying to initialize a LevelRoot when one is already active!");
            await LoadAndWait(TestScenes.Level2);

            Assert.AreSame(first, LevelRoot.Current);
        }

        [Test]
        public async Task RegionLoad_RegistersWithLevel() {
            await LoadAndWait(TestScenes.Level);
            await LoadAndWait(TestScenes.RegionA);

            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.RegionA), out _, out RegionRoot region);
            Assert.IsTrue(region.Initialized);
            Assert.AreSame(RegionAEntry, region.ManifestEntry);
            Assert.AreSame(region, LevelRoot.Current.LoadedRegions[TestScenes.RegionAID]);
        }

        [Test]
        public async Task RegionUnload_DeregistersAndFiresUnloading() {
            await LoadAndWait(TestScenes.Level);
            await LoadAndWait(TestScenes.RegionA);
            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.RegionA), out _, out RegionRoot region);
            int unloading = 0;
            region.Unloading += () => unloading++;

            await UnloadAndWait(TestScenes.RegionA);

            Assert.AreEqual(1, unloading);
            Assert.IsFalse(region.Initialized);
            Assert.IsFalse(LevelRoot.Current.LoadedRegions.ContainsKey(TestScenes.RegionAID));
        }

        [Test]
        public async Task RegionWithoutLevel_LogsErrorInsteadOfThrowing() {
            // Suspected bug #18: RegionRoot.Initialize dereferences LevelRoot.Current without checking it.
            LogAssert.Expect(LogType.Error, new Regex("LevelRoot"));
            SceneLoadOperations ops = Loader.LoadScene(TestScenes.RegionA, true, false, null);

            await WaitUntil(() => !Loader.IsLoadingAnyScenes(), 5);

            Assert.IsFalse(Loader.IsLoadingAnyScenes(), "The region's load never finished.");
            Loader.GetSceneLoadedState(TestScenes.Name(TestScenes.RegionA), out _, out RegionRoot region);
            Assert.IsFalse(region != null && region.Initialized);
            Assert.IsTrue(ops.IsValid);
        }

        [Test]
        public async Task LevelLoadRegion_LoadsRegionScene() {
            await LoadAndWait(TestScenes.Level);

            Assert.IsTrue(LevelRoot.Current.LoadRegion(RegionAEntry));

            await WaitUntilOrFail(() => LevelRoot.Current.LoadedRegions.ContainsKey(TestScenes.RegionAID),
                                  "region registers");
        }

        [Test]
        public async Task LevelUnloadRegion_UnloadsRegionScene() {
            await LoadAndWait(TestScenes.Level);
            await LoadAndWait(TestScenes.RegionA);

            IProgressSource op = LevelRoot.Current.UnloadRegion(RegionAEntry);

            Assert.IsNotNull(op);
            await AwaitOrFail(op.WaitForCompletionTask(), "unloading region");
            Assert.IsFalse(LevelRoot.Current.LoadedRegions.ContainsKey(TestScenes.RegionAID));
            Assert.AreEqual(0, LoadedCount(TestScenes.RegionA));
        }

        [Test]
        public async Task AlwaysLoadedRegion_CannotBeLoadedOrUnloadedExplicitly() {
            await LoadAndWait(TestScenes.Level);
            LevelManifestRegionEntry always = CreateRegionEntry(9, TestScenes.LevelID, TestScenes.RegionA,
                                                                alwaysLoaded: true);

            LogAssert.Expect(LogType.Error, new Regex("always loaded"));
            Assert.IsFalse(LevelRoot.Current.LoadRegion(always));
            LogAssert.Expect(LogType.Error, new Regex("always loaded"));
            Assert.IsNull(LevelRoot.Current.UnloadRegion(always));

            await Frames();
            Assert.AreEqual(0, LoadedCount(TestScenes.RegionA));
        }

        [Test]
        public async Task LevelUnloadedBeforeRegions_DoesNotThrow() {
            await LoadAndWait(TestScenes.Level);
            await LoadAndWait(TestScenes.RegionA);

            Loader.UnloadScenes();
            await WaitUntilOrFail(() => !Loader.IsUnloadingAnyScenes, "unloading");

            Assert.IsNull(LevelRoot.Current);
            Assert.AreEqual(0, LoadedCount(TestScenes.RegionA));
        }
    }
}
