#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
JSON Migrator: old -> new split format (Prefab.json + Material.json)

Features
--------
- Reads an input "old" JSON and produces two outputs:
  * Prefab.json (only values different from Prefab template)
  * Material.json (only values different from Material template)
- Uses template JSONs as defaults so outputs only contain effective overrides.
- Extensible: format-specific migrators (surfaces, decals, netlanes).

Assumptions (v1)
----------------
- For "surfaces":
    - Old JSON resembles the provided DefaultSurface.json (keys: "UiPriority",
      "Float", "Vector", "prefabIdentifierInfos").
    - New "Material.json" preserves "Float" and "Vector" values from old JSON.
    - New "Prefab.json" maps:
        * UI priority: old["UiPriority"] ->
          Components.Game.Prefabs.UIObject.m_Priority
      Other Prefab settings are left to template defaults unless you add more
      mapping rules below.
- For "decals" and "netlanes":
    - Placeholders/stubs are provided; add your mapping rules in the respective
      migrator functions or create new plugins.

Usage
-----
python json_migrator.py \
    --format surfaces \
    --input /path/to/old.json \
    --out-dir /path/to/out \
    --prefab-template /path/to/templates/Surfaces/Prefab.json \
    --material-template /path/to/templates/Surfaces/Material.json

You can also point to a directory of inputs using --input-dir.
"""

import argparse
import json
import os
import shutil
import sys
from copy import deepcopy
from typing import Any, Dict, Tuple, Optional, List

Json = Dict[str, Any]

# Build portable fallback path for _DefaultJson inside LocalLow
base_local_low = os.path.join(os.environ.get("LOCALAPPDATA", ""), "..", "LocalLow")
base_local_low = os.path.abspath(base_local_low)
DEFAULT_JSON_FALLBACK = os.path.join(
    base_local_low,
    "Colossal Order",
    "Cities Skylines II",
    "ModsData",
    "ExtraAssetsImporter",
    "_DefaultJson"
)

# ------------------------------ IO Helpers ------------------------------ #

def read_json(path: str) -> Json:
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path: str, data: Json) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write("\n")


# ---------------------------- Diff / Compare ---------------------------- #

def _is_number(x: Any) -> bool:
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def _approx_equal(a: Any, b: Any, tol: float = 0.0) -> bool:
    """Equality helper. If both are numbers, allows tolerance; else strict."""
    if _is_number(a) and _is_number(b):
        return abs(a - b) <= tol
    return a == b


def diff_overrides(candidate: Any, template: Any, tol: float = 0.0) -> Any:
    """
    Returns only the parts of 'candidate' that differ from 'template'.

    Rules:
    - dict: recurse; drop keys equal to template; if empty -> return {}
    - list/tuple: if exactly equal to template -> return [], else return candidate
      (coarse-grained for lists; per-element diff is avoided for simplicity)
    - primitives: return candidate if != template (with tol for numbers)
    """
    # If candidate is None or missing, no override
    if candidate is None and template is not None:
        return None if not _approx_equal(candidate, template, tol) else {}

    # Dict
    if isinstance(candidate, dict) and isinstance(template, dict):
        out: Dict[str, Any] = {}
        for k, v in candidate.items():

            if(k not in template):
                continue

            t = template.get(k)

            if isinstance(v, dict) and isinstance(t, dict):
                sub = diff_overrides(v, t, tol)
                if isinstance(sub, dict) and len(sub) == 0:
                    continue
                out[k] = sub
            else:
                # Lists or primitives
                if isinstance(v, list) and isinstance(t, list):
                    # Entire-list compare
                    if v == t:
                        continue
                    out[k] = v
                else:
                    if not _approx_equal(v, t, tol):
                        out[k] = v
        return out

    # List: only keep if different
    if isinstance(candidate, list) and isinstance(template, list):
        return [] if candidate == template else candidate

    # Primitive: keep if different
    if not _approx_equal(candidate, template, tol):
        return candidate

    # Same -> return empty dict indicating no overrides
    return {} if isinstance(candidate, dict) else candidate


# ----------------------- Utilities for nested access --------------------- #

def get_in(d: Json, path: List[str], default: Any = None) -> Any:
    cur = d
    for p in path:
        if not isinstance(cur, dict) or p not in cur:
            return default
        cur = cur[p]
    return cur


def set_in(d: Json, path: List[str], value: Any) -> None:
    cur = d
    for p in path[:-1]:
        if p not in cur or not isinstance(cur[p], dict):
            cur[p] = {}
        cur = cur[p]
    cur[path[-1]] = value


# --------------------------- Migrator Registry --------------------------- #

class Migrator:
    def __init__(self, prefab_template: Json, material_template: Json):
        self.prefab_template = prefab_template
        self.material_template = material_template

    def migrate(self, old: Json) -> Tuple[Json, Json]:
        """Return (prefab_override, material_override)."""
        raise NotImplementedError


class SurfacesMigrator(Migrator):
    """
    Maps old DefaultSurface-like JSON to the new split format.
    """
    def migrate(self, old: Json) -> Tuple[Json, Json]:
        # ----- Build Prefab override (start empty; only write if differs) -----
        prefab_override: Json = {}

        # Map UI Priority -> Game.Prefabs.UIObject.m_Priority
        old_priority = old.get("UiPriority", None)
        if old_priority is None and "Float" in old:
            old_priority = int(old["Float"].get("UiPriority", None))

        if old_priority is not None:
            # Find default priority in template (if present)
            default_priority = get_in(
                self.prefab_template,
                ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                default=-1
            )
            if not _approx_equal(old_priority, default_priority):
                set_in(
                    prefab_override,
                    ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                    old_priority
                )

        # You can add more surface-specific mappings here if needed, e.g.:
        # - mapping old["prefabIdentifierInfos"] to
        #   Components.Game.Prefabs.ObsoleteIdentifiers.PrefabIdentifiers
        # - mapping colors if present in the old file, etc.

        # ----- Build Material candidate from old values -----
        material_candidate: Json = {}

        # MaterialName / ShaderName are left to template defaults unless provided
        for top_key in ("MaterialName", "ShaderName"):
            if top_key in old:
                material_candidate[top_key] = old[top_key]

        # Copy Float / Vector from old if present
        for block in ("Float", "Vector"):
            if block in old and isinstance(old[block], dict):
                material_candidate.setdefault(block, {})
                for k, v in old[block].items():
                    # Directly carry values; diff will drop those equal to template
                    material_candidate[block][k] = v

        # Compute minimal overrides vs templates
        prefab_override = diff_overrides(prefab_override, self.prefab_template, tol=0.0)
        material_override = diff_overrides(material_candidate, self.material_template, tol=0.0)

        # Ensure we don't emit empty top-level objects (write {} if nothing differs)
        if not prefab_override:
            prefab_override = {}
        if not material_override:
            material_override = {}

        return prefab_override, material_override


class DecalsMigrator(Migrator):
    """
    Placeholder migrator for Decals. Fill in mapping rules as needed.
    """
    def migrate(self, old: Json) -> Tuple[Json, Json]:
        # Strategy: mirror Surfaces if the old Decal JSON uses Float/Vector too.
        prefab_override: Json = {}
        material_candidate: Json = {}
        # Example: if "UiPriority" exists, map it like Surfaces.
        old_priority = old.get("UiPriority", None)
        if old_priority is None and "Float" in old:
            old_priority = old["Float"].get("UiPriority", None)

        if old_priority is not None:
            default_priority = get_in(
                self.prefab_template,
                ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                default=-1
            )
            if not _approx_equal(old_priority, default_priority):
                set_in(
                    prefab_override,
                    ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                    old_priority
                )

        # Carry over Float/Vector if exist
        for block in ("Float", "Vector"):
            if block in old and isinstance(old[block], dict):
                material_candidate.setdefault(block, {})
                material_candidate[block].update(old[block])

        prefab_override = diff_overrides(prefab_override, self.prefab_template, tol=0.0)
        material_override = diff_overrides(material_candidate, self.material_template, tol=0.0)
        return prefab_override or {}, material_override or {}


class NetLanesMigrator(Migrator):
    """
    Placeholder migrator for NetLanes. Fill in mapping rules as needed.
    """
    def migrate(self, old: Json) -> Tuple[Json, Json]:
        prefab_override: Json = {}
        material_candidate: Json = {}

        # Example mapping (adjust once you know the old NetLane JSON structure):
        # - If there's a "Priority" field, map to UIObject.m_Priority
        for guess_key in ("UiPriority", "Priority"):
            if guess_key in old:
                default_priority = get_in(
                    self.prefab_template,
                    ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                    default=-1
                )
                if not _approx_equal(old[guess_key], default_priority):
                    set_in(
                        prefab_override,
                        ["Components", "Game.Prefabs.UIObject", "m_Priority"],
                        old[guess_key]
                    )
                break

        # Carry over Float/Vector if present
        for block in ("Float", "Vector"):
            if block in old and isinstance(old[block], dict):
                material_candidate.setdefault(block, {})
                material_candidate[block].update(old[block])

        prefab_override = diff_overrides(prefab_override, self.prefab_template, tol=0.0)
        material_override = diff_overrides(material_candidate, self.material_template, tol=0.0)
        return prefab_override or {}, material_override or {}


MIGRATORS = {
    "surfaces": SurfacesMigrator,
    "decals": DecalsMigrator,
    "netlanes": NetLanesMigrator,
}


# ------------------------------- CLI Logic ------------------------------- #

def load_templates(prefab_path: str, material_path: str) -> Tuple[Json, Json]:
    try:
        prefab_template = read_json(prefab_path)
        material_template = read_json(material_path)
    except Exception as e:
        print(f"[ERROR] Failed to read templates: {e}", file=sys.stderr)
        raise
    return prefab_template, material_template


def migrate_file(fmt: str, input_path: str, out_dir: str, prefab_tmpl: str, material_tmpl: str) -> Tuple[str, str]:
    prefab_template, material_template = load_templates(prefab_tmpl, material_tmpl)

    migrator_cls = MIGRATORS.get(fmt.lower())
    if not migrator_cls:
        raise ValueError(f"Unsupported format: {fmt}. Supported: {', '.join(MIGRATORS.keys())}")
    migrator = migrator_cls(prefab_template=prefab_template, material_template=material_template)

    old_json = read_json(input_path)
    prefab_over, material_over = migrator.migrate(old_json)

    # Write results
    # base = os.path.splitext(os.path.basename(input_path))[0]
    # out_prefab = os.path.join(out_dir, base, "Prefab.json")
    # out_material = os.path.join(out_dir, base, "Material.json")

    out_prefab = os.path.join(out_dir, "Prefab.json")
    out_material = os.path.join(out_dir, "Material.json")

    write_json(out_prefab, prefab_over if prefab_over else {})
    write_json(out_material, material_over if material_over else {})

    return out_prefab, out_material

def copy_sibling_files(src_dir: str, dst_dir: str):
    for f in os.listdir(src_dir):
        spath = os.path.join(src_dir, f)
        dpath = os.path.join(dst_dir, f)
        if os.path.isfile(spath) and not f.lower().endswith(".json"):
            os.makedirs(dst_dir, exist_ok=True)
            shutil.copy2(spath, dpath)

def migrate_directory(fmt: str, in_dir: str, out_dir: str, prefab_tmpl: str, material_tmpl: str, fail_fast: bool=False):
    """Recursively migrate all *.json files in in_dir, keeping subdirectory structure."""
    for root, _, files in os.walk(in_dir):
        rel = os.path.relpath(root, in_dir)
        for f in files:
            if not f.lower().endswith(".json"):
                continue
            ipath = os.path.join(root, f)
            opath = os.path.join(out_dir, rel)
            os.makedirs(opath, exist_ok=True)
            try:
                out_prefab, out_material = migrate_file(fmt, ipath, opath, prefab_tmpl, material_tmpl)
                copy_sibling_files(root, opath)
                print(f"[OK] {ipath} -> Prefab: {out_prefab}, Material: {out_material}")
            except Exception as e:
                print(f"[ERROR] {ipath}: {e}", file=sys.stderr)
                if fail_fast:
                    raise

def find_template_path(fmt: str, filename: str) -> str:
    """Search for template file case-insensitive in _DefaultJson first, then fallback path."""
    base_dirs = ["_DefaultJson", DEFAULT_JSON_FALLBACK]
    for base_dir in base_dirs:
        if not os.path.isdir(base_dir):
            continue
        for root, dirs, files in os.walk(base_dir):
            if os.path.basename(root).lower() == fmt.lower():
                for f in files:
                    if f.lower() == filename.lower():
                        return os.path.join(root, f)
    # fallback guessed path
    return os.path.join(base_dirs[-1], fmt.capitalize(), filename)

def exit(code: int):
    try:
        input("Press Enter to exit...")
    except EOFError:
        pass
    sys.exit(code)

def main(argv=None):
    parser = argparse.ArgumentParser(description="Migrate old JSON to new split format with template-based overrides.")
    parser.add_argument("--format", choices=list(MIGRATORS.keys()),
                        help="Input type (surfaces, decals, netlanes). Required if using --input or --input-dir.")
    group_in = parser.add_mutually_exclusive_group()
    group_in.add_argument("--input", help="Path to a single input JSON file.")
    group_in.add_argument("--input-dir", help="Path to a directory of old JSON files (recursive).")

    parser.add_argument("--out-dir", help="Output base directory. If not set, defaults to 'Surfaces', 'Decals', 'NetLanesDecal' depending on format.")
    parser.add_argument("--prefab-template", help="Path to the Prefab.json template for the target format.")
    parser.add_argument("--material-template", help="Path to the Material.json template for the target format.")
    parser.add_argument("--fail-fast", action="store_true", help="Stop on first conversion error (default continues).")

    args = parser.parse_args(argv)

    # ------------------- Default auto-discovery mode ------------------- #
    if not (args.input or args.input_dir):
        autodirs = {
            "surfaces": ("CustomSurfaces", "Surfaces"),
            "decals": ("CustomDecals", "Decals"),
            "netlanes": ("CustomNetLanes", "NetLanesDecal"),
        }
        found = []
        for fmt, (ind, outd) in autodirs.items():
            if not os.path.isdir(ind):
                os.makedirs(ind, exist_ok=True)  # créer si manquant
            if os.listdir(ind):  # ne considérer que si non vide
                found.append((fmt, ind, outd))

        if not found:
            parser.error("No --input/--input-dir provided and no Custom* folders with JSON files found.")

        errors = 0
        for fmt, ind, outd in found:
            os.makedirs(outd, exist_ok=True)

            prefab_tmpl = args.prefab_template or find_template_path(outd, "Prefab.json")
            material_tmpl = args.material_template or find_template_path(outd, "Material.json")

            if not os.path.isfile(prefab_tmpl) or not os.path.isfile(material_tmpl):
                print(f"[ERROR] Missing template(s) for {fmt}: {prefab_tmpl}, {material_tmpl}", file=sys.stderr)
                errors += 1
                continue

            try:
                migrate_directory(fmt, ind, outd, prefab_tmpl, material_tmpl, args.fail_fast)
            except Exception as e:
                errors += 1
                print(f"[ERROR] {ind}: {e}", file=sys.stderr)
                if args.fail_fast:
                    exit(1)
        if errors:
            exit(2)
        return

    # ------------------- Explicit mode ------------------- #
    if not args.format:
        parser.error("--format is required when using --input or --input-dir.")

    out_dir = args.out_dir or {
        "surfaces": "Surfaces",
        "decals": "Decals",
        "netlanes": "NetLanesDecal"
    }[args.format]
    os.makedirs(out_dir, exist_ok=True)

    if not (args.prefab_template and args.material_template):
        args.prefab_template = find_template_path(args.format, "Prefab.json")
        args.material_template = find_template_path(args.format, "Material.json")

    if not (os.path.isfile(args.prefab_template) and os.path.isfile(args.material_template)):
        parser.error(f"Missing template files: {args.prefab_template}, {args.material_template}")

    if args.input:
        try:
            out_prefab, out_material = migrate_file(
                fmt=args.format,
                input_path=args.input,
                out_dir=out_dir,
                prefab_tmpl=args.prefab_template,
                material_tmpl=args.material_template
            )
            print(f"[OK] {args.input} -> Prefab: {out_prefab}, Material: {out_material}")
        except Exception as e:
            print(f"[ERROR] {args.input}: {e}", file=sys.stderr)
            exit(1)
    else:
        try:
            migrate_directory(
                fmt=args.format,
                in_dir=args.input_dir,
                out_dir=out_dir,
                prefab_tmpl=args.prefab_template,
                material_tmpl=args.material_template,
                fail_fast=args.fail_fast
            )
        except Exception as e:
            print(f"[ERROR] {args.input_dir}: {e}", file=sys.stderr)
            exit(1)

if __name__ == "__main__":
    main()
    exit(0)
