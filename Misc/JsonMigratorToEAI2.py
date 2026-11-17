#!/usr/bin/env python3
# -*- coding: utf-8 -*-

from __future__ import annotations  # 'Json | None' annotations on Python 3.8 / 3.9

import argparse
import traceback
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

# JSON files read by each old importer, per asset folder.
OLD_JSON_FILES = {
    "surfaces": ("surface.json",),
    "decals": ("decal.json",),
    "netlanes": ("netLane.json", "decal.json"),
}

# Material keys the new decals / net lanes importers set themselves when Material.json doesn't have them.
# Their default can differ from the exported template, so they are always kept.
PROTECTED_MATERIAL_KEYS = {
    "decals": {"colossal_DecalLayerMask", "colossal_TextureArea", "colossal_MeshSize"},
    "netlanes": {"colossal_DecalLayerMask", "colossal_TextureArea", "colossal_MeshSize"},
}

# Components written by the migrator. Their default values are read from the templates exported by EAI
# ("Export default JSONs"), never written here.
UI_OBJECT = "Game.Prefabs.UIObject"
OBSOLETE_IDENTIFIERS = "Game.Prefabs.ObsoleteIdentifiers"
RENDERED_AREA = "Game.Prefabs.RenderedArea"
UTILITY_LANE = "Game.Prefabs.UtilityLane"
CURVE_PROPERTIES = "Game.Prefabs.CurveProperties"

# Templates needed per asset type: file -> components that must be in it.
REQUIRED_TEMPLATES = {
    "surfaces": {"Prefab.json": (UI_OBJECT, OBSOLETE_IDENTIFIERS, RENDERED_AREA)},
    "decals": {"Prefab.json": (UI_OBJECT, OBSOLETE_IDENTIFIERS), "Material.json": ()},
    "netlanes": {
        "Prefab.json": (UI_OBJECT, OBSOLETE_IDENTIFIERS, UTILITY_LANE),
        "Material.json": (),
        "RenderPrefab.json": (CURVE_PROPERTIES,),
    },
}

# Surfaces: the shader properties the new importer maps to the RenderedArea component
# (SurfacesImporterNew.ProcessMaterialJSON).
SURFACE_FLOAT_TO_RENDERED_AREA = {
    "_DrawOrder": "m_RendererPriority",
    "colossal_DecalLayerMask": "m_DecalLayerMask",
    "_Metallic": "m_Metallic",
    "_Smoothness": "m_Smoothness",
    "_NormalOpacity": "m_NormalOpacity",
    "_MetallicOpacity": "m_MetallicOpacity",
    "_NormalAlphaSource": "m_NormalAlphaSource",
    "_MetallicAlphaSource": "m_MetallicAlphaSource",
    "colossal_UVScale": "m_UVScale",
    "colossal_EdgeNormal": "m_EdgeNormal",
}

SURFACE_VECTOR_TO_RENDERED_AREA = {
    "_BaseColor": "m_BaseColor",
    "colossal_EdgeFadeRange": "m_EdgeFadeRange",
    "colossal_EdgeNormalRange": "m_EdgeNormalRange",
    "colossal_EdgeNoise": "m_EdgeNoise",
}

DECAL_LAYERS = (("Terrain", 1), ("Roads", 2), ("Buildings", 4), ("Vehicles", 8), ("Creatures", 16), ("Other", 32))


def decal_layers_to_json(mask: int) -> Any:
    """DecalLayers flags as written by EAI's templates ("Terrain", "Terrain, Roads"...). Unknown bits: the number."""
    names = [name for name, bit in DECAL_LAYERS if mask & bit]
    known = sum(bit for _, bit in DECAL_LAYERS)
    if not names or mask & ~known:
        return mask
    return ", ".join(names)


def _is_default(value: Any, default: Any) -> bool:
    """True if value equals the template's default (vectors per component, enum strings ignoring spaces and case)."""
    if isinstance(value, dict) and isinstance(default, dict):
        return all(_approx_equal(value.get(k), default.get(k), 1e-6) for k in set(value) | set(default))
    if isinstance(value, str) and isinstance(default, str):
        return value.replace(" ", "").lower() == default.replace(" ", "").lower()
    return _approx_equal(value, default, 1e-6)


def _fmt_number(x: Any) -> str:
    return str(int(x)) if _is_number(x) and float(x).is_integer() else str(x)


