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

using Cysharp.Threading.Tasks;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// A RegionRoot whose initialization waits for <see cref="Gate"/>, so tests can act while a scene is
    /// initializing.
    /// </summary>
    public class TestGatedRegionRoot : RegionRoot {
        /// <summary>
        /// If set, Initialize waits for it before registering with the level.
        /// </summary>
        public static UniTaskCompletionSource Gate { get; set; }

        /// <summary>
        /// Whether Initialize has been called since the last reset.
        /// </summary>
        public static bool InitializeStarted { get; private set; }

        public static void ResetStaticState() {
            Gate = null;
            InitializeStarted = false;
        }

        public override async UniTask Initialize() {
            InitializeStarted = true;
            if (Gate != null) await Gate.Task;
            await base.Initialize();
        }
    }
}
