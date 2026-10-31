#!/usr/bin/env python3
"""Asset audit: every model / icon / portrait / sound id the game asks for, checked against what the asset export ships.

Reads the game data (data/), the code's lookup conventions (mirrored here from game/scripts; see each section), the
generated model manifest (game/assets/generated/manifest.json), the audio manifest and game/audio/sound_events.json.
Each id is one of:
  missing      nothing ships and the game draws a primitive / "?" box / plays nothing
  fallback     the game shows a stand-in asset (a lower level scaled up, a kit piece, a generic line icon, neutral mood)
  tint         the game shows another model tinted (drone variants, cybernetics tiers, defense tiers)
  placeholder  a synthesised stand-in sound (PlaceholderSounds.cs)
  silent       an event the game fires with no sound mapped
Writes docs/MISSING_ASSETS.md (--write) and prints a one-line summary. Never fails the build: exit code is always 0.

Usage: python3 -I tools/asset-audit/asset_audit.py [--write] [--quiet]
"""
import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "data")
GEN = os.path.join(ROOT, "game", "assets", "generated")
ICONS = os.path.join(ROOT, "game", "ui", "icons")
SCRIPTS = os.path.join(ROOT, "game", "scripts")
CORE = os.path.join(ROOT, "src", "Remnants.Core")
OUT = os.path.join(ROOT, "docs", "MISSING_ASSETS.md")

HIGH, MED, LOW = "high", "medium", "low"
PRIORITY_ORDER = {HIGH: 0, MED: 1, LOW: 2}


# ---------------------------------------------------------------- loading

def jsonc(path):
    """The game data allows // comments and trailing commas (System.Text.Json with comments)."""
    text = open(path, encoding="utf-8").read()
    out, i, n, in_str = [], 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1]); i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True; out.append(c)
        elif text.startswith("//", i):
            while i < n and text[i] != "\n": i += 1
            continue
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


def data(name):
    path = os.path.join(DATA, name)
    return jsonc(path) if os.path.exists(path) else None


def snake(pascal):
    return re.sub(r"(?<!^)([A-Z])", r"_\1", pascal).lower()


def enum_members(name):
    """Members of a Core enum, parsed from source (comments and initialisers stripped)."""
    for dirpath, _, files in os.walk(CORE):
        for f in files:
            if not f.endswith(".cs"): continue
            src = open(os.path.join(dirpath, f), encoding="utf-8").read()
            m = re.search(r"\benum\s+" + name + r"\b[^{]*\{(.*?)\}", src, re.S)
            if m:
                body = re.sub(r"//[^\n]*|/\*.*?\*/|\[[^\]]*\]", "", m.group(1), flags=re.S)
                return [p.split("=")[0].strip() for p in body.split(",") if p.split("=")[0].strip()]
    return []


manifest = json.load(open(os.path.join(GEN, "manifest.json")))
MODELS = {a["id"]: a for a in manifest.get("assets", [])}
audio_manifest = json.load(open(os.path.join(GEN, "audio", "manifest.json")))
SOUNDS = {s["id"] for s in audio_manifest.get("sounds", [])}
EVENTS_FILE = json.load(open(os.path.join(ROOT, "game", "audio", "sound_events.json")))


def svg(icon):
    return os.path.exists(os.path.join(ICONS, icon + ".svg"))


def gen_file(rel):
    return os.path.exists(os.path.join(GEN, rel))


def placeholder_sounds():
    src = open(os.path.join(SCRIPTS, "Audio", "PlaceholderSounds.cs"), encoding="utf-8").read()
    return set(re.findall(r'\["([a-z0-9_]+)"\]\s*=\s*new\(', src))


PLACEHOLDER_SOUNDS = placeholder_sounds()


# ---------------------------------------------------------------- findings

findings = []  # dicts: category, id, status, used, look, priority, note
seen = set()


def add(category, asset_id, status, used, look, priority, note=""):
    key = (category, asset_id, status)
    if key in seen: return
    seen.add(key)
    findings.append(dict(category=category, id=asset_id, status=status, used=used, look=look.strip(), priority=priority, note=note))


def first(*ids):
    return next((i for i in ids if i in MODELS), None)


def short(text, limit=110):
    text = re.sub(r"\s+", " ", text or "").strip()
    return text if len(text) <= limit else text[: limit - 1].rstrip() + "…"


# ---------------------------------------------------------------- models

