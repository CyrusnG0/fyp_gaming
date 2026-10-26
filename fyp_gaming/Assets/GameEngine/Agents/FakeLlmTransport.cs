using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LuanShi.Engine
{
    /// <summary>
    /// Scripted transport for harnesses and offline development. It performs no game access; every
    /// response is labelled provider="fake" so an offline run can never be mistaken for a live one.
    /// </summary>
    public sealed class FakeLlmTransport : ILlmTransport
    {
        private sealed class Scripted
        {
            public string Text;
            public LlmErrorKind Error;
            public string Detail;
            public int DelayMs;
            public int? InputTokens;
            public int? OutputTokens;
            public TaskCompletionSource<LlmResponse> Deferred;
            public string RequestId;
        }

        private readonly object _gate = new object();
        private readonly Queue<Scripted> _queue = new Queue<Scripted>();
        private readonly List<LlmRequest> _requests = new List<LlmRequest>();

        public string Provider => "fake";
        public string Model { get; set; } = "fake-model";

        /// <summary>Number of SendAsync calls, including cancelled and failed calls.</summary>
        public int CallCount { get { lock (_gate) return _requests.Count; } }

        public IReadOnlyList<LlmRequest> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        public void EnqueueSuccess(string text, int delayMs = 0, int? inputTokens = null,
            int? outputTokens = null, string requestId = null)
        {
            lock (_gate)
            {
                _queue.Enqueue(new Scripted
                {
                    Text = text, DelayMs = delayMs, InputTokens = inputTokens,
                    OutputTokens = outputTokens, RequestId = requestId,
                });
            }
        }

        public void EnqueueError(LlmErrorKind error, string detail = null, int delayMs = 0)
        {
            lock (_gate)
            {
                _queue.Enqueue(new Scripted
                {
                    Error = error == LlmErrorKind.None ? LlmErrorKind.Network : error,
                    Detail = detail, DelayMs = delayMs,
                });
            }
        }

        /// <summary>
        /// Queue a response the test completes by hand. Useful for cancellation and stale-response
        /// checks without timing-dependent sleeps.
        /// </summary>
        public TaskCompletionSource<LlmResponse> EnqueueDeferred()
        {
            var completion = new TaskCompletionSource<LlmResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _queue.Enqueue(new Scripted { Deferred = completion });
            }
            return completion;
        }

        public void Reset()
        {
            lock (_gate)
            {
                _queue.Clear();
                _requests.Clear();
            }
        }

        public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            Scripted item;
            lock (_gate)
            {
                _requests.Add(req);
                item = _queue.Count == 0 ? null : _queue.Dequeue();
            }

            ct.ThrowIfCancellationRequested();
            if (item == null)
                return LlmResponse.Failure(LlmErrorKind.Network, "fake transport has no scripted response");

            if (item.Deferred != null)
            {
                LlmResponse deferred = await WaitDeferredAsync(item.Deferred.Task, ct);
                return deferred ?? LlmResponse.Failure(LlmErrorKind.Network, "fake deferred response was null");
            }

            if (item.DelayMs > 0)
                await Task.Delay(item.DelayMs, ct);
            ct.ThrowIfCancellationRequested();

            if (item.Error != LlmErrorKind.None)
                return LlmResponse.Failure(item.Error, item.Detail, item.DelayMs);

            return LlmResponse.Success(item.Text, item.DelayMs, item.InputTokens,
                item.OutputTokens, item.RequestId);
        }

        private static async Task<LlmResponse> WaitDeferredAsync(Task<LlmResponse> task, CancellationToken ct)
        {
            if (task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                return await task;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(task, cancelled.Task);
                if (completed != task) throw new OperationCanceledException(ct);
            }
            return await task;
        }
    }
}
