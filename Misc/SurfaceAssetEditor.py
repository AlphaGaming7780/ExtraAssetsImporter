"""
Editor for .Surface files (Cities Skylines II / Colossal.IO.AssetDatabase).

The binary format is the one written by SurfaceAsset.SaveData() (see
Colossal.IO.AssetDatabase, decompiled) and has been validated by exact
round-trip (loading then re-writing reproduces the original byte for byte)
on real .Surface files from the game. See the SurfaceFile docstrings for
the format details.

.Surface files can also be edited directly inside .cok packages (Colossal
Order mod/asset packages, e.g. the ones under
"...AppData/LocalLow/Colossal Order/Cities Skylines II/.packages/"): a .cok
is just a plain zip archive. Each entry has a sibling "<name>.cid" file,
which is a stable Colossal asset-database ID (not a content hash), so
overwriting a .Surface entry's bytes never invalidates it.

Self-contained: reads the game's material data (property catalog and the
materialID -> name table) directly from resources.assets via UnityPy, with
no separate decoder module and no on-disk cache.
"""

import os
import sys
import struct
import tempfile
import zipfile
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
from typing import Literal

try:
    import UnityPy
except ImportError:
    UnityPy = None

# A .Surface file is located either directly on disk, or as a member inside
# a .cok (zip) archive. The Literal tags let the type checker narrow which
# tuple shape we're dealing with after a `source[0] == "disk"` check.
DiskSource = tuple[Literal["disk"], str]              # ("disk", fullpath)
CokSource = tuple[Literal["cok"], str, str]           # ("cok", cok_path, member)
FileSource = DiskSource | CokSource

# ==============================================================================
# CONFIG
# ==============================================================================

# Directory the script lives in. When frozen by PyInstaller (or similar),
# __file__ points inside a transient extraction folder, not next to the
# actual .exe, so we anchor to sys.executable's folder instead.
BASE_DIR = os.path.dirname(sys.executable) if getattr(sys, "frozen", False) \
    else os.path.dirname(os.path.abspath(__file__))

# "Cities2_Data" folder of the game install (contains resources.assets).
# The game itself sets CSII_INSTALLATIONPATH (used by its modding
# toolchain), so we reuse it when present. No hardcoded fallback path here
# on purpose (a Steam library path is specific to one machine) - if the env
# var is missing, GAME_DATA_PATH is left empty and the "Game data folder..."
# button in the UI lets the user point at it manually.
_INSTALL_PATH = os.environ.get("CSII_INSTALLATIONPATH", "")
GAME_DATA_PATH = os.path.join(_INSTALL_PATH, "Cities2_Data") if _INSTALL_PATH else ""

# ==============================================================================
# MATERIAL DATA (game data, via UnityPy) - property catalog + materialID table
# ==============================================================================

class GameMaterialData:
    """
    Reads the game's material property catalog and materialID -> name table
    directly from Cities2_Data/resources.assets via UnityPy.

    Every method is static: there is no per-instance state, this class only
    groups the related parsing logic under one name instead of leaving it as
    loose module-level functions.
    """

    # Mapping between the Unity serialization section of a Material property
    # (m_SavedProperties) and the binary type used in the .Surface format. This
    # is the only remaining "fixed" table: it describes a file format (how to
    # read bytes), not a list of game properties, so it doesn't need to be
    # maintained by hand when the game adds/removes properties or materials.
    UNITY_SECTION_TO_TYPE = {
        "m_Floats": "float",
        "m_Ints": "int",
        "m_Colors": "vector4",   # Color and Vector4 are both stored as 4 floats (RGBA / XYZW)
        "m_TexEnvs": "hash128",  # texture references -> asset-database ID (Hash128 CID, not a content hash) in the .Surface format
    }

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    @staticmethod
    def load(game_data_path: str = GAME_DATA_PATH) -> tuple[dict, dict]:
        """
        Loads resources.assets with UnityPy in a single pass and returns
        (material_catalog, material_id_map). No on-disk caching: this always
        re-reads the game file (a few seconds for the ~220 MB resources.assets),
        so the data can never go stale after a game update.
        """
        if UnityPy is None:
            raise RuntimeError("UnityPy is not installed. `pip install UnityPy`")

        resources_path = os.path.join(game_data_path, "resources.assets")
        if not os.path.isfile(resources_path):
            raise FileNotFoundError(f"Not found: {resources_path}")

        env = UnityPy.load(resources_path)

        material_names_by_pathid = {}
        for obj in env.objects:
            if obj.type.name == "Material":
                try:
                    material_names_by_pathid[obj.path_id] = obj.read().m_Name
                except Exception:
                    continue

        id_map = GameMaterialData._extract_material_id_map(env, material_names_by_pathid)
        catalog = GameMaterialData._extract_material_catalog(env, set(id_map.values()))

        return catalog, id_map

    @staticmethod
    def get_properties_for_material(catalog: dict, material_name: str) -> dict:
        """
        Returns {prop_name: {"type": ..., "default": ...}} for this specific
        material. If the name is absent from the catalog (unknown material /
        catalog out of sync with the installed game), falls back to the union of
        all known properties across all materials rather than failing.
        """
        if material_name in catalog:
            return catalog[material_name]

        fallback = {}
        for props in catalog.values():
            fallback.update(props)
        return fallback

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _read_int(data: bytes, offset: int) -> int:
        return struct.unpack_from("<i", data, offset)[0]

    @staticmethod
    def _read_unity_pptr(data: bytes, offset: int) -> tuple[int, int, int]:
        """Reads a Unity PPtr (fileID: int32, pathID: int64). Returns (fileID, pathID, new_offset)."""
        file_id = GameMaterialData._read_int(data, offset)
        path_id = struct.unpack_from("<q", data, offset + 4)[0]
        return file_id, path_id, offset + 12

    @staticmethod
    def _read_unity_string(data: bytes, offset: int) -> tuple[str, int]:
        """Reads a Unity string (int32 length-prefixed, 4-byte aligned)."""
        length = GameMaterialData._read_int(data, offset)
        offset += 4
        text = data[offset:offset + length].decode("utf-8", errors="replace")
        offset += length
        offset += (4 - offset % 4) % 4
        return text, offset

    @staticmethod
    def _extract_material_id_map(env, material_names_by_pathid: dict) -> dict:
        """
        Builds { materialID: material_name, ... } by directly reading the
        serialized AssetDatabaseResources instance in resources.assets.

        m_MaterialLibrary is a private, non-Unity-Object field (so no reliable
        read_typetree() here: the typetree generated for AssetDatabaseResources
        fails on a neighboring field, UnityObjectsMap). So we parse the
        MonoBehaviour object as raw binary, using the standard Unity layout
        (PPtr, length-prefixed strings) and then the field layout of
        AssetDatabaseResources / MaterialLibrary / MaterialDescription as
        defined in Colossal.IO.AssetDatabase (AssetDatabaseResources.cs,
        MaterialLibrary.cs, MaterialStackProperties.cs).
        """
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue

            raw = obj.get_raw_data()
            if len(raw) < 28:
                continue

            # Generic MonoBehaviour header: m_GameObject PPtr (12) + m_Enabled+pad (4) + m_Script PPtr (12)
            offset = 28
            try:
                name, offset = GameMaterialData._read_unity_string(raw, offset)
            except (struct.error, UnicodeDecodeError, IndexError):
                continue

            if name != "AssetDatabaseResources":
                continue

            # m_TerrainRenderMaterial, m_TerrainSplatMaterial, m_MoonAlbedo, m_MoonNormal (PPtr)
            # then m_Shaders.{waterUpdate, waterUpdateLegacy, waterRenderUtils, snowUpdate} (PPtr)
            for _ in range(8):
                _, _, offset = GameMaterialData._read_unity_pptr(raw, offset)

            # m_MaterialLibrary.m_Materials: List<MaterialDescription>
            count = GameMaterialData._read_int(raw, offset)
            offset += 4

            id_map = {}
            for _ in range(count):
                material_hash = GameMaterialData._read_int(raw, offset)
                offset += 4
                _, mat_path_id, offset = GameMaterialData._read_unity_pptr(raw, offset)
                offset += 4  # m_SupportsVT (bool, 4-byte aligned)
                offset += 4  # m_MipBiasOverride (int)
                stacks_count = GameMaterialData._read_int(raw, offset)
                offset += 4
                for _ in range(stacks_count):
                    propnames_count = GameMaterialData._read_int(raw, offset)
                    offset += 4
                    for _ in range(propnames_count):
                        _, offset = GameMaterialData._read_unity_string(raw, offset)

                id_map[material_hash] = material_names_by_pathid.get(
                    mat_path_id, f"UnknownMaterial_{mat_path_id}"
                )

            return id_map

        raise RuntimeError(
            "AssetDatabaseResources not found in resources.assets "
            "(the game's format may have changed since this parser was written)."
        )

    @staticmethod
    def _extract_material_catalog(env, target_names: set) -> dict:
        """
        Builds, for each Unity Material whose name is in target_names, the real
        list of its properties with their type, inferred from the Unity section
        the property is serialized in (see UNITY_SECTION_TO_TYPE), as well as
        the default value defined on the Material (the one used if a .Surface
        file doesn't override it).

        target_names restricts the scan to the materials that actually appear
        in material_id_map (the ones .Surface files can reference) -
        resources.assets also contains many other, unrelated Material objects
        that the editor has no use for.

        Returns { material_name: { property_name: {"type": ..., "default": ...}, ... }, ... }
        For "hash128" (texture), "default" is the name of the shader's default
        texture (resolved via its PPtr), or None if no default texture is
        assigned.
        """
        texture_names_by_pathid = {}
        for obj in env.objects:
            if obj.type.name in ("Texture2D", "Texture2DArray", "Texture3D", "Cubemap", "RenderTexture"):
                try:
                    texture_names_by_pathid[obj.path_id] = obj.read().m_Name
                except Exception:
                    continue

        def default_value(section: str, value):
            if section == "m_Colors":
                return [value["r"], value["g"], value["b"], value["a"]]
            if section == "m_TexEnvs":
                path_id = value["m_Texture"]["m_PathID"]
                if path_id == 0:
                    return None
                return texture_names_by_pathid.get(path_id, f"UnknownTexture_{path_id}")
            return value  # float / int: already a plain Python value

        catalog = {}

        for obj in env.objects:
            if obj.type.name != "Material":
                continue

            data = obj.read()
            name = data.m_Name
            if name not in target_names or name in catalog:
                continue

            tree = obj.read_typetree()
            saved = tree.get("m_SavedProperties", {})

            material_props = {}
            for section, ptype in GameMaterialData.UNITY_SECTION_TO_TYPE.items():
                for prop_name, value in saved.get(section, []):
                    material_props[prop_name] = {"type": ptype, "default": default_value(section, value)}

            catalog[name] = material_props

        return catalog


