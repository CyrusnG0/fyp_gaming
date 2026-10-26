"""Canonical-log audit: player-facing UI must never read or write the canonical history (LS-014).

Why this exists: `KingdomDemo.Report.cs` used to write `engine.Log.ToJsonLines()` to
`persistentDataPath/luanshi_log.jsonl`, which leaked every seat's hidden history in hotseat. The court
report now exports only the viewing seat's filtered observation through `HistoryExport`. The canonical
log remains an explicit engine/harness API for replay and research; it is not a player-reachable file.

This audit scans the whole UI script directory, not just the court-report file, so a future adapter
cannot reintroduce a canonical dump elsewhere. Every normalised `.Log` member access is forbidden
unless it is `Debug.Log` / `Debug.Log*`; the ban also covers call results (`GetEngine().Log`) and
null-forgiving aliases (`engine!.Log`). Comments are stripped (without stripping string literals) and
whitespace around `.` / `?.` is normalised before matching, so formatting or an alias such as
`var e = engine; e.Log.Records` cannot hide the access.

Usage: python canonical_log_audit.py [path ...]
Each path may be a .cs file or a directory (its *.cs files are audited). With no arguments it audits
Assets/GameUI/Scripts in the same checkout.
Exit code: 0 when clean, 1 on a forbidden access, 2 on a setup problem.
"""
import os
import re
import sys

# The label is what the report calls the forbidden form; the pattern is matched against the
# comment-stripped, dot-normalised source. Any `.Log` member access is forbidden except Debug.Log*,
# so aliases (`e.Log`), null-forgiving forms (`engine!.Log`) and call results (`GetEngine().Log`)
# cannot slip through. `\b(?!Debug)` exempts only the exact Debug identifier; a longer name such as
# `Debug.LogWarning` never matches `.Log\b` anyway.
FORBIDDEN = (
    ("non-Debug .Log", re.compile(r"\b(?!Debug)\w+[!?]?\s*\.\s*Log\b")),
    (".Log after a call", re.compile(r"\)\s*[!?]?\s*\.\s*Log\b")),
    (".Log.Records", re.compile(r"\.\s*Log\s*\.\s*Records\b")),
    ("EventLog", re.compile(r"\bEventLog\b")),
    ("EventRecord", re.compile(r"\bEventRecord\b")),
    ("ToJsonLines", re.compile(r"\bToJsonLines\b")),
    ("luanshi_log", re.compile(r"\bluanshi_log\b")),
)

DEFAULT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "Assets", "GameUI", "Scripts",
)


def expand(paths):
    """Expand directories to every *.cs file beneath them; pass explicit files through unchanged."""
    files = []
    for path in paths:
        if os.path.isdir(path):
            for root, _, names in os.walk(path):
                files.extend(
                    os.path.join(root, name)
                    for name in sorted(names)
                    if name.endswith(".cs")
                )
        else:
            files.append(path)
    return files


def strip_comments(src):
    """Blank // and /* */ comments while keeping string/char literals and line count intact."""
    out = list(src)
    i, n, state = 0, len(src), "code"
    while i < n:
        c = src[i]
        if state == "code":
            if c == "/" and i + 1 < n and src[i + 1] == "/":
                out[i] = out[i + 1] = " "
                i += 2
                while i < n and src[i] not in "\r\n":
                    out[i] = " "
                    i += 1
                continue
            if c == "/" and i + 1 < n and src[i + 1] == "*":
                out[i] = out[i + 1] = " "
                i += 2
                while i < n:
                    if src[i] == "*" and i + 1 < n and src[i + 1] == "/":
                        out[i] = out[i + 1] = " "
                        i += 2
                        break
                    if src[i] not in "\r\n":
                        out[i] = " "
                    i += 1
                continue
            # Enter a string literal without letting its contents start a comment.
            if c == "@" and i + 1 < n and src[i + 1] == '"':
                i += 2
                state = "verbatim"
                continue
            if c == "@" and i + 2 < n and src[i + 1] == "$" and src[i + 2] == '"':
                i += 3
                state = "verbatim"
                continue
            if c == "$":
                if i + 1 < n and src[i + 1] == '"':
                    i += 2
                    state = "string"
                    continue
                if i + 2 < n and src[i + 1] == "@" and src[i + 2] == '"':
                    i += 3
                    state = "verbatim"
                    continue
            if c == '"':
                i += 1
                state = "string"
                continue
            if c == "'":
                i += 1
                state = "char"
                continue
            i += 1
        elif state == "string":
            if c == "\\":
                i += 2
                continue
            if c == '"':
                state = "code"
            i += 1
        elif state == "verbatim":
            if c == '"' and i + 1 < n and src[i + 1] == '"':
                i += 2
                continue
            if c == '"':
                state = "code"
            i += 1
        else:  # char literal
            if c == "\\":
                i += 2
                continue
            if c == "'":
                state = "code"
            i += 1
    return "".join(out)


def normalise_dots(src):
    """Collapse whitespace and remove it around '.' / '?.', returning text plus line mapping."""
    line_at = [0] * len(src)
    line = 1
    for index, char in enumerate(src):
        line_at[index] = line
        if char == "\n":
            line += 1

    out, match_lines = [], []
    i, n, prev = 0, len(src), None
    while i < n:
        if src[i].isspace():
            j = i
            while j < n and src[j].isspace():
                j += 1
            next_char = src[j] if j < n else None
            remove = prev == "." or next_char == "."
            if not remove and next_char == "?":
                k = j + 1
                while k < n and src[k].isspace():
                    k += 1
                remove = k < n and src[k] == "."
            if not remove:
                out.append(" ")
                match_lines.append(line_at[i])
            i = j
            continue
        out.append(src[i])
        match_lines.append(line_at[i])
        prev = src[i]
        i += 1
    return "".join(out), match_lines


def audit(path):
    src = open(path, encoding="utf-8").read()
    cleaned = strip_comments(src)
    normalised, match_lines = normalise_dots(cleaned)
    problems = []
    for label, pattern in FORBIDDEN:
        for match in pattern.finditer(normalised):
            line = match_lines[match.start()] if match.start() < len(match_lines) else 0
            problems.append((line, "%s:%d: forbidden canonical-log use `%s`"
                                   % (path, line, label)))
    return sorted(set(problems))


def main(argv):
    paths = expand(argv or [DEFAULT])
    if not paths:
        print("error: no .cs files found to audit", file=sys.stderr)
        return 2

    exit_code = 0
    for path in paths:
        if not os.path.isfile(path):
            print("error: file not found: %s" % path, file=sys.stderr)
            return 2
        problems = audit(path)
        print("--- canonical-log use in %s ---" % path)
        if not problems:
            print("  no forbidden canonical .Log (non-Debug) / Log.Records / EventLog / "
                  "EventRecord / ToJsonLines / luanshi_log use")
        for _, problem in problems:
            print("  CANONICAL-LOG PROBLEM " + problem)
        if problems:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