class AssetContext:
    def __init__(self, mod_name: str, cat_name: str, asset_name: str):
        self.mod_name = mod_name
        self.cat_name = cat_name
        self.asset_name = asset_name


def find_key(d: Json, key: str) -> str | None:
    """Return the key of d matching key, case-insensitively (Colossal's JSON decoder ignores case)."""
    if not isinstance(d, dict):
        return None
    for k in d.keys():
        if k.lower() == key.lower():
            return k
    return None


def get_ci(d: Json, key: str, default: Any = None) -> Any:
    k = find_key(d, key)
    return d[k] if k is not None else default


class Migrator:
    fmt = ""

    def __init__(self, templates: Dict[str, Json]):
        # Templates exported by EAI, by file name (Prefab.json, Material.json, RenderPrefab.json).
        self.templates = templates
        self.material_template = templates.get("Material.json")

    # --- Template helpers --- #

    def defaults(self, file: str, component: str) -> Json:
        """Default JSON of a component, from the exported template."""
        values = get_ci(get_ci(self.templates.get(file) or {}, "Components") or {}, component)
        if not isinstance(values, dict):
            raise RuntimeError(f"the {file} template has no {component} component")
        return values

    @staticmethod
    def strip_defaults(values: Json, defaults: Json, always: Tuple[str, ...] = ()) -> Tuple[Json, List[str]]:
        """
        Keep the values that differ from the defaults (and the keys in always), with the template's spelling.
        Return them and the keys the template doesn't know (not written).
        """
        out: Json = {}
        unknown: List[str] = []
        for k, v in values.items():
            dk = find_key(defaults, k)
            if dk is None:
                unknown.append(k)
            elif v is not None and (dk in always or not _is_default(v, defaults[dk])):
                out[dk] = v
        return out, unknown

    @staticmethod
    def report_unknown(ctx: AssetContext, block: str, keys: List[str]) -> None:
        if keys:
            print(f"[INFO] {ctx.cat_name}/{ctx.asset_name}: not in EAI's template, skipped: "
                  f"{', '.join(block + '.' + k for k in keys)}")

    # --- Prefab.json helpers --- #

    @staticmethod
    def old_priority(old: Json | None, read_float: bool) -> Any:
        """UiPriority of an old JSON. Like the old importers, a UiPriority in Float wins."""
        if not old:
            return None
        priority = get_ci(old, "UiPriority")
        if read_float:
            float_priority = get_ci(get_ci(old, "Float") or {}, "UiPriority")
            if float_priority is not None:
                priority = float_priority
        return int(priority) if _is_number(priority) else priority

    def obsolete_identifiers(self, old: Json | None) -> List[Json]:
        # Fields of one identifier, from the template (m_Name, m_Type, m_Hash).
        template_ids = get_ci(self.defaults("Prefab.json", OBSOLETE_IDENTIFIERS), "PrefabIdentifiers") or []
        fields = template_ids[0] if template_ids and isinstance(template_ids[0], dict) else {}
        out: List[Json] = []
        for info in (get_ci(old, "prefabIdentifierInfos") or []) if old else []:
            if not isinstance(info, dict):
                continue
            entry = {find_key(fields, k): v for k, v in info.items() if find_key(fields, k) and v is not None}
            if "m_Name" in entry and "m_Type" in entry:
                out.append(entry)
        return out

    @staticmethod
    def add_identifier(identifiers: List[Json], name: str, type_name: str) -> None:
        if not any(i.get("m_Name") == name and i.get("m_Type") == type_name for i in identifiers):
            identifiers.append({"m_Name": name, "m_Type": type_name})

    def build_prefab(self, priority: Any, identifiers: List[Json], extra_components: Json | None = None, extra_fields: Json | None = None) -> Json:
        components: Json = {}
        if priority is not None:
            # Kept even when empty: with the component, EAI uses its default priority, not the game's.
            components[UI_OBJECT], _ = self.strip_defaults({"m_Priority": priority}, self.defaults("Prefab.json", UI_OBJECT))
        if identifiers:
            components[OBSOLETE_IDENTIFIERS] = {"PrefabIdentifiers": identifiers}
        if extra_components:
            components.update(extra_components)

        prefab: Json = {}
        if components:
            prefab["Components"] = components
        if extra_fields:
            prefab.update(extra_fields)
        return prefab

    # --- Material.json helpers --- #

    def build_material(self, old: Json | None) -> Json:
        """Copy Float / Vector of the old JSON. Values equal to the template are dropped, except protected keys."""
        if not old:
            return {}

        candidate: Json = {}
        for block in ("Float", "Vector"):
            values = get_ci(old, block)
            if isinstance(values, dict) and values:
                candidate[block] = {k: v for k, v in values.items() if k != "UiPriority"}

        if not self.material_template:
            raise RuntimeError("the Material.json template is required")
        out = {}
        protected = PROTECTED_MATERIAL_KEYS.get(self.fmt, set())
        for block, values in candidate.items():
            template_block = self.material_template.get(block) or {}
            kept: Json = {}
            for k, v in values.items():
                if k in protected or k not in template_block:
                    # Protected, or unknown to the template (the template can be outdated): keep it.
                    kept[k] = v
                    continue
                t = template_block[k]
                differs = diff_overrides(v, t) != {} if isinstance(v, dict) and isinstance(t, dict) else not _approx_equal(v, t)
                if differs:
                    kept[k] = v
            if kept:
                out[block] = kept

        return {k: v for k, v in out.items() if v}

    def migrate(self, old_jsons: Dict[str, Json], ctx: AssetContext) -> Tuple[Json, Json, Json]:
        """
        Return (Prefab.json, Material.json, RenderPrefab.json) for one asset; an empty dict means no file.
        old_jsons maps lower-case file names to their content.
        """
        raise NotImplementedError