# ==============================================================================
# .Surface BINARY FORMAT (read/write)
# ==============================================================================

VT_HEADER_SIZE = 276  # SurfaceAssetVTHeader, [StructLayout(..., Size = 276)]


def _read_7bit_int(data: bytes, offset: int) -> tuple[int, int]:
    """.NET 7-bit-encoded integer (length prefix used by BinaryWriter.Write(string))."""
    result = 0
    shift = 0
    while True:
        b = data[offset]
        offset += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    return result, offset


def _write_7bit_int(value: int) -> bytes:
    out = bytearray()
    v = value
    while v >= 0x80:
        out.append((v & 0x7F) | 0x80)
        v >>= 7
    out.append(v)
    return bytes(out)


def _read_net_string(data: bytes, offset: int) -> tuple[str, int]:
    length, offset = _read_7bit_int(data, offset)
    text = data[offset:offset + length].decode("utf-8")
    offset += length
    return text, offset


def _write_net_string(text: str) -> bytes:
    encoded = text.encode("utf-8")
    return _write_7bit_int(len(encoded)) + encoded


class SurfaceFile:
    """
    In-memory model of a .Surface file, with full parsing/serialization.
    Round-trip verified byte-for-byte on real files from the game (loading
    a file then re-writing it without modifying it reproduces the original
    exactly).

    Format (source: SurfaceAsset.cs, BinaryReaderExtensions.cs,
    BinaryWriterExtensions.cs in Colossal.IO.AssetDatabase):

        uint16  version               (=1)
        int32   materialTemplateHash  (materialID, see MaterialLibrary)
        byte    isVT
        [if isVT]
            276 raw bytes              (SurfaceAssetVTHeader, fixed size)
            byte    presence
            [if presence] 16 bytes    (Hash128 asset-database ID of the linked VTSurfaceAsset)

        Then, in this order, 5 "dictionaries" all using the same
        format [presence:1][count:int32][ (name:.NET string, value) * count ]:
            m_Floats    value = float          (4 bytes)
            m_Ints      value = int32          (4 bytes)
            m_Vectors   value = 4 floats       (16 bytes)
            m_Colors    value = 4 floats       (16 bytes)
            m_Textures  value = presence:1 + [16 bytes Hash128 asset-database ID]

        Then m_Keywords: HashSet<string>, same presence+count+strings format
        (no value, just the name).

    The VT header (276 raw bytes + VTSurfaceAsset hash) is copied through
    unchanged on write: this editor never modifies it. Changing the
    materialID of a VT file is therefore not offered (the VT header encodes
    the stack configuration of the original material and would become
    inconsistent).
    """

    def __init__(self):
        self.version = 1
        self.material_id = 0
        self.is_vt = 0
        self.vt_header_raw = b""
        self.vt_surface_hash = None  # hex str (32 chars) or None

        self.floats: dict[str, float] = {}
        self.ints: dict[str, int] = {}
        self.vectors: dict[str, tuple] = {}
        self.colors: dict[str, tuple] = {}
        self.textures: dict[str, str | None] = {}  # value: hex hash (32 chars) or None
        self.keywords: list[str] = []

    # ------------------------------------------------------------------
    # Reading
    # ------------------------------------------------------------------

    @classmethod
    def load(cls, path: str) -> "SurfaceFile":
        with open(path, "rb") as f:
            data = f.read()
        return cls.load_bytes(data)

    @classmethod
    def load_bytes(cls, data: bytes) -> "SurfaceFile":
        surf = cls()
        offset = 0
        surf.version = struct.unpack_from("<H", data, offset)[0]
        offset += 2
        surf.material_id = struct.unpack_from("<i", data, offset)[0]
        offset += 4
        surf.is_vt = data[offset]
        offset += 1

        if surf.is_vt:
            surf.vt_header_raw = data[offset:offset + VT_HEADER_SIZE]
            offset += VT_HEADER_SIZE
            presence = data[offset]
            offset += 1
            if presence not in (0x00, 0xFF):
                raise ValueError(f"m_VTSurfaceAsset: unexpected presence byte {presence:#x}")
            if presence == 0xFF:
                surf.vt_surface_hash = data[offset:offset + 16].hex()
                offset += 16

        surf.floats, offset = surf._read_value_dict(data, offset, "m_Floats", surf._read_float)
        surf.ints, offset = surf._read_value_dict(data, offset, "m_Ints", surf._read_int)
        surf.vectors, offset = surf._read_value_dict(data, offset, "m_Vectors", surf._read_vector4)
        surf.colors, offset = surf._read_value_dict(data, offset, "m_Colors", surf._read_vector4)
        surf.textures, offset = surf._read_value_dict(data, offset, "m_Textures", surf._read_texture)

        presence = data[offset]
        offset += 1
        if presence not in (0x00, 0xFF):
            raise ValueError(f"m_Keywords: unexpected presence byte {presence:#x}")
        if presence == 0xFF:
            count = struct.unpack_from("<i", data, offset)[0]
            offset += 4
            for _ in range(count):
                kw, offset = _read_net_string(data, offset)
                surf.keywords.append(kw)

        if offset != len(data):
            raise ValueError(
                f"Parsing ended at offset {offset}, but the file is {len(data)} bytes long "
                "(format variant not handled by this editor - file not modified)."
            )

        return surf

    @staticmethod
    def _read_value_dict(data: bytes, offset: int, label: str, value_reader):
        presence = data[offset]
        offset += 1
        result = {}
        if presence not in (0x00, 0xFF):
            raise ValueError(f"{label}: unexpected presence byte {presence:#x}")
        if presence == 0xFF:
            count = struct.unpack_from("<i", data, offset)[0]
            offset += 4
            for _ in range(count):
                name, offset = _read_net_string(data, offset)
                value, offset = value_reader(data, offset)
                result[name] = value
        return result, offset

    @staticmethod
    def _read_float(data: bytes, offset: int):
        return struct.unpack_from("<f", data, offset)[0], offset + 4

    @staticmethod
    def _read_int(data: bytes, offset: int):
        return struct.unpack_from("<i", data, offset)[0], offset + 4

    @staticmethod
    def _read_vector4(data: bytes, offset: int):
        return struct.unpack_from("<4f", data, offset), offset + 16

    @staticmethod
    def _read_texture(data: bytes, offset: int):
        presence = data[offset]
        offset += 1
        if presence == 0xFF:
            return data[offset:offset + 16].hex(), offset + 16
        if presence == 0x00:
            return None, offset
        raise ValueError(f"m_Textures: unexpected presence byte {presence:#x}")

    # ------------------------------------------------------------------
    # Writing
    # ------------------------------------------------------------------

    def to_bytes(self) -> bytes:
        out = bytearray()
        out += struct.pack("<H", self.version)
        out += struct.pack("<i", self.material_id)
        out.append(self.is_vt)

        if self.is_vt:
            if len(self.vt_header_raw) != VT_HEADER_SIZE:
                raise ValueError("Invalid VT header (wrong size)")
            out += self.vt_header_raw
            if self.vt_surface_hash is not None:
                out.append(0xFF)
                out += bytes.fromhex(self.vt_surface_hash)
            else:
                out.append(0x00)

        out += self._write_value_dict(self.floats, lambda v: struct.pack("<f", v))
        out += self._write_value_dict(self.ints, lambda v: struct.pack("<i", v))
        out += self._write_value_dict(self.vectors, lambda v: struct.pack("<4f", *v))
        out += self._write_value_dict(self.colors, lambda v: struct.pack("<4f", *v))
        out += self._write_value_dict(
            self.textures,
            lambda v: (b"\xff" + bytes.fromhex(v)) if v is not None else b"\x00",
        )

        out.append(0xFF)
        out += struct.pack("<i", len(self.keywords))
        for kw in self.keywords:
            out += _write_net_string(kw)

        return bytes(out)

    @staticmethod
    def _write_value_dict(entries: dict, value_writer) -> bytes:
        out = bytearray()
        out.append(0xFF)
        out += struct.pack("<i", len(entries))
        for name, value in entries.items():
            out += _write_net_string(name)
            out += value_writer(value)
        return bytes(out)

    def save(self, path: str) -> None:
        with open(path, "wb") as f:
            f.write(self.to_bytes())

    @staticmethod
    def parse_vt_header(raw: bytes) -> dict:
        """
        Parses the 276-byte SurfaceAssetVTHeader for read-only display (see
        SurfaceAssetVTHeader.cs / PerStackData.cs / AtlassedSize.cs in
        Colossal.IO.AssetDatabase). This is display-only: to_bytes() always
        writes vt_header_raw back untouched, this method never round-trips
        through a rebuilt header.

            int32  m_NbVTStacks
            PerStackData m_StackData0  (at offset 4, 136 bytes)
            PerStackData m_StackData1  (at offset 140, 136 bytes)

        PerStackData: AtlassedSize (2 int32, 8 bytes) followed by 4
        "m_TextureGUIDLayerN" Hash128 (16 bytes each) and 4
        "m_VTTextureGUIDLayerN" Hash128 (16 bytes each).

        Returns {"nb_vt_stacks": int, "stacks": [stack0, stack1]} where each
        stack is {"atlas_size": (x, y), "texture_guids": [...], "vt_texture_guids": [...]}
        (GUID entries are hex strings, or None for an all-zero/unused Hash128).
        """
        def read_hash(offset: int):
            h = raw[offset:offset + 16].hex()
            return None if h == "0" * 32 else h

        def read_stack(base: int) -> dict:
            x, y = struct.unpack_from("<2i", raw, base)
            texture_guids = [read_hash(base + 8 + i * 16) for i in range(4)]
            vt_texture_guids = [read_hash(base + 72 + i * 16) for i in range(4)]
            return {"atlas_size": (x, y), "texture_guids": texture_guids, "vt_texture_guids": vt_texture_guids}

        nb_vt_stacks = struct.unpack_from("<i", raw, 0)[0]
        return {"nb_vt_stacks": nb_vt_stacks, "stacks": [read_stack(4), read_stack(140)]}

    # ------------------------------------------------------------------
    # Uniform access for the UI
    # ------------------------------------------------------------------

    SECTION_TYPES = {
        "floats": "float",
        "ints": "int",
        "vectors": "vector4",
        "colors": "vector4",
        "textures": "hash128",
    }

    def all_properties(self):
        """[(section, name, type), ...] for all properties present, sorted."""
        rows = []
        for section, ptype in self.SECTION_TYPES.items():
            for name in getattr(self, section):
                rows.append((section, name, ptype))
        rows.sort(key=lambda r: (r[0], r[1]))
        return rows

    def remove_property(self, section: str, name: str) -> None:
        del getattr(self, section)[name]

    def set_property(self, section: str, name, value) -> None:
        getattr(self, section)[name] = value


