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
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Step collection, ordering, argument passing and failure handling in ExecutionStepSequencer.
    /// </summary>
    public class UnitTest_ExecutionStepSequencer : SequencingTestFixture {
        private static readonly ExecutionStepParameter<string> TestParam = new();

        [Test]
        public async Task Execute_RunsActiveChildStepsInOrder() {
            List<string> order = new List<string>();
            RecordingStep first = CreateStep<RecordingStep>("First");
            RecordingStep inactive = CreateStep<RecordingStep>("Inactive");
            GameObject notAStep = CreateGameObject("Not a step");
            RecordingStep last = CreateStep<RecordingStep>("Last");
            first.OnExecute = _ => { order.Add("First"); return UniTask.CompletedTask; };
            inactive.OnExecute = _ => { order.Add("Inactive"); return UniTask.CompletedTask; };
            last.OnExecute = _ => { order.Add("Last"); return UniTask.CompletedTask; };
            inactive.gameObject.SetActive(false);

            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", first, inactive, notAStep.transform, last);
            await sequencer.Execute(new ExecutionStepArguments());

            CollectionAssert.AreEqual(new[] { "First", "Last" }, order);
        }

        [Test]
        public async Task Execute_WaitsForEachStep() {
            UniTaskCompletionSource gate = new UniTaskCompletionSource();
            RecordingStep slow = CreateStep<RecordingStep>("Slow");
            RecordingStep next = CreateStep<RecordingStep>("Next");
            slow.OnExecute = _ => gate.Task;
            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", slow, next);

            UniTask run = sequencer.Execute(new ExecutionStepArguments());
            await UniTask.Yield();
            await UniTask.Yield();
            Assert.AreEqual(1, slow.Calls.Count);
            Assert.AreEqual(0, next.Calls.Count, "The next step started before the previous one finished.");

            gate.TrySetResult();
            await run;
            Assert.AreEqual(1, next.Calls.Count);
        }

        [Test]
        public async Task Execute_PassesSameArgumentsBetweenSteps() {
            RecordingStep writer = CreateStep<RecordingStep>("Writer");
            RecordingStep reader = CreateStep<RecordingStep>("Reader");
            string seen = null;
            writer.OnExecute = args => { TestParam.Set(args, "value"); return UniTask.CompletedTask; };
            reader.OnExecute = args => { seen = TestParam.GetOrDefault(args, null); return UniTask.CompletedTask; };
            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", writer, reader);
            ExecutionStepArguments args = new ExecutionStepArguments();

            await sequencer.Execute(args);

            Assert.AreEqual("value", seen);
            Assert.AreEqual("value", TestParam.GetOrDefault(args, null), "The caller sees changes made by steps.");
        }

        [Test]
        public async Task Execute_ThrowingStep_StopsSequenceAndFaults() {
            RecordingStep thrower = CreateStep<RecordingStep>("Thrower");
            RecordingStep after = CreateStep<RecordingStep>("After");
            thrower.OnExecute = _ => throw new InvalidOperationException("step failed");
            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", thrower, after);

            InvalidOperationException ex = null;
            try {
                await sequencer.Execute(new ExecutionStepArguments());
            } catch (InvalidOperationException e) {
                ex = e;
            }

            Assert.IsNotNull(ex);
            Assert.AreEqual(0, after.Calls.Count);
        }

        [Test]
        public async Task Execute_CanRunAgain() {
            RecordingStep step = CreateStep<RecordingStep>();
            ExecutionStepSequencer sequencer = CreateSequencer("Sequencer", step);

            await sequencer.Execute(new ExecutionStepArguments());
            await sequencer.Execute(new ExecutionStepArguments());

            Assert.AreEqual(2, step.Calls.Count);
        }

        [Test]
        public async Task ExternalSequencerStep_RunsNestedSequencerWithSameArguments() {
            RecordingStep inner = CreateStep<RecordingStep>("Inner");
            ExecutionStepSequencer nested = CreateSequencer("Nested", inner);
            ExternalSequencerStep external = CreateStep<ExternalSequencerStep>();
            SetField(external, "_sequencer", nested);
            RecordingStep after = CreateStep<RecordingStep>("After");
            ExecutionStepSequencer outer = CreateSequencer("Outer", external, after);
            ExecutionStepArguments args = new ExecutionStepArguments();

            await outer.Execute(args);

            Assert.AreSame(args, inner.Calls[0]);
            Assert.AreEqual(1, after.Calls.Count);
        }

        [Test]
        public async Task PlayOnAwakeDisabled_DoesNotRunAutomatically() {
            RecordingStep step = CreateStep<RecordingStep>();
            CreateSequencer("Sequencer", step);

            await UniTask.Yield();
            await UniTask.Yield();

            Assert.AreEqual(0, step.Calls.Count);
        }
    }
}