class SurfacesMigrator(Migrator):
    """
    The new surfaces have no material: everything goes in the RenderedArea component of Prefab.json.
    (Material.json is abandoned for surfaces, so the migrator doesn't write one.)
    """
    fmt = "surfaces"

    def is_default(self, field: str, value: Any) -> bool:
        """True if value is the template's default for this RenderedArea field (only non-default values are written)."""
        return _is_default(value, get_ci(self.rendered_area_defaults, field))

    def migrate(self, old_jsons: Dict[str, Json], ctx: AssetContext) -> Tuple[Json, Json, Json]:
        self.rendered_area_defaults = self.defaults("Prefab.json", RENDERED_AREA)
        old = old_jsons.get("surface.json")

        # The RenderedArea component is always written: with it, the new importer gives the surface
        # the same default draw order per category as the old importer (Ground -100, Grass -99...).
        rendered_area: Json = {}
        roundness = get_ci(old, "m_Roundness") if old else None
        if _is_number(roundness) and not self.is_default("m_Roundness", roundness):
            rendered_area["m_Roundness"] = roundness

        floats = (get_ci(old, "Float") or {}) if old else {}
        vectors = (get_ci(old, "Vector") or {}) if old else {}
        ignored: List[str] = []

        for key, value in floats.items():
            if key == "UiPriority":
                continue
            field = SURFACE_FLOAT_TO_RENDERED_AREA.get(key)
            if field is None or not _is_number(value):
                ignored.append(key)
                continue
            if field == "m_RendererPriority":
                if int(value) == 0:
                    print(f"[WARN] {ctx.cat_name}/{ctx.asset_name}: _DrawOrder 0 can't be kept, the new importer "
                          f"uses the category's default draw order instead.")
                    continue
                rendered_area[field] = int(value)
            elif field == "m_DecalLayerMask":
                layers = decal_layers_to_json(int(value))
                if not self.is_default(field, layers):
                    rendered_area[field] = layers
            elif not self.is_default(field, value):
                rendered_area[field] = value

        for key, value in vectors.items():
            field = SURFACE_VECTOR_TO_RENDERED_AREA.get(key)
            if field is None or not isinstance(value, dict):
                ignored.append(key)
                continue
            if field == "m_BaseColor":
                color = [value.get(c, 1) for c in ("x", "y", "z", "w")]
                rgba = "RGBA({})".format(", ".join(_fmt_number(c) for c in color))
                if not self.is_default(field, rgba):
                    rendered_area[field] = rgba
            else:
                xy = {"x": value.get("x", 0), "y": value.get("y", 0)}
                if not self.is_default(field, xy):
                    rendered_area[field] = xy

        if ignored:
            print(f"[INFO] {ctx.cat_name}/{ctx.asset_name}: not used by the new surfaces, skipped: {', '.join(ignored)}")

        prefab = self.build_prefab(
            self.old_priority(old, read_float=True),
            self.obsolete_identifiers(old),
            {RENDERED_AREA: rendered_area},
        )
        return prefab, {}, {}


