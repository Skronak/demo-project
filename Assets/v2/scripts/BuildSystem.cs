using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Chef d'orchestre : possède la grille (GridData), génère l'affichage (GridView),
/// gère le placement / déplacement / suppression et la sauvegarde.
///
/// Contrôles :
///   1..9        : choisir un objet du catalogue et le placer
///   Clic gauche : poser l'objet (mode placement) / saisir un objet pour le déplacer (mode normal)
///   R           : pivoter de 90° pendant le placement / déplacement
///   Clic droit ou Échap : annuler
///   Suppr / X   : supprimer l'objet survolé
///   F5 / F9     : sauvegarder / charger
/// </summary>
public class BuildSystem : MonoBehaviour
{
    enum Mode { Idle, Placing, Moving }

    [Header("Grille (paramétrable)")]
    [SerializeField] int width = 30;
    [SerializeField] int height = 30;
    [SerializeField] float cellSize = 1f;

    [Header("Références")]
    [SerializeField] Camera cam;
    [SerializeField] IsometricCameraController cameraController;
    [SerializeField] GridView gridView;
    [SerializeField] List<PlaceableData> catalog = new List<PlaceableData>();

    [Header("Couleurs de surbrillance")]
    [SerializeField] Color hoverColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] Color validColor = new Color(0.2f, 1f, 0.3f, 0.4f);
    [SerializeField] Color invalidColor = new Color(1f, 0.2f, 0.2f, 0.4f);

    [Header("Fantôme")]
    [Tooltip("Matériau utilisant le shader Building/GhostURP. Sinon un matériau plat de secours est utilisé.")]
    [SerializeField] Material ghostMaterial;
    [SerializeField] Color ghostValidColor = new Color(0.3f, 1f, 0.4f, 0.55f);
    [SerializeField] Color ghostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.55f);

    Material ghostMaterialInstance;

    GridData grid;
    readonly Dictionary<int, GameObject> views = new Dictionary<int, GameObject>();
    readonly Dictionary<string, PlaceableData> byId = new Dictionary<string, PlaceableData>();
    readonly Dictionary<PlaceableData, Material> placeholderMaterials = new Dictionary<PlaceableData, Material>();

    Transform objectsRoot;
    Transform highlight;
    Material highlightMaterial;

    Mode mode = Mode.Idle;
    PlaceableData currentType;
    int currentRotation;
    int movingId;
    GameObject ghost;

    public GridData Grid => grid;

    // ------------------------------------------------------------------
    // Initialisation
    // ------------------------------------------------------------------

    void Awake()
    {
        if (cameraController == null) cameraController = FindFirstObjectByType<IsometricCameraController>();
        if (cam == null) cam = cameraController != null ? cameraController.Camera : Camera.main;
        if (gridView == null) gridView = GetComponent<GridView>();
        if (gridView == null) gridView = gameObject.AddComponent<GridView>();

        foreach (var t in catalog)
        {
            if (t == null) continue;
            if (byId.ContainsKey(t.Id)) Debug.LogWarning($"Id en double dans le catalogue : {t.Id}");
            else byId.Add(t.Id, t);
        }

        objectsRoot = new GameObject("PlacedObjects").transform;
        objectsRoot.SetParent(transform, false);

        var hl = new GameObject("Highlight");
        hl.transform.SetParent(transform, false);
        hl.AddComponent<MeshFilter>().sharedMesh = GridMaterials.UnitQuad();
        var mr = hl.AddComponent<MeshRenderer>();
        highlightMaterial = GridMaterials.CreateUnlit(hoverColor, 3002);
        mr.sharedMaterial = highlightMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        highlight = hl.transform;
        hl.SetActive(false);

        // Une seule instance de matériau pour tous les fantômes (un seul fantôme existe à la fois).
        ghostMaterialInstance = ghostMaterial != null
            ? new Material(ghostMaterial)
            : GridMaterials.CreateUnlit(ghostValidColor, 3003);
        ghostMaterialInstance.renderQueue = 3003; // au-dessus de la grille et de la surbrillance

        GenerateGrid(width, height, cellSize);
    }

    /// <summary>(Re)génère une grille vide de la taille demandée. Supprime les objets existants.</summary>
    public void GenerateGrid(int newWidth, int newHeight, float newCellSize)
    {
        CancelAction();
        ClearViews();

        width = Mathf.Max(1, newWidth);
        height = Mathf.Max(1, newHeight);
        cellSize = Mathf.Max(0.01f, newCellSize);

        grid = new GridData(width, height, cellSize);
        gridView.Build(grid);

        if (cameraController != null)
        {
            cameraController.FocusOn(grid.Center);
            cameraController.SetBounds(grid.Origin,
                grid.Origin + new Vector3(width * cellSize, 0f, height * cellSize));
        }
    }

    [ContextMenu("Regenerate Grid")]
    void RegenerateFromInspector()
    {
        if (Application.isPlaying) GenerateGrid(width, height, cellSize);
    }

    // ------------------------------------------------------------------
    // Boucle principale
    // ------------------------------------------------------------------

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null || grid == null) return;

        HandleHotkeys(kb);

        bool hasCell = TryGetMouseCell(out int cx, out int cy);

        switch (mode)
        {
            case Mode.Idle:
                UpdateIdle(kb, mouse, hasCell, cx, cy);
                break;
            case Mode.Placing:
            case Mode.Moving:
                UpdateGhostMode(kb, mouse, hasCell, cx, cy);
                break;
        }
    }

    void HandleHotkeys(Keyboard kb)
    {
        for (int i = 0; i < 9 && i < catalog.Count; i++)
        {
            if (kb[Key.Digit1 + i].wasPressedThisFrame && catalog[i] != null)
                StartPlacing(catalog[i]);
        }

        if (kb.escapeKey.wasPressedThisFrame) CancelAction();
        if (kb.gKey.wasPressedThisFrame) ToggleGrid();
        if (kb.f5Key.wasPressedThisFrame) SaveGame();
        if (kb.f9Key.wasPressedThisFrame) LoadGame();
    }

    void UpdateIdle(Keyboard kb, Mouse mouse, bool hasCell, int cx, int cy)
    {
        if (!hasCell) { HideHighlight(); return; }

        int id = grid.GetIdAt(cx, cy);
        if (id != 0)
        {
            var e = grid.Get(id);
            ShowHighlight(e.X, e.Y, e.SizeX, e.SizeY, hoverColor);

            if (mouse.leftButton.wasPressedThisFrame) BeginMove(id);
            else if (kb.deleteKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame) RemoveObject(id);
        }
        else
        {
            ShowHighlight(cx, cy, 1, 1, hoverColor);
        }
    }

    void UpdateGhostMode(Keyboard kb, Mouse mouse, bool hasCell, int cx, int cy)
    {
        if (mouse.rightButton.wasPressedThisFrame) { CancelAction(); return; }
        if (kb.rKey.wasPressedThisFrame) currentRotation = (currentRotation + 1) & 3;

        if (!hasCell)
        {
            HideHighlight();
            if (ghost != null) ghost.SetActive(false);
            return;
        }

        Vector2Int fp = GridData.Footprint(currentType.size, currentRotation);
        int ax = cx - fp.x / 2; // ancre : le curseur est (à peu près) au centre de l'objet
        int ay = cy - fp.y / 2;
        int ignoreId = mode == Mode.Moving ? movingId : 0;
        bool valid = grid.CanPlace(ax, ay, fp.x, fp.y, ignoreId);

        ShowHighlight(ax, ay, fp.x, fp.y, valid ? validColor : invalidColor);
        ghostMaterialInstance.color = valid ? ghostValidColor : ghostInvalidColor;

        if (ghost != null)
        {
            ghost.SetActive(true);
            PositionView(ghost, ax, ay, fp.x, fp.y, currentRotation);
        }

        if (valid && mouse.leftButton.wasPressedThisFrame)
        {
            if (mode == Mode.Placing) PlaceObject(ax, ay, fp);
            else CommitMove(ax, ay, fp);
        }
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    void StartPlacing(PlaceableData type)
    {
        CancelAction();
        currentType = type;
        currentRotation = 0;
        ghost = CreateGhost(type);
        mode = Mode.Placing;
    }

    void PlaceObject(int ax, int ay, Vector2Int fp)
    {
        int id = grid.Place(currentType.Id, ax, ay, currentRotation, fp.x, fp.y);
        if (id == 0) return;

        var view = CreateView(currentType);
        PositionView(view, ax, ay, fp.x, fp.y, currentRotation);
        views.Add(id, view);
    }

    void BeginMove(int id)
    {
        var e = grid.Get(id);
        if (e == null || !byId.TryGetValue(e.TypeId, out var type)) return;
        if (!views.TryGetValue(id, out var view)) return;

        currentType = type;
        currentRotation = e.Rotation;
        movingId = id;
        view.SetActive(false); // l'original est masqué, le fantôme prend le relais
        ghost = CreateGhost(type);
        mode = Mode.Moving;
    }

    void CommitMove(int ax, int ay, Vector2Int fp)
    {
        if (grid.Move(movingId, ax, ay, currentRotation, fp.x, fp.y))
        {
            var view = views[movingId];
            PositionView(view, ax, ay, fp.x, fp.y, currentRotation);
        }
        CancelAction(); // remet l'original visible et quitte le mode déplacement
    }

    void RemoveObject(int id)
    {
        if (!grid.Remove(id)) return;
        if (views.TryGetValue(id, out var view))
        {
            Destroy(view);
            views.Remove(id);
        }
    }

    void CancelAction()
    {
        if (mode == Mode.Moving && views.TryGetValue(movingId, out var original) && original != null)
            original.SetActive(true);

        if (ghost != null) Destroy(ghost);
        ghost = null;
        movingId = 0;
        mode = Mode.Idle;
        HideHighlight();
    }

    // ------------------------------------------------------------------
    // Sauvegarde / chargement
    // ------------------------------------------------------------------

    /// <summary>Affiche / masque la grille. Les objets posés restent visibles.</summary>
    public void ToggleGrid()
    {
        gridView.Toggle();
    }

    public void SaveGame()
    {
        SaveSystem.Save(grid);
    }

    public void LoadGame()
    {
        if (!SaveSystem.TryLoad(out var data))
        {
            Debug.LogWarning("Aucune sauvegarde valide trouvée.");
            return;
        }

        GenerateGrid(data.width, data.height, data.cellSize);

        foreach (var o in data.objects)
        {
            if (!byId.TryGetValue(o.typeId, out var type))
            {
                Debug.LogWarning($"Type inconnu dans la sauvegarde : {o.typeId}");
                continue;
            }

            int rot = GridData.NormalizeRotation(o.rotation);
            Vector2Int fp = GridData.Footprint(type.size, rot);
            int id = grid.Place(type.Id, o.x, o.y, rot, fp.x, fp.y);
            if (id == 0)
            {
                Debug.LogWarning($"Objet ignoré (position invalide) : {o.typeId} en ({o.x},{o.y})");
                continue;
            }

            var view = CreateView(type);
            PositionView(view, o.x, o.y, fp.x, fp.y, rot);
            views.Add(id, view);
        }

        Debug.Log($"Grille chargée : {data.width}x{data.height}, {grid.Count} objets");
    }

    // ------------------------------------------------------------------
    // Visuels
    // ------------------------------------------------------------------

    GameObject CreateView(PlaceableData type)
    {
        GameObject root;

        if (type.prefab != null)
        {
            root = Instantiate(type.prefab, objectsRoot);
            // Les prefabs sont conçus pour des cases de 1 unité : on les met à l'échelle de la grille.
            root.transform.localScale = type.prefab.transform.localScale * grid.CellSize;
        }
        else
        {
            // Cube de remplacement : pivot au centre de l'empreinte, au sol.
            root = new GameObject(type.name + "_placeholder");
            root.transform.SetParent(objectsRoot, false);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, type.height * 0.5f, 0f);
            cube.transform.localScale = new Vector3(
                type.size.x * grid.CellSize * 0.9f,
                type.height,
                type.size.y * grid.CellSize * 0.9f);

            var r = cube.GetComponent<Renderer>();
            if (!placeholderMaterials.TryGetValue(type, out var mat))
            {
                mat = new Material(r.sharedMaterial) { color = type.color }; // un matériau partagé par type
                if (type.texture != null) mat.mainTexture = type.texture;
                if (type.material != null) mat.mainTexture = type.material.mainTexture;
                placeholderMaterials.Add(type, mat);
            }
            r.sharedMaterial = mat;
        }

        return root;
    }

    /// <summary>Crée une copie de l'objet rendue avec le matériau fantôme, sans colliders.</summary>
    GameObject CreateGhost(PlaceableData type)
    {
        var g = CreateView(type);

        foreach (var c in g.GetComponentsInChildren<Collider>())
            Destroy(c);

        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterialInstance;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return g;
    }

    void PositionView(GameObject view, int x, int y, int sx, int sy, int rotation)
    {
        view.transform.SetPositionAndRotation(
            grid.FootprintCenter(x, y, sx, sy),
            Quaternion.Euler(0f, rotation * 90f, 0f));
    }

    void ClearViews()
    {
        foreach (var kv in views)
            if (kv.Value != null) Destroy(kv.Value);
        views.Clear();
    }

    void ShowHighlight(int x, int y, int sx, int sy, Color color)
    {
        highlight.gameObject.SetActive(true);
        highlight.position = grid.CellToWorld(x, y) + Vector3.up * 0.03f;
        highlight.localScale = new Vector3(sx * grid.CellSize, 1f, sy * grid.CellSize);
        highlightMaterial.color = color;
    }

    void HideHighlight()
    {
        if (highlight != null) highlight.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Souris -> case (raycast mathématique sur un plan, aucun collider)
    // ------------------------------------------------------------------

    bool TryGetMouseCell(out int x, out int y)
    {
        x = y = 0;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var plane = new Plane(Vector3.up, grid.Origin);
        if (!plane.Raycast(ray, out float distance)) return false;

        return grid.WorldToCell(ray.GetPoint(distance), out x, out y);
    }
}
