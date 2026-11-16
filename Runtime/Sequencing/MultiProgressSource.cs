using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace Infohazard.Sequencing {
    public class MultiProgressSource : IProgressSource {
        private List<IProgressSource> _sources;

        public MultiProgressSource(List<IProgressSource> list) {
            _sources = list;
        }

        public MultiProgressSource(IEnumerable<IProgressSource> sources) {
            _sources = sources.ToList();
        }

        // With no sources there is nothing to wait for, so it counts as complete.
        public float Progress => _sources.Count == 0 ? 1 : _sources.Sum(source => source.Progress) / _sources.Count;

        public UniTask WaitForCompletionTask() {
            if (_sources.Count == 0) return UniTask.CompletedTask;
            return UniTask.WhenAll(_sources.Select(source => source.WaitForCompletionTask()));
        }
    }
}