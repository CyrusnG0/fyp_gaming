using System;

namespace LuanShi.Engine
{
    /// <summary>
    /// Dispatches an already parsed, validated request to an injected
    /// <see cref="InvestigationService"/> (LS-020).
    ///
    /// This class intentionally has no provider, file, network, command-execution or budget
    /// dependency. It never constructs an action command, never mutates game state and never
    /// inspects model-supplied identity: match and seat context live only inside the service,
    /// which was built by the host from a fresh detached observation. The future host MUST obtain
    /// investigation-budget permission before calling <see cref="Dispatch"/>; this class neither
    /// admits nor consumes budget, so a successful dispatch is not itself permission to run.
    /// </summary>
    public sealed class InvestigationDispatcher
    {
        private readonly InvestigationService _service;

        public InvestigationDispatcher(InvestigationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Runs exactly one read-only tool operation. The returned result keeps the LS-018
        /// bounded response envelope unchanged; callers must honour its <c>ok</c>,
        /// <c>truncated</c> and error fields rather than retrying blindly.
        /// </summary>
        public InvestigationResult Dispatch(InvestigationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.IsValidated)
            {
                throw new InvalidOperationException(
                    "Only requests produced by InvestigationRequestParser may be dispatched.");
            }

            switch (request.Tool)
            {
                case InvestigationRequestTool.ManualLookup:
                    return _service.ManualLookup(request.Topic);
                case InvestigationRequestTool.InspectEntity:
                    return _service.InspectEntity(request.EntityKind, request.EntityId);
                case InvestigationRequestTool.SearchHistory:
                    return _service.SearchHistory(request.Query, request.Limit, request.Offset);
                case InvestigationRequestTool.RecallMemory:
                    return _service.RecallMemory(request.Query, request.Limit);
                default:
                    throw new InvalidOperationException(
                        "The parsed request carries an unsupported tool.");
            }
        }
    }
}
