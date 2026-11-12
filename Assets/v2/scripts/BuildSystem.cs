using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Chef d'orchestre : gère les couches (chacune avec sa grille, son sol et ses objets),
/// le mode édition, le placement / déplacement / suppression sur la couche active, et la sauvegarde.
///
/// Les couches sont empilées en profondeur : la couche active est au premier plan, les autres
/// sont derrière (dans l'ordre de leur numéro), plus petites et plus basses sur l'axe Y.
///
/// Contrôles :
///   Choisir un objet (menu ou touches 1..9) : active le mode édition
///   Clic gauche : poser / saisir un objet pour le déplacer
///   R : pivoter (hors sprites)   Clic droit ou Échap : annuler / quitter l'édition
///   Suppr / X : supprimer l'objet survolé   F5 / F9 : sauvegarder / charger
/// </summary>
public class BuildSystem : MonoBehaviour
{
    enum Mode { Idle, Placing, Moving }

    [Header("Grille par défaut (nouvelles couches)")]
    [SerializeField] int width = 30;
    [SerializeField] int height = 30;
    [SerializeField] float cellSize = 1f;

    [Header("Couches")]
    [Tooltip("Nombre maximum de couches.")]
    [SerializeField, Range(1, 8)] int maxLayers = 5;
    [Tooltip("Nombre de couches au lancement.")]
    [SerializeField, Range(1, 8)] int initialLayerCount = 1;
    [Tooltip("Taille propre à chaque couche (couche 1 = première entrée, etc.). " +
             "Une couche sans entrée valide utilise la grille par défaut.")]
    [SerializeField] List<Vector2Int> layerSizes = new List<Vector2Int>();
    [Tooltip("Distance entre deux couches sur l'axe Y (la profondeur pour la caméra).")]
    [SerializeField] float layerSpacing = 2f;
    [Tooltip("La caméra est orthographique : l'éloignement ne rétrécit pas les objets. " +
             "Ce facteur le fait à la place : la couche juste derrière est à ce facteur, la suivante à son carré, etc.")]
    [SerializeField, Range(0.3f, 1f)] float layerScaleFactor = 0.8f;
    [Tooltip("Durée de l'animation quand on change de couche active (secondes).")]
    [SerializeField] float layerTransitionTime = 0.35f;
    [Tooltip("Sol et rue de chaque couche : la couche i utilise le modèle i (le dernier s'il y en a moins). " +
             "Chaque modèle est un objet avec un composant GroundView (prefab ou objet de scène). " +
             "Vide = le GroundView présent dans la scène sert de modèle à toutes les couches.")]
    [SerializeField] List<GroundView> groundTemplates = new List<GroundView>();

    [Header("Références")]
    [SerializeField] Camera cam;
    [SerializeField] IsometricCameraController cameraController;
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

    // --- Couches ---
    readonly List<BuildLayer> layers = new List<BuildLayer>();
    int activeIndex;
    Transform layersRoot;

    // La couche active, et raccourcis vers ses données.
    BuildLayer Active => layers[activeIndex];
    GridData grid => Active.Grid;
    Dictionary<int, GameObject> views => Active.Views;

    readonly Dictionary<string, PlaceableData> byId = new Dictionary<string, PlaceableData>();
    readonly Dictionary<PlaceableData, Material> placeholderMaterials = new Dictionary<PlaceableData, Material>();
    readonly Dictionary<PlaceableData, Material> spriteMaterials = new Dictionary<PlaceableData, Material>();
    Mesh spriteQuadMesh;

    Transform highlight;
    Material highlightMaterial;
    Material ghostMaterialInstance;
    Material ghostSpriteInstance; // matériau du fantôme quand l'objet est un sprite
    Material activeGhostMaterial; // matériau à teinter (vert / rouge) selon la validité

    Mode mode = Mode.Idle;
    PlaceableData currentType;
    int currentRotation;
    int movingId;
    GameObject ghost;

    // Mode édition : la grille n'est visible et le placement possible que lorsqu'il est actif.
    bool editMode;
    public bool EditMode => editMode;
    public event System.Action<bool> EditModeChanged;

    // --- API publique des couches ---
    public int LayerCount => layers.Count;
    public int MaxLayers => maxLayers;
    public int ActiveLayerIndex => activeIndex;
    public GridData ActiveGrid => grid;
    public event System.Action LayersChanged;

    public IReadOnlyList<PlaceableData> Catalog => catalog;

