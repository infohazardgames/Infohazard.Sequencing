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
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Level and region lookups and initial-region selection.
    /// </summary>
    public class UnitTest_LevelManifest : SequencingTestFixture {
        private LevelManifestLevelEntry _level1;
        private LevelManifestLevelEntry _level2;
        private LevelManifestRegionEntry _plain;
        private LevelManifestRegionEntry _byDefault;
        private LevelManifestRegionEntry _always;

        [SetUp]
        public void SetUp() {
            _plain = CreateRegionEntry(10, 1, "Assets/Test/Level1_Plain.unity");
            _byDefault = CreateRegionEntry(11, 1, "Assets/Test/Level1_Default.unity", loadedByDefault: true);
            _always = CreateRegionEntry(12, 1, "Assets/Test/Level1_Always.unity", alwaysLoaded: true);
            _level1 = CreateLevelEntry(1, "Assets/Test/Level1.unity", _plain, _byDefault, _always);
            _level2 = CreateLevelEntry(2, "Assets/Other/Level2.unity");
            CreateManifest(_level1, _level2);
        }

        #region Level lookups

        [Test]
        public void GetLevelWithID_FindsLevelOrNull() {
            Assert.AreSame(_level1, Manifest.GetLevelWithID(1));
            Assert.AreSame(_level2, Manifest.GetLevelWithID(2));
            Assert.IsNull(Manifest.GetLevelWithID(3));
        }

        [Test]
        [TestCase("Level2")]
        [TestCase("Assets/Other/Level2.unity")]
        public void GetLevelWithSceneNameOrPath_AcceptsNameOrPath(string nameOrPath) {
            Assert.AreSame(_level2, Manifest.GetLevelWithSceneNameOrPath(nameOrPath));
        }

        [Test]
        [TestCase("Level3")]
        [TestCase("Assets/Test/Level2.unity")]
        [TestCase("Level1_Plain")]
        public void GetLevelWithSceneNameOrPath_NoMatch_ReturnsNull(string nameOrPath) {
            Assert.IsNull(Manifest.GetLevelWithSceneNameOrPath(nameOrPath));
        }

        [Test]
        public void GetLevelWithName_UsesAssetName() {
            Assert.AreSame(_level1, Manifest.GetLevelWithName(_level1.name));
            Assert.IsNull(Manifest.GetLevelWithName("Nope"));
        }

        [Test]
        public void Levels_InManifestOrder() {
            CollectionAssert.AreEqual(new[] { _level1, _level2 }, Manifest.Levels);
        }

        #endregion

        #region Region lookups

        [Test]
        public void GetRegionWithID_FindsRegionOrNull() {
            Assert.AreSame(_byDefault, _level1.GetRegionWithID(11));
            Assert.IsNull(_level1.GetRegionWithID(99));
        }

        [Test]
        public void GetRegionWithSceneName_FindsRegionOrNull() {
            Assert.AreSame(_always, _level1.GetRegionWithSceneName("Level1_Always"));
            Assert.IsNull(_level1.GetRegionWithSceneName("Level1"));
        }

        #endregion

        #region Initial regions

        [Test]
        public void GetInitialRegions_Default_IsLoadedByDefaultAndAlwaysLoaded() {
            CollectionAssert.AreEquivalent(new[] { 11, 12 }, _level1.GetInitialRegions().ToList());
        }

        [Test]
        public void GetInitialRegions_NoRegions_IsEmpty() {
            CollectionAssert.IsEmpty(_level2.GetInitialRegions());
        }

        [Test]
        public void GetInitialRegions_CustomLogic_ReplacesDefault() {
            TestInitialRegionsLogic logic = CreateAsset<TestInitialRegionsLogic>();
            logic.Regions = new[] { _plain };
            SetField(_level1, "_customInitialRegionsLogic", logic);

            CollectionAssert.AreEqual(new[] { 10 }, _level1.GetInitialRegions().ToList());
        }

        [Test]
        public void GetInitialRegions_CustomLogicReturnsNull_FallsBackToDefault() {
            TestInitialRegionsLogic logic = CreateAsset<TestInitialRegionsLogic>();
            logic.Regions = null;
            SetField(_level1, "_customInitialRegionsLogic", logic);

            CollectionAssert.AreEquivalent(new[] { 11, 12 }, _level1.GetInitialRegions().ToList());
        }

        [Test]
        public void GetInitialRegions_CustomLogicReturnsEmpty_LoadsNothing() {
            TestInitialRegionsLogic logic = CreateAsset<TestInitialRegionsLogic>();
            logic.Regions = new LevelManifestRegionEntry[0];
            SetField(_level1, "_customInitialRegionsLogic", logic);

            CollectionAssert.IsEmpty(_level1.GetInitialRegions());
        }

        #endregion
    }
}