# ==============================================================================
# UI (Tkinter)
# ==============================================================================

class SurfaceEditorApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title(".Surface File Editor")
        self.root.geometry("1050x800")

        self.current_dir = BASE_DIR
        self.current_path: str | None = None
        self.current_source: FileSource | None = None
        self.surface: SurfaceFile | None = None
        self.game_data_path = GAME_DATA_PATH
        self.material_catalog = {}
        self.material_id_map = {}
        self._all_surface_files: list[tuple[str, FileSource]] = []  # (relpath, source)
        self._filtered_files: list[tuple[str, FileSource]] = []

        self._build_ui()
        self._load_game_data_async()
        self._refresh_file_list()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self):
        root = self.root

        # --- Top bar: folder + file list ---
        top = ttk.Frame(root, padding=6)
        top.pack(side=tk.TOP, fill=tk.X)

        ttk.Button(top, text="Browse .Surface folder...", command=self._choose_folder).pack(side=tk.LEFT)
        self.folder_label = ttk.Label(top, text=self.current_dir)
        self.folder_label.pack(side=tk.LEFT, padx=8)

        ttk.Button(top, text="Game data folder...", command=self._choose_game_folder).pack(side=tk.LEFT, padx=(8, 0))

        self.status_var = tk.StringVar(value="")
        ttk.Label(top, textvariable=self.status_var, foreground="#555").pack(side=tk.RIGHT)

        # --- Body: file list (left) / details (right) ---
        body = ttk.Frame(root, padding=(6, 0, 6, 6))
        body.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        left = ttk.Frame(body)
        left.pack(side=tk.LEFT, fill=tk.Y)
        ttk.Label(left, text=".Surface files (subfolders + .cok archives)").pack(anchor=tk.W)

        search_row = ttk.Frame(left)
        search_row.pack(fill=tk.X, pady=(2, 4))
        ttk.Label(search_row, text="Search:").pack(side=tk.LEFT)
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._apply_file_filter())
        ttk.Entry(search_row, textvariable=self.search_var, width=37).pack(side=tk.LEFT, padx=(4, 0))

        self.file_count_var = tk.StringVar(value="0 files")
        ttk.Label(left, textvariable=self.file_count_var, foreground="#777").pack(anchor=tk.W)

        self.file_listbox = tk.Listbox(left, width=45, exportselection=False)
        self.file_listbox.pack(fill=tk.Y, expand=True)
        self.file_listbox.bind("<<ListboxSelect>>", self._on_file_selected)

        right = ttk.Frame(body)
        right.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(10, 0))

        # File header
        header = ttk.LabelFrame(right, text="File", padding=8)
        header.pack(fill=tk.X)

        self.material_var = tk.StringVar(value="-")
        self.vt_var = tk.StringVar(value="-")
        self._material_label_to_id: dict[str, int] = {}

        row1 = ttk.Frame(header)
        row1.pack(fill=tk.X)
        ttk.Label(row1, text="Material:").pack(side=tk.LEFT)
        self.material_combo = ttk.Combobox(row1, textvariable=self.material_var, width=40, state="readonly")
        self.material_combo.pack(side=tk.LEFT, padx=(4, 12))
        self.material_combo.bind("<<ComboboxSelected>>", self._on_material_selected)
        ttk.Label(row1, text="VT:").pack(side=tk.LEFT)
        ttk.Label(row1, textvariable=self.vt_var).pack(side=tk.LEFT, padx=(4, 12))

        self.unlock_var = tk.BooleanVar(value=True)
        self.unlock_check = ttk.Checkbutton(
            row1, text="Unlock editing (.cok)", variable=self.unlock_var, command=self._on_unlock_toggled
        )
        self.unlock_check.pack(side=tk.LEFT, padx=(4, 0))

        row2 = ttk.Frame(header)
        row2.pack(fill=tk.X, pady=(4, 0))
        self.lock_status_var = tk.StringVar(value="")
        ttk.Label(row2, textvariable=self.lock_status_var, foreground="#b35c00").pack(anchor=tk.W)

        # Notebook: Properties / Keywords
        notebook = ttk.Notebook(right)
        notebook.pack(fill=tk.BOTH, expand=True, pady=(8, 0))

        props_tab = ttk.Frame(notebook, padding=6)
        notebook.add(props_tab, text="Properties")
        self._build_properties_tab(props_tab)

        keywords_tab = ttk.Frame(notebook, padding=6)
        notebook.add(keywords_tab, text="Keywords")
        self._build_keywords_tab(keywords_tab)

        vt_tab = ttk.Frame(notebook, padding=6)
        notebook.add(vt_tab, text="VT Header")
        self._build_vt_tab(vt_tab)

        # --- Bottom: save ---
        bottom = ttk.Frame(root, padding=6)
        bottom.pack(side=tk.BOTTOM, fill=tk.X)
        ttk.Button(bottom, text="Save as...", command=self._save_as).pack(side=tk.RIGHT, padx=(6, 0))
        self.save_overwrite_btn = ttk.Button(bottom, text="Save (overwrite file)", command=self._save_overwrite)
        self.save_overwrite_btn.pack(side=tk.RIGHT)

    def _build_properties_tab(self, parent):
        columns = ("section", "name", "type", "value")
        self.tree = ttk.Treeview(parent, columns=columns, show="headings", height=14)
        for col, label, width in (
            ("section", "Section", 90), ("name", "Name", 260), ("type", "Type", 70), ("value", "Value", 260)
        ):
            self.tree.heading(col, text=label)
            self.tree.column(col, width=width, anchor=tk.W)
        self.tree.pack(fill=tk.BOTH, expand=True)
        self.tree.bind("<<TreeviewSelect>>", self._on_property_selected)

        # Edit panel for the selected property
        edit = ttk.LabelFrame(parent, text="Selected property", padding=8)
        edit.pack(fill=tk.X, pady=(8, 0))

        self.edit_name_var = tk.StringVar()
        self.edit_section_var = tk.StringVar()
        self.value_vars = [tk.StringVar() for _ in range(4)]

        info_row = ttk.Frame(edit)
        info_row.pack(fill=tk.X)
        ttk.Label(info_row, textvariable=self.edit_name_var, font=("Segoe UI", 9, "bold")).pack(side=tk.LEFT)
        ttk.Label(info_row, textvariable=self.edit_section_var, foreground="#777").pack(side=tk.LEFT, padx=8)

        self.value_frame = ttk.Frame(edit)
        self.value_frame.pack(fill=tk.X, pady=6)

        self.texture_warning = ttk.Label(
            edit,
            text="Warning: this value is the texture's ID in the game's asset database (Hash128 CID), "
                 "not a hash of its pixel content. Entering an ID that doesn't exist in the database "
                 "will make the texture missing in-game.",
            foreground="#b35c00", wraplength=520, justify=tk.LEFT,
        )

        btn_row = ttk.Frame(edit)
        btn_row.pack(fill=tk.X, pady=(4, 0))
        self.apply_value_btn = ttk.Button(btn_row, text="Apply value", command=self._apply_property_value)
        self.apply_value_btn.pack(side=tk.LEFT)
        self.remove_prop_btn = ttk.Button(
            btn_row, text="Remove this property", command=self._remove_selected_property
        )
        self.remove_prop_btn.pack(side=tk.LEFT, padx=(6, 0))

        # Add panel
        add = ttk.LabelFrame(parent, text="Add a material property", padding=8)
        add.pack(fill=tk.X, pady=(8, 0))

        add_row1 = ttk.Frame(add)
        add_row1.pack(fill=tk.X)
        ttk.Label(add_row1, text="Available property:").pack(side=tk.LEFT)
        self.add_prop_var = tk.StringVar()
        self.add_prop_combo = ttk.Combobox(add_row1, textvariable=self.add_prop_var, width=45, state="readonly")
        self.add_prop_combo.pack(side=tk.LEFT, padx=6)
        self.add_prop_combo.bind("<<ComboboxSelected>>", self._on_add_prop_selected)

        ttk.Label(add_row1, text="Section (if Vector4):").pack(side=tk.LEFT, padx=(12, 0))
        self.add_section_var = tk.StringVar(value="colors")
        self.add_section_combo = ttk.Combobox(
            add_row1, textvariable=self.add_section_var, width=10, state="readonly",
            values=("colors", "vectors"),
        )
        self.add_section_combo.pack(side=tk.LEFT, padx=6)

        self.add_value_frame = ttk.Frame(add)
        self.add_value_frame.pack(fill=tk.X, pady=6)
        self.add_value_vars = [tk.StringVar() for _ in range(4)]

        self.add_button = ttk.Button(add, text="Add", command=self._add_property)
        self.add_button.pack(anchor=tk.E)

    def _build_keywords_tab(self, parent):
        self.keywords_listbox = tk.Listbox(parent, height=12)
        self.keywords_listbox.pack(fill=tk.BOTH, expand=True)

        row = ttk.Frame(parent)
        row.pack(fill=tk.X, pady=(8, 0))
        self.new_keyword_var = tk.StringVar()
        self.keyword_entry = ttk.Entry(row, textvariable=self.new_keyword_var, width=40)
        self.keyword_entry.pack(side=tk.LEFT)
        self.add_keyword_btn = ttk.Button(row, text="Add", command=self._add_keyword)
        self.add_keyword_btn.pack(side=tk.LEFT, padx=6)
        self.remove_keyword_btn = ttk.Button(row, text="Remove selected", command=self._remove_keyword)
        self.remove_keyword_btn.pack(side=tk.LEFT)

    def _build_vt_tab(self, parent):
        self.vt_info_var = tk.StringVar(value="This file is not a VT (Virtual Texturing) surface.")
        ttk.Label(parent, textvariable=self.vt_info_var, wraplength=560, justify=tk.LEFT).pack(
            anchor=tk.W, pady=(0, 8)
        )

        columns = ("field", "value")
        self.vt_tree = ttk.Treeview(parent, columns=columns, show="headings", height=18)
        self.vt_tree.heading("field", text="Field")
        self.vt_tree.heading("value", text="Value")
        self.vt_tree.column("field", width=220, anchor=tk.W)
        self.vt_tree.column("value", width=360, anchor=tk.W)
        self.vt_tree.pack(fill=tk.BOTH, expand=True)

        btn_row = ttk.Frame(parent)
        btn_row.pack(fill=tk.X, pady=(8, 0))
        self.remove_vt_btn = ttk.Button(btn_row, text="Remove VT...", command=self._remove_vt)
        self.remove_vt_btn.pack(side=tk.LEFT)

    # ------------------------------------------------------------------
    # Loading game data (catalog + materialID table)
    # ------------------------------------------------------------------

    def _show_loading_popup(self, message: str) -> tk.Toplevel:
        popup = tk.Toplevel(self.root)
        popup.title("Please wait")
        popup.resizable(False, False)
        popup.transient(self.root)
        popup.protocol("WM_DELETE_WINDOW", lambda: None)  # not user-closable
        ttk.Label(popup, text=message, padding=24).pack()
        popup.update_idletasks()

        self.root.update_idletasks()
        x = self.root.winfo_x() + (self.root.winfo_width() - popup.winfo_width()) // 2
        y = self.root.winfo_y() + (self.root.winfo_height() - popup.winfo_height()) // 2
        popup.geometry(f"+{max(x, 0)}+{max(y, 0)}")

        popup.grab_set()
        popup.update()
        return popup

    def _load_game_data(self, game_data_path: str) -> bool:
        popup = self._show_loading_popup("Loading material data from the game files...")
        try:
            self.material_catalog, self.material_id_map = GameMaterialData.load(game_data_path)
            self.game_data_path = game_data_path
            self.status_var.set(f"{len(self.material_catalog)} materials loaded from the game.")
            return True
        except Exception as exc:
            self.status_var.set("Material data unavailable (see message).")
            messagebox.showwarning(
                "Material data",
                "Could not load material data from the game:\n"
                f"{exc}\n\n"
                "Editing existing values is still possible, but adding new "
                "properties and resolving material names will not be.\n\n"
                "Use 'Game data folder...' to point at the correct "
                "'Cities2_Data' folder.",
            )
            return False
        finally:
            popup.destroy()

    def _load_game_data_async(self):
        self._load_game_data(self.game_data_path)

    def _choose_game_folder(self):
        folder = filedialog.askdirectory(
            title="Select the game's 'Cities2_Data' folder",
            initialdir=self.game_data_path if os.path.isdir(self.game_data_path) else BASE_DIR,
        )
        if not folder:
            return
        if self._load_game_data(folder):
            self._refresh_add_combo()
            if self.surface is not None:
                self._refresh_header()

    # ------------------------------------------------------------------
    # File management
    # ------------------------------------------------------------------

    def _choose_folder(self):
        folder = filedialog.askdirectory(initialdir=self.current_dir)
        if folder:
            self.current_dir = folder
            self.folder_label.config(text=folder)
            self._refresh_file_list()

    def _refresh_file_list(self):
        self._all_surface_files = []
        try:
            for dirpath, _dirnames, filenames in os.walk(self.current_dir):
                for fname in filenames:
                    fullpath = os.path.join(dirpath, fname)
                    if fname.endswith(".Surface"):
                        relpath = os.path.relpath(fullpath, self.current_dir)
                        self._all_surface_files.append((relpath, ("disk", fullpath)))
                    elif fname.endswith(".cok"):
                        self._collect_cok_surfaces(fullpath)
        except OSError:
            pass
        self._all_surface_files.sort(key=lambda item: item[0].lower())
        self._apply_file_filter()

    def _collect_cok_surfaces(self, cok_path: str):
        # .cok packages are plain zip archives (Colossal Order mod/asset
        # packages): each entry has a sibling "<name>.cid" file, which is
        # just a stable Colossal asset-database ID, not a content hash, so
        # editing a .Surface entry never invalidates it.
        try:
            with zipfile.ZipFile(cok_path) as zf:
                members = [n for n in zf.namelist() if n.endswith(".Surface")]
        except (OSError, zipfile.BadZipFile):
            return
        cok_relpath = os.path.relpath(cok_path, self.current_dir)
        for member in members:
            relpath = f"{cok_relpath}!{member}"
            self._all_surface_files.append((relpath, ("cok", cok_path, member)))

    def _apply_file_filter(self):
        query = self.search_var.get().strip().lower()
        if query:
            self._filtered_files = [
                item for item in self._all_surface_files if query in item[0].lower()
            ]
        else:
            self._filtered_files = list(self._all_surface_files)

        self.file_listbox.delete(0, tk.END)
        for relpath, _source in self._filtered_files:
            self.file_listbox.insert(tk.END, relpath)
        self.file_count_var.set(f"{len(self._filtered_files)} / {len(self._all_surface_files)} files")

    def _on_file_selected(self, _event):
        sel = self.file_listbox.curselection()
        if not sel:
            return
        relpath, source = self._filtered_files[sel[0]]
        try:
            if source[0] == "disk":
                _, fullpath = source
                self.surface = SurfaceFile.load(fullpath)
            else:
                _, cok_path, member = source
                with zipfile.ZipFile(cok_path) as zf:
                    data = zf.read(member)
                self.surface = SurfaceFile.load_bytes(data)
            self.current_source = source
            self.current_path = relpath
        except Exception as exc:
            messagebox.showerror("Read error", f"Could not read {relpath}:\n{exc}")
            self.surface = None
            self.current_source = None
            self.current_path = None
            return
        self._refresh_lock_ui()
        self._refresh_header()
        self._refresh_properties_table()
        self._refresh_add_combo()
        self._refresh_keywords_list()
        self._refresh_vt_tab()
        self._update_editable_state()

    def _refresh_header(self):
        surf = self.surface
        if surf is None:
            return

        self._material_label_to_id = {
            f"{mid} - {mname}": mid for mid, mname in self.material_id_map.items()
        }
        self.material_combo["values"] = sorted(self._material_label_to_id, key=lambda s: self._material_label_to_id[s])

        name = self.material_id_map.get(surf.material_id, "Unknown")
        current_label = f"{surf.material_id} - {name}"
        self._material_label_to_id.setdefault(current_label, surf.material_id)
        self.material_var.set(current_label)

        self.vt_var.set("yes (header not editable)" if surf.is_vt else "no")

    # ------------------------------------------------------------------
    # Editing lock (read-only by default for files inside .cok archives)
    # ------------------------------------------------------------------

    def _is_editable(self) -> bool:
        if self.surface is None or self.current_source is None:
            return False
        if self.current_source[0] == "disk":
            return True
        return self.unlock_var.get()

    def _require_unlocked(self) -> bool:
        if self._is_editable():
            return True
        messagebox.showinfo(
            "Read-only",
            "This file comes from a .cok archive and is locked. "
            "Check 'Unlock editing (.cok)' first.",
        )
        return False

    def _refresh_lock_ui(self):
        source = self.current_source
        if source is None or source[0] == "disk":
            self.unlock_var.set(True)
            self.unlock_check.config(state="disabled")
            self.lock_status_var.set("")
        else:
            self.unlock_var.set(False)
            self.unlock_check.config(state="normal")
            self.lock_status_var.set(
                "Read-only: this file is inside a .cok archive. Check 'Unlock editing (.cok)' to modify it."
            )

    def _on_unlock_toggled(self):
        if self.unlock_var.get():
            self.lock_status_var.set("Editing unlocked for this .cok entry - changes overwrite the archive on Save.")
        else:
            self.lock_status_var.set(
                "Read-only: this file is inside a .cok archive. Check 'Unlock editing (.cok)' to modify it."
            )
        self._update_editable_state()

    def _update_editable_state(self):
        editable = self._is_editable()
        state_normal = "normal" if editable else "disabled"

        vt_locked = self.surface is not None and self.surface.is_vt
        self.material_combo.config(state="disabled" if (vt_locked or not editable) else "readonly")

        self.apply_value_btn.config(state=state_normal)
        self.remove_prop_btn.config(state=state_normal)
        self.add_prop_combo.config(state=("readonly" if editable else "disabled"))
        self.add_section_combo.config(state=("readonly" if editable else "disabled"))
        self.add_button.config(state=state_normal)

        self.keyword_entry.config(state=state_normal)
        self.add_keyword_btn.config(state=state_normal)
        self.remove_keyword_btn.config(state=state_normal)

        self.save_overwrite_btn.config(state=state_normal)

        is_disk_source = self.current_source is not None and self.current_source[0] == "disk"
        self.remove_vt_btn.config(state=("normal" if (vt_locked and editable and is_disk_source) else "disabled"))

        for w in self.value_frame.winfo_children():
            try:
                w.config(state=state_normal)  # pyright: ignore[reportCallIssue]
            except tk.TclError:
                pass
        for w in self.add_value_frame.winfo_children():
            try:
                w.config(state=state_normal)  # pyright: ignore[reportCallIssue]
            except tk.TclError:
                pass

    # ------------------------------------------------------------------
    # Properties table
    # ------------------------------------------------------------------

    def _format_value(self, section, value):
        if section in ("vectors", "colors"):
            return ", ".join(f"{v:.4f}" for v in value)
        if section == "textures":
            return value if value is not None else "(none)"
        if section == "floats":
            return f"{value:.6f}"
        return str(value)

    def _refresh_properties_table(self):
        self.tree.delete(*self.tree.get_children())
        surf = self.surface
        if surf is not None:
            for section, name, ptype in surf.all_properties():
                value = getattr(surf, section)[name]
                self.tree.insert("", tk.END, iid=f"{section}::{name}",
                                  values=(section, name, ptype, self._format_value(section, value)))
        self._clear_edit_panel()

    def _clear_edit_panel(self):
        self.edit_name_var.set("(no selection)")
        self.edit_section_var.set("")
        for w in self.value_frame.winfo_children():
            w.destroy()
        self.texture_warning.pack_forget()

    def _on_property_selected(self, _event):
        sel = self.tree.selection()
        if not sel or self.surface is None:
            self._clear_edit_panel()
            return
        section, name = sel[0].split("::", 1)
        value = getattr(self.surface, section)[name]
        self.edit_name_var.set(name)
        self.edit_section_var.set(f"section: {section}")

        for w in self.value_frame.winfo_children():
            w.destroy()
        self.texture_warning.pack_forget()

        if section in ("floats", "ints"):
            self.value_vars[0].set(str(value))
            ttk.Entry(self.value_frame, textvariable=self.value_vars[0], width=20).pack(side=tk.LEFT)
        elif section in ("vectors", "colors"):
            labels = ("R", "G", "B", "A") if section == "colors" else ("X", "Y", "Z", "W")
            for i, lbl in enumerate(labels):
                ttk.Label(self.value_frame, text=lbl + ":").pack(side=tk.LEFT)
                self.value_vars[i].set(f"{value[i]:.6f}")
                ttk.Entry(self.value_frame, textvariable=self.value_vars[i], width=10).pack(side=tk.LEFT, padx=(2, 8))
        elif section == "textures":
            self.value_vars[0].set(value or "")
            ttk.Entry(self.value_frame, textvariable=self.value_vars[0], width=40).pack(side=tk.LEFT)
            self.texture_warning.pack(fill=tk.X, pady=(6, 0))

        self._update_editable_state()

    def _apply_property_value(self):
        if not self._require_unlocked():
            return
        surf = self.surface
        sel = self.tree.selection()
        if not sel or surf is None:
            return
        section, name = sel[0].split("::", 1)
        try:
            if section == "floats":
                value = float(self.value_vars[0].get())
            elif section == "ints":
                value = int(self.value_vars[0].get())
            elif section in ("vectors", "colors"):
                value = tuple(float(self.value_vars[i].get()) for i in range(4))
            elif section == "textures":
                text = self.value_vars[0].get().strip()
                value = self._parse_texture_hash(text)
            else:
                return
        except ValueError as exc:
            messagebox.showerror("Invalid value", str(exc))
            return

        surf.set_property(section, name, value)
        self._refresh_properties_table()
        self.status_var.set(f"Property '{name}' updated (not saved to disk).")

    @staticmethod
    def _parse_texture_hash(text: str):
        if text == "":
            return None
        text = text.lower()
        if len(text) != 32 or any(c not in "0123456789abcdef" for c in text):
            raise ValueError("The texture hash must be exactly 32 hexadecimal characters (or empty).")
        return text

    def _remove_selected_property(self):
        if not self._require_unlocked():
            return
        surf = self.surface
        sel = self.tree.selection()
        if not sel or surf is None:
            return
        section, name = sel[0].split("::", 1)
        if not messagebox.askyesno("Remove", f"Remove property '{name}' ({section})?"):
            return
        surf.remove_property(section, name)
        self._refresh_properties_table()
        self._refresh_add_combo()
        self.status_var.set(f"Property '{name}' removed (not saved to disk).")

    # ------------------------------------------------------------------
    # Adding a property
    # ------------------------------------------------------------------

    def _refresh_add_combo(self):
        self.add_prop_combo.set("")
        for w in self.add_value_frame.winfo_children():
            w.destroy()
        surf = self.surface
        if surf is None:
            self.add_prop_combo["values"] = ()
            return

        name = self.material_id_map.get(surf.material_id)
        props = GameMaterialData.get_properties_for_material(self.material_catalog, name) if name else {}
        already_present = {n for _, n, _ in surf.all_properties()}
        available = sorted(n for n in props if n not in already_present)
        self.add_prop_combo["values"] = available
        self._available_props = props

    def _on_add_prop_selected(self, _event):
        name = self.add_prop_var.get()
        prop = self._available_props.get(name)
        for w in self.add_value_frame.winfo_children():
            w.destroy()
        if prop is None:
            return
        ptype = prop["type"]
        default = prop["default"]

        if ptype in ("float", "int"):
            self.add_value_vars[0].set("" if default is None else str(default))
            ttk.Entry(self.add_value_frame, textvariable=self.add_value_vars[0], width=20).pack(side=tk.LEFT)
        elif ptype == "vector4":
            labels = ("R", "G", "B", "A")
            for i, lbl in enumerate(labels):
                ttk.Label(self.add_value_frame, text=lbl + ":").pack(side=tk.LEFT)
                val = default[i] if default else 0.0
                self.add_value_vars[i].set(f"{val:.6f}")
                ttk.Entry(self.add_value_frame, textvariable=self.add_value_vars[i], width=10).pack(
                    side=tk.LEFT, padx=(2, 8)
                )
        elif ptype == "hash128":
            ttk.Label(self.add_value_frame, text="Texture hash (32 hex, empty = none):").pack(side=tk.LEFT)
            self.add_value_vars[0].set("")
            ttk.Entry(self.add_value_frame, textvariable=self.add_value_vars[0], width=40).pack(
                side=tk.LEFT, padx=(4, 0)
            )

        self._update_editable_state()

    def _add_property(self):
        if not self._require_unlocked():
            return
        surf = self.surface
        name = self.add_prop_var.get()
        if not name or surf is None:
            return
        prop = self._available_props.get(name)
        if prop is None:
            return
        ptype = prop["type"]

        try:
            if ptype == "float":
                surf.floats[name] = float(self.add_value_vars[0].get())
            elif ptype == "int":
                surf.ints[name] = int(self.add_value_vars[0].get())
            elif ptype == "vector4":
                value = tuple(float(self.add_value_vars[i].get()) for i in range(4))
                section = self.add_section_var.get()
                getattr(surf, section)[name] = value
            elif ptype == "hash128":
                value = self._parse_texture_hash(self.add_value_vars[0].get().strip())
                surf.textures[name] = value
        except ValueError as exc:
            messagebox.showerror("Invalid value", str(exc))
            return

        self._refresh_properties_table()
        self._refresh_add_combo()
        self.status_var.set(f"Property '{name}' added (not saved to disk).")

    # ------------------------------------------------------------------
    # Material selection
    # ------------------------------------------------------------------

    def _on_material_selected(self, _event):
        surf = self.surface
        if surf is None or surf.is_vt:
            return
        if not self._require_unlocked():
            return
        label = self.material_var.get()
        new_id = self._material_label_to_id.get(label)
        if new_id is None:
            return
        surf.material_id = new_id
        self._refresh_header()
        self._refresh_add_combo()
        self.status_var.set(f"Material updated: {label} (not saved to disk).")

    # ------------------------------------------------------------------
    # Keywords
    # ------------------------------------------------------------------

    def _refresh_keywords_list(self):
        self.keywords_listbox.delete(0, tk.END)
        if self.surface is not None:
            for kw in self.surface.keywords:
                self.keywords_listbox.insert(tk.END, kw)

    def _add_keyword(self):
        if not self._require_unlocked():
            return
        surf = self.surface
        kw = self.new_keyword_var.get().strip()
        if not kw or surf is None:
            return
        if kw not in surf.keywords:
            surf.keywords.append(kw)
            self._refresh_keywords_list()
        self.new_keyword_var.set("")

    def _remove_keyword(self):
        if not self._require_unlocked():
            return
        surf = self.surface
        sel = self.keywords_listbox.curselection()
        if not sel or surf is None:
            return
        kw = self.keywords_listbox.get(sel[0])
        surf.keywords.remove(kw)
        self._refresh_keywords_list()

    # ------------------------------------------------------------------
    # VT header (read-only display + strip VT)
    # ------------------------------------------------------------------

    def _refresh_vt_tab(self):
        self.vt_tree.delete(*self.vt_tree.get_children())
        surf = self.surface

        if surf is None or not surf.is_vt:
            self.vt_info_var.set("This file is not a VT (Virtual Texturing) surface.")
            return

        info = (
            "Read-only: the VT header encodes the baked virtual-texturing atlas layout for this "
            "asset and isn't editable here. Use 'Remove VT...' to strip virtual texturing instead "
            "(this matches the pre-packaging format found in ImportedData)."
        )
        if self.current_source is not None and self.current_source[0] == "cok":
            info += " 'Remove VT...' is disabled for .cok entries - export via 'Save as...' first."
        self.vt_info_var.set(info)

        header = SurfaceFile.parse_vt_header(surf.vt_header_raw)
        self.vt_tree.insert("", tk.END, values=("Number of VT stacks", header["nb_vt_stacks"]))
        self.vt_tree.insert(
            "", tk.END, values=("Linked VTSurfaceAsset ID", surf.vt_surface_hash or "(none)")
        )
        for i, stack in enumerate(header["stacks"]):
            x, y = stack["atlas_size"]
            self.vt_tree.insert("", tk.END, values=(f"Stack {i} atlas size", f"{x} x {y}"))
            for layer, guid in enumerate(stack["texture_guids"]):
                self.vt_tree.insert(
                    "", tk.END, values=(f"Stack {i} texture GUID layer {layer}", guid or "(none)")
                )
            for layer, guid in enumerate(stack["vt_texture_guids"]):
                self.vt_tree.insert(
                    "", tk.END, values=(f"Stack {i} VT texture GUID layer {layer}", guid or "(none)")
                )

    def _remove_vt(self):
        if self.current_source is not None and self.current_source[0] == "cok":
            messagebox.showinfo(
                "Not available for .cok entries",
                "Removing VT is only available for .Surface files on disk, not for entries "
                "inside a .cok package. Use 'Save as...' to export this file to disk first.",
            )
            return
        if not self._require_unlocked():
            return
        surf = self.surface
        if surf is None or not surf.is_vt:
            return
        if not messagebox.askyesno(
            "Remove VT",
            "Remove Virtual Texturing from this surface?\n\n"
            "This clears the VT header and its linked VTSurfaceAsset. The existing "
            "float/int/vector/color/texture properties and keywords are kept as-is - "
            "this only strips the VT wrapper, matching the format used before "
            "packaging (see ImportedData). It does not re-bake or undo any atlasing.",
        ):
            return
        surf.is_vt = 0
        surf.vt_header_raw = b""
        surf.vt_surface_hash = None
        self._refresh_header()
        self._refresh_vt_tab()
        self._update_editable_state()
        self.status_var.set("VT removed from this surface (not saved to disk).")

    # ------------------------------------------------------------------
    # Saving
    # ------------------------------------------------------------------

    def _save_overwrite(self):
        if self.surface is None or self.current_source is None:
            return
        if not self._require_unlocked():
            return
        if self.current_source[0] == "disk":
            self._save_overwrite_disk()
        else:
            self._save_overwrite_cok()

    def _save_overwrite_disk(self):
        surf = self.surface
        source = self.current_source
        if surf is None or source is None or source[0] != "disk":
            return
        _, fullpath = source
        if not messagebox.askyesno(
            "Save",
            f"Overwrite the original file?\n{fullpath}\n\n"
            "A backup copy (.bak) will be created if it doesn't already exist.",
        ):
            return
        backup_path = fullpath + ".bak"
        if not os.path.isfile(backup_path):
            with open(fullpath, "rb") as src, open(backup_path, "wb") as dst:
                dst.write(src.read())
        try:
            surf.save(fullpath)
        except Exception as exc:
            messagebox.showerror("Write error", str(exc))
            return
        self.status_var.set(f"Saved: {fullpath} (backup: {os.path.basename(backup_path)})")

    def _save_overwrite_cok(self):
        surf = self.surface
        source = self.current_source
        if surf is None or source is None or source[0] != "cok":
            return
        _, cok_path, member = source
        if not messagebox.askyesno(
            "Save",
            f"Overwrite '{member}' inside the archive?\n{cok_path}\n\n"
            "A backup copy of the whole archive (.cok.bak) will be created if it doesn't already exist. "
            "The '.cid' companion entry is left untouched (it's a stable asset ID, not a content hash).",
        ):
            return
        backup_path = cok_path + ".bak"
        if not os.path.isfile(backup_path):
            with open(cok_path, "rb") as src, open(backup_path, "wb") as dst:
                dst.write(src.read())
        try:
            self._replace_zip_member(cok_path, member, surf.to_bytes())
        except Exception as exc:
            messagebox.showerror("Write error", str(exc))
            return
        self.status_var.set(
            f"Saved: {member} inside {os.path.basename(cok_path)} "
            f"(backup: {os.path.basename(backup_path)})"
        )

    @staticmethod
    def _replace_zip_member(zip_path: str, member: str, new_data: bytes) -> None:
        """Rewrites a whole zip archive with one member's content replaced.

        zipfile has no in-place edit: every other member is copied through
        unchanged (same ZipInfo metadata, so compression/timestamps/etc are
        preserved), and the target member gets the new bytes.
        """
        tmp_fd, tmp_path = tempfile.mkstemp(suffix=".cok.tmp", dir=os.path.dirname(zip_path) or ".")
        os.close(tmp_fd)
        try:
            with zipfile.ZipFile(zip_path, "r") as src_zf, \
                 zipfile.ZipFile(tmp_path, "w", zipfile.ZIP_DEFLATED) as dst_zf:
                for info in src_zf.infolist():
                    if info.filename == member:
                        dst_zf.writestr(info, new_data)
                    else:
                        dst_zf.writestr(info, src_zf.read(info.filename))
            os.replace(tmp_path, zip_path)
        except Exception:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)
            raise

    def _save_as(self):
        surf = self.surface
        if surf is None:
            return
        default_name = "new.Surface"
        if self.current_source is not None:
            if self.current_source[0] == "disk":
                default_name = os.path.basename(self.current_source[1])
            else:
                default_name = os.path.basename(self.current_source[2])
        path = filedialog.asksaveasfilename(
            initialdir=self.current_dir,
            initialfile=default_name,
            defaultextension=".Surface",
            filetypes=[(".Surface", "*.Surface")],
        )
        if not path:
            return
        try:
            surf.save(path)
        except Exception as exc:
            messagebox.showerror("Write error", str(exc))
            return
        self.status_var.set(f"Saved as: {path}")


def main():
    root = tk.Tk()
    SurfaceEditorApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
