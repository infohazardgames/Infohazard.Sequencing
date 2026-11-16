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

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// State for <see cref="TestPersistedSingleton"/>.
    /// </summary>
    [Serializable]
    public class TestSingletonState : PersistedData {
        private int _value;

        public int Value {
            get => _value;
            set {
                if (_value == value) return;
                _value = value;
                NotifyStateChanged();
            }
        }
    }

    /// <summary>
    /// A persisted singleton that counts its load callbacks. Like a typical game singleton, its
    /// <see cref="LoadDefaultState"/> resets the state to defaults.
    /// </summary>
    public class TestPersistedSingleton : PersistedSingleton<TestPersistedSingleton, TestSingletonState> {
        public const int DefaultValue = 10;

        public int LoadStateCalls { get; private set; }
        public int LoadDefaultStateCalls { get; private set; }

        protected override void LoadState() {
            LoadStateCalls++;
        }

        protected override void LoadDefaultState() {
            LoadDefaultStateCalls++;
            State.Value = DefaultValue;
        }
    }
}
