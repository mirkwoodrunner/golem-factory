"""PostToolUse hook: after a Write or Edit to godot/data/*.json, check the file parses as strict JSON.

DefinitionLoader reads these with System.Text.Json's defaults (JsonDocument.Parse): no comments, no trailing
commas, no NaN or Infinity. Python's json rejects the first two; the NaN/Infinity guard below rejects the rest,
so the hook refuses what the loader would refuse. It checks syntax only. Unknown keys and bad references are
still the loader's job and show up in `dotnet test` or on launch.

Exit 2 puts stderr in front of Claude, so the error is fixed straight away. Exit 1 would show it to the
user only. Any other file, or a payload with no path, exits 0 without a word.
"""
import json
import re
import sys

DATA_FILE = re.compile(r"(^|/)godot/data/[^/]+\.json$", re.IGNORECASE)


def reject_constant(name):
    raise ValueError(f"{name} is not valid JSON")


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0
    path = (payload.get("tool_input") or {}).get("file_path") or ""
    if not DATA_FILE.search(path.replace("\\", "/")):
        return 0
    try:
        with open(path, encoding="utf-8-sig") as f:
            json.loads(f.read(), parse_constant=reject_constant)
    except FileNotFoundError:
        return 0
    except json.JSONDecodeError as e:
        print(f"{path}: not valid JSON at line {e.lineno}, column {e.colno}: {e.msg}. "
              "DefinitionLoader will refuse it on launch; fix the file.", file=sys.stderr)
        return 2
    except ValueError as e:
        print(f"{path}: {e}. DefinitionLoader will refuse it on launch; fix the file.", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
