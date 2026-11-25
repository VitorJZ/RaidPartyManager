"""Embed the versioned sound effects manifest in a standalone Tampermonkey script."""

import argparse
import hashlib
import json
from pathlib import Path

from GenerateElevenLabsSfx import MANIFEST, ROOT, read_manifest

SOURCE = ROOT / "Tools" / "ElevenLabsSfx.Tampermonkey.js"
OUTPUT = ROOT / "Logs" / "ElevenLabsUserscriptInstall" / "ElevenLabsSfx.user.js"
MARKER = "/* BUILD:MANIFEST */ null"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()

    content = SOURCE.read_text(encoding="utf-8")
    if content.count(MARKER) != 1:
        parser.error("The source must have exactly one manifest marker.")
    jobs = []
    for item in read_manifest(MANIFEST):
        request = [item[k] for k in ("filename", "duration_seconds", "loop", "text")]
        signature = hashlib.sha256(json.dumps(request, ensure_ascii=False).encode()).hexdigest()
        jobs.append(dict(zip(("file", "seconds", "loop", "prompt"), request), signature=signature))
    if any(round(item["seconds"] * 10, 8) != round(item["seconds"] * 10) for item in jobs):
        parser.error("The Chrome form only accepts multiples of 0.1 seconds.")

    encoded = json.dumps(jobs, ensure_ascii=True, separators=(",", ":"))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(content.replace(MARKER, encoded), encoding="utf-8", newline="\n")
    print(f"{len(jobs)} sound effects in {args.output}")


if __name__ == "__main__":
    main()