    // ------------------------------------------------------------------
    // Initialisation
    // ------------------------------------------------------------------

    void Awake()
    {
        if (cameraController == null) cameraController = FindFirstObjectByType<IsometricCameraController>();
        if (cam == null) cam = cameraController != null ? cameraController.Camera : Camera.main;

        foreach (var t in catalog)
        {
            if (t == null) continue;
            if (byId.ContainsKey(t.Id)) Debug.LogWarning($"Id en double dans le catalogue : {t.Id}");
            else byId.Add(t.Id, t);
        }

        // Racine des couches : à l'origine du monde, indépendante de la position de ce composant.
        layersRoot = new GameObject("Layers").transform;

        var hl = new GameObject("Highlight");
        hl.transform.SetParent(layersRoot, false);
        hl.AddComponent<MeshFilter>().sharedMesh = GridMaterials.UnitQuad();
        var mr = hl.AddComponent<MeshRenderer>();
        highlightMaterial = GridMaterials.CreateUnlit(hoverColor, 3000);
        mr.sharedMaterial = highlightMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        highlight = hl.transform;
        hl.SetActive(false);

        // Une seule instance de matériau pour tous les fantômes (un seul fantôme existe à la fois).
        ghostMaterialInstance = ghostMaterial != null
            ? new Material(ghostMaterial)
            : GridMaterials.CreateUnlit(ghostValidColor, 3000);
        ghostMaterialInstance.renderQueue = 3000;

        // Modèles de sol : ceux de la liste, sinon le GroundView déjà présent dans la scène.
        if (groundTemplates.Count == 0)
        {
            var sceneGround = FindFirstObjectByType<GroundView>();
            if (sceneGround != null) groundTemplates.Add(sceneGround);
        }

        int count = Mathf.Clamp(initialLayerCount, 1, maxLayers);
        for (int i = 0; i < count; i++)
        {
            var size = SizeForLayer(i);
            CreateLayer(size.x, size.y, cellSize);
        }

        // Les objets de scène servant de modèles ne doivent pas s'afficher eux-mêmes.
        foreach (var t in groundTemplates)
        {
            if (t != null && t.gameObject.scene.IsValid()) t.gameObject.SetActive(false);
        }

        activeIndex = 0;
        ApplyActiveLayer(true);
        if (cameraController != null) cameraController.FocusOn(grid.Center);
    }

#if UNITY_EDITOR
    // Référence le shader dans le composant pour qu'il soit inclus dans les builds.
    void OnValidate()
    {
        if (spriteShader == null) spriteShader = Shader.Find("Building/SpriteSheetURP");
        initialLayerCount = Mathf.Min(initialLayerCount, maxLayers);
    }
#endif

    [ContextMenu("Regenerate Active Layer With Default Size")]
    void RegenerateFromInspector()
    {
        if (Application.isPlaying) GenerateGrid(width, height, cellSize);
    }

    Vector2Int SizeForLayer(int index)
    {
        if (index < layerSizes.Count && layerSizes[index].x > 0 && layerSizes[index].y > 0)
            return layerSizes[index];
        return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
    }

    // ------------------------------------------------------------------
    // Couches
    // ------------------------------------------------------------------

    // Les couches lointaines sont mises à l'échelle : le sol doit rester assez large pour couvrir la vue.
    float GroundExtentScale => 1f / Mathf.Pow(layerScaleFactor, Mathf.Max(0, maxLayers - 1));

    GroundView PickGroundTemplate(int index)
    {
        if (groundTemplates.Count == 0) return null;
        return groundTemplates[Mathf.Min(index, groundTemplates.Count - 1)];
    }

    BuildLayer CreateLayer(int w, int h, float cs)
    {
        var layer = new BuildLayer { Name = "Couche " + (layers.Count + 1) };

        // Chaque couche a son propre sol et sa propre rue : copie du modèle correspondant.
        GroundView template = PickGroundTemplate(layers.Count);
        GameObject rootGO = template != null ? Instantiate(template.gameObject) : new GameObject();
        rootGO.name = layer.Name;
        rootGO.SetActive(true);

        var rt = rootGO.transform;
        rt.SetParent(layersRoot, false);
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        layer.Root = rootGO;

        layer.GroundView = rootGO.GetComponentInChildren<GroundView>(true);
        if (layer.GroundView == null) layer.GroundView = rootGO.AddComponent<GroundView>();

        layer.GridView = rootGO.AddComponent<GridView>();
        layer.GridView.SetShowGround(false); // le sol est dessiné par GroundView

        var objects = new GameObject("PlacedObjects").transform;
        objects.SetParent(rootGO.transform, false);
        layer.ObjectsRoot = objects;

        layers.Add(layer);
        RebuildLayerGrid(layer, w, h, cs);
        return layer;
    }