class DecalsMigrator(Migrator):
    fmt = "decals"

    def migrate(self, old_jsons: Dict[str, Json], ctx: AssetContext) -> Tuple[Json, Json, Json]:
        old = old_jsons.get("decal.json")
        prefab = self.build_prefab(self.old_priority(old, read_float=True), self.obsolete_identifiers(old))
        return prefab, self.build_material(old), {}


class NetLanesMigrator(Migrator):
    fmt = "netlanes"

    def migrate(self, old_jsons: Dict[str, Json], ctx: AssetContext) -> Tuple[Json, Json, Json]:
        old = old_jsons.get("netlane.json")  # prefab settings
        old_material = old_jsons.get("decal.json")  # material, read like the old decals importer

        identifiers = self.obsolete_identifiers(old)
        # The old importer named net lanes "<Mod> <Category> <Name> NetLane"; the new one uses "NetLaneDecal".
        # Keep the old name so existing saves find the new net lane.
        self.add_identifier(
            identifiers,
            f"{ctx.mod_name} {ctx.cat_name} {ctx.asset_name} NetLane",
            "NetLaneGeometryPrefab",
        )

        # The game reads CurveProperties on the mesh (render prefab): it goes in RenderPrefab.json,
        # like the old importer that added it to the render prefab.
        render_prefab: Json = {}
        curve = get_ci(old, "curveProperties") if old else None
        if isinstance(curve, dict):
            values, unknown = self.strip_defaults(curve, self.defaults("RenderPrefab.json", CURVE_PROPERTIES))
            render_prefab = {"Components": {CURVE_PROPERTIES: values}}
            self.report_unknown(ctx, "curveProperties", unknown)

        components: Json = {}

        utility = get_ci(old, "utilityLane") if old else None
        if isinstance(utility, dict):
            # UtilityType is always written: its default (WaterPipe) is not obvious.
            values, unknown = self.strip_defaults(utility, self.defaults("Prefab.json", UTILITY_LANE), always=("UtilityType",))
            components[UTILITY_LANE] = values
            self.report_unknown(ctx, "utilityLane", unknown)

        fields: Json = {}
        pathfind = get_ci(old, "PathfindPrefab") if old else None
        if pathfind:
            fields["m_PathfindPrefab"] = pathfind

        prefab = self.build_prefab(self.old_priority(old, read_float=False), identifiers, components, fields)
        return prefab, self.build_material(old_material), render_prefab


MIGRATORS = {
    "surfaces": SurfacesMigrator,
    "decals": DecalsMigrator,
    "netlanes": NetLanesMigrator,
}


# ------------------------------- CLI Logic ------------------------------- #

def load_template(path: str | None) -> Json | None:
    if not path or not os.path.isfile(path):
        return None
    try:
        return read_json(path)
    except Exception as e:
        print(f"[WARN] Failed to read template {path}: {e}")
        return None


def old_mod_name(in_dir: str) -> str:
    """Mod name like the old importers: first .dll next to the old folder, else that folder's name; part before the first '_'."""
    parent = os.path.dirname(os.path.abspath(in_dir))
    dlls = sorted(f for f in os.listdir(parent) if f.lower().endswith(".dll") and os.path.isfile(os.path.join(parent, f)))
    name = os.path.splitext(dlls[0])[0] if dlls else os.path.basename(parent)
    return name.split("_")[0]


def migrate_asset(fmt: str, asset_dir: str, out_dir: str, ctx: AssetContext, templates: Dict[str, Json]) -> None:
    migrator = MIGRATORS[fmt](templates)

    old_jsons: Dict[str, Json] = {}
    for f in os.listdir(asset_dir):
        if f.lower() in (n.lower() for n in OLD_JSON_FILES[fmt]):
            old_jsons[f.lower()] = read_json(os.path.join(asset_dir, f))

    prefab, material, render_prefab = migrator.migrate(old_jsons, ctx)

    written = []
    for name, content in (("Prefab.json", prefab), ("Material.json", material), ("RenderPrefab.json", render_prefab)):
        if content:
            write_json(os.path.join(out_dir, name), content)
            written.append(name)

    print(f"[OK] {asset_dir} -> {', '.join(written) if written else 'no JSON needed'}")


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

