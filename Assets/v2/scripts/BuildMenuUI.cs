using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Menu de construction : un bouton ouvre un panneau listant les objets du catalogue
/// sous forme de miniatures. Cliquer sur une miniature ferme le menu et sélectionne
/// l'objet pour le placer sur la grille.
///
/// Toute l'interface est créée par code : il suffit d'ajouter ce composant à un objet de la scène
/// (par exemple celui qui porte BuildSystem). Un EventSystem est créé au besoin.
/// </summary>
public class BuildMenuUI : MonoBehaviour
{
    [SerializeField] BuildSystem buildSystem;

    [Header("Disposition")]
    [SerializeField] int columns = 4;
    [SerializeField] Vector2 tileSize = new Vector2(120f, 140f);
    [SerializeField] float spacing = 10f;
    [SerializeField] float maxPanelHeight = 460f;

    [Header("Apparence")]
    [SerializeField] string buttonLabel = "Construire";
    [SerializeField] Color buttonColor = new Color(0.20f, 0.45f, 0.85f, 1f);
    [SerializeField] string doneLabel = "Terminer";
    [SerializeField] Color doneColor = new Color(0.80f, 0.30f, 0.25f, 1f);
    [SerializeField] Color panelColor = new Color(0.09f, 0.10f, 0.12f, 0.95f);
    [SerializeField] Color tileColor = new Color(0.20f, 0.22f, 0.26f, 1f);
    [SerializeField] Color tileHoverColor = new Color(0.30f, 0.34f, 0.40f, 1f);

    GameObject blocker;
    GameObject panel;
    Button doneButton;
    ScrollRect scroll;
    Font font;
    bool isOpen;
    readonly Dictionary<PlaceableData, Sprite> spriteCache = new Dictionary<PlaceableData, Sprite>();