    void RebuildLayerGrid(BuildLayer layer, int w, int h, float cs)
    {
        foreach (var kv in layer.Views)
            if (kv.Value != null) Destroy(kv.Value);
        layer.Views.Clear();

        layer.Grid = new GridData(w, h, cs) { RequireGround = requireGround, GroundSide = groundSide };
        layer.GridView.Build(layer.Grid);
        layer.GroundView.Build(layer.Grid, GroundExtentScale);
    }

    void ClearLayers()
    {
        foreach (var l in layers)
            if (l.Root != null) Destroy(l.Root);
        layers.Clear();
    }

    /// <summary>Recalcule profondeurs cibles, visibilité de la grille, surbrillance et limites de la caméra.</summary>
    void ApplyActiveLayer(bool instant)
    {
        // La couche active passe au premier plan ; les autres restent derrière, dans l'ordre de leur numéro.
        int rank = 1;
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            l.TargetDepth = i == activeIndex ? 0f : rank++;
            if (instant)
            {
                l.Depth = l.TargetDepth;
                l.DepthVelocity = 0f;
            }
            l.GridView.SetVisible(i == activeIndex && editMode);
        }

        highlight.SetParent(Active.Root.transform, false);
        HideHighlight();
        UpdateCameraBounds();
        if (instant) UpdateLayerTransforms(true);

