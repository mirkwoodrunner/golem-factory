"""Convert the Unity ScriptableObject .asset files into the JSON the Godot build loads.

    python Tools/Data/convert_unity_assets.py            # writes godot/data/*.json
    python Tools/Data/convert_unity_assets.py --check    # exit 1 if the JSON is stale

Milestone G1 of docs/godot-conversion-plan.md. The Unity project is the source of truth
for authored data until cutover (G10), so this is re-runnable, and --check is how a pass
proves the two have not drifted. After cutover the JSON is edited directly and this
script retires with Assets/.

What it does with Unity's YAML:
  * The class of each asset comes from its m_Script GUID, resolved through the .cs.meta
    files -- not from the folder it sits in or m_EditorClassIdentifier (which Unity leaves
    blank on older assets).
  * A reference {fileID: N, guid: G} becomes the referenced asset's m_Name, which is what
    DefinitionCatalog and the save file already key on. A sprite reference becomes the
    PNG's file name. {fileID: 0} becomes null.
  * Scalars are written as Unity stored them: enums as integers, bools as 0/1. The C#
    loader owns turning those into types, so this script needs no copy of the C# schema.
  * Unity's own bookkeeping (m_*) is dropped.

Dependency-free on purpose: the asset files use a small YAML subset (block maps, block
lists, inline {..} maps, []), parsed below.
"""
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(REPO, "Assets")
SOURCE = os.path.join(ASSETS, "_Project", "ScriptableObjects")
OUT = os.path.join(REPO, "godot", "data")

# C# class -> output file. Every definition class the Core loader knows.
OUTPUT_FILE = {
    "ChassisDefinition": "chassis.json",
    "LogicCoreDefinition": "logic_cores.json",
    "AppendageActionDefinition": "appendages.json",
    "RecipeDefinition": "recipes.json",
    "ClockTowerStageDefinition": "clock_tower_stages.json",
    "DraftableCardDefinition": "assembly_line_cards.json",
    "DraftableCardCatalog": "assembly_line_decks.json",
}

GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
CLASS = re.compile(r"\b(?:class|struct)\s+(\w+)")


# --- GUID index ------------------------------------------------------------------------

def index_guids():
    """guid -> path, for every asset under Assets/ that has a .meta."""
    guids = {}
    for root, _, files in os.walk(ASSETS):
        for name in files:
            if name.endswith(".meta"):
                with open(os.path.join(root, name), encoding="utf-8") as f:
                    m = GUID.search(f.read())
                if m:
                    guids[m.group(1)] = os.path.join(root, name[:-len(".meta")])
    return guids


def script_class(path):
    """The first class declared in a .cs file named after it (Unity's own rule)."""
    stem = os.path.splitext(os.path.basename(path))[0]
    with open(path, encoding="utf-8-sig") as f:
        names = CLASS.findall(f.read())
    if stem in names:
        return stem
    raise ValueError(f"{path}: no class named {stem}")


# --- The YAML subset -------------------------------------------------------------------

def parse_scalar(text):
    text = text.strip()
    if text == "":
        return ""
    if text == "[]":
        return []
    if text.startswith("{") and text.endswith("}"):
        inner = text[1:-1].strip()
        out = {}
        for part in inner.split(","):
            if part.strip():
                key, _, value = part.partition(":")
                out[key.strip()] = parse_scalar(value)
        return out
    if re.fullmatch(r"-?\d+", text):
        return int(text)
    if re.fullmatch(r"-?\d*\.\d+(?:[eE][-+]?\d+)?|-?\d+[eE][-+]?\d+", text):
        return float(text)
    if text[0] in "'\"":
        raise ValueError(f"quoted scalar not supported: {text}")
    return text


