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

# The placeables Sandbox.unity's BuildModeController offers, in its _availablePrefabs order
# (the build menu's row order). Converted to placeables.json: each prefab's root sprite and
# the authored fields of every gameplay component on it.
SANDBOX_SCENE = os.path.join(ASSETS, "_Project", "Scenes", "Sandbox.unity")
PLACEABLES_FILE = "placeables.json"

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


def parse_documents(path):
    """Every document of a multi-document Unity YAML file (a prefab): [(fileID, {Type: body})]."""
    with open(path, encoding="utf-8") as f:
        text = f.read()
    docs = []
    for chunk in re.split(r"^--- !u!\d+ &(\d+).*$", text, flags=re.M)[1:]:
        docs.append(chunk)
    out = []
    for file_id, body in zip(docs[0::2], docs[1::2]):
        lines = []
        for line in body.splitlines():
            if line.startswith(("%", "---")) or not line.strip():
                continue
            stripped = line.lstrip(" ")
            lines.append((len(line) - len(stripped), stripped))
        doc, end = parse_block(lines, 0, 0)
        if end != len(lines):
            raise ValueError(f"{path}: unparsed from line {end}: {lines[end]}")
        out.append((int(file_id), doc))
    return out


def sandbox_roster(guids):
    """The prefab paths Sandbox's BuildModeController offers, in order."""
    with open(SANDBOX_SCENE, encoding="utf-8") as f:
        text = f.read()
    block = re.search(r"_availablePrefabs:\n((?:  - .*\n)+)", text).group(1)
    return [guids[g] for g in re.findall(r"guid: ([0-9a-f]{32})", block)]


def convert_placeables(guids, names):
    placeables = []
    for path in sandbox_roster(guids):
        docs = dict(parse_documents(path))
        objects = {fid: d["GameObject"] for fid, d in docs.items() if "GameObject" in d}
        transforms = {fid: d["Transform"] for fid, d in docs.items() if "Transform" in d}
        # The root is the GameObject whose Transform has no parent.
        root_go = next(t["m_GameObject"]["fileID"] for t in transforms.values()
                       if t["m_Father"]["fileID"] == 0)
        entry = {"name": objects[root_go]["m_Name"], "sprite": None, "parts": {}}
        for fid, d in docs.items():
            if "SpriteRenderer" in d and d["SpriteRenderer"]["m_GameObject"]["fileID"] == root_go:
                sprite = d["SpriteRenderer"].get("m_Sprite")
                entry["sprite"] = resolve(sprite, guids, names) if sprite else None
            if "MonoBehaviour" in d and d["MonoBehaviour"]["m_GameObject"]["fileID"] == root_go:
                mb = d["MonoBehaviour"]
                cls = script_class(guids.get(mb["m_Script"]["guid"]))
                fields = {}
                for k, v in mb.items():
                    if k.startswith("m_"):
                        continue
                    # In-prefab and scene references ({fileID: N} with no guid) are wiring
                    # the Godot scene does itself; only asset references carry data.
                    if isinstance(v, dict) and set(v) == {"fileID"}:
                        continue
                    fields[k] = resolve(v, guids, names)
                entry["parts"][cls] = fields
        placeables.append(entry)
        if entry["name"] == "BeltPrefab":
            placeables.append(belt_splitter_entry(entry))
    return json.dumps(placeables, indent=2, ensure_ascii=True) + "\n"


# Placeables with no Unity prefab, added in the Godot build at the user's call. Kept here so a
# regeneration from the Unity assets cannot drop them.
SPLITTER_COST = [{"itemType": "Scrap", "quantity": 4}]


def belt_splitter_entry(belt):
    """The belt splitter (G10). Core's PlaceableBeltSplitter has existed since the belt pass, but
    Unity never authored a prefab, so it was never in the build menu. It IS a belt -- same lane,
    same cargo sprites -- plus the splitter part, with its own picture and no drag runs (a run
    of splitters would only be a slow belt)."""
    return {
        "name": "BeltSplitterPrefab",
        "sprite": None,
        "parts": {
            "PlaceableBuilding": {"cost": SPLITTER_COST, "dragPlaceable": 0},
            "PlaceableBelt": belt["parts"]["PlaceableBelt"],
            "PlaceableBeltSplitter": {"sprite": "belt_splitter.png"},
        },
    }


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

    result = {
        file: json.dumps(dict(sorted(table.items())), indent=2, ensure_ascii=True) + "\n"
        for file, table in outputs.items()
    }
    result[PLACEABLES_FILE] = convert_placeables(guids, names)
    return result


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