    void Start()
    {
        if (buildSystem == null) buildSystem = FindFirstObjectByType<BuildSystem>();
        if (buildSystem == null)
        {
            Debug.LogError("BuildMenuUI : aucun BuildSystem trouvé dans la scène.");
            enabled = false;
            return;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildUI();
        SetOpen(false);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (isOpen && kb != null && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    // ------------------------------------------------------------------

    void SetOpen(bool value)
    {
        isOpen = value;
        if (panel != null) panel.SetActive(value);
        if (blocker != null) blocker.SetActive(value);
        if (value && scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    void OnEditModeChanged(bool editing)
    {
        if (doneButton != null) doneButton.gameObject.SetActive(editing);
    }

    void OnDestroy()
    {
        if (buildSystem != null) buildSystem.EditModeChanged -= OnEditModeChanged;
    }

    void OnSelect(PlaceableData data)
    {
        buildSystem.SelectPlaceable(data);
        SetOpen(false);
    }

    // ------------------------------------------------------------------
    // Construction de l'interface
    // ------------------------------------------------------------------

    public static void EnsureEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        else if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            // Le module « Standalone » ne fonctionne pas avec le nouvel Input System.
            var old = es.GetComponent<StandaloneInputModule>();
            if (old != null) Destroy(old);
            es.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("BuildMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvasT = canvasGO.transform;

        // 1) Fond invisible plein écran : un clic en dehors du menu le ferme.
        var blockerRT = NewRect("Blocker", canvasT);
        Stretch(blockerRT);
        var blockerImage = blockerRT.gameObject.AddComponent<Image>();
        blockerImage.color = new Color(0f, 0f, 0f, 0f);
        var blockerButton = blockerRT.gameObject.AddComponent<Button>();
        blockerButton.transition = Selectable.Transition.None;
        blockerButton.onClick.AddListener(() => SetOpen(false));
        blocker = blockerRT.gameObject;

        // 2) Panneau de la liste
        BuildPanel(canvasT);

        // 3) Bouton d'ouverture (créé en dernier : il reste au-dessus du fond)
        var openButton = CreateButton(canvasT, "MenuButton", buttonLabel, buttonColor);
        var openRT = (RectTransform)openButton.transform;
        openRT.anchorMin = openRT.anchorMax = openRT.pivot = Vector2.zero;
        openRT.anchoredPosition = new Vector2(20f, 20f);
        openRT.sizeDelta = new Vector2(200f, 60f);
        openButton.onClick.AddListener(() => SetOpen(!isOpen));

        // 4) Bouton « Terminer » : visible uniquement en mode édition
        doneButton = CreateButton(canvasT, "DoneButton", doneLabel, doneColor);
        var doneRT = (RectTransform)doneButton.transform;
        doneRT.anchorMin = doneRT.anchorMax = doneRT.pivot = Vector2.zero;
        doneRT.anchoredPosition = new Vector2(240f, 20f);
        doneRT.sizeDelta = new Vector2(200f, 60f);
        doneButton.onClick.AddListener(() => buildSystem.SetEditMode(false));
        doneButton.gameObject.SetActive(buildSystem.EditMode);
        buildSystem.EditModeChanged += OnEditModeChanged;
    }

    void BuildPanel(Transform canvasT)
    {
        var catalog = buildSystem.Catalog;
        int cols = Mathf.Max(1, columns);
        int rows = Mathf.Max(1, Mathf.CeilToInt(catalog.Count / (float)cols));

        float width = cols * tileSize.x + (cols + 1) * spacing;
        float contentHeight = rows * tileSize.y + (rows + 1) * spacing;
        float height = Mathf.Min(maxPanelHeight, contentHeight);

        var panelRT = NewRect("Panel", canvasT);
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = Vector2.zero;
        panelRT.anchoredPosition = new Vector2(20f, 90f); // au-dessus du bouton
        panelRT.sizeDelta = new Vector2(width, height);
        panelRT.gameObject.AddComponent<Image>().color = panelColor;
        panel = panelRT.gameObject;

        var viewport = NewRect("Viewport", panelRT);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        int pad = Mathf.RoundToInt(spacing);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = tileSize;
        grid.spacing = new Vector2(spacing, spacing);
        grid.padding = new RectOffset(pad, pad, pad, pad);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = panelRT.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;
        scroll.viewport = viewport;
        scroll.content = content;

        foreach (var data in catalog)
        {
            if (data != null) CreateTile(content, data);
        }
    }

    void CreateTile(Transform parent, PlaceableData data)
    {
        string label = string.IsNullOrEmpty(data.displayName) ? data.name : data.displayName;

        var tileRT = NewRect(label, parent);
        var bg = tileRT.gameObject.AddComponent<Image>();
        bg.color = Color.white;

        var button = tileRT.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.normalColor = tileColor;
        colors.selectedColor = tileColor;
        colors.highlightedColor = tileHoverColor;
        colors.pressedColor = tileHoverColor * 0.85f;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.AddListener(() => OnSelect(data));

        // Miniature
        var iconRT = NewRect("Icon", tileRT);
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(8f, 36f);
        iconRT.offsetMax = new Vector2(-8f, -8f);
        var icon = iconRT.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        Sprite sprite = GetSprite(data);
        if (sprite != null)
        {
            icon.sprite = sprite;
            icon.color = Color.white;
        }
        else
        {
            icon.color = data.color; // à défaut : un aplat de la couleur de l'objet
        }

        // Nom
        var labelRT = NewRect("Label", tileRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 0f);
        labelRT.offsetMin = new Vector2(4f, 4f);
        labelRT.offsetMax = new Vector2(-4f, 32f);
        var text = labelRT.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = 16;
    }

    Sprite GetSprite(PlaceableData data)
    {
        if (data.icon != null) return data.icon;
        if (data.texture == null) return null;

        if (!spriteCache.TryGetValue(data, out var sprite))
        {
            var t = data.texture;
            int cols = Mathf.Max(1, data.sheetColumns);
            int rows = Mathf.Max(1, data.sheetRows);
            float fw = t.width / (float)cols;
            float fh = t.height / (float)rows;

            // Pour une feuille de sprites : miniature = première image (en haut à gauche).
            sprite = Sprite.Create(t, new Rect(0f, t.height - fh, fw, fh), new Vector2(0.5f, 0.5f),
                                   100f, 0, SpriteMeshType.FullRect);
            spriteCache.Add(data, sprite);
        }
        return sprite;
    }

    Button CreateButton(Transform parent, string name, string label, Color color)
    {
        var rt = NewRect(name, parent);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = Color.white;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = color;
        colors.selectedColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        colors.pressedColor = color * 0.8f;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        var labelRT = NewRect("Label", rt);
        Stretch(labelRT);
        var text = labelRT.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 26;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.raycastTarget = false;

        return button;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}