def audit_buildings():
    # ModelLibrary.Building: {id}_l{level}, else the closest lower level scaled up, else Placeholders.Building.
    # Icons.Building: bld_{id}, else bld_cat_{category}, else ui_build.
    for b in data("buildings.json"):
        bid, name, cat = b["id"], b["name"], b.get("category", "")
        levels = len(b.get("levels", [])) or 1
        if bid == "campfire":
            if "campfire_cooking" not in MODELS:
                add("Colony buildings", "campfire_cooking", "missing", "ModelLibrary.Building (campfire)", f"{name}: the colony campfire", HIGH)
        else:
            for lvl in range(1, levels + 1):
                mid = f"{bid}_l{lvl}"
                if mid in MODELS: continue
                lower = next((f"{bid}_l{l}" for l in range(lvl - 1, 0, -1) if f"{bid}_l{l}" in MODELS), None)
                pri = HIGH if lvl == 1 else MED
                if lower:
                    add("Colony buildings", mid, "fallback", f"ModelLibrary.Building, level {lvl}", f"{name} ({cat}) upgraded to level {lvl}", pri, f"shows {lower} scaled ×{1 + 0.12 * (lvl - int(lower[-1])):.2f}")
                else:
                    add("Colony buildings", mid, "missing", f"ModelLibrary.Building, level {lvl}", f"{name} ({cat}), level {lvl}", pri, "draws a primitive placeholder block")
        icon = f"bld_{bid}"
        if not svg(icon):
            fb = next((i for i in (f"bld_cat_{cat.lower()}", "ui_build") if svg(i)), "?")
            add("UI icons", icon, "fallback", "Icons.Building (build menu, building panel)", f"line icon for {name}", HIGH, f"shows {fb}")


def audit_crops():
    src = open(os.path.join(SCRIPTS, "Colony", "TownView.cs"), encoding="utf-8").read()
    block = re.search(r"CropRows = new\(\)\s*\{(.*?)\};", src, re.S)
    families = re.findall(r'\]\s*=\s*"([a-z_]+)"', block.group(1)) if block else []
    for fam in families:
        for stage in (1, 2, 3):
            mid = f"crop_{fam}_s{stage}"
            if mid not in MODELS:
                add("Colony buildings", mid, "missing", "TownView crop rows on a Farm", f"{fam} crop rows, growth stage {stage}/3", MED, "farm field shows bare plot")


def audit_kit():
    # Fixed ids the town dressing, scavenge maps, fog and loot piles instance (TownDressing, ScavengeModels, FogOverlay, MapView).
    kit = {
        "ground_tile_dirt": ("TownDressing ground", "dirt ground tile", HIGH),
        "rubble": ("TownDressing, scavenge Rubble terrain, wrecked building", "rubble heap", HIGH),
        "dead_tree": ("TownDressing", "dead tree", MED),
        "building_shell": ("TownDressing", "ruined building shell", MED),
        "cover_low_barrier": ("TownDressing, low cover", "low barrier", HIGH),
        "cover_low_sandbags": ("TownDressing, low cover", "sandbag line", HIGH),
        "cover_high_wall": ("TownDressing, high cover", "high wall section", HIGH),
        "wall_segment": ("ScavengeModels.Wall", "wall segment, 2 m", HIGH),
        "wrecked_car": ("ScavengeModels.Car (Vehicle terrain)", "burnt-out car", HIGH),
        "loot_crate": ("ScavengeModels.Crate (loot piles, caches, remains, stashes)", "loot crate", HIGH),
        "exit_marker": ("ScavengeModels.ExitMarker", "extraction exit marker", HIGH),
        "network_pylon": ("ScavengeModels (Pylon terrain)", "Network pylon", MED),
        "radio_mast_l1": ("ScavengeModels.Story (mast unit)", "Radio Mast", MED),
        "item_flare_lit": ("FogOverlay flare", "a burning flare on the ground", MED),
        "item_motion_sensor": ("FogOverlay motion sensor", "a placed motion sensor", MED),
        "scout_drone": ("ScavengeModels drone unit", "scout drone", HIGH),
    }
    for mid, (used, look, pri) in kit.items():
        if mid not in MODELS:
            add("Scavenge maps & props", mid, "missing", used, look, pri, "draws a primitive box")
    for tags, used in ((("cover_low",), "low cover variants"), (("cover_high",), "high cover variants"), (("tile", "ground"), "ground tile variants")):
        if not any(all(t in a.get("tags", []) for t in tags) for a in MODELS.values()):
            add("Scavenge maps & props", "tag:" + "+".join(tags), "missing", f"ScavengeModels.Tagged ({used})", used, HIGH)
    # Survivor bodies (ModelLibrary.SurvivorId).
    for mid in ("survivor_adult_male", "survivor_adult_female", "survivor_child"):
        if mid not in MODELS:
            add("Survivors & characters", mid, "missing", "ModelLibrary.Survivor", mid.replace("_", " "), HIGH, "draws a capsule")


