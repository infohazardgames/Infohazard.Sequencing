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
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// An execution step that records each call's arguments, and can optionally run a callback.
    /// </summary>
    public class RecordingStep : MonoBehaviour, IExecutionStep {
        /// <summary>
        /// Arguments of each call, in order.
        /// </summary>
        public List<ExecutionStepArguments> Calls { get; } = new();

        /// <summary>
        /// Optional behavior to run when executed.
        /// </summary>
        public Func<ExecutionStepArguments, UniTask> OnExecute { get; set; }

        public UniTask Execute(ExecutionStepArguments arguments) {
            Calls.Add(arguments);
            return OnExecute?.Invoke(arguments) ?? UniTask.CompletedTask;
        }
    }
}