        LayersChanged?.Invoke();
    }

    void UpdateCameraBounds()
    {
        if (cameraController == null) return;
        var g = grid;
        cameraController.SetBounds(g.Origin,
            g.Origin + new Vector3(g.Width * g.CellSize, 0f, g.Height * g.CellSize));
    }

    void LateUpdate()
    {
        if (layers.Count > 0) UpdateLayerTransforms(false);
    }

    // Place chaque couche selon sa profondeur : décalage sur Y + réduction d'échelle autour du point visé
    // par la caméra (effet de perspective avec une caméra orthographique, et léger parallaxe au déplacement).
    void UpdateLayerTransforms(bool instant)
    {
        Vector3 focus = cameraController != null ? cameraController.FocusPoint : Vector3.zero;

        foreach (var l in layers)
        {
            if (!instant)
            {
                l.Depth = Mathf.SmoothDamp(l.Depth, l.TargetDepth, ref l.DepthVelocity, layerTransitionTime);
                if (Mathf.Abs(l.Depth - l.TargetDepth) < 0.002f)
                {
                    l.Depth = l.TargetDepth;
                    l.DepthVelocity = 0f;
                }
            }

            float s = Mathf.Pow(layerScaleFactor, l.Depth);
            var t = l.Root.transform;
            t.localScale = Vector3.one * s;
            t.localPosition = new Vector3(focus.x * (1f - s), -l.Depth * layerSpacing, focus.z * (1f - s));
        }
    }

    /// <summary>Ajoute une couche (si le maximum n'est pas atteint) et la rend active.</summary>
    public bool AddLayer()
    {
        if (layers.Count >= maxLayers) return false;

        CancelAction();
        var size = SizeForLayer(layers.Count);
        CreateLayer(size.x, size.y, cellSize);
        activeIndex = layers.Count - 1;
        ApplyActiveLayer(false);
        return true;
    }

    /// <summary>Supprime une couche et tous ses objets. Il en reste toujours au moins une.</summary>
    public bool RemoveLayer(int index)
    {
        if (layers.Count <= 1 || index < 0 || index >= layers.Count) return false;

        CancelAction();
        Destroy(layers[index].Root);
        layers.RemoveAt(index);

        for (int i = 0; i < layers.Count; i++)
        {
            layers[i].Name = "Couche " + (i + 1);
            layers[i].Root.name = layers[i].Name;
        }

        if (index < activeIndex) activeIndex--;
        else if (index == activeIndex) activeIndex = Mathf.Min(index, layers.Count - 1);

        ApplyActiveLayer(false);
        return true;
    }

    /// <summary>Amène une couche au premier plan et la rend éditable.</summary>
    public void SetActiveLayer(int index)
    {
        index = Mathf.Clamp(index, 0, layers.Count - 1);
        if (index == activeIndex) return;

        CancelAction();
        activeIndex = index;
        ApplyActiveLayer(false);
    }

    /// <summary>Redimensionne la grille d'une couche. Ses objets sont supprimés.</summary>
    public void ResizeLayer(int index, int newWidth, int newHeight)
    {
        if (index < 0 || index >= layers.Count) return;

        var l = layers[index];
        if (index == activeIndex) CancelAction();

        RebuildLayerGrid(l, Mathf.Max(1, newWidth), Mathf.Max(1, newHeight), l.Grid.CellSize);
        if (index == activeIndex) UpdateCameraBounds();
        LayersChanged?.Invoke();
    }

    /// <summary>Régénère la grille de la couche active (taille et case données). Ses objets sont supprimés.</summary>
    public void GenerateGrid(int newWidth, int newHeight, float newCellSize)
    {
        CancelAction();
        RebuildLayerGrid(Active, Mathf.Max(1, newWidth), Mathf.Max(1, newHeight), Mathf.Max(0.01f, newCellSize));
        UpdateCameraBounds();
        if (cameraController != null) cameraController.FocusOn(grid.Center);
        LayersChanged?.Invoke();
    }

    // ------------------------------------------------------------------
    // Boucle principale
    // ------------------------------------------------------------------

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null || layers.Count == 0) return;

        // Pas de raccourcis pendant la saisie dans un champ de l'interface.
        if (IsTypingInUI()) return;

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
            PositionView(Active, ghost, ax, ay, fp.x, fp.y, currentRotation);
            ghost.transform.localPosition += Vector3.up * 0.04f; // le fantôme passe devant la surbrillance
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

    /// <summary>Sélectionne un type d'objet à placer (appelé par le menu de construction).</summary>
    public void SelectPlaceable(PlaceableData type)
    {
        if (type != null) StartPlacing(type);
    }

    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    static bool IsTypingInUI()
    {
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return false;
        var input = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
        return input != null && input.isFocused;
    }

    /// <summary>
    /// Active / désactive le mode édition. Actif : la grille de la couche active est affichée et on peut
    /// placer, déplacer ou supprimer des objets. Inactif : grille masquée, aucune interaction.
    /// </summary>
    public void SetEditMode(bool value)
    {
        if (editMode == value) return;
        editMode = value;

        if (!value) CancelAction();
        if (layers.Count > 0) Active.GridView.SetVisible(value);
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

        var view = CreateView(Active, currentType);
        PositionView(Active, view, ax, ay, fp.x, fp.y, currentRotation);
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
            PositionView(Active, view, ax, ay, fp.x, fp.y, currentRotation);
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
        if (layers.Count > 0 && mode == Mode.Moving
            && views.TryGetValue(movingId, out var original) && original != null)
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

    public void SaveGame()
    {
        var data = new SaveData { activeLayer = activeIndex };
        foreach (var l in layers)
            data.layers.Add(SaveSystem.CreateLayerData(l.Grid, l.Name));
        SaveSystem.Save(data);
    }

    public void LoadGame()
    {
        if (!SaveSystem.TryLoad(out var data))
        {
            Debug.LogWarning("Aucune sauvegarde valide trouvée.");
            return;
        }

        CancelAction();
        ClearLayers();

        int count = Mathf.Min(data.layers.Count, maxLayers);
        if (data.layers.Count > maxLayers)
            Debug.LogWarning($"La sauvegarde contient {data.layers.Count} couches : seules les {maxLayers} premières sont chargées.");

        for (int i = 0; i < count; i++)
        {
            var ld = data.layers[i];
            var layer = CreateLayer(ld.width, ld.height, ld.cellSize > 0f ? ld.cellSize : cellSize);

            foreach (var o in ld.objects)
            {
                if (!byId.TryGetValue(o.typeId, out var type))
                {
                    Debug.LogWarning($"Type inconnu dans la sauvegarde : {o.typeId}");
                    continue;
                }

                int rot = IsSprite(type) ? 0 : GridData.NormalizeRotation(o.rotation);
                Vector2Int fp = GridData.Footprint(type.size, rot);
                int id = layer.Grid.Place(type.Id, o.x, o.y, rot, fp.x, fp.y);
                if (id == 0)
                {
                    Debug.LogWarning($"Objet ignoré (position invalide) : {o.typeId} en ({o.x},{o.y}) couche {i + 1}");
                    continue;
                }

                var view = CreateView(layer, type);
                PositionView(layer, view, o.x, o.y, fp.x, fp.y, rot);
                layer.Views.Add(id, view);
            }
        }

        activeIndex = Mathf.Clamp(data.activeLayer, 0, layers.Count - 1);
        ApplyActiveLayer(true);
        if (cameraController != null) cameraController.FocusOn(grid.Center);

        Debug.Log($"Sauvegarde chargée : {layers.Count} couche(s)");
    }

    // ------------------------------------------------------------------
    // Visuels
    // ------------------------------------------------------------------

    GameObject CreateView(BuildLayer layer, PlaceableData type)
    {
        GameObject root;
        float cs = layer.Grid.CellSize;

        if (type.prefab != null)
        {
            root = Instantiate(type.prefab, layer.ObjectsRoot);
            // Les prefabs sont conçus pour des cases de 1 unité : on les met à l'échelle de la grille.
            root.transform.localScale = type.prefab.transform.localScale * cs;
        }
        else if (type.visual == PlaceableVisual.Sprite)
        {
            root = CreateSpriteView(layer, type);
        }
        else
        {
            // Cube de remplacement : pivot au centre de l'empreinte, au sol.
            root = new GameObject(type.name + "_placeholder");
            root.transform.SetParent(layer.ObjectsRoot, false);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, type.height * 0.5f, 0f);
            cube.transform.localScale = new Vector3(
                type.size.x * cs,
                type.height,
                type.size.y * cs);

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
    GameObject CreateSpriteView(BuildLayer layer, PlaceableData type)
    {
        var root = new GameObject(type.name + "_sprite");
        root.transform.SetParent(layer.ObjectsRoot, false);

        float cs = layer.Grid.CellSize;
        float yaw = SpriteYawValue;
        bool swap = (Mathf.RoundToInt(yaw / 90f) & 1) == 1;
        float w = (swap ? type.size.y : type.size.x) * cs;
        float d = (swap ? type.size.x : type.size.y) * cs;

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
    // Tout le contenu transparent utilise la file de rendu 3000 : l'ordre entre couches (et à l'intérieur
    // d'une couche) est alors donné par la profondeur, c'est-à-dire par les décalages sur l'axe Y.
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

        material.renderQueue = 3000;
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

    /// <summary>Crée une copie de l'objet (sur la couche active) rendue avec le matériau fantôme, sans colliders.</summary>
    GameObject CreateGhost(PlaceableData type)
    {
        var g = CreateView(Active, type);

        // Un sprite garde sa texture (et son animation) : son fantôme est une copie teintée et semi-transparente.
        // Les autres objets utilisent le matériau fantôme uni.
        if (ghostSpriteInstance != null) { Destroy(ghostSpriteInstance); ghostSpriteInstance = null; }
        Material ghostMat = ghostMaterialInstance;
        if (IsSprite(type))
        {
            ghostSpriteInstance = new Material(GetSpriteMaterial(type));
            ghostSpriteInstance.renderQueue = 3000;
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

    // Coordonnées locales : la couche (Root) peut être déplacée et mise à l'échelle.
    void PositionView(BuildLayer layer, GameObject view, int x, int y, int sx, int sy, int rotation)
    {
        view.transform.localPosition = layer.Grid.FootprintCenter(x, y, sx, sy);
        view.transform.localRotation = Quaternion.Euler(0f, rotation * 90f, 0f);
    }

    void ShowHighlight(int x, int y, int sx, int sy, Color color)
    {
        highlight.gameObject.SetActive(true);
        highlight.localPosition = grid.CellToWorld(x, y) + Vector3.up * 0.03f;
        highlight.localScale = new Vector3(sx * grid.CellSize, 1f, sy * grid.CellSize);
        highlightMaterial.color = color;
    }

    void HideHighlight()
    {
        if (highlight != null) highlight.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Souris -> case de la couche active (raycast mathématique sur un plan, aucun collider)
    // ------------------------------------------------------------------

    bool TryGetMouseCell(out int x, out int y)
    {
        x = y = 0;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Transform layerT = Active.Root.transform;
        var plane = new Plane(Vector3.up, layerT.TransformPoint(grid.Origin));
        if (!plane.Raycast(ray, out float distance)) return false;

        // Conversion dans le repère local de la couche (échelle et décalage inclus).
        Vector3 local = layerT.InverseTransformPoint(ray.GetPoint(distance));
        return grid.WorldToCell(local, out x, out y);
    }
}