def parse_block(lines, start, indent):
    """Parse a block map or list whose items sit at `indent`. Returns (value, next index)."""
    if lines[start][1].startswith("- "):
        items = []
        i = start
        while i < len(lines) and lines[i][0] == indent and lines[i][1].startswith("- "):
            body = lines[i][1][2:]
            if ":" in body and not body.startswith("{"):
                # "- key: value" opens a map whose further keys sit at indent + 2.
                key, _, value = body.partition(":")
                item = {}
                i = assign(item, key.strip(), value, lines, i, indent + 2)
                while i < len(lines) and lines[i][0] == indent + 2 and not lines[i][1].startswith("- "):
                    key, _, value = lines[i][1].partition(":")
                    i = assign(item, key.strip(), value, lines, i, indent + 2)
                items.append(item)
            else:
                items.append(parse_scalar(body))
                i += 1
        return items, i

    out = {}
    i = start
    while i < len(lines) and lines[i][0] == indent:
        key, _, value = lines[i][1].partition(":")
        i = assign(out, key.strip(), value, lines, i, indent)
    return out, i


def assign(target, key, value, lines, i, indent):
    if value.strip() != "":
        target[key] = parse_scalar(value)
        return i + 1
    i += 1
    # A block value: deeper lines, or a list at the SAME indent (Unity's list style).
    if i < len(lines) and (lines[i][0] > indent or (lines[i][0] == indent and lines[i][1].startswith("- "))):
        target[key], i = parse_block(lines, i, lines[i][0])
    else:
        target[key] = ""
    return i


def parse_asset(path):
    with open(path, encoding="utf-8") as f:
        raw = f.read().splitlines()
    lines = []
    for line in raw:
        if line.startswith(("%", "---")) or not line.strip():
            continue
        stripped = line.lstrip(" ")
        lines.append((len(line) - len(stripped), stripped))
    doc, end = parse_block(lines, 0, 0)
    if end != len(lines):
        raise ValueError(f"{path}: unparsed from line {end}: {lines[end]}")
    if len(doc) != 1 or "MonoBehaviour" not in doc:
        raise ValueError(f"{path}: expected one MonoBehaviour document")
    return doc["MonoBehaviour"]


# --- Conversion ------------------------------------------------------------------------

def resolve(value, guids, names):
    if isinstance(value, list):
        return [resolve(v, guids, names) for v in value]
    if isinstance(value, dict):
        if set(value) >= {"fileID"} and set(value) <= {"fileID", "guid", "type"}:
            if value["fileID"] == 0:
                return None
            target = guids.get(value.get("guid"))
            if target is None:
                raise ValueError(f"dangling reference {value}")
            if target.endswith(".asset"):
                return names[target]
            return os.path.basename(target)  # a sprite: its file name
        return {k: resolve(v, guids, names) for k, v in value.items()}
    return value


def convert():
    guids = index_guids()
    assets = []
    for root, _, files in os.walk(SOURCE):
        for name in sorted(files):
            if name.endswith(".asset"):
                path = os.path.join(root, name)
                assets.append((path, parse_asset(path)))

    names = {path: doc["m_Name"] for path, doc in assets}
    outputs = {file: {} for file in OUTPUT_FILE.values()}
    for path, doc in assets:
        script = guids.get(doc["m_Script"]["guid"])
        cls = script_class(script)
        if cls not in OUTPUT_FILE:
            raise ValueError(f"{path}: no output file for class {cls}")
        fields = {k: resolve(v, guids, names) for k, v in doc.items() if not k.startswith("m_")}
        table = outputs[OUTPUT_FILE[cls]]
        if doc["m_Name"] in table:
            raise ValueError(f"duplicate {cls} name {doc['m_Name']}")
        table[doc["m_Name"]] = fields

    return {
        file: json.dumps(dict(sorted(table.items())), indent=2, ensure_ascii=True) + "\n"
        for file, table in outputs.items()
    }


def main():
    check = "--check" in sys.argv[1:]
    outputs = convert()
    stale = []
    for file, text in outputs.items():
        path = os.path.join(OUT, file)
        current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
        if current != text:
            stale.append(file)
            if not check:
                os.makedirs(OUT, exist_ok=True)
                with open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
    count = sum(len(json.loads(t)) for t in outputs.values())
    if check:
        print(f"{count} definitions; stale: {', '.join(stale) or 'none'}")
        sys.exit(1 if stale else 0)
    print(f"{count} definitions; wrote: {', '.join(stale) or 'nothing (up to date)'}")


if __name__ == "__main__":
    main()
