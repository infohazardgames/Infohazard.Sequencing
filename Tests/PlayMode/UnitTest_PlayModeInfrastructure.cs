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
using NUnit.Framework;
using UnityEngine;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Checks that <see cref="SequencingTestFixture"/> works in play mode, where Unity runs component lifecycles.
    /// </summary>
    public class UnitTest_PlayModeInfrastructure : SequencingTestFixture {
        [Test]
        [TestCase(TestHandlerType.File)]
        [TestCase(TestHandlerType.PlayerPrefs)]
        public async Task PersistenceManager_RoundTripsAcrossFrames(TestHandlerType handlerType) {
            Assert.IsTrue(Application.isPlaying);

            PersistenceManager manager = CreatePersistenceManager(handlerType);
            await UniTask.Yield();

            manager.LoadGlobalData();
            manager.LoadedGlobalData.MostRecentProfile = "Saved";
            manager.SaveGlobalData();
            await UniTask.Yield();

            manager = ReloadPersistenceManager(manager, null, null, handlerType);
            Assert.AreEqual("Saved", manager.LoadedGlobalData.MostRecentProfile);
        }

        [Test]
        public async Task LevelRoot_RegistersAndCleansUp() {
            LevelManifest manifest = CreateManifest(CreateLevelEntry(1, "Assets/Test/Level1.unity"));
            LevelRoot root = CreateGameObject("Level root").AddComponent<LevelRoot>();
            SetField(root, "_manifestEntry", manifest.Levels[0]);

            await root.Initialize();
            Assert.AreSame(root, LevelRoot.Current);
            Assert.AreEqual(1, root.LevelIndex);

            root.Cleanup();
            Assert.IsNull(LevelRoot.Current);
        }
    }
}
