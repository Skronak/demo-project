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

    [Tooltip("Optionnel. material du cube de remplacement (si pas de prefab). " +
             "Elle est multipliée par la couleur : laissez la couleur blanche pour la voir telle quelle.")]
    public Material material;

    
    public string Id => string.IsNullOrEmpty(id) ? name : id;

    void OnValidate()
    {
        size.x = Mathf.Max(1, size.x);
        size.y = Mathf.Max(1, size.y);
    }
}