def handle_texture_sharing(src_path: str, dst_path: str, pack_root: str, importer_folder: str, precomputed_hash: str | None = None):
    """
    If identical texture already seen, remove dst_path (if any) and write JSON redirect
    with path pointing to the asset folder (relative to pack_root, prefixed by the importer folder name,
    e.g. Decals\\Graffiti\\CRIME: the new importers read it relative to the mod's root).
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
            asset_ref = importer_folder
        else:
            asset_ref = os.path.join(importer_folder, rel).replace("/", "\\")

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
        return img.quantize(256)  # Ensure 8-bit per channel
    elif args.force_8bit_method == "libimagequant":
        try:
            import imagequant
        except ImportError:
            print("[WARN] The 'imagequant' package is not installed (pip install imagequant). Using Pillow's quantization instead.")
            args.force_8bit_method = "PIL"
            return img.quantize(256)
        return imagequant.quantize_pil_image(
            img,
            max_colors=256,
        )
    else:
        raise ValueError(f"Unknown 8-bit conversion method: {args.force_8bit_method}")

def process_surface_maskmap(src_path: str, dst_path: str) -> str | None:
    """
    The old surfaces importer loaded _MaskMap.png as an sRGB texture: the game read its RGB channels
    converted from sRGB to linear. The new importer loads it as linear data. Convert RGB from sRGB to
    linear so the surface looks the same (alpha is not affected by sRGB).
    """
    try:
        with Image.open(src_path) as src:
            has_alpha = src.mode in ("RGBA", "LA") or src.has_transparency_data
            img = src.convert("RGBA" if has_alpha else "RGB")
        arr = np.array(img).astype(np.float32) / 255.0
        rgb = arr[:, :, :3]
        arr[:, :, :3] = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
        img = Image.fromarray(np.clip(np.round(arr * 255.0), 0, 255).astype(np.uint8))

        if args.force_8bit and TEXTURE_NAMES["_maskmap"] in args.force_8bit_types:
            img = QuantizeImageTo8Bit(img)

        img.save(dst_path)
        print(f"[INFO] MaskMap converted from sRGB to linear: {src_path}")
        return hash_image(img)
    except Exception as e:
        print(f"[WARN] Failed converting mask map {src_path}: {e}")
        return None


def copy_sibling_files(src_dir: str, dst_dir: str, fmt: str, pack_root: str, importer_folder: str, skip_names: Tuple[str, ...] = ()):
    skip = {n.lower() for n in skip_names}
    for f in os.listdir(src_dir):
        spath = os.path.join(src_dir, f)
        dpath = os.path.join(dst_dir, f)
        if not os.path.isfile(spath) or f.lower() in skip:
            continue
        os.makedirs(dst_dir, exist_ok=True)
        flower = f.lower()

        # NormalMap processing (returns hash of the saved file)
        if fmt == "surfaces" and TEXTURE_NAMES["_normalmap"].lower() in flower:
            h = process_normalmap(spath, dpath)
            handle_texture_sharing(dpath, dpath, pack_root, importer_folder, precomputed_hash=h)
        elif fmt == "surfaces" and args.surface_maskmap_linear and TEXTURE_NAMES["_maskmap"].lower() in flower:
            h = process_surface_maskmap(spath, dpath)
            if h is None:
                handle_texture_sharing(spath, dpath, pack_root, importer_folder, precomputed_hash=None)
            else:
                handle_texture_sharing(dpath, dpath, pack_root, importer_folder, precomputed_hash=h)
        else:
            # other files: let the handler compute the hash & copy
            handle_texture_sharing(spath, dpath, pack_root, importer_folder, precomputed_hash=None)


def migrate_directory(fmt: str, in_dir: str, out_dir: str, templates: Dict[str, Json], mod_name: str, fail_fast: bool = False) -> int:
    """
    Migrate an old importer folder (<in_dir>/<Category>/<Asset>) to a new one, keeping the folder structure.
    Each asset folder gets a Prefab.json / Material.json / RenderPrefab.json built from its old JSON files.
    Return the number of assets that failed.
    """
    errors = 0
    importer_folder = DIRS[fmt][1]
    print(f"[INFO] {in_dir} -> {out_dir} (mod name: {mod_name})")

    for root, _, _ in os.walk(in_dir):
        rel = os.path.relpath(root, in_dir)
        opath = os.path.join(out_dir, rel)
        depth = 0 if rel == "." else len(rel.split(os.sep))

        skip: Tuple[str, ...] = ()
        if depth == 2:
            cat_name = os.path.basename(os.path.dirname(root))
            asset_name = os.path.basename(root)
            if cat_name.startswith(".") or asset_name.startswith("."):
                print(f"[INFO] Skipped (name starts with '.'): {root}")
            else:
                os.makedirs(opath, exist_ok=True)
                try:
                    migrate_asset(fmt, root, opath, AssetContext(mod_name, cat_name, asset_name), templates)
                except Exception as e:
                    errors += 1
                    print(f"[ERROR] {root}: {e}", file=sys.stderr)
                    if fail_fast:
                        raise
            skip = OLD_JSON_FILES[fmt]

        copy_sibling_files(root, opath, fmt, out_dir, importer_folder, skip)

    return errors


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
    return os.path.join(base_dirs[-1], fmt, filename)

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
    parser.add_argument("--input-dir", help="Path to a directory of an old importer. If not set, defaults to 'CustomSurfaces', 'CustomDecals', 'CustomNetLanes' depending on format.")

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

    parser.add_argument(
        "--mod-name",
        help="Mod name used in the old prefab names (to keep the old net lane names). "
             "Default: like EAI, the first .dll next to the old folders, else that folder's name (part before the first '_')."
    )

    parser.add_argument(
        "--surface-maskmap-linear",
        action="store_true",
        help="Convert the RGB channels of the surfaces' _MaskMap from sRGB to linear. The old surfaces importer "
             "loaded the mask map as sRGB, the new one as linear: this keeps the look of the old surfaces."
    )

    global args
    args = parser.parse_args(argv)

    def run(fmt: str, in_dir: str, out_dir: str) -> int:
        """Migrate one old importer folder. Return the number of assets that failed."""
        # The templates are required: the default values come from them, only the values that differ are written.
        templates: Dict[str, Json] = {}
        missing: List[str] = []
        for file, components in REQUIRED_TEMPLATES[fmt].items():
            path = find_template_path(DIRS[fmt][1], file)
            template = load_template(path)
            if not isinstance(template, dict) or not template:
                missing.append(path)
                continue
            for component in components:
                if not isinstance(get_ci(get_ci(template, "Components") or {}, component), dict):
                    missing.append(f"{component} in {path}")
            templates[file] = template
        if missing:
            raise RuntimeError(
                f"Missing {fmt} templates: {'; '.join(missing)}. "
                f"Click 'Export default JSONs' in EAI's settings (with the latest EAI), then run the script again.")

        os.makedirs(out_dir, exist_ok=True)
        mod_name = args.mod_name or old_mod_name(in_dir)
        return migrate_directory(fmt, in_dir, out_dir, templates, mod_name, args.fail_fast)

    def finish(errors: int) -> None:
        if errors:
            print(f"[WARN] {errors} error(s): some assets were NOT migrated (see the [ERROR] lines). "
                  f"Fix them and run the script again. Don't remove the old Custom* folders yet.")
            exit(2)
        print("[INFO] Done. Check the new folders, then remove the old Custom* folders so the old and new assets are not loaded together.")

    # ------------------- Default auto-discovery mode ------------------- #
    if not (args.format or args.input_dir or args.output_dir):
        found = []
        for fmt, (ind, outd) in DIRS.items():
            if os.path.isdir(ind) and os.listdir(ind):
                found.append((fmt, ind, outd))

        print(f"[INFO] Auto-discovered input directories: {found}")

        if not found:
            parser.error("No --input-dir/--output-dir provided and no Custom* folders found.")

        errors = 0
        for fmt, ind, outd in found:
            try:
                errors += run(fmt, ind, outd)
            except Exception as e:
                errors += 1
                print(f"[ERROR] {ind}: {e}", file=sys.stderr)
                if args.fail_fast:
                    exit(1)

        finish(errors)
        return

    # ------------------- Explicit mode ------------------- #
    if not args.format:
        parser.error("--format is required when using --output-dir or --input-dir.")

    (in_dir, out_dir) = DIRS[args.format]
    try:
        errors = run(args.format, args.input_dir or in_dir, args.output_dir or out_dir)
    except Exception as e:
        print(f"[ERROR] {args.input_dir or in_dir}: {e}", file=sys.stderr)
        exit(1)
    finish(errors)

if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise  # exit() already asked to press Enter; keep its exit code
    except Exception:
        traceback.print_exc()
        exit(1)
    exit(0)
