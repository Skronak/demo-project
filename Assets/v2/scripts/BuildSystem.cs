using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
    [Tooltip("Optionnel : sol décoratif autour de la grille. Trouvé automatiquement dans la scène si vide.")]
    [SerializeField] GroundView groundView;
    [SerializeField] List<PlaceableData> catalog = new List<PlaceableData>();
    [Tooltip("Garde les raccourcis clavier 1 à 9 en plus du menu.")]
    [SerializeField] bool enableNumberHotkeys = true;

    [Header("Sol")]
    [Tooltip("Si coché, un objet ne peut être posé que s'il touche la ligne du sol.")]
    [SerializeField] bool requireGround = true;
    [Tooltip("Ligne qui sert de sol. FirstRow = y 0, LastRow = dernière ligne. " +
             "Choisissez celle qui est en BAS de l'écran avec l'orientation de votre caméra.")]
    [SerializeField] GroundSide groundSide = GroundSide.LastRow;
    [Tooltip("L'objet se colle automatiquement au sol : seule sa position horizontale suit la souris.")]
    [SerializeField] bool snapToGround = true;

    [Header("Sprites 2D (objets sans prefab)")]
    [Tooltip("Shader Building/SpriteSheetURP (assigné automatiquement dans l'éditeur). " +
             "Affiche les sprites avec transparence et animation.")]
    [SerializeField] Shader spriteShader;
    [Tooltip("Rotation (degrés, multiple de 90) appliquée aux sprites pour qu'ils soient droits à l'écran. " +
             "-1 = automatique, d'après le yaw de départ de la caméra.")]
    [SerializeField] float spriteYaw = -1f;

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
    readonly Dictionary<PlaceableData, Material> spriteMaterials = new Dictionary<PlaceableData, Material>();
    Mesh spriteQuadMesh;
    Material ghostSpriteInstance; // matériau du fantôme quand l'objet est un sprite
    Material activeGhostMaterial; // matériau à teinter (vert / rouge) selon la validité

    Transform objectsRoot;
    Transform highlight;
    Material highlightMaterial;

    Mode mode = Mode.Idle;

    // Mode édition : la grille n'est visible et le placement possible que lorsqu'il est actif.
    bool editMode;
    public bool EditMode => editMode;
    public event System.Action<bool> EditModeChanged;
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
        if (groundView == null) groundView = FindFirstObjectByType<GroundView>();

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
        grid.RequireGround = requireGround;
        grid.GroundSide = groundSide;
        gridView.Build(grid);
        gridView.SetVisible(editMode);
        if (groundView != null) groundView.Build(grid);

        if (cameraController != null)
        {
            cameraController.FocusOn(grid.Center);
            cameraController.SetBounds(grid.Origin,
                grid.Origin + new Vector3(width * cellSize, 0f, height * cellSize));
        }
    }

#if UNITY_EDITOR
    // Référence le shader dans le composant pour qu'il soit inclus dans les builds.
    void OnValidate()
    {
        if (spriteShader == null) spriteShader = Shader.Find("Building/SpriteSheetURP");
    }
