using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public struct PlacedObjectData
{
    public string typeId;
    public int x, y;
    public int rotation; // 0..3
}

[Serializable]
public class LayerSaveData
{
    public string name;
    public int width;
    public int height;
    public float cellSize;
    public List<PlacedObjectData> objects = new List<PlacedObjectData>();
}

[Serializable]
public class SaveData
{
    public int version = 2;
    public int activeLayer;
    public List<LayerSaveData> layers = new List<LayerSaveData>();

    // Champs de l'ancien format (version 1 : une seule grille). Ils ne sont plus écrits,
    // mais restent lus pour charger les anciennes sauvegardes dans la première couche.
    public int width;
    public int height;
    public float cellSize;
    public List<PlacedObjectData> objects = new List<PlacedObjectData>();
}

/// <summary>
/// Sauvegarde / chargement en JSON. On ne stocke que, pour chaque couche, les dimensions
/// et la liste des objets : la grille d'occupation se reconstruit au chargement.
/// </summary>
public static class SaveSystem
{
    public static string DefaultPath => Path.Combine(Application.persistentDataPath, "grid_save.json");

    public static LayerSaveData CreateLayerData(GridData grid, string name)
    {
        var data = new LayerSaveData
        {
            name = name,
            width = grid.Width,
            height = grid.Height,
            cellSize = grid.CellSize
        };

        foreach (var e in grid.Entries)
        {
            data.objects.Add(new PlacedObjectData
            {
                typeId = e.TypeId,
                x = e.X,
                y = e.Y,
                rotation = e.Rotation
            });
        }
        return data;
    }

    public static void Save(SaveData data, string path = null)
    {
        path = string.IsNullOrEmpty(path) ? DefaultPath : path;
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log($"Sauvegarde : {path} ({data.layers.Count} couche(s))");
    }

    public static bool TryLoad(out SaveData data, string path = null)
    {
        data = null;
        path = string.IsNullOrEmpty(path) ? DefaultPath : path;
        if (!File.Exists(path)) return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogError($"Lecture de la sauvegarde impossible : {ex.Message}");
            return false;
        }

        if (data == null) return false;

        // Ancien format : une seule grille -> première couche.
        if ((data.layers == null || data.layers.Count == 0) && data.width > 0 && data.height > 0)
        {
            data.layers = new List<LayerSaveData>
            {
                new LayerSaveData
                {
                    name = "Couche 1",
                    width = data.width,
                    height = data.height,
                    cellSize = data.cellSize,
                    objects = data.objects ?? new List<PlacedObjectData>()
                }
            };
            data.activeLayer = 0;
        }

        return data.layers != null && data.layers.Count > 0;
    }
}