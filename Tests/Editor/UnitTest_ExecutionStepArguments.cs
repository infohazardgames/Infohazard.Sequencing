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

using System.Collections.Generic;
using NUnit.Framework;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Typed arguments passed between execution steps.
    /// </summary>
    public class UnitTest_ExecutionStepArguments {
        [Test]
        public void Get_Unset_ReturnsFalseAndDefault() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<int> param = new ExecutionStepParameter<int>();

            Assert.IsFalse(param.Get(args, out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void GetOrDefault_Unset_ReturnsDefault() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<string> param = new ExecutionStepParameter<string>();
            Assert.AreEqual("fallback", param.GetOrDefault(args, "fallback"));
        }

        [Test]
        public void SetThenGet_ReturnsValue() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<string> param = new ExecutionStepParameter<string>();

            param.Set(args, "value");

            Assert.IsTrue(param.Get(args, out string value));
            Assert.AreEqual("value", value);
            Assert.AreEqual("value", param.GetOrDefault(args, "fallback"));
        }

        [Test]
        public void Set_Twice_Overwrites() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<int> param = new ExecutionStepParameter<int>();

            param.Set(args, 1);
            param.Set(args, 2);

            Assert.AreEqual(2, param.GetOrDefault(args, 0));
        }

        [Test]
        public void SetNull_CountsAsSet() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<string> param = new ExecutionStepParameter<string>();

            param.Set(args, null);

            Assert.IsTrue(param.Get(args, out string value));
            Assert.IsNull(value);
            Assert.IsNull(param.GetOrDefault(args, "fallback"));
        }

        [Test]
        public void ParametersOfSameType_AreIndependent() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<int> first = new ExecutionStepParameter<int>();
            ExecutionStepParameter<int> second = new ExecutionStepParameter<int>();

            first.Set(args, 1);

            Assert.IsFalse(second.Get(args, out _));
            Assert.AreEqual(1, first.GetOrDefault(args, 0));
        }

        [Test]
        public void ArgumentObjects_AreIndependent() {
            ExecutionStepArguments first = new ExecutionStepArguments();
            ExecutionStepArguments second = new ExecutionStepArguments();
            ExecutionStepParameter<int> param = new ExecutionStepParameter<int>();

            param.Set(first, 1);

            Assert.IsFalse(param.Get(second, out _));
        }

        [Test]
        public void SetArgumentAndGetArgument_MatchParameterMethods() {
            ExecutionStepArguments args = new ExecutionStepArguments();
            ExecutionStepParameter<IEnumerable<int>> param = new ExecutionStepParameter<IEnumerable<int>>();
            int[] regions = { 1, 2 };

            args.SetArgument(param, regions);

            Assert.IsTrue(args.GetArgument(param, out IEnumerable<int> value));
            Assert.AreSame(regions, value);
        }

        [Test]
        public void SharedStepParameters_AreDistinct() {
            // Steps communicate through these static parameters; they must not alias each other.
            ExecutionStepArguments args = new ExecutionStepArguments();
            LoadSceneOrLevelStep.ParamSceneToLoad.Set(args, "Scene");
            LoadProfileSaveDataStep.ParamProfileName.Set(args, "Profile");
            LoadStateSaveDataStep.ParamStateName.Set(args, "State");

            Assert.AreEqual("Scene", LoadSceneOrLevelStep.ParamSceneToLoad.GetOrDefault(args, null));
            Assert.AreEqual("Profile", LoadProfileSaveDataStep.ParamProfileName.GetOrDefault(args, null));
            Assert.AreEqual("State", LoadStateSaveDataStep.ParamStateName.GetOrDefault(args, null));
        }
    }
}
