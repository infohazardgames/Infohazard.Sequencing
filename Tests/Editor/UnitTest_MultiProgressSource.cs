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
using System.Linq;
using Cysharp.Threading.Tasks;
using Infohazard.Sequencing.Tests.PlayMode;
using NUnit.Framework;

namespace Infohazard.Sequencing.Tests.Editor {
    /// <summary>
    /// Combined progress and completion of several loading operations.
    /// </summary>
    public class UnitTest_MultiProgressSource {
        [Test]
        public void Progress_IsAverageOfSources() {
            FakeProgressSource a = new FakeProgressSource { Progress = 0.2f };
            FakeProgressSource b = new FakeProgressSource { Progress = 0.6f };
            MultiProgressSource multi = new MultiProgressSource(new List<IProgressSource> { a, b });

            Assert.AreEqual(0.4f, multi.Progress, 1e-5f);

            b.Progress = 1;
            Assert.AreEqual(0.6f, multi.Progress, 1e-5f);
        }

        [Test]
        public void EnumerableConstructor_SnapshotsSources() {
            FakeProgressSource a = new FakeProgressSource { Progress = 1 };
            List<IProgressSource> sources = new List<IProgressSource> { a };
            MultiProgressSource multi = new MultiProgressSource(sources.AsEnumerable());

            sources.Add(new FakeProgressSource { Progress = 0 });

            Assert.AreEqual(1f, multi.Progress, 1e-5f);
        }

        [Test]
        public void WaitForCompletionTask_CompletesWhenAllSourcesComplete() {
            FakeProgressSource a = new FakeProgressSource();
            FakeProgressSource b = new FakeProgressSource();
            MultiProgressSource multi = new MultiProgressSource(new List<IProgressSource> { a, b });

            UniTask task = multi.WaitForCompletionTask();
            Assert.AreEqual(UniTaskStatus.Pending, task.Status);

            a.Complete();
            Assert.AreEqual(UniTaskStatus.Pending, task.Status);

            b.Complete();
            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
        }

        [Test]
        public void NoSources_IsCompleteWithFullProgress() {
            // Suspected bug #20: 0 / 0 reports NaN.
            MultiProgressSource multi = new MultiProgressSource(new List<IProgressSource>());

            Assert.AreEqual(1f, multi.Progress);
            Assert.AreEqual(UniTaskStatus.Succeeded, multi.WaitForCompletionTask().Status);
        }
    }
}
