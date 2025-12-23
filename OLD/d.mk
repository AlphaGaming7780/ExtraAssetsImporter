# [NEW] Convert to Editor

# Converting an EAI Asset Pack into a Standalone Asset Pack

This page explains how to convert an **EAI Asset Pack mod** into a **standalone Asset Pack** that can be published using the **editor**.

> [!NOTE]
> This only works for assets created with the **new importers**.  
> Assets made with the old importers are **not compatible**.
>
> Any wiki page prefixed with **[NEW]** refers to the new importers.

> [!IMPORTANT]
> This feature is still in **beta**. Use it at your own risk, make backups of your data, and avoid impacting other users.

> [!WARNING]
> Standalone Asset Pack mods **do not support ExtraAssetMenu** for now.  
> You must manually place your objects into a base game menu or use **Find It**.

---

## How to do it

For this tutorial, I will use my mod **EAI - Surface Pack** as an example.

---

## 1. Create or locate the `_AssetPacks` folder

Locate (or create) the `_AssetPacks` folder inside the **ExtraAssetImporter `ModsData` directory**.

The full path should look like this:
```
C:\Users%USERNAME%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\ExtraAssetsImporter\_AssetPacks
```

---

## 2. Move your assets into `_AssetPacks`

Inside the `_AssetPacks` folder, create a folder named after your asset pack.  
This folder name **defines the name of your pack**.

It must contain the default folder structure required by the **new importers**, for example:
```
_AssetPacks
  └─ EAI - Surface Pack ← Pack name
     ├─ Surfaces
     ├─ Decals
     └─ ...
```

In my case, I copied the `CustomAssets` folder from my original EAI Asset Pack mod into `_AssetPacks`, then renamed it to: `EAI - Surface Pack`

Final result:

<img width="918" height="195" alt="image" src="https://github.com/user-attachments/assets/4929229f-6834-480c-b308-61a50b03b114" />

---

## 3. (Recommended) Add an `AssetPack.json`

For a smoother experience, it is **highly recommended** to add an `AssetPack.json` file to your pack.

This file marks all assets as belonging to an **Asset Pack** in-game, improving clarity and consistency.

---

## 4. Load the game

You can now start the game.

During loading, you will notice:
- First, **EAI loads all assets**
- Then, a **second loading phase** occurs
- This second phase loads **one Asset Pack at a time**

> [!NOTE]
> The loading process uses the **EAI asset database system**.
>
> EAI stores a hash of:
> - Input files
> - Generated output files
>
> This avoids regenerating unchanged assets.
>
> ⚠️ If you modify an asset **inside the editor**, EAI will detect it as corrupted and **regenerate it**, causing you to lose your editor changes.

<img width="548" height="168" alt="image" src="https://github.com/user-attachments/assets/832bd055-c405-4b1c-ac81-9f11354fe731" />

---

## 5. Pack and publish

That’s it 🎉  
Your asset pack is now recognized as an **Editor Asset Pack**.

Final steps:
1. Open the **editor**
2. Pack the assets
3. Publish the Asset Pack

Done.

