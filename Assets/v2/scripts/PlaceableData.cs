using UnityEngine;

/// <summary>
/// Définition d'un type d'objet constructible.
/// Créer via : Assets > Create > Building > Placeable Data
/// </summary>
[CreateAssetMenu(menuName = "Building/Placeable Data", fileName = "NewPlaceable")]
public class PlaceableData : ScriptableObject
{
    [Tooltip("Identifiant unique utilisé dans la sauvegarde. Si vide, le nom de l'asset est utilisé.")]
    public string id;

    public string displayName;

    [Tooltip("Optionnel. Le pivot du prefab doit être au CENTRE de son empreinte, au niveau du sol. " +
             "Si vide, un cube de remplacement est généré.")]
    public GameObject prefab;

    [Tooltip("Taille en cases (X, Z) avant rotation.")]
    public Vector2Int size = Vector2Int.one;

    [Tooltip("Hauteur du cube de remplacement (si pas de prefab).")]
    public float height = 1f;

    [Tooltip("Couleur du cube de remplacement (si pas de prefab).")]
    public Color color = Color.white;

    [Tooltip("Optionnel. Texture du cube de remplacement (si pas de prefab). " +
             "Elle est multipliée par la couleur : laissez la couleur blanche pour la voir telle quelle.")]
    public Texture2D texture;

    [Header("Apparence sans prefab")]
    [Tooltip("Sprite : image à plat, avec transparence (jeu 2D). Cube : cube de remplacement.")]
    public PlaceableVisual visual = PlaceableVisual.Sprite;

    [Header("Animation (feuille de sprites, mode Sprite)")]
    [Tooltip("Nombre de colonnes de la feuille de sprites. 1 colonne et 1 ligne = image fixe.")]
    public int sheetColumns = 1;
    [Tooltip("Nombre de lignes de la feuille de sprites.")]
    public int sheetRows = 1;
    [Tooltip("Nombre d'images à jouer (0 = colonnes x lignes). Les images sont lues de gauche à droite, de haut en bas.")]
    public int frameCount = 0;
    [Tooltip("Images par seconde.")]
    public float framesPerSecond = 8f;

    [Header("Interface")]
    [Tooltip("Miniature affichée dans le menu de construction. Générable via Building > Generate Icons.")]
    public Sprite icon;

    public string Id => string.IsNullOrEmpty(id) ? name : id;

    void OnValidate()
    {
        size.x = Mathf.Max(1, size.x);
        size.y = Mathf.Max(1, size.y);
        sheetColumns = Mathf.Max(1, sheetColumns);
        sheetRows = Mathf.Max(1, sheetRows);
        frameCount = Mathf.Clamp(frameCount, 0, sheetColumns * sheetRows);
        framesPerSecond = Mathf.Max(0f, framesPerSecond);
    }
}

public enum PlaceableVisual
{
    Sprite, // image à plat avec transparence (et animation éventuelle)
    Cube    // cube de remplacement
}