def audit_items():
    items = data("items.json")
    food_ids = {f["id"] for f in (data("food.json") or {}).get("items", [])} if isinstance(data("food.json"), dict) else set()
    gear_kinds = {"Weapon", "Armor", "Clothing", "Headgear"}
    by_kind = defaultdict(list)
    for it in items:
        iid, kind, name = it["id"], it["kind"], it["name"]
        common = it.get("lootWeight", 0) >= 3 or it.get("tier", 9) <= 1
        # Held / worn models (SurvivorDressing.AttachGear, GearBenchPanel): item_{id}.
        enemy_only = "tier" not in it
        if kind in gear_kinds and f"item_{iid}" not in MODELS:
            add("Gear models", f"item_{iid}", "missing", "SurvivorDressing.AttachGear, gear bench preview",
                f"{name} ({kind.lower()}" + (", enemy gear" if enemy_only else f", tier {it['tier']}") + ")",
                LOW if enemy_only else HIGH if common else MED,
                "not drawn on survivors; bench shows a grey box")
        # Inventory icons: food has its own (icon_food_{id}_64.png); everything else wants a rendered icon
        # (assets/generated/icons/{id}.png) and shows the line icon of its kind until then.
        if kind == "Food" or iid in food_ids:
            if not gen_file(f"ui/food/icon_food_{iid}_64.png"):
                add("Item icons", f"icon_food_{iid}", "fallback", "FoodText.Icon (kitchen, larder, harvest)", f"{name}, coloured food icon", MED, "shows item_food line icon")
            continue
        line = f"item_{kind.lower()}"
        if not gen_file(f"icons/{iid}.png"):
            add("Item icons", f"icons/{iid}.png", "fallback", "Icons.Item (inventory, loot, trade, bench, expedition)",
                f"{name}, rendered {kind.lower()} icon" + (" (enemy gear)" if enemy_only else ""), LOW if enemy_only or not common else MED,
                f"shows the {line} line icon")
        if not svg(line):
            fb = {"Headgear": "body_head", "Mod": "skill_crafting"}.get(kind, "item_generic")
            add("UI icons", line, "fallback", "Icons.Item kind line icon", f"line icon for {kind} items", HIGH, f"shows {fb}")


def audit_gear_mods():
    src = open(os.path.join(SCRIPTS, "Colony", "GearModels.cs"), encoding="utf-8").read()
    mapped = dict(re.findall(r'\["([a-z0-9_]+)"\]\s*=\s*"([a-z0-9_]+)"', src))
    gm = data("gear_mods.json")
    mods = gm.get("mods", []) if isinstance(gm, dict) else gm
    for m in mods:
        mid = m["id"]
        if (m.get("params") or {}).get("apply") == "recolor": continue
        candidates = [c for c in (mapped.get(mid), m.get("model")) if c]
        if not any(c in MODELS for c in candidates):
            effect = ", ".join(f"{k} {v}" for k, v in (m.get("effect") or {}).items())
            add("Gear mods", candidates[0] if candidates else mid, "missing", f"GearModels.ModModel ({mid})",
                f"{m.get('name', mid)}: {m.get('slot', '?')} mod for {'/'.join(m.get('types', []))} ({short(effect, 60)})", LOW,
                "mod not shown on the gear")


def audit_vehicles():
    v = data("vehicles.json")
    for t in v.get("tiers", []):
        mid = f"vehicle_{t['model']}"
        if mid not in MODELS:
            add("Vehicles", mid, "missing", "VehiclePreview (garage), parked vehicle", f"{t['name']} (tier {t['tier']})", HIGH, "garage shows a rust box")
        for lvl in (2, 3):
            if f"{mid}_l{lvl}" not in MODELS and mid in MODELS:
                add("Vehicles", f"{mid}_l{lvl}", "missing", "vehicle condition/level variants", f"{t['name']} level {lvl}", LOW)
        parked = f"vehicle_parked_extraction_t{t['tier']}"
        if t["tier"] >= 2 and parked not in MODELS:
            add("Vehicles", parked, "fallback", "ScavengeModels.ParkedVehicle", f"{t['name']} parked at the extraction point", MED, f"shows {mid} scaled down")
    for u in v.get("upgrades", []):
        model = u.get("model")
        if not model:
            add("Vehicles", f"upgrade:{u['id']}", "missing", "VehicleDressing (data has no model)", f"{u['name']}: {short(u.get('description', ''), 70)}", LOW, "upgrade invisible on the vehicle")
            continue
        if u.get("slot") == "Armor":
            for tier in range(u.get("minTier", 0), 6):
                if f"{model}_t{tier}" not in MODELS:
                    add("Vehicles", f"{model}_t{tier}", "missing", f"VehicleDressing armor ({u['id']})", f"{u['name']} sized for tier {tier}", LOW)
        elif model not in MODELS:
            add("Vehicles", model, "missing", f"VehicleDressing ({u['id']}, {u.get('slot')})", f"{u['name']}: {short(u.get('description', ''), 70)}", LOW, "upgrade invisible on the vehicle")


def audit_cybernetics():
    cy = data("cybernetics.json")
    tiers = {t["tier"]: t for t in cy.get("tiers", [])}
    for node in cy.get("nodes", []):
        model, slot = node.get("model"), node.get("slot")
        if not model: continue
        if slot == "Eye":
            if f"prosthetic_{model}" not in MODELS:
                add("Cybernetics", f"prosthetic_{model}", "missing", "SurvivorDressing.FitEye", f"{node['id']}: {short(node.get('description', ''))}", MED, "implant not drawn")
            continue
        kind = "arm" if slot == "Arm" else "leg"
        for side in ("l", "r"):
            exact = f"prosthetic_{kind}_{model}_{side}"
            if exact in MODELS: continue
            stand_in = first(model, f"prosthetic_{kind}_cyborg_{side}")
            tint = tiers.get(node.get("tier"), {}).get("tint")
            add("Cybernetics", exact, "tint" if stand_in and tint else "fallback" if stand_in else "missing",
                "SurvivorDressing.FitLimbs", f"{node['id']} (tier {node.get('tier')}): {short(node.get('description', ''))}", MED,
                f"shows {stand_in}" + (f" tinted {tint}" if tint else "") if stand_in else "limb hidden, nothing drawn")


def audit_devices_and_enemies():
    # DeviceViews: device_{kind}[_disabled|_hacked], doors device_door[_hacked][_open]; relay = relay_node.
    for kind in enum_members("DeviceKind"):
        name = "relay_node" if kind == "Relay" else kind.lower()
        variants = [""]
        if kind in ("Camera", "Turret"): variants += ["_disabled", "_hacked"]
        elif kind == "Door": variants += ["_hacked", "_open", "_hacked_open"]
        else: variants += ["_hacked"]
        for var in variants:
            mid = f"device_{name}{var}"
            if mid not in MODELS:
                add("Devices & electronics", mid, "missing", "DeviceViews", f"{kind} device{var.replace('_', ' ')}", HIGH if var == "" else MED, "draws a cyan block")
    for e in data("enemies.json"):
        eid, ai = e["id"], e.get("ai")
        if eid in ("cole", "tally_scav", "tally_blade"):
            continue  # story models below (ScavengeModels.Story: the Tally band shares story_tally_member_1..5)
        if ai == "Device" or eid == "camera":
            if f"device_{eid}" not in MODELS:
                add("Enemies", f"device_{eid}", "missing", "ScavengeModels.Unit (device enemy)", e["name"], HIGH, "draws a capsule")
            continue
        if f"enemy_{eid}" not in MODELS:
            pri = HIGH if ai in ("Husk", "Linked") and not eid.startswith("tally") else MED
            add("Enemies", f"enemy_{eid}", "missing", "ScavengeModels.Unit (enemy)", f"{e['name']} ({ai} AI); equipment {', '.join(e.get('equipment', [])) or '-'}", pri, "draws a capsule")
    # Drones: their own model, else the scout drone tinted by role (ScavengeModels.RoleTint).
    for role in ("medic", "cargo", "combat"):
        mid = f"{role}_drone"
        if mid not in MODELS:
            add("Devices & electronics", mid, "tint" if "scout_drone" in MODELS else "missing", "ScavengeModels.Unit (drone)", f"{role} drone, rigged like scout_drone", MED, "shows scout_drone tinted")


def audit_defenses():
    raids = data("raids.json") or {}
    defs = raids.get("defenses", [])
    if isinstance(defs, dict): defs = defs.get("defenses", [])
    fallbacks = {"Wall": ["wall_segment", "cover_high_wall"], "Barricade": ["cover_low_barrier", "cover_low_sandbags"],
                 "Post": ["cover_low_sandbags"], "Turret": ["device_turret"]}
    for d in defs:
        did, kind = d["id"], d.get("kind", "")
        own = [c for c in (d.get("model"), f"defense_{did}") if c]
        if not any(c in MODELS for c in own):
            kit = first(*fallbacks.get(kind, []))
            add("Defenses & traps", own[0], "tint" if kit and kind == "Wall" else "fallback" if kit else "missing",
                "DefenseModels.Build (colony defenses, raids)", f"{d.get('name', did)} ({kind}): {short(d.get('description', ''), 80)}", MED,
                f"shows {kit}" + (" tinted by tier" if kind == "Wall" else "") if kit else "draws a primitive stand-in")
        if not svg(f"def_{did}"):
            add("UI icons", f"def_{did}", "fallback", "RaidText.Icon (defense catalog, panel)", f"line icon for {d.get('name', did)}", LOW, f"shows def_cat_ icon")


def audit_salvage():
    for n in (data("salvage.json") or {}).get("nodes", []):
        if n.get("model") and n["model"] not in MODELS:
            add("Scavenge maps & props", n["model"], "missing", f"SalvageViews ({n['id']} salvage node)", f"{n['name']} salvage node", MED, "draws a brown block")


# ---------------------------------------------------------------- story

def walk(node, fn):
    if isinstance(node, dict):
        fn(node)
        for v in node.values(): walk(v, fn)
    elif isinstance(node, list):
        for v in node: walk(v, fn)


def audit_story():
    src = open(os.path.join(SCRIPTS, "Story", "StoryArt.cs"), encoding="utf-8").read()
    asset = dict(re.findall(r'\["([a-z_]+)"\]\s*=\s*"(story_[a-z_]+)"', src))
    mood_alias = dict(re.findall(r'\["([a-z_]+)"\]\s*=\s*"([a-z_]+)"', src.split("MoodAlias", 1)[1].split("};", 1)[0])) if "MoodAlias" in src else {}
    moods = defaultdict(lambda: defaultdict(int))
    dialogue_dir = os.path.join(DATA, "story", "dialogue")

    def line(n):
        if "speaker" in n and n["speaker"] in asset:
            moods[n["speaker"]][n.get("mood", "neutral")] += 1
    for f in sorted(os.listdir(dialogue_dir)):
        walk(jsonc(os.path.join(dialogue_dir, f)), line)
    for speaker, counts in sorted(moods.items()):
        base = asset[speaker]
        for mood in ("neutral", "radio"):
            if not gen_file(f"portraits/{base}_{mood}.png"):
                add("Story portraits", f"{base}_{mood}", "missing", "StoryArt.Portrait", f"{speaker} portrait, {mood}", HIGH, "draws an initial on a tint")
        lacking = {m: c for m, c in counts.items() if m != "neutral" and not gen_file(f"portraits/{base}_{m}.png")}
        for mood, count in sorted(lacking.items(), key=lambda kv: -kv[1]):
            alias = mood_alias.get(mood)
            shown = alias if alias and gen_file(f"portraits/{base}_{alias}.png") else "neutral"
            add("Story portraits", f"{base}_{mood}", "fallback", f"StoryArt.Portrait ({count} dialogue lines)", f"{speaker} looking {mood}",
                MED if count >= 10 else LOW, f"shows the {shown} portrait")
    for marker in ("marker_mission_main", "marker_mission_side", "marker_radio_contact", "marker_caravan"):
        if not gen_file(f"ui/{marker}.png"):
            add("Story portraits", marker, "missing", "StoryArt.Marker (world map)", marker.replace("_", " "), HIGH)
    for frame in ("frame_radio", "frame_dialogue"):
        if not gen_file(f"ui/{frame}.png"):
            add("Story portraits", frame, "fallback", "StoryArt.Frame", f"dialogue 9-slice {frame}", MED, "HUD panel frame")
    # Story units on maps (ScavengeModels.Story): story_{character}, Juniper bandaged, the Tally band, the Long Haul trucks.
    chars = {}
    for f in sorted(os.listdir(os.path.join(DATA, "story", "maps"))):
        walk(jsonc(os.path.join(DATA, "story", "maps", f)), lambda n: chars.setdefault(n["character"], f) if isinstance(n.get("character"), str) else None)
    # StoryArt.BodyModel: characters.json id -> its story_* asset (the same table as the portraits).
    ids = {asset.get(c, f"story_{c}"): f"npc on {m}" for c, m in chars.items()}
    ids.update({"story_juniper_bandaged": "Juniper once treated", "story_cole_brannigan": "Cole (enemy)",
                **{f"story_tally_member_{i}": "Tally scavs and cutters" for i in range(1, 6)},
                "story_seven_colony": "Seven once it joins (colony, party)", "story_pell_adult": "Pell grown up (colony, party)",
                "long_haul_lead_truck": "M6 convoy", "long_haul_cargo_truck": "M6 convoy", "long_haul_gun_truck": "M6 convoy"})
    for mid, used in sorted(ids.items()):
        if mid not in MODELS:
            note = "story unit drawn as a plain survivor"
            add("Survivors & characters", mid, "missing", f"ScavengeModels.Story ({used})", mid.replace("story_", "").replace("_", " "),
                MED, note)


# ---------------------------------------------------------------- UI icons

ICON_PREFIXES = ("ui", "stat", "item", "body", "skill", "job", "res", "attr", "cond", "weather", "buff", "bld", "cat", "def", "stance", "ping", "sched", "drone")


def audit_ui_icons():
    literal_ids = defaultdict(set)
    sound_like = SOUNDS | PLACEHOLDER_SOUNDS | set(MODELS)
    for dirpath, _, files in os.walk(SCRIPTS):
        for f in files:
            if not f.endswith(".cs"): continue
            rel = os.path.relpath(os.path.join(dirpath, f), ROOT)
            for lit in re.findall(r'"([a-z]+_[a-z0-9_]+)"', open(os.path.join(dirpath, f), encoding="utf-8").read()):
                if lit.split("_")[0] in ICON_PREFIXES and lit not in sound_like and not lit.startswith(("item_generic_", "ui_scale")):
                    literal_ids[lit].add(rel)
    # Ids built from data: resources, skills, weather kinds, buffs, attributes, body parts.
    built = {}
    for r in data("resources.json") or []:
        built[f"res_{r['id']}"] = ("Icons.Resource", f"{r['name']} resource", HIGH, "item_material")
    for s in (data("perks.json") or {}).get("skills", []):
        built[f"skill_{s['skill'].lower()}"] = ("Icons.Skill", f"{s['name']} skill", MED, "skill_crafting")
    for k, w in ((data("weather.json") or {}).get("kinds") or {}).items():
        built[f"weather_{k}"] = ("TurnBar weather", f"{w.get('name', k)} weather", MED, "weather_clear")
    for attr in ("strength", "agility", "endurance", "intellect", "creativity", "perception"):
        built[f"attr_{attr}"] = ("Icons.Attribute", f"{attr} attribute", MED, "?")
    for part in enum_members("BodyPart"):
        built[f"body_{snake(part)}"] = ("Icons.BodyPart", f"{part} body part", MED, "body_full")
    for icon, (used, look, pri, fb) in built.items():
        if not svg(icon):
            add("UI icons", icon, "fallback" if svg(fb) else "missing", used, look, pri, f"shows {fb}" if svg(fb) else "shows item_generic / box")
    for icon, files in sorted(literal_ids.items()):
        if svg(icon) or icon in built: continue
        add("UI icons", icon, "missing", ", ".join(sorted(files))[:120], "line icon (24×24 white SVG, 2 px stroke)", HIGH if icon.startswith(("ui_", "stat_")) else MED,
            "falls back to the next id or item_generic")


# ---------------------------------------------------------------- sounds

EVENTS = {k: v for k, v in EVENTS_FILE.get("events", {}).items() if not k.startswith("//")}


def sound_ids(value):
    if isinstance(value, str): return [value]
    if isinstance(value, list): return [v for v in value if isinstance(v, str)]
    if isinstance(value, dict): return sound_ids(value.get("sounds", []))
    return []


def resolve(event):
    name = event
    while True:
        if name in EVENTS: return name, EVENTS[name]
        if "." not in name: return None, None
        name = name.rsplit(".", 1)[0]


def check_sound(sid, used, look, pri):
    if sid in SOUNDS: return
    if sid in PLACEHOLDER_SOUNDS:
        add("Sounds", sid, "placeholder", used, look, pri, "synthesised blip (PlaceholderSounds)")
    else:
        add("Sounds", sid, "missing", used, look, pri, "plays nothing")


def fired_events():
    """(event, used, priority) the code fires: literals plus the names ScavengeAudio / ColonyAudio build from enums and data."""
    out = {}
    for dirpath, _, files in os.walk(SCRIPTS):
        for f in files:
            if not f.endswith(".cs"): continue
            src = open(os.path.join(dirpath, f), encoding="utf-8").read()
            for ev in re.findall(r'Fire\("([a-z_.]+)"', src):
                out[ev] = (f, HIGH if ev.startswith("ui.") else MED)
            for sting in re.findall(r'MusicDirector\.Sting\("([a-z_]+)"', src):
                out[f"music.{sting}"] = (f"{f} (MusicDirector.Sting)", MED)
    for t in enum_members("ColonyEventType"):
        out.setdefault(f"colony.{snake(t)}", ("ColonyAudio.EventName", MED))
    per_unit = {"Moved", "Died", "BledOut", "Downed", "Injured", "LimbLost"}
    barks = {"Spotted", "Flanked", "Retreated"}
    enemies = [e["id"] for e in data("enemies.json")]
    whos = enemies + ["survivor", "npc", "drone", "device", "decoy"]
    for t in enum_members("ScavengeEventType"):
        s = snake(t)
        if t in ("Attacked", "Missed", "Overwatch"): continue
        if t in per_unit:
            name = "died" if t == "BledOut" else s
            for who in whos:
                out.setdefault(f"scavenge.{name}.{who}", ("ScavengeAudio.EventNames", HIGH if t in ("Moved", "Died") else MED))
        elif t in barks:
            out.setdefault(f"scavenge.{s}", ("ScavengeAudio.EventNames", MED))
            for e in enemies: out.setdefault(f"scavenge.bark.{e}", ("ScavengeAudio.EventNames (enemy bark)", MED))
        else:
            out.setdefault(f"scavenge.{s}", ("ScavengeAudio.EventNames", MED))
    for ev in ("scavenge.miss", "scavenge.overwatch", "scavenge.overwatch_trigger", "scavenge.attack.unseen", "scavenge.attack.unknown"):
        out.setdefault(ev, ("ScavengeAudio.EventNames", HIGH))
    actions = set()
    for it in data("items.json"):
        for a in it.get("actions", []): actions.add(a["id"])
    for a in sorted(actions): out.setdefault(f"scavenge.attack.{a}", ("ScavengeAudio.EventNames (attack)", HIGH))
    for who in whos: out.setdefault(f"scavenge.hit.{who}", ("ScavengeAudio.EventNames (hit)", HIGH))
    for t in enum_members("Terrain"): out.setdefault(f"scavenge.step.{snake(t)}", ("ScavengeAudio footsteps", HIGH))
    for k in enum_members("DeviceKind"): out.setdefault(f"scavenge.device.{snake(k)}", ("ScavengeAudio (hacked device)", MED))
    for k in enum_members("ContactKind"): out.setdefault(f"scavenge.contact.{snake(k)}", ("ScavengeAudio.CheckContacts", MED))
    return out


