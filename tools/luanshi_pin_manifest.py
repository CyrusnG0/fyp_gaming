#!/usr/bin/env python3
"""Write a copy of an experiment-batch manifest pinned to a chosen commit (default: HEAD).

A manifest cannot name the commit that contains it, so the example manifest pins the commit its code was tested at,
not the integration commit a real batch must run on. Generate the real manifest from the example once the final
integration commit is checked out, and keep the copy OUTSIDE the tracked tree (or in an ignored folder) so writing it
does not move HEAD:

  python tools/luanshi_pin_manifest.py fyp_gaming/Tests/ExperimentBatch/examples/example-manifest.json --out <path>/manifest.json
  python tools/luanshi_experiment_batch.py run <path>/manifest.json --out <runs-dir> --require-head

Only pinned_commit changes. The source file is never modified and an existing output file is never overwritten.
Exit code: 0 written, 2 invalid input.
"""
import argparse
import json
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def resolve(commit):
    out = subprocess.run(["git", "-C", REPO, "rev-parse", "--verify", "--quiet", commit + "^{commit}"], capture_output=True, text=True)
    return out.stdout.strip() if out.returncode == 0 else None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("manifest")
    parser.add_argument("--out", required=True, help="new manifest path (must not exist)")
    parser.add_argument("--commit", default="HEAD", help="revision to pin (default HEAD)")
    args = parser.parse_args(argv)
    full = resolve(args.commit)
    if full is None:
        print("error: %s is not a commit in this repository" % args.commit)
        return 2
    if os.path.exists(args.out):
        print("error: %s already exists; refusing to overwrite" % args.out)
        return 2
    try:
        with open(args.manifest, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        print("error: cannot read manifest (%s)" % error)
        return 2
    manifest["pinned_commit"] = full
    with open(args.out, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print("pinned %s to %s" % (args.out, full))
    return 0


if __name__ == "__main__":
    sys.exit(main())
