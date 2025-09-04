import os
import struct
from time import sleep

# AllowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_"
AllowedChars = "abcdefghijklmnopqrstuvwxyz_"

properties = {
    "_Metallic": "float",
    "_Smoothness": "float",
    "colossal_DecalLayerMask": "float",
    "_DrawOrder": "float",
    "_MetallicOpacity": "float",
    "_NormalOpacity": "float",

    "_BaseColor": "vector4",
    "colossal_MeshSize": "vector4",
    "colossal_TextureArea": "vector4",

    "_BaseColorMap": "hash128",
    "_NormalMap": "hash128",
    "_MaskMap": "hash128",
}

#See https://github.com/AlphaGaming7780/ExtraAssetsImporter/wiki/Game-Materials
MaterialNames = [
    "Default",
    "DefaultCutout",
    "Windows",
    "VegRoot",
    "VegLeaves",
    "VegImpostor",
    "CharacterBody",
    "CharacterDeformable",
    "CharacterEye",
    "CharacterHead",
    "CharacterMouth",
    "DefaultGlassDoubleSided",
    "Grass",
    "DefaultDoubleSided",
    "DefaultCutoutDoubleSided",
    "DefaultGlass",
    "Curved",
    "NetCompositionMeshLit",
    "DefaultDecal",
    "CurvedDecalDeterioration",
    "CurvedDecal",
    "CurvedOverlay",
    "DefaultPipeline",
    "CurvedPipeline",
    "Base",
    "HairApproximateDyed",
    "CharacterGeneric",
    "CharacterGenericTransparent",
    "WaterGeneric",
    "WaterStream",
    "CurvedTransparent",
    "CurvedCutout",
    "CurvedDecalColorOnly",
]

def read_short(data, offset) -> int:
    return struct.unpack_from("<h", data, offset)[0]

def read_ushort(data, offset) -> int:
    return struct.unpack_from("<H", data, offset)[0]

def read_uint(data, offset) -> int:
    return struct.unpack_from("<I", data, offset)[0]

def read_int(data, offset) -> int:
    return struct.unpack_from("<i", data, offset)[0]

# Function to read a float (4 bytes)
def read_float(data, offset) -> float:
    return struct.unpack_from("<f", data, offset)[0]

# Function to read a Vector4 (4 floats, 16 bytes)
def read_vector4(data, offset) -> tuple[float, float, float, float]:
    return struct.unpack_from("<4f", data, offset)

# Function to read a 128-bit hash (16 bytes)
def read_hash128(data : bytes, offset) -> bytearray:
    result : bytearray = bytearray()
    for o in range(0, 16):
        x = data[offset + o]
        x = (((x & 0xF) << 4) | ( (x & 0xF0)  >> 4)) & 0xFF
        result.append(x)
    return result.hex()

def get_files_with_extension(directory, extension) -> list[str]:
    return [file for file in os.listdir(directory) if file.endswith(extension)]

def extract_strings_with_offsets(data):
    pos = 0
    results = {}

    while pos < len(data):
        length = data[pos]
        pos += 1

        # Verify length is reasonable 
        if 1 <= length < 256 and pos + length <= len(data):

            fistChar = data[pos]

            if(not AllowedChars.count(chr(fistChar)) > 0 ) : #
                # print(f"Skipping suspicious string at pos {pos} with length {length} and first char {chr(fistChar)}")
                continue

            text_bytes = data[pos:pos+length]
            try:
                text = text_bytes.decode("utf-8")
                results[text] = pos # Saving the offset of the string start
                pos += length
                continue
            except UnicodeDecodeError:
                # No text, skip
                pass

    return results


current_path = os.path.abspath(__file__)
current_path = os.path.dirname(current_path)
files : list[str] = get_files_with_extension(current_path, ".Surface")

for file_path in files:
    file_path = os.path.join(current_path, file_path)
    print(f"\nProcessing file: {file_path}")

    with open(file_path, "rb") as f:
        data : bytes = f.read()

        preview = []
        for b in data:
            c = chr(b)
            if AllowedChars.count(c) > 0:
                preview.append(c)
            else:
                preview.append(f"[{b:02X}]")

        preview_str = "".join(preview)
        print(f"File preview ({len(data)} bytes):{preview_str}\n")

        results = {}

        strings = extract_strings_with_offsets(data)

        print("Extracted strings and their offsets:")
        for k, v in strings.items():
            print(f"{k} { "(Supported)" if properties.__contains__(k) else "(Not Supported)" } -> offset {v}")
        
        print("\nExtracting properties:")

        materialID = read_short(data, 2)
        print(f"Material ID: {materialID} ({MaterialNames[materialID] if materialID < len(MaterialNames) else 'Unknown'})")

        for name, ptype in properties.items():

            if( not strings.__contains__(name)):
                continue

            idx = strings[name]

            results[name] = None

            if idx == -1:
                print(name, "→ not found")
                continue

            start = idx + len(name)

            if ptype == "float":
                results[name] = read_float(data, start)
            elif ptype == "vector4":
                results[name] = read_vector4(data, start)
            elif ptype == "hash128":
                results[name] = read_hash128(data, start+1)

            print(f"{name} ({ptype}) →", results[name])

sleep(99999)