ENEMY_IDS = {e["id"] for e in data("enemies.json")} - {"turret", "camera"}
# ScavengeAudio.NoFootsteps: units that move with their own sound (servos, rotors) instead of footsteps.
NO_FOOTSTEPS = set(re.findall(r'"([a-z_]+)"', re.search(r"NoFootsteps\s*=\s*\[(.*?)\]", open(os.path.join(SCRIPTS, "Audio", "ScavengeAudio.cs"), encoding="utf-8").read(), re.S).group(1)))


def audit_sounds():
    # Every id the map names.
    for ev, value in EVENTS.items():
        for sid in sound_ids(value):
            check_sound(sid, f"sound_events.json events[{ev}]", ev, HIGH if ev.startswith(("ui.", "scavenge.step", "scavenge.attack", "scavenge.hit")) else MED)
    for section, pri in (("music", HIGH), ("layers", MED), ("zoneAmbience", MED)):
        for key, value in (EVENTS_FILE.get(section) or {}).items():
            if key.startswith("//"): continue
            for sid in sound_ids(value): check_sound(sid, f"sound_events.json {section}[{key}]", f"{section} {key}", pri)
    for key, value in (EVENTS_FILE.get("ambience") or {}).items():
        if key.startswith("//"): continue
        if key == "work" and isinstance(value, dict):
            for b, loop in value.items():
                if not b.startswith("//"):
                    for sid in sound_ids(loop): check_sound(sid, f"sound_events.json ambience.work[{b}]", f"{b} work loop", MED)
        else:
            for sid in sound_ids(value): check_sound(sid, f"sound_events.json ambience[{key}]", f"{key} ambience", HIGH)
    for key, value in (EVENTS_FILE.get("weather") or {}).items():
        for sid in sound_ids(value): check_sound(sid, f"sound_events.json weather[{key}]", f"{key} weather loop", MED)
    for f in ("ScavengeAudio.cs", "MusicDirector.cs", "ColonyAudio.cs"):
        path = os.path.join(SCRIPTS, "Audio", f)
        for sid in re.findall(r'\b(?:Play|PlayAt)\("([a-z0-9_]+)"', open(path, encoding="utf-8").read()):
            check_sound(sid, f"{f} literal", sid, MED)
    # Buildings with no work loop of their own fall to ambience.work.default.
    work = (EVENTS_FILE.get("ambience") or {}).get("work") or {}
    if work.get("default") == []:
        for b in data("buildings.json"):
            if b.get("job") and b["id"] not in work:
                add("Sounds", f"work_{b['id']}", "silent", "ambience.work default []", f"{b['name']} working loop ({b['job']})", LOW, "no work loop near the building")
    # Every event the code fires.
    for ev, (used, pri) in sorted(fired_events().items()):
        hit, value = resolve(ev)
        if hit is None:
            add("Sound events", ev, "silent", used, "unmapped event", pri, "no entry in sound_events.json (or parent)")
        elif hit != ev and ev.split(".")[-1] in ENEMY_IDS and (ev.split(".")[1] in ("died", "hit", "bark")
                                                               or ev.split(".")[1] == "moved" and ev.split(".")[-1] in NO_FOOTSTEPS):
            add("Sound events", ev, "fallback", used, f"{ev.split('.')[1]} sound of a {ev.split('.')[-1]}",
                MED if ev.split(".")[1] in ("died", "hit") else LOW, f"plays generic {hit}" if sound_ids(value) else f"silent via {hit} []")
        elif sound_ids(value) == [] and hit == ev and pri == HIGH:
            add("Sound events", ev, "silent", used, "mapped to [] (deliberately silent)", LOW, "review: frequent event with no sound")


# ---------------------------------------------------------------- report

SCREENSHOTS = [
    ("defenses_placeholders.png", "`--colony --raid-demo=placement` (taken before the gate, nest, spike strip and first two towers were wired to "
     "the shipped `perimeter_*` / `watchtower_l1/_l2` models): the other traps, the floodlight, bell and Fortified Tower are primitive "
     "stand-ins (flat trap discs, lamp-post floodlights, bell post); walls are `wall_segment` tinted by tier."),
    ("story_m6_long_haul.png", "`--scavenge --story-mission=m6` (taken before the fix): Marrow walked as a stock survivor because the code looked up "
     "`story_marrow`; story units now go through StoryArt's id table (`story_abel_marrow`, `story_iris_vahn`, `story_doc_hale`). "
     "`long_haul_marrow_bike` and `long_haul_tanker` ship but nothing uses them."),
    ("gear_bench_line_icons.png", "`--colony --gear-demo`: every gear row shows its kind's line icon; no rendered item icon "
     "(`assets/generated/icons/{id}.png`) ships yet."),
]

