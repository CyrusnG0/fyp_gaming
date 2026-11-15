"""Independent audit of a spatial_context matched pair, from the saved files alone.

The C# batch decides whether a pair differs only in the production spatial context. This re-derives that from
prompts.ndjson, the recorder's exchanges and run records, and match.json, with no copy of the production prompt text:
it looks for the section markers and checks structure (treatment system prompt = control system prompt + one constant
addendum, same seat observation hash, different version and schema hash, a truncation report per treatment request,
no hidden id in any prompt). It says nothing about model behaviour: the offline fake provider's replies are scripted.
"""
import json
import os

ADDENDUM_MARK = "SPATIAL CONTEXT AND SIGHT BOUNDARIES"
SECTION_MARK = "SPATIAL CONTEXT (seat-filtered"


def _lines(path):
    with open(path, encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def _arm(batch_dir, name):
    arm_dir = os.path.join(batch_dir, name)
    with open(os.path.join(arm_dir, "match.json"), encoding="utf-8") as handle:
        match = json.load(handle)
    files = match["files"]
    exchanges = [x for x in _lines(os.path.join(arm_dir, files["exchanges"]["path"])) if x.get("exchange_event") == "request"]
    records, seats = {}, {}
    for r in _lines(os.path.join(arm_dir, files["run_jsonl"]["path"])):
        seats[r["seat_id"]] = seats.get(r["seat_id"], 0) + 1
        records[(r["seat_id"], seats[r["seat_id"]])] = r.get("observation_hash")
    return {"match": match, "prompts": _lines(os.path.join(arm_dir, files["prompts"]["path"])), "exchanges": exchanges, "observations": records}


def audit_spatial_pair(batch_dir, seed, recorded_pairs, problems, notes):
    """Appends to problems/notes. recorded_pairs is batch-result's entry for this pair (a list of 0 or 1)."""
    label = "seed%d" % seed
    try:
        control, treatment = _arm(batch_dir, label + "-control"), _arm(batch_dir, label + "-treatment")
    except (OSError, KeyError, ValueError) as error:
        problems.append("%s: spatial evidence could not be read (%s)" % (label, error))
        return
    found = []
    if any(ADDENDUM_MARK in p["system"] or SECTION_MARK in p["user"] for p in control["prompts"]):
        found.append("a control prompt carries spatial material")
    if not treatment["prompts"] or not all(ADDENDUM_MARK in p["system"] and SECTION_MARK in p["user"] for p in treatment["prompts"]):
        found.append("a treatment prompt lacks the spatial addendum or section")
    by_key = {(p["seat"], p["seat_request"]): p for p in control["prompts"]}
    suffixes = set()
    for p in treatment["prompts"]:
        other = by_key.get((p["seat"], p["seat_request"]))
        if other is None or not p["system"].startswith(other["system"]) or ADDENDUM_MARK not in p["system"][len(other["system"]):]:
            found.append("treatment system prompt %s#%s is not control + a spatial addendum" % (p["seat"], p["seat_request"]))
            break
        suffixes.add(p["system"][len(other["system"]):])
    if len(suffixes) > 1:
        found.append("the appended addendum is not the same text in every request")
    versions = {a: {x["prompt_version"] for x in arm["exchanges"]} for a, arm in (("control", control), ("treatment", treatment))}
    hashes = {a: {x["schema_hash"] for x in arm["exchanges"]} for a, arm in (("control", control), ("treatment", treatment))}
    if versions["control"] & versions["treatment"] or not all("spatial" in v for v in versions["treatment"]) or any("spatial" in v for v in versions["control"]):
        found.append("prompt versions: control %s, treatment %s" % (sorted(versions["control"]), sorted(versions["treatment"])))
    if hashes["control"] & hashes["treatment"]:
        found.append("a schema hash is shared between the arms")
    if control["observations"] != treatment["observations"] or not control["observations"]:
        found.append("the seat observation hashes differ between the arms")
    if any("spatial_truncation" in x for x in control["exchanges"]) or not all("spatial_truncation" in x for x in treatment["exchanges"]):
        found.append("spatial_truncation reports: expected none in control and one per treatment request")
    cut = [x["spatial_truncation"] for x in treatment["exchanges"] if x.get("spatial_truncation", {}).get("truncated")]
    c_evidence, t_evidence = control["match"].get("spatial_evidence") or {}, treatment["match"].get("spatial_evidence") or {}
    if c_evidence.get("world_start_digest") is None or c_evidence.get("world_start_digest") != t_evidence.get("world_start_digest"):
        found.append("the arms did not start from the same canonical world")
    for arm_name, evidence in (("control", c_evidence), ("treatment", t_evidence)):
        if evidence.get("hidden_id_leaks"):
            found.append("%s prompts leak hidden ids: %s" % (arm_name, evidence["hidden_id_leaks"][:2]))
    for item in found:
        problems.append("%s: spatial pair check: %s" % (label, item))
    if recorded_pairs and (recorded_pairs[0].get("spatial_problems") == []) != (not found):
        problems.append("%s: batch-result spatial_problems disagrees with the saved prompts (%d found here)" % (label, len(found)))
    if not found:
        notes.append("%s: spatial pair ok from saved files: %d treatment request(s), %d truncated by the production builder%s" % (
            label, len(treatment["prompts"]), len(cut), " (%s)" % ", ".join(sorted({c.get("reason", "?") for c in cut})) if cut else ""))
