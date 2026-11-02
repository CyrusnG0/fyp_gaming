using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

// [LS015-W4] Harness-only transport for the stale-response tests.
//
// FakeLlmTransport awaits its deferred completion with the runner's CancellationToken, so cancelling
// a turn faults the SendAsync task and the runner never sees a late completion. This transport
// deliberately IGNORES the token: SendAsync returns the deferred task directly and only the test can
// complete it. That lets the harness prove that the runner itself - not the transport - makes a late
// response inert. It is never used by production code.

internal static partial class Harness
{
    private sealed class W3UncancellableTransport : ILlmTransport
    {
        private readonly object _gate = new object();
        private readonly Queue<TaskCompletionSource<LlmResponse>> _pending =
            new Queue<TaskCompletionSource<LlmResponse>>();
        private int _calls;

        public string Provider => "fake-uncancellable";
        public string Model => "fake-uncancellable";

        /// <summary>Number of SendAsync calls, including calls that got no scripted task.</summary>
        public int CallCount { get { lock (_gate) return _calls; } }

        /// <summary>Queue the task for the next SendAsync; the test completes it by hand.</summary>
        public TaskCompletionSource<LlmResponse> EnqueueDeferred()
        {
            var completion = new TaskCompletionSource<LlmResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _pending.Enqueue(completion);
            return completion;
        }

        public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            // Intentionally do not inspect or register on ct (see the file header).
            TaskCompletionSource<LlmResponse> completion;
            lock (_gate)
            {
                _calls++;
                completion = _pending.Count == 0 ? null : _pending.Dequeue();
            }
            if (completion != null) return completion.Task;

            // No scripted answer: stay in flight so the runner keeps requesting instead of ending.
            return new TaskCompletionSource<LlmResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