CATEGORY_ORDER = ["Colony buildings", "Survivors & characters", "Enemies", "Scavenge maps & props", "Devices & electronics",
                  "Defenses & traps", "Gear models", "Gear mods", "Cybernetics", "Vehicles", "Story portraits", "Item icons",
                  "UI icons", "Sounds", "Sound events"]


def report():
    by_cat = defaultdict(list)
    for f in findings: by_cat[f["category"]].append(f)
    lines = ["# Missing assets", "",
             "Generated by `tools/asset-audit/asset_audit.py --write` (also run as a non-failing report by `run-tests.sh`). "
             "Do not edit by hand: fix the asset (or the data) and re-run.", "",
             "Status: **missing** = nothing ships, the game draws a primitive / box / plays nothing; **fallback** = a stand-in asset "
             "(lower level scaled, kit piece, generic line icon, neutral mood); **tint** = another model tinted; **placeholder** = "
             "synthesised stand-in sound; **silent** = event fired with no sound.", "",
             "Priority: **high** = seen or heard every session (colony buildings, common items, enemies, UI icons, frequent sounds).", "",
             "| Category | missing | fallback | tint | placeholder | silent | high |", "|---|---:|---:|---:|---:|---:|---:|"]
    for cat in CATEGORY_ORDER:
        rows = by_cat.get(cat, [])
        c = lambda s: sum(1 for r in rows if r["status"] == s)
        lines.append(f"| {cat} | {c('missing')} | {c('fallback')} | {c('tint')} | {c('placeholder')} | {c('silent')} | {sum(1 for r in rows if r['priority'] == HIGH)} |")
    lines.append(f"| **Total** | {sum(1 for r in findings if r['status']=='missing')} | {sum(1 for r in findings if r['status']=='fallback')} | "
                 f"{sum(1 for r in findings if r['status']=='tint')} | {sum(1 for r in findings if r['status']=='placeholder')} | "
                 f"{sum(1 for r in findings if r['status']=='silent')} | {sum(1 for r in findings if r['priority']==HIGH)} |")
    lines += ["", f"Checked against {len(MODELS)} models, {len(SOUNDS)} sounds, {len(os.listdir(ICONS))} line icons.", ""]
    shots = [(f, c) for f, c in SCREENSHOTS if os.path.exists(os.path.join(ROOT, "docs", "missing_assets", f))]
    if shots:
        lines += ["## Screenshots", "", "Captured with `godot-mono --path game -- <flags> --screenshot=...` after a `--headless --import` "
                  "(a fresh worktree without `.godot/imported` shows every model as a primitive).", ""]
        for f, c in shots: lines += [f"![{f}](missing_assets/{f})", "", c, ""]
    for cat in CATEGORY_ORDER:
        rows = sorted(by_cat.get(cat, []), key=lambda r: (PRIORITY_ORDER[r["priority"]], r["status"], r["id"]))
        lines += [f"## {cat}", ""]
        if not rows:
            lines += ["Nothing missing.", ""]
            continue
        lines += ["| Priority | Id | Status | Used in | Should be | Meanwhile |", "|---|---|---|---|---|---|"]
        for r in rows:
            cell = lambda s: s.replace("|", "\\|")
            lines.append(f"| {r['priority']} | `{r['id']}` | {r['status']} | {cell(r['used'])} | {cell(r['look'])} | {cell(r['note'])} |")
        lines.append("")
    return "\n".join(lines)


def main():
    for audit in (audit_buildings, audit_crops, audit_kit, audit_items, audit_gear_mods, audit_vehicles, audit_cybernetics,
                  audit_devices_and_enemies, audit_defenses, audit_salvage, audit_story, audit_ui_icons, audit_sounds):
        audit()
    text = report()
    if "--write" in sys.argv:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        open(OUT, "w", encoding="utf-8").write(text + "\n")
    counts = defaultdict(int)
    for f in findings: counts[f["status"]] += 1
    high = sum(1 for f in findings if f["priority"] == HIGH and f["status"] in ("missing", "silent"))
    print(f"[asset-audit] {counts['missing']} missing, {counts['fallback']} fallback, {counts['tint']} tint, "
          f"{counts['placeholder']} placeholder, {counts['silent']} silent ({high} high-priority missing/silent)"
          + ("" if "--write" not in sys.argv else f" -> {os.path.relpath(OUT, ROOT)}"))
    if "--quiet" not in sys.argv and "--write" not in sys.argv:
        print(text)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:  # a report, never a gate
        print(f"[asset-audit] WARNING: audit failed: {e!r}")
        sys.exit(0)
