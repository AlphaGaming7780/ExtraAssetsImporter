import os
import shutil
from PIL import Image, ImageChops

# 📁 Dossiers
base_path = ".Inputs"
output_path = ".Outputs"
os.makedirs(output_path, exist_ok=True)

# 🔍 Mots-clés pour détecter les textures par rôle
texture_keywords = {
    "basecolor": "BaseColor",
    "gloss": "Gloss",
    "roughness": "Roughness",
    "metallic": "Metallic",
    "coat": "Coat",
    "normal": "Normal"
}

# 🔎 Recherche automatique des fichiers
for filename in os.listdir(base_path):
    lowered = filename.lower()
    for key in texture_keywords:
        if key in lowered and filename.lower().endswith((".jpg", ".png")):
            texture_keywords[key] = os.path.join(base_path, filename)

# 📏 Détecter la taille depuis une texture existante
default_size = (1024, 1024)
for path in texture_keywords.values():
    if isinstance(path, str) and os.path.exists(path):
        default_size = Image.open(path).size
        break

# 🧱 Charge une image (ou noir si absente)
def load_or_black(path, size):
    if path and isinstance(path, str) and os.path.exists(path):
        return Image.open(path).convert("L").resize(size)
    else:
        return Image.new("L", size, color=0)

# 🔄 Inverser une image en niveaux de gris
def invert_grayscale(image):
    return ImageChops.invert(image)

# ---------- 🎨 BaseColorMap ----------
if isinstance(texture_keywords["basecolor"], str):
    src = texture_keywords["basecolor"]
    dst = os.path.join(output_path, "_BaseColorMap.png")
    shutil.copyfile(src, dst)
    print("✅ BaseColorMap copiée → _BaseColorMap.png")
else:
    print("❌ BaseColorMap non trouvée.")

# ---------- NormalMap ----------

def convert_normal_to_opengl(image):
    """Inverse le canal vert pour conversion DirectX → OpenGL."""
    r, g, b = image.split()
    g_inv = ImageChops.invert(g)
    return Image.merge("RGB", (r, g_inv, b))

if isinstance(texture_keywords["normal"], str):
    src = texture_keywords["normal"]
    dst = os.path.join(output_path, "_NormalMap.png")

    img = Image.open(src).convert("RGB").resize(default_size)

    convert_normal_to_opengl(img).save(dst)

    # shutil.copyfile(src, dst)
    print("✅ NormalMap copiée → _NormalMap.png")
else:
    print("❌ NormalMap non trouvée.")

# ---------- 🎨 MaskMap ----------
metallic = load_or_black(texture_keywords["metallic"], default_size)
coat = load_or_black(texture_keywords["coat"], default_size)
black = Image.new("L", default_size, color=0)

# 🧠 PRÉFÉRENCE : Gloss > Roughness > Noir
if isinstance(texture_keywords["gloss"], str):
    gloss = load_or_black(texture_keywords["gloss"], default_size)
    print("✅ Gloss utilisée pour alpha (MaskMap).")
elif isinstance(texture_keywords["roughness"], str):
    rough = load_or_black(texture_keywords["roughness"], default_size)
    gloss = invert_grayscale(rough)
    print("🔁 Roughness détectée et inversée pour alpha (MaskMap).")
else:
    gloss = black
    print("⚠️ Aucune Gloss ni Roughness trouvée — canal alpha noir.")

maskmap = Image.merge("RGBA", (metallic, coat, black, gloss))
maskmap.save(os.path.join(output_path, "_MaskMap.png"))
print("✅ MaskMap générée → _MaskMap.png")
