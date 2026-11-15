"""Shared vocabulary, documented rule constants and defensive redaction.

Everything here is inert data: no file I/O, no analysis. Keeping it separate lets the loader,
analysis and report modules agree on one schema version and one event vocabulary.
"""
import re

# Schema 2 changes reported field semantics: request kinds, neutral-contact classification,
# summary last-command fidelity and the season-end scoreboard.
SCHEMA = "luanshi.experiment-context-analysis/2"
TOOL_VERSION = "2.0.0"

RUN_RECORD = "run_record"
EXCHANGE_REQUEST = "exchange_request"
EXCHANGE_RESULT = "exchange_result"
EXPERIMENT_CONFIG = "experiment_config"
TURN_END = "turn_end"
UNKNOWN_EXCHANGE = "unknown_exchange"

# One joined request is exactly one of these.
REQUEST_KINDS = ("command", "inquiry", "note", "no_command")

IDENTITY_FIELDS = ("run_id", "seat_id", "season", "request_index", "turn_token")
OPTIONAL_IDENTITY_FIELDS = ("prompt_version", "schema_hash", "provider", "model",
                            "game_seed", "seed")

# A documented, static scenario description. Values are copied from
# Assets/GameEngine/Scenarios/CompactScenario.cs (compact-contact-v1) and are never derived from
# the run under analysis: they describe the fixed match, not what any seat did.
SCENARIO_GEOMETRY = {
    "compact-contact-v1": {
        "centre": (13, 8),
        "centre_label": "city-neutral (Wancheng), the contested neutral city",
        "map_relative_path": "fyp_gaming/Tests/Harness/demo-map.json",
        "map_width": 26,
        "map_height": 17,
    },
}

# Documented rule constants used to describe what a seat could see, with provenance.
MORALE_YIELD_PENALTY_THRESHOLD = 30   # Assets/GameEngine/BalanceConfig.cs (LOCKED, ADR-010)
REVOLT_RISK_MORALE_THRESHOLD = 25     # Assets/GameEngine/BalanceConfig.cs (design §4.1)

# Events that mean a resource/morale/supply penalty actually landed in a seat's view.
PENALTY_EVENT_TYPES = ("starvation", "revolt", "desertion")
# Capture-shaped events: a city changing hands or a watchtower being taken (both are public).
CAPTURE_EVENT_TYPES = ("city_captured", "watch_tower_captured")
PRESSURE_EVENT_TYPES = ("starvation", "revolt", "desertion", "city_captured",
                        "watch_tower_captured", "battle_field", "battle_siege",
                        "army_destroyed", "army_routed")

# Only these response fields are ever read. raw_body, content_text, reasoning fields and
# error_detail are deliberately absent so they cannot reach a report.
RESULT_RESPONSE_FIELDS = ("ok", "http_status", "finish_reason", "input_tokens", "output_tokens",
                          "reasoning_tokens", "total_tokens", "reasoning_content_chars",
                          "content_chars", "content_trimmed_empty", "error_kind", "latency_ms")

REDACTED = "[REDACTED]"

_SECRET_PATTERNS = (
    re.compile(r"(?i)\bsk-[A-Za-z0-9_\-]{8,}"),
    re.compile(r"(?i)\bBearer\s+[A-Za-z0-9._\-]{8,}"),
    re.compile(r"(?i)(api[_-]?key|api[_-]?token|access[_-]?token|secret|password)"
               r"(\s*[\"']?\s*[:=]\s*[\"']?)([^\s\"',;}\]]{4,})"),
    re.compile(r"(?i)\bLUANSHI_LLM_API_KEY\s*[:=]\s*\S+"),
    re.compile(r"(?i)\b[a-z0-9_]*(api_?key|token|secret)[a-z0-9_]*\s*=\s*[^\s;,\"']{6,}"),
)


def redact_text(value):
    """Defensive scrub applied to every string this tool emits."""
    if not isinstance(value, str):
        return value
    text = value
    for pattern in _SECRET_PATTERNS:
        if pattern.groups >= 3:
            text = pattern.sub(lambda m: m.group(1) + m.group(2) + REDACTED, text)
        else:
            text = pattern.sub(REDACTED, text)
    return text


def redact_structure(value):
    """Recursively redact strings inside JSON-ready data (dict/list/str)."""
    if isinstance(value, str):
        return redact_text(value)
    if isinstance(value, list):
        return [redact_structure(item) for item in value]
    if isinstance(value, tuple):
        return [redact_structure(item) for item in value]
    if isinstance(value, dict):
        return {key: redact_structure(item) for key, item in value.items()}
    return value
