using System.Collections.Generic;
using UnityEngine;

/// <summary>Un objet posé sur la grille (données pures, aucune référence Unity lourde).</summary>
public class PlacedEntry
{
    public int Id;
    public string TypeId;
    public int X, Y;          // coin (min x, min y) de l'empreinte
    public int Rotation;      // 0..3 (x 90°)
    public int SizeX, SizeY;  // empreinte APRÈS rotation
}

/// <summary>
/// Modèle de la grille : un simple tableau 1D d'entiers (0 = libre, sinon id de l'objet).
/// Aucune dépendance à un GameObject -> très léger et facile à tester.
/// Convention : la case (x, y) correspond au monde (x * cellSize, y * cellSize) sur les axes X et Z.
/// </summary>
public class GridData
{
    public readonly int Width;
    public readonly int Height;
    public readonly float CellSize;
    public readonly Vector3 Origin;

    readonly int[] cells;
    readonly Dictionary<int, PlacedEntry> entries = new Dictionary<int, PlacedEntry>();
    int nextId = 1;

    public GridData(int width, int height, float cellSize, Vector3 origin = default)
    {
        Width = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        CellSize = Mathf.Max(0.01f, cellSize);
        Origin = origin;
        cells = new int[Width * Height];
    }

    public int Count => entries.Count;
    public IEnumerable<PlacedEntry> Entries => entries.Values;

    /// <summary>Centre de la grille dans le monde.</summary>
    public Vector3 Center => Origin + new Vector3(Width * CellSize * 0.5f, 0f, Height * CellSize * 0.5f);

    // ---------- Utilitaires ----------

    /// <summary>Empreinte (largeur, profondeur) d'un objet selon sa rotation.</summary>
    public static Vector2Int Footprint(Vector2Int size, int rotation)
    {
        return (rotation & 1) == 0 ? size : new Vector2Int(size.y, size.x);
    }

    public static int NormalizeRotation(int rotation) => ((rotation % 4) + 4) % 4;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Convertit un point monde en case. Retourne false si hors grille.</summary>
    public bool WorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
        y = Mathf.FloorToInt((world.z - Origin.z) / CellSize);
        return InBounds(x, y);
    }

    /// <summary>Coin (min) de la case dans le monde.</summary>
    public Vector3 CellToWorld(int x, int y) =>
        Origin + new Vector3(x * CellSize, 0f, y * CellSize);

    /// <summary>Centre d'une empreinte de sx*sy cases dont le coin est (x, y).</summary>
    public Vector3 FootprintCenter(int x, int y, int sx, int sy) =>
        Origin + new Vector3((x + sx * 0.5f) * CellSize, 0f, (y + sy * 0.5f) * CellSize);

    // ---------- Requêtes ----------

    public int GetIdAt(int x, int y) => InBounds(x, y) ? cells[y * Width + x] : 0;

    public PlacedEntry Get(int id)
    {
        entries.TryGetValue(id, out var e);
        return e;
    }

    /// <summary>
    /// Vérifie que l'empreinte est dans la grille et libre.
    /// ignoreId : id d'un objet à ignorer (utile pour le déplacer sur ses propres cases).
    /// </summary>
    public bool CanPlace(int x, int y, int sx, int sy, int ignoreId = 0)
    {
        if (x < 0 || y < 0 || x + sx > Width || y + sy > Height) return false;
        for (int j = y; j < y + sy; j++)
        {
            int row = j * Width;
            for (int i = x; i < x + sx; i++)
            {
                int v = cells[row + i];
                if (v != 0 && v != ignoreId) return false;
            }
        }
        return true;
    }

    // ---------- Modifications ----------

    /// <summary>Place un objet. Retourne son id, ou 0 si impossible.</summary>
    public int Place(string typeId, int x, int y, int rotation, int sx, int sy)
    {
        if (!CanPlace(x, y, sx, sy)) return 0;

        var e = new PlacedEntry
        {
            Id = nextId++,
            TypeId = typeId,
            X = x,
            Y = y,
            Rotation = NormalizeRotation(rotation),
            SizeX = sx,
            SizeY = sy
        };
        entries.Add(e.Id, e);
        Fill(e, e.Id);
        return e.Id;
    }

    public bool Remove(int id)
    {
        if (!entries.TryGetValue(id, out var e)) return false;
        Fill(e, 0);
        entries.Remove(id);
        return true;
    }

    /// <summary>Déplace (et/ou fait pivoter) un objet existant. Retourne false si la destination est invalide.</summary>
    public bool Move(int id, int x, int y, int rotation, int sx, int sy)
    {
        if (!entries.TryGetValue(id, out var e)) return false;
        if (!CanPlace(x, y, sx, sy, id)) return false;

        Fill(e, 0);
        e.X = x;
        e.Y = y;
        e.Rotation = NormalizeRotation(rotation);
        e.SizeX = sx;
        e.SizeY = sy;
        Fill(e, id);
        return true;
    }

    void Fill(PlacedEntry e, int value)
    {
        for (int j = e.Y; j < e.Y + e.SizeY; j++)
        {
            int row = j * Width;
            for (int i = e.X; i < e.X + e.SizeX; i++)
                cells[row + i] = value;
        }
    }
}
