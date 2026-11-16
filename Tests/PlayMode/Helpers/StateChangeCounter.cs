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
    /// Counts <see cref="PersistedData.StateChanged"/> invocations while it is subscribed.
    /// Disposing it unsubscribes.
    /// </summary>
    public sealed class StateChangeCounter : IDisposable {
        private readonly PersistedData _data;
        private bool _subscribed;

        /// <summary>
        /// Number of StateChanged invocations seen.
        /// </summary>
        public int Count { get; private set; }

        public StateChangeCounter(PersistedData data) {
            _data = data;
            _data.StateChanged += Data_StateChanged;
            _subscribed = true;
        }

        /// <summary>
        /// Reset <see cref="Count"/> to zero.
        /// </summary>
        public void Reset() => Count = 0;

        public void Dispose() {
            if (!_subscribed) return;
            _subscribed = false;
            _data.StateChanged -= Data_StateChanged;
        }

        private void Data_StateChanged() => Count++;
    }
}
