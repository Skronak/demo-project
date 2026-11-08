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
public class SaveData
{
    public int version = 1;
    public int width;
    public int height;
    public float cellSize;
    public List<PlacedObjectData> objects = new List<PlacedObjectData>();
}

/// <summary>
/// Sauvegarde / chargement en JSON. On ne stocke que les dimensions et la liste des objets :
/// la grille d'occupation se reconstruit au chargement.
/// </summary>
public static class SaveSystem
{
    public static string DefaultPath => Path.Combine(Application.persistentDataPath, "grid_save.json");

    public static SaveData CreateSaveData(GridData grid)
    {
        var data = new SaveData
        {
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

    public static void Save(GridData grid, string path = null)
    {
        path = string.IsNullOrEmpty(path) ? DefaultPath : path;
        string json = JsonUtility.ToJson(CreateSaveData(grid), true);
        File.WriteAllText(path, json);
        Debug.Log($"Grille sauvegardée : {path} ({grid.Count} objets)");
    }

    public static bool TryLoad(out SaveData data, string path = null)
    {
        data = null;
        path = string.IsNullOrEmpty(path) ? DefaultPath : path;
        if (!File.Exists(path)) return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            return data != null && data.width > 0 && data.height > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Lecture de la sauvegarde impossible : {ex.Message}");
            return false;
        }
    }
}