#endif

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

        // Pointeur sur l'interface : on n'interagit pas avec la grille.
        if (IsPointerOverUI())
        {
            HideHighlight();
            if (ghost != null) ghost.SetActive(false);
            return;
        }

        bool hasCell = TryGetMouseCell(out int cx, out int cy);

        switch (mode)
        {
            case Mode.Idle:
                if (editMode) UpdateIdle(kb, mouse, hasCell, cx, cy);
                else HideHighlight(); // hors mode édition : aucune interaction avec la grille
                break;
            case Mode.Placing:
            case Mode.Moving:
                UpdateGhostMode(kb, mouse, hasCell, cx, cy);
                break;
        }
    }

    void HandleHotkeys(Keyboard kb)
    {
        for (int i = 0; enableNumberHotkeys && i < 9 && i < catalog.Count; i++)
        {
            if (kb[Key.Digit1 + i].wasPressedThisFrame && catalog[i] != null)
                StartPlacing(catalog[i]);
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (mode != Mode.Idle) CancelAction(); // 1er Échap : annule l'objet en cours
            else SetEditMode(false);               // 2e Échap : quitte le mode édition
        }
        if (kb.f5Key.wasPressedThisFrame) SaveGame();
        if (kb.f9Key.wasPressedThisFrame) LoadGame();
    }

    void UpdateIdle(Keyboard kb, Mouse mouse, bool hasCell, int cx, int cy)
    {
        if (mouse.rightButton.wasPressedThisFrame) { SetEditMode(false); return; }
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
        // Les sprites 2D restent toujours droits : pas de rotation.
        if (kb.rKey.wasPressedThisFrame && !IsSprite(currentType)) currentRotation = (currentRotation + 1) & 3;

        if (!hasCell)
        {
            HideHighlight();
            if (ghost != null) ghost.SetActive(false);
            return;
        }

        Vector2Int fp = GridData.Footprint(currentType.size, currentRotation);
        int ax = cx - fp.x / 2; // ancre : le curseur est (à peu près) au centre de l'objet
        int ay = cy - fp.y / 2;
        if (requireGround && snapToGround)
            ay = groundSide == GroundSide.FirstRow ? 0 : grid.Height - fp.y; // l'objet reste collé au sol
        int ignoreId = mode == Mode.Moving ? movingId : 0;
        bool valid = grid.CanPlace(ax, ay, fp.x, fp.y, ignoreId);

        ShowHighlight(ax, ay, fp.x, fp.y, valid ? validColor : invalidColor);
        var tintMaterial = activeGhostMaterial != null ? activeGhostMaterial : ghostMaterialInstance;
        tintMaterial.color = valid ? ghostValidColor : ghostInvalidColor;

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

    public IReadOnlyList<PlaceableData> Catalog => catalog;

    /// <summary>Sélectionne un type d'objet à placer (appelé par le menu de construction).</summary>
    public void SelectPlaceable(PlaceableData type)
    {
        if (type != null) StartPlacing(type);
    }

    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// Active / désactive le mode édition. Actif : la grille est affichée et on peut placer,
    /// déplacer ou supprimer des objets. Inactif : grille masquée, aucune interaction.
    /// </summary>
    public void SetEditMode(bool value)
    {
        if (editMode == value) return;
        editMode = value;

        if (!value) CancelAction();
        if (gridView != null) gridView.SetVisible(value);
        EditModeChanged?.Invoke(value);
    }

    void StartPlacing(PlaceableData type)
    {
        SetEditMode(true); // choisir un objet (menu ou raccourci) active le mode édition
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
        if (ghostSpriteInstance != null) Destroy(ghostSpriteInstance);
        ghostSpriteInstance = null;
        activeGhostMaterial = null;
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

            int rot = IsSprite(type) ? 0 : GridData.NormalizeRotation(o.rotation);
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
        else if (type.visual == PlaceableVisual.Sprite)
        {
            root = CreateSpriteView(type);
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
                type.size.x * grid.CellSize,
                type.height,
                type.size.y * grid.CellSize);

            var r = cube.GetComponent<Renderer>();
            if (!placeholderMaterials.TryGetValue(type, out var mat))
            {
                mat = new Material(r.sharedMaterial) { color = type.color }; // un matériau partagé par type
                if (type.texture != null) mat.mainTexture = type.texture;
                placeholderMaterials.Add(type, mat);
            }
            r.sharedMaterial = mat;
        }

        return root;
    }

    static bool IsSprite(PlaceableData type)
    {
        return type != null && type.prefab == null && type.visual == PlaceableVisual.Sprite;
    }

    float SpriteYawValue
    {
        get
        {
            float yaw = spriteYaw >= 0f
                ? spriteYaw
                : (cameraController != null ? cameraController.StartYaw : 0f);
            return Mathf.Round(yaw / 90f) * 90f;
        }
    }

    /// <summary>
    /// Objet 2D : un quad posé à plat (plan XZ) qui remplit l'empreinte de l'objet,
    /// tourné pour être droit à l'écran, avec transparence et animation éventuelle.
    /// </summary>
    GameObject CreateSpriteView(PlaceableData type)
    {
        var root = new GameObject(type.name + "_sprite");
        root.transform.SetParent(objectsRoot, false);

        float yaw = SpriteYawValue;
        bool swap = (Mathf.RoundToInt(yaw / 90f) & 1) == 1;
        float w = (swap ? type.size.y : type.size.x) * grid.CellSize;
        float d = (swap ? type.size.x : type.size.y) * grid.CellSize;

        var quad = new GameObject("Sprite");
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        quad.transform.localScale = new Vector3(w, 1f, d);

        if (spriteQuadMesh == null) spriteQuadMesh = CreateCenteredQuad();
        quad.AddComponent<MeshFilter>().sharedMesh = spriteQuadMesh;

        var mr = quad.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetSpriteMaterial(type);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return root;
    }

    // Un matériau partagé par type : toutes les copies sont animées ensemble, sans coût CPU.
    Material GetSpriteMaterial(PlaceableData type)
    {
        if (spriteMaterials.TryGetValue(type, out var material)) return material;

        Shader shader = spriteShader != null ? spriteShader : Shader.Find("Building/SpriteSheetURP");
        if (shader == null)
        {
            Debug.LogWarning("Shader « Building/SpriteSheetURP » introuvable : ajoutez SpriteSheetURP.shader au projet. " +
                             "Repli sur Sprites/Default (transparence mais sans animation).");
            shader = Shader.Find("Sprites/Default");
        }

        material = new Material(shader) { color = type.color };
        if (type.texture != null) material.mainTexture = type.texture;

        if (material.HasProperty("_Columns"))
        {
            int cols = Mathf.Max(1, type.sheetColumns);
            int rows = Mathf.Max(1, type.sheetRows);
            int frames = type.frameCount > 0 ? type.frameCount : cols * rows;
            material.SetFloat("_Columns", cols);
            material.SetFloat("_Rows", rows);
            material.SetFloat("_FrameCount", frames);
            material.SetFloat("_Fps", type.framesPerSecond);
        }

        material.renderQueue = 3000; // sous les lignes de la grille (3001) et la surbrillance (3002)
        spriteMaterials.Add(type, material);
        return material;
    }

    // Quad 1x1 centré à l'origine, posé à plat sur XZ, normale vers le haut.
    static Mesh CreateCenteredQuad()
    {
        var mesh = new Mesh { name = "SpriteQuadXZ" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f, -0.5f)
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Crée une copie de l'objet rendue avec le matériau fantôme, sans colliders.</summary>
    GameObject CreateGhost(PlaceableData type)
    {
        var g = CreateView(type);

        // Un sprite garde sa texture (et son animation) : son fantôme est une copie teintée et semi-transparente.
        // Les autres objets utilisent le matériau fantôme uni.
        if (ghostSpriteInstance != null) { Destroy(ghostSpriteInstance); ghostSpriteInstance = null; }
        Material ghostMat = ghostMaterialInstance;
        if (IsSprite(type))
        {
            ghostSpriteInstance = new Material(GetSpriteMaterial(type));
            ghostSpriteInstance.renderQueue = 3003;
            ghostMat = ghostSpriteInstance;
        }
        activeGhostMaterial = ghostMat;

        foreach (var c in g.GetComponentsInChildren<Collider>())
            Destroy(c);

        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
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