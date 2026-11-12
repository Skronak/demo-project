using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sol décoratif : une grande bande qui commence exactement au bord de la grille (côté « sol »)
/// et s'étend loin vers l'extérieur et sur les côtés, pour rester visible quand on zoome ou déplace la caméra.
/// Il ne recouvre jamais la grille (sauf si « Edge Offset » est positif).
///
/// Le côté (première ou dernière ligne) suit le réglage « Ground Side » de BuildSystem.
/// Utilisation : ajouter ce composant à un objet vide placé en (0, 0, 0). BuildSystem le trouve tout seul.
/// Pensez à décocher « Show Ground » sur GridView pour ne pas dessiner deux sols.
/// </summary>
public class GroundView : MonoBehaviour
{
    [Header("Sol")]
    [SerializeField] Color groundColor = new Color(0.45f, 0.30f, 0.18f, 1f);
    [Tooltip("Optionnel. Mettre « Wrap Mode » sur Repeat dans les réglages d'import de la texture.")]
    [SerializeField] Texture2D groundTexture;
    [Tooltip("Taille (en unités monde) d'une répétition de la texture.")]
    [SerializeField] float textureTileSize = 2f;

    [Header("Bande de surface (herbe)")]
    [SerializeField] bool showEdgeStrip = true;
    [SerializeField] Color edgeColor = new Color(0.30f, 0.65f, 0.25f, 1f);
    [Tooltip("Épaisseur de la bande, en unités monde.")]
    [SerializeField] float edgeThickness = 0.3f;

    [Header("Rue vue de profil (remplace la bande d'herbe)")]
    [Tooltip("Optionnel. Image de rue vue de profil, répétée le long du bord de la grille. " +
             "Import conseillé : Wrap Mode Repeat, Alpha Is Transparency, Filter Mode Point pour du pixel art. " +
             "Les bords gauche et droit de l'image doivent se raccorder pour que la répétition soit invisible.")]
    [SerializeField] Texture2D streetTexture;
    [Tooltip("Largeur (unités monde) d'une répétition de l'image. La hauteur de la rue est déduite du ratio de l'image.")]
    [SerializeField] float streetTileWidth = 4f;
    [Tooltip("À cocher si la rue apparaît inversée gauche/droite (c'est le cas avec une caméra à yaw 180).")]
    [SerializeField] bool streetFlipHorizontal = true;
    [Tooltip("À cocher si la rue apparaît à l'envers (haut/bas).")]
    [SerializeField] bool streetFlipVertical = false;

    [Header("Dimensions")]
    [Tooltip("Étendue du sol au-delà de la grille (côtés et profondeur), en unités monde. " +
             "0 = automatique, d'après le zoom maximum de la caméra.")]
    [SerializeField] float extent = 0f;
    [Tooltip("Fait remonter le bord du sol dans la grille de cette distance (unités monde). " +
             "0 = le sol s'arrête exactement au bord de la grille.")]
    [SerializeField] float edgeOffset = 0f;
    [SerializeField] float height = 0f;

    GameObject bodyObject;
    GameObject stripObject;
    Mesh bodyMesh;
    Mesh stripMesh;
    Material bodyMaterial;
    Material stripMaterial;
    GridData lastGrid;
    float lastExtentScale = 1f;
    IsometricCameraController rig;

    /// <summary>(Re)construit le sol pour la grille donnée. Appelé par BuildSystem.</summary>
    public void Build(GridData grid, float extentScale = 1f)
    {
        lastGrid = grid;
        lastExtentScale = extentScale;

        // La géométrie est construite en coordonnées locales : l'objet (une couche) peut être déplacé
        // et mis à l'échelle. extentScale agrandit le sol pour compenser la réduction des couches lointaines.
        float e = (extent > 0f ? extent : AutoExtent()) * extentScale;
        float x0 = grid.Origin.x - e;
        float x1 = grid.Origin.x + grid.Width * grid.CellSize + e;
        float y = grid.Origin.y + height;

        // Avec une texture de rue, l'épaisseur de la bande suit le ratio de l'image.
        bool useStreet = streetTexture != null && streetTileWidth > 0f;
        float stripThickness = useStreet
            ? streetTileWidth * streetTexture.height / streetTexture.width
            : edgeThickness;

        float edgeZ, bodyZ0, bodyZ1, stripZ0, stripZ1;
        if (grid.GroundSide == GroundSide.LastRow)
        {
            // Sol du côté des z croissants, collé au bord haut (z max) de la grille.
            edgeZ = grid.Origin.z + grid.Height * grid.CellSize - edgeOffset;
            bodyZ0 = edgeZ;
            bodyZ1 = edgeZ + e;
            stripZ0 = edgeZ;
            stripZ1 = edgeZ + stripThickness;
        }
        else
        {
            // Sol du côté des z décroissants, collé au bord bas (z min) de la grille.
            edgeZ = grid.Origin.z + edgeOffset;
            bodyZ0 = edgeZ - e;
            bodyZ1 = edgeZ;
            stripZ0 = edgeZ - stripThickness;
            stripZ1 = edgeZ;
        }

        // Corps du sol
        EnsurePart(ref bodyObject, ref bodyMesh, ref bodyMaterial, "GroundBody", 3000);
        float tile = Mathf.Max(0.01f, textureTileSize);
        SetQuad(bodyMesh, x0, x1, bodyZ0, bodyZ1, y, 0f, (x1 - x0) / tile, 0f, (bodyZ1 - bodyZ0) / tile);
        bodyMaterial.color = groundColor;
        if (groundTexture != null)
        {
            groundTexture.wrapMode = TextureWrapMode.Repeat;
            bodyMaterial.mainTexture = groundTexture;
        }
        else
        {
            bodyMaterial.mainTexture = null;
        }

        // Bande de surface (rue ou herbe), légèrement au-dessus pour passer devant le corps
        if ((showEdgeStrip || useStreet) && stripThickness > 0f)
        {
            EnsurePart(ref stripObject, ref stripMesh, ref stripMaterial, "GroundStrip", 3000);

            if (useStreet)
            {
                // La répétition est ancrée à l'origine de la grille et ne se répète que le long du bord.
                float ox = grid.Origin.x;
                float uA = (x0 - ox) / streetTileWidth;
                float uB = (x1 - ox) / streetTileWidth;
                if (streetFlipHorizontal) { uA = -uA; uB = -uB; }

                // v = 1 correspond au haut de l'image : il est placé côté grille.
                float vEdge = streetFlipVertical ? 0f : 1f;
                float vFar = 1f - vEdge;
                bool edgeAtZ0 = grid.GroundSide == GroundSide.LastRow;
                SetQuad(stripMesh, x0, x1, stripZ0, stripZ1, y + 0.005f,
                        uA, uB, edgeAtZ0 ? vEdge : vFar, edgeAtZ0 ? vFar : vEdge);

                streetTexture.wrapModeU = TextureWrapMode.Repeat;
                streetTexture.wrapModeV = TextureWrapMode.Clamp;
                stripMaterial.mainTexture = streetTexture;
                stripMaterial.color = Color.white;
            }
            else
            {
                SetQuad(stripMesh, x0, x1, stripZ0, stripZ1, y + 0.005f, 0f, 1f, 0f, 1f);
                stripMaterial.mainTexture = null;
                stripMaterial.color = edgeColor;
            }
            stripObject.SetActive(true);
        }
        else if (stripObject != null)
        {
            stripObject.SetActive(false);
        }
    }

    float AutoExtent()
    {
        if (rig == null) rig = FindFirstObjectByType<IsometricCameraController>();
        // Largement de quoi couvrir la vue au zoom maximum, même sur écran large ou en rotation.
        return rig != null ? Mathf.Max(50f, rig.MaxZoom * 3f) : 150f;
    }

    void EnsurePart(ref GameObject go, ref Mesh mesh, ref Material material, string name, int queue)
    {
        if (go != null) return;

        go = new GameObject(name);
        go.transform.SetParent(transform, false);
        mesh = new Mesh { name = name + "Mesh" };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        material = GridMaterials.CreateUnlit(Color.white, queue);
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // Quad horizontal (plan XZ) aux coordonnées monde, normale vers le haut.
    // u0 / u1 : coordonnée u en x0 / x1 ; v0 / v1 : coordonnée v en z0 / z1.
    static void SetQuad(Mesh mesh, float x0, float x1, float z0, float z1, float y,
                        float u0, float u1, float v0, float v1)
    {
        mesh.Clear();
        mesh.vertices = new[]
        {
            new Vector3(x0, y, z0),
            new Vector3(x0, y, z1),
            new Vector3(x1, y, z1),
            new Vector3(x1, y, z0)
        };
        mesh.uv = new[]
        {
            new Vector2(u0, v0),
            new Vector2(u0, v1),
            new Vector2(u1, v1),
            new Vector2(u1, v0)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();
    }

    // Permet de régler l'apparence dans l'inspecteur pendant que le jeu tourne.
    void OnValidate()
    {
        if (Application.isPlaying && lastGrid != null) Build(lastGrid, lastExtentScale);
    }

    void OnDestroy()
    {
        if (bodyMaterial != null) Destroy(bodyMaterial);
        if (stripMaterial != null) Destroy(stripMaterial);
        if (bodyMesh != null) Destroy(bodyMesh);
        if (stripMesh != null) Destroy(stripMesh);
    }
}