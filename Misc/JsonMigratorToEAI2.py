#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse
import json
import os
import shutil
import sys
from typing import Any, Dict, Tuple, List
import hashlib


# For NormalMap processing and 8bit conversion
from PIL import Image
from PIL.Image import Quantize
import numpy as np

script_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
os.chdir(script_dir)

Json = Dict[str, Any]

DIRS = {
    "surfaces": ("CustomSurfaces", "Surfaces"),
    "decals": ("CustomDecals", "Decals"),
    "netlanes": ("CustomNetLanes", "NetLanesDecal"),
}

TEXTURE_NAMES = { 
    "_basecolormap": "_BaseColorMap",
    "_normalmap": "_NormalMap",
    "_maskmap": "_MaskMap"
 }

# global registry of seen textures by type -> hash -> asset_relative_path
SEEN_TEXTURES = {
    "_basecolormap": {},
    "_normalmap": {},
    "_maskmap": {}
}

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

    Special rule:
    - If dict looks like a Vector4 (keys x,y,z,w), compare as a whole.
      * If all components equal -> drop
      * If at least one differs -> keep full vector
    """
    # If candidate is None or missing, no override
    if candidate is None and template is not None:
        return None if not _approx_equal(candidate, template, tol) else {}

    # Dict
    if isinstance(candidate, dict) and isinstance(template, dict):

        # --- Vector4 special case ---
        if set(candidate.keys()) >= {"x", "y", "z", "w"} and set(template.keys()) >= {"x", "y", "z", "w"}:
            same = True
            for comp in ("x", "y", "z", "w"):
                if not _approx_equal(candidate.get(comp), template.get(comp), tol):
                    same = False
                    break
            if same:
                return {}  # identical, drop
            else:
                return candidate  # at least one different → keep full vector

        # --- Normal dict diff ---
        out: Dict[str, Any] = {}
        for k, v in candidate.items():
            if k not in template:
                continue
            t = template.get(k)

            if isinstance(v, dict) and isinstance(t, dict):
                sub = diff_overrides(v, t, tol)
                if isinstance(sub, dict) and len(sub) == 0:
                    continue
                out[k] = sub
            else:
                if isinstance(v, list) and isinstance(t, list):
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
            old_priority = old["Float"].get("UiPriority", None)

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

        old_Roundness = old.get("m_Roundness", None)
        if old_Roundness is None and "Float" in old:
            old_Roundness = old["Float"].get("m_Roundness", None)

        if old_Roundness is not None:
            default_Roundness = get_in(
                self.prefab_template,
                ["Components", "Game.Prefabs.RenderedArea", "Roundness"],
                default=0.5
            )
            if not _approx_equal(old_Roundness, default_Roundness):
                set_in(
                    prefab_override,
                    ["Components", "Game.Prefabs.RenderedArea", "Roundness"],
                    old_Roundness
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


def migrate_file(fmt: str, input_path: str, out_dir: str, prefab_tmpl: str, material_tmpl: str) -> Tuple [ str | None, str | None] :
    prefab_template, material_template = load_templates(prefab_tmpl, material_tmpl)

    migrator_cls = MIGRATORS.get(fmt.lower())
    if not migrator_cls:
        raise ValueError(f"Unsupported format: {fmt}. Supported: {', '.join(MIGRATORS.keys())}")
    migrator = migrator_cls(prefab_template=prefab_template, material_template=material_template)

    old_json = read_json(input_path)
    prefab_over, material_over = migrator.migrate(old_json)

    out_prefab = os.path.join(out_dir, "Prefab.json")
    out_material = os.path.join(out_dir, "Material.json")

    isPrefabNone = (prefab_over == None or prefab_over == {})
    isMaterialNone = (material_over == None or material_over == {})

    if(not isPrefabNone): 
        write_json(out_prefab, prefab_over)
    if(not isMaterialNone): 
        write_json(out_material, material_over)

    return out_prefab if not isPrefabNone else None, out_material if not isMaterialNone else None
              
def process_normalmap(src_path: str, dst_path: str):
    """Detect and process NormalMap: invert R channel in linear space like GIMP."""
    try:

        with Image.open(src_path).convert("RGB") as img:
            arr = np.array(img).astype(np.float32) / 255.0 

        # compute mean color for detection
        mean = arr.mean(axis=(0,1))
        r_mean, g_mean, b_mean = mean[0], mean[1], mean[2]

        # detect pink normal map (R≈0.9, G≈0.5, B≈1)
        if abs(r_mean - 0.9) < 0.11 and abs(g_mean - 0.5) < 0.11 and abs(b_mean - 0.9) < 0.11:
            print(f"[INFO] Detected pink NormalMap: {src_path}")

            # Convert sRGB -> Linear
            mask = arr[:,:,0] <= 0.04045
            r_lin = np.empty_like(arr[:,:,0])
            r_lin[mask]  = arr[:,:,0][mask] / 12.92
            r_lin[~mask] = ((arr[:,:,0][~mask] + 0.055) / 1.055) ** 2.4

            # Invert in linear space
            r_inv = 1.0 - r_lin

            # Convert back Linear -> sRGB
            mask = r_inv <= 0.0031308
            r_new = np.empty_like(r_inv)
            r_new[mask]  = r_inv[mask] * 12.92
            r_new[~mask] = 1.055 * (r_inv[~mask] ** (1/2.4)) - 0.055

            arr[:,:,0] = r_new
            arr = np.clip(arr * 255.0, 0, 255).astype(np.uint8)

            img.close()
            img = Image.fromarray(arr)
            # img.save(dst_path)
        else:
            print(f"[INFO] NormalMap does not appear pink {mean}, no R inversion: {src_path}")
        #     # aucune modification, réutiliser l'image convertie en RGBA
        #     out_img = img

        if args.force_8bit and TEXTURE_NAMES["_normalmap"] in args.force_8bit_types:
            img = QuantizeImageTo8Bit(img)  # Ensure 8-bit per channel

        # save final image
        img.save(dst_path)

        # compute hash from the saved image buffer (no reopen needed)
        h = hash_image(img) #hashlib.sha1(img.tobytes()).hexdigest()
        try:
            img.close()
        except Exception:
            pass
        return h

    except Exception as e:
        print(f"[WARN] Failed processing normal map {src_path}: {e}")
        # fallback: copy as-is and hash
        shutil.copy2(src_path, dst_path)
        return hash_image_file(dst_path)

def hash_image(img: Image.Image) -> str:

    # if img.mode not in ("RGB", "RGBA"):
    #     raise Exception(f"Unsupported image mode for hashing: {img.mode}")

    return hashlib.sha1(img.tobytes()).hexdigest()

def hash_image_file(path: str) -> str:
    """Return SHA1 of image pixels (RGBA). Opens file and closes it properly."""
    with Image.open(path) as img:
        return hash_image(img)
        # img_rgba = img.convert("RGBA")
        # return hashlib.sha1(img_rgba.tobytes()).hexdigest()

def handle_texture_sharing(src_path: str, dst_path: str, pack_root: str, precomputed_hash: str | None = None):
    """
    If identical texture already seen, remove dst_path (if any) and write JSON redirect
    with path pointing to the asset folder (relative to pack_root, prefixed by pack folder name).
    Otherwise copy the texture and register it.
    """
    name = os.path.basename(src_path).lower()
    key = None
    for k in SEEN_TEXTURES.keys():
        if k in name:
            key = k
            break

    # If not one of the special textures, just copy
    if key is None:
        shutil.copy2(src_path, dst_path)
        return

    # get hash (use precomputed if available)
    if precomputed_hash:
        h = precomputed_hash
    else:
        # open once to hash (hash_image_file closes file)
        h = hash_image_file(src_path)

    # check duplicate
    existing = SEEN_TEXTURES[key].get(h)
    if existing:
        # duplicate found -> remove dst file if created and write redirect JSON
        try:
            if os.path.exists(dst_path):
                os.remove(dst_path)
        except Exception:
            pass

        # write JSON pointing to the asset folder (format: PackName\Category\AssetName)
        jpath = os.path.splitext(dst_path)[0] + ".json"
        write_json(jpath, {"path": existing})
        print(f"[SHARE] {src_path} -> {existing}")
    else:
        # new texture: ensure it's on disk at dst_path (if we already saved it, fine; else copy)
        if not os.path.exists(dst_path):
            if( not ensure_8bit_texture(src_path, dst_path)):
                shutil.copy2(src_path, dst_path)

        # asset folder (dst_path parent) relative to pack_root but prefixed by pack folder name
        asset_dir = os.path.dirname(dst_path)
        rel = os.path.relpath(asset_dir, start=pack_root).replace("/", "\\")
        if rel == ".":
            asset_ref = os.path.basename(pack_root)
        else:
            asset_ref = os.path.join(os.path.basename(pack_root), rel).replace("/", "\\")

        SEEN_TEXTURES[key][h] = asset_ref
        # debug
        # print(f"[REGISTER] {h} -> {asset_ref}")

def ensure_8bit_texture(src_path: str, dst_path: str) -> bool:
    """
    Copies the texture from src_path to dst_path if needed, ensures it's in 8-bit
    per channel format, and returns the SHA-1 hash of the resulting file.

    Returns:
        bool: True if conversion was applied, False if not needed or failed.
    """

    fname_lower = os.path.basename(src_path).lower()

    if not args.force_8bit or not any(val.lower() in fname_lower for val in args.force_8bit_types):
        return False

    try:
        with Image.open(src_path) as img:
            mode = img.mode
            if mode not in ("RGB", "RGBA"):

                if(img.has_transparency_data):
                    print(f"[INFO] Converting {src_path} from {mode} to 8-bit with alpha")
                    img = img.convert("RGBA")
                else:
                    print(f"[INFO] Converting {src_path} from {mode} to 8-bit")
                    img = img.convert("RGB")

            img = QuantizeImageTo8Bit(img)

            img.save(dst_path)
            try:
                img.close()
            except Exception:
                pass
            return True

    except Exception as e:
        print(f"[WARN] Failed to ensure 8-bit for {src_path}: {e}")
        return False

def QuantizeImageTo8Bit(img : Image.Image) -> Image.Image:
    if args.force_8bit_method == "PIL":
        return img.quantize(256, Quantize.LIBIMAGEQUANT)  # Ensure 8-bit per channel
    elif args.force_8bit_method == "libimagequant":
        import imagequant
        return imagequant.quantize_pil_image(
            img,
            max_colors=256,
        )
    else:
        raise ValueError(f"Unknown 8-bit conversion method: {args.force_8bit_method}")

def copy_sibling_files(src_dir: str, dst_dir: str, fmt: str, pack_root: str):
    for f in os.listdir(src_dir):
        spath = os.path.join(src_dir, f)
        dpath = os.path.join(dst_dir, f)
        if not os.path.isfile(spath) or f.lower().endswith(".json"):
            continue
        os.makedirs(dst_dir, exist_ok=True)

        # NormalMap processing (returns hash of the saved file)
        if fmt == "surfaces" and TEXTURE_NAMES["_normalmap"].lower() in f.lower():
            h = process_normalmap(spath, dpath)
            handle_texture_sharing(dpath, dpath, pack_root, precomputed_hash=h)
        else:
            # non-normalmap: just let handler compute hash & copy
            handle_texture_sharing(spath, dpath, pack_root, precomputed_hash=None)


def migrate_directory(fmt: str, in_dir: str, out_dir: str, prefab_tmpl: str, material_tmpl: str, fail_fast: bool=False):
    """Recursively migrate all *.json files in in_dir, keeping subdirectory structure."""
    for root, _, files in os.walk(in_dir):
        rel = os.path.relpath(root, in_dir)
        opath = os.path.join(out_dir, rel)
        for f in files:
            if not f.lower().endswith(".json"):
                continue
            ipath = os.path.join(root, f)
            os.makedirs(opath, exist_ok=True)
            try:
                out_prefab, out_material = migrate_file(fmt, ipath, opath, prefab_tmpl, material_tmpl)

                if(out_prefab == None or out_prefab == {}):
                    print(f"[OK] {ipath} -> Material: {out_material}")
                elif(out_material == None or out_material == {}):
                    print(f"[OK] {ipath} -> Prefab: {out_prefab}")
                elif( ( out_material == None or out_material == {} ) and ( out_prefab == None or out_prefab == {} ) ):
                    print(f"[OK] {ipath} -> No changes needed.")    
                else:
                    print(f"[OK] {ipath} -> Prefab: {out_prefab}, Material: {out_material}")
            except Exception as e:
                print(f"[ERROR] {ipath}: {e}", file=sys.stderr)
                if fail_fast:
                    raise

        copy_sibling_files(root, opath, fmt, out_dir)

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
    parser = argparse.ArgumentParser(description = (
        "Migrate old importer folders to the new importer format:\n"
        "\t- Convert old JSON files to the new split format using template-based overrides,\n"
        "\t- Fix pink Normal Maps for surfaces,\n"
        "\t- Optionally convert textures to 8-bit to save space.\n\n"
        "This script must be placed next to the old importer folders and then run. "
        "It will automatically discover all supported old importer folders.\n"
        "You can specify a specific asset type to convert using --format. "
        "If no --input-dir or --output-dir is provided, the default folders will be used.")
    )
    parser.add_argument("--format", choices=list(MIGRATORS.keys()),
                        help="Input type (surfaces, decals, netlanes). Required if using --input-dir or --output-dir.")
    group_in = parser.add_mutually_exclusive_group()
    group_in.add_argument("--input-dir", help="Path to a directory of an old importer. If not set, defaults to 'CustomSurfaces', 'CustomDecals', 'CustomNetLanes' depending on format.")

    parser.add_argument("--output-dir", help="Output base directory. If not set, defaults to 'Surfaces', 'Decals', 'NetLanesDecal' depending on format.")
    parser.add_argument("--fail-fast", action="store_true", help="Stop on first conversion error (default continues).")

    parser.add_argument(
        "--force-8bit",
        action="store_true",
        default=True,
        help="Force textures to be converted to 8-bit per channel before hashing/sharing. (default: enabled)"
    )

    parser.add_argument(
        "--no-force-8bit",
        action="store_false",
        dest="force_8bit",
        help="Disable automatic 8-bit conversion."
    )

    parser.add_argument(
        "--force-8bit-types",
        nargs="+",
        choices=TEXTURE_NAMES.values(),
        default=TEXTURE_NAMES.values(),
        help="Specify which texture types to apply 8-bit conversion to. Default: all."
    )

    parser.add_argument(
        "--force-8bit-method",
        choices=["PIL", "libimagequant"],
        default="libimagequant",
        help="Method to use for 8-bit conversion. 'PIL' uses Pillow's built-in quantization, 'libimagequant' uses the imagequant library for better quality. (default: libimagequant)"
    )

    global args
    args = parser.parse_args(argv)

    # ------------------- Default auto-discovery mode ------------------- #
    if not ( args.format or args.input_dir or args.output_dir):
        found = []
        for fmt, (ind, outd) in DIRS.items():
            if os.path.isdir(ind) and os.listdir(ind):
                found.append((fmt, ind, outd))

        print(f"[INFO] Auto-discovered input directories: {found}")

        if not found:
            parser.error("No --input-dir/--output-dir provided and no Custom* folders found.")

        errors = 0
        for fmt, ind, outd in found:
            os.makedirs(outd, exist_ok=True)

            prefab_tmpl = find_template_path(outd, "Prefab.json")
            material_tmpl = find_template_path(outd, "Material.json")

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
        parser.error("--format is required when using --output_dir or --input-dir.")

    ( in_dir , out_dir ) = DIRS[args.format]
    os.makedirs(out_dir, exist_ok=True)

    prefab_tmpl = find_template_path(out_dir, "Prefab.json")
    material_tmpl = find_template_path(out_dir, "Material.json")

    if not os.path.isfile(prefab_tmpl) or not os.path.isfile(material_tmpl):
        print(f"[ERROR] Missing template file(s) : {prefab_tmpl}, {material_tmpl}", file=sys.stderr)
        exit(1)

    try:
        migrate_directory(
            fmt=args.format,
            in_dir= args.input_dir or in_dir,
            out_dir= args.output_dir or out_dir,
            prefab_tmpl = prefab_tmpl,
            material_tmpl = material_tmpl,
            fail_fast=args.fail_fast
        )
    except Exception as e:
        print(f"[ERROR] {args.input_dir}: {e}", file=sys.stderr)
        exit(1)

if __name__ == "__main__":
    try:
        main()
    except Exception as err:
        print(f"{err}")
        raise

    finally:
        exit(0)
