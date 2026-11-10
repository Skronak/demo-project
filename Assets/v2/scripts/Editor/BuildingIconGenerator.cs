#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Génère une miniature (PNG + Sprite) pour chaque PlaceableData qui a un prefab,
/// et l'assigne au champ « icon ».
///
/// IMPORTANT : ce fichier doit se trouver dans un dossier nommé "Editor" (ex. Assets/Editor/).
/// Menu : Building > Generate Icons
/// Les PlaceableData dont l'icône a été assignée à la main (hors dossier Assets/Building/Icons) sont ignorés.
/// </summary>
public static class BuildingIconGenerator
{
    const string IconDir = "Assets/Building/Icons";

    [MenuItem("Building/Generate Icons")]
    public static void Run()
    {
        var pending = new List<PlaceableData>();
        foreach (string guid in AssetDatabase.FindAssets("t:PlaceableData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<PlaceableData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data.prefab == null) continue;

            // Ne pas écraser une icône choisie à la main.
            if (data.icon != null && !AssetDatabase.GetAssetPath(data.icon).StartsWith(IconDir)) continue;

            pending.Add(data);
        }

        if (pending.Count == 0)
        {
            Debug.Log("Génération des icônes : aucun PlaceableData avec prefab à traiter.");
            return;
        }

        EnsureFolder("Assets", "Building");
        EnsureFolder("Assets/Building", "Icons");

        int total = pending.Count;
        int frames = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            frames++;

            // Unity calcule les aperçus en arrière-plan : on réessaie à chaque tick.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(pending[i].prefab);
                if (preview == null) continue;

                SaveIcon(pending[i], preview);
                pending.RemoveAt(i);
            }

            if (pending.Count == 0 || frames > 900)
            {
                EditorApplication.update -= tick;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Icônes générées : {total - pending.Count}/{total}" +
                          (pending.Count > 0 ? " (relancez le menu pour les manquantes)" : ""));
            }
        };
        EditorApplication.update += tick;
    }

    static void SaveIcon(PlaceableData data, Texture source)
    {
        Texture2D readable = ToReadable(source);
        string path = $"{IconDir}/{data.name}.png";
        File.WriteAllBytes(path, readable.EncodeToPNG());
        Object.DestroyImmediate(readable);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        data.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        EditorUtility.SetDirty(data);
    }

    // Les aperçus ne sont pas toujours lisibles côté CPU : on passe par une RenderTexture.
    static Texture2D ToReadable(Texture source)
    {
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
