"""Reconstruct inquiry/note choices and their confirmations from whatever the run recorded.

Evidence order: the model reply text (``raw_model_text``) first, then the runner's
``deliberation_event``, then the bare ``inquiry_tool`` field. A chosen note reply and a confirmed
saved note are different facts and are never conflated; a storage failure is a tool failure, not an
invalid model reply.
"""
from experiment_context_loader import (REQUEST_KIND_INQUIRY, REQUEST_KIND_NOTE)


def classify_note_outcome(outcome):
    """invalid_reply | storage | tool — the failure class of a note deliberation outcome."""
    if isinstance(outcome, str) and outcome.startswith("invalid:"):
        return "invalid_reply"
    if outcome == "storage_failed":
        return "storage"
    return "tool"


def classify_inquiry_outcome(outcome):
    if isinstance(outcome, str) and outcome.startswith("invalid:"):
        return "invalid_reply"
    if outcome in ("failed", "dispatch_denied"):
        return "tool"
    return None


def _inquiry_from_reply(request, reply, event):
    tool = reply.get("tool")
    inquiry = {"request_index": request.request_index, "tool": tool,
               "evidence": "model_reply", "outcome": None,
               "outcome_evidence": "unknown", "sources": request.cites()}
    for key in ("kind", "id", "limit", "topic"):
        if key in reply:
            inquiry[key] = reply[key]
    if event is not None and event.get("kind") == "inquiry":
        inquiry["outcome"] = event.get("outcome")
        inquiry["outcome_evidence"] = "deliberation_event"
        event_tool = event.get("tool")
        if event_tool and tool and event_tool != tool:
            inquiry["tool_conflict"] = True
    if inquiry["outcome"] is not None:
        failure_class = classify_inquiry_outcome(inquiry["outcome"])
        if failure_class:
            inquiry["failure_class"] = failure_class
    return inquiry


def _inquiry_from_run(request, event, inquiry_tool):
    if event is not None and event.get("kind") == "inquiry":
        outcome = event.get("outcome")
        inquiry = {"request_index": request.request_index,
                   "tool": event.get("tool") or inquiry_tool,
                   "evidence": "deliberation_event", "outcome": outcome,
                   "outcome_evidence": "deliberation_event",
                   "result_chars": event.get("result_chars"),
                   "sources": request.cites()}
        failure_class = classify_inquiry_outcome(outcome)
        if failure_class:
            inquiry["failure_class"] = failure_class
        return inquiry
    return {"request_index": request.request_index, "tool": inquiry_tool,
            "evidence": "inquiry_tool", "outcome": None,
            "outcome_evidence": "unknown", "sources": request.cites()}


def _note_from_reply(request, reply):
    note_text = reply.get("note")
    if not isinstance(note_text, str):
        note_text = None
    return {"request_index": request.request_index,
            "note_chars": len(note_text.strip()) if note_text else 0,
            "evidence": "model_reply", "sources": request.cites()}


def _note_from_run(request, event):
    result_chars = event.get("result_chars")
    return {"request_index": request.request_index,
            "note_chars": result_chars if isinstance(result_chars, int) else None,
            "evidence": "deliberation_event", "sources": request.cites()}


def tool_activity(request, kind=None):
    """Describe one joined request's inquiry/note activity.

    Returns ``{"kind", "inquiry", "note", "note_saved", "note_failure"}``. The chosen reply and
    the confirmed save are separate entries, and a failed note carries its failure class.
    """
    kind = kind or request.request_kind()
    reply, _ = request.model_reply()
    event = request.deliberation_event()
    inquiry_tool = request.inquiry_tool()
    result = {"kind": kind, "inquiry": None, "note": None,
              "note_saved": None, "note_failure": None}
    if kind == REQUEST_KIND_INQUIRY:
        if isinstance(reply, dict) and "tool" in reply:
            result["inquiry"] = _inquiry_from_reply(request, reply, event)
        else:
            result["inquiry"] = _inquiry_from_run(request, event, inquiry_tool)
        return result
    if kind != REQUEST_KIND_NOTE:
        return result

    if isinstance(reply, dict) and "note" in reply:
        result["note"] = _note_from_reply(request, reply)
    elif event is not None and event.get("kind") == "note":
        result["note"] = _note_from_run(request, event)

    if event is not None and event.get("kind") == "note":
        outcome = event.get("outcome")
        if outcome == "saved":
            result["note_saved"] = {
                "request_index": request.request_index,
                "evidence": "deliberation_event", "outcome": outcome,
                "result_chars": event.get("result_chars"),
                "sources": request.cites()}
        else:
            result["note_failure"] = {
                "request_index": request.request_index,
                "outcome": outcome,
                "failure_class": classify_note_outcome(outcome),
                "sources": request.cites()}
    return result
