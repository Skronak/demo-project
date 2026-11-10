using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Affiche la grille avec seulement 2 objets : un mesh de lignes unique et un quad de sol.
/// Coût : O(largeur + hauteur) sommets, pas de GameObject par case.
/// </summary>
public class GridView : MonoBehaviour
{
    [Header("Lignes")]
    [SerializeField] Color lineColor = new Color(1f, 1f, 1f, 0.25f);
    [Tooltip("Optionnel. Sinon un matériau unlit transparent est créé automatiquement.")]
    [SerializeField] Material lineMaterial;

    [Header("Sol")]
    [SerializeField] bool showGround = true;
    [SerializeField] Color groundColor = new Color(0.18f, 0.20f, 0.22f, 1f);
    [Tooltip("Optionnel. Sinon un matériau unlit est créé automatiquement.")]
    [SerializeField] Material groundMaterial;

    [Header("Visibilité")]
    [SerializeField] bool startVisible = true;
    [Tooltip("Si coché, le sol est masqué en même temps que les lignes. Sinon seules les lignes disparaissent.")]
    [SerializeField] bool hideGroundWithGrid = false;

    Mesh lineMesh;
    GameObject linesObject;
    GameObject groundObject;
    bool visible = true;
    bool initialized;

    public bool IsVisible => visible;

    public void Build(GridData grid)
    {
        if (!initialized)
        {
            visible = startVisible;
            initialized = true;
        }

        BuildLines(grid);
        BuildGround(grid);
        ApplyVisibility();
    }

    public void SetVisible(bool value)
    {
        visible = value;
        ApplyVisibility();
    }

    public void Toggle() => SetVisible(!visible);

    // Seuls les objets de la grille (lignes, sol) sont affectés :
    // les objets posés ne sont pas enfants de ce composant et restent toujours visibles.
    void ApplyVisibility()
    {
        if (linesObject != null) linesObject.SetActive(visible);
        if (groundObject != null) groundObject.SetActive(showGround && (visible || !hideGroundWithGrid));
    }

    void BuildLines(GridData grid)
    {
        if (linesObject == null)
        {
            linesObject = new GameObject("GridLines");
            linesObject.transform.SetParent(transform, false);
            lineMesh = new Mesh { name = "GridLinesMesh", indexFormat = IndexFormat.UInt32 };
            linesObject.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            var mr = linesObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lineMaterial != null ? lineMaterial : GridMaterials.CreateUnlit(Color.white, 3001);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        int w = grid.Width, h = grid.Height;
        float cs = grid.CellSize;
        float totalW = w * cs, totalH = h * cs;
        const float y = 0.02f; // léger décalage pour éviter le z-fighting avec le sol

        int vertexCount = ((w + 1) + (h + 1)) * 2;
        var verts = new Vector3[vertexCount];
        var cols = new Color[vertexCount];
        var idx = new int[vertexCount];

        int k = 0;
        for (int i = 0; i <= w; i++)
        {
            verts[k++] = grid.Origin + new Vector3(i * cs, y, 0f);
            verts[k++] = grid.Origin + new Vector3(i * cs, y, totalH);
        }
        for (int j = 0; j <= h; j++)
        {
            verts[k++] = grid.Origin + new Vector3(0f, y, j * cs);
            verts[k++] = grid.Origin + new Vector3(totalW, y, j * cs);
        }
        for (int i = 0; i < vertexCount; i++)
        {
            idx[i] = i;
            cols[i] = lineColor;
        }

        lineMesh.Clear();
        lineMesh.vertices = verts;
        lineMesh.colors = cols;
        lineMesh.SetIndices(idx, MeshTopology.Lines, 0);
        lineMesh.RecalculateBounds();
    }

    void BuildGround(GridData grid)
    {
        if (groundObject == null)
        {
            groundObject = new GameObject("GridGround");
            groundObject.transform.SetParent(transform, false);
            groundObject.AddComponent<MeshFilter>().sharedMesh = GridMaterials.UnitQuad();
            var mr = groundObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = groundMaterial != null ? groundMaterial : GridMaterials.CreateUnlit(groundColor, 3000);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        groundObject.transform.position = grid.Origin;
        groundObject.transform.localScale = new Vector3(grid.Width * grid.CellSize, 1f, grid.Height * grid.CellSize);
    }
}

/// <summary>Petits utilitaires de mesh / matériaux partagés.</summary>
public static class GridMaterials
{
    /// <summary>Matériau unlit transparent (Sprites/Default, toujours inclus dans les builds).</summary>
    public static Material CreateUnlit(Color color, int renderQueue)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = color;
        m.renderQueue = renderQueue;
        return m;
    }

    /// <summary>Quad 1x1 posé à plat sur XZ, coin en (0,0,0), normale vers le haut.</summary>
    public static Mesh UnitQuad()
    {
        var mesh = new Mesh { name = "UnitQuadXZ" };
        mesh.vertices = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(0, 0, 1),
            new Vector3(1, 0, 1),
            new Vector3(1, 0, 0)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.RecalculateBounds();
        return mesh;
    }
}
