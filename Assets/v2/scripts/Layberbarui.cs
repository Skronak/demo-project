using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barre de couches, toujours visible en haut à droite de l'écran :
///   [1] [2] [3] ...  boutons des couches (la couche active est en bleu ; cliquer l'amène au premier plan)
///   [+]              ajoute une couche (jusqu'au maximum)
///   [-]              supprime la couche active (un second clic dans les 3 secondes confirme)
///   [L×H]            ouvre le panneau de taille de la grille de la couche active
///
/// Toute l'interface est créée par code : ajoutez simplement ce composant à un objet de la scène
/// (par exemple celui qui porte BuildSystem).
/// </summary>
public class LayerBarUI : MonoBehaviour
{
    [SerializeField] BuildSystem buildSystem;

    [Header("Disposition")]
    [SerializeField] Vector2 buttonSize = new Vector2(64f, 64f);
    [SerializeField] float spacing = 8f;
    [SerializeField] Vector2 margin = new Vector2(20f, 20f);
    [Tooltip("Taille maximale (en cases) acceptée dans le panneau de taille.")]
    [SerializeField] int maxGridSize = 500;

    [Header("Couleurs")]
    [SerializeField] Color layerColor = new Color(0.20f, 0.22f, 0.26f, 0.95f);
    [SerializeField] Color activeColor = new Color(0.20f, 0.45f, 0.85f, 1f);
    [SerializeField] Color addColor = new Color(0.25f, 0.60f, 0.35f, 1f);
    [SerializeField] Color removeColor = new Color(0.75f, 0.30f, 0.25f, 1f);
    [SerializeField] Color toolColor = new Color(0.35f, 0.38f, 0.45f, 1f);
    [SerializeField] Color panelColor = new Color(0.09f, 0.10f, 0.12f, 0.95f);

    Font font;
    RectTransform barRoot;
    GameObject sizePanel;
    Text sizeTitle;
    InputField widthInput;
    InputField heightInput;
    bool removeArmed;
    float removeArmedUntil;

    void Start()
    {
        if (buildSystem == null) buildSystem = FindFirstObjectByType<BuildSystem>();
        if (buildSystem == null)
        {
            Debug.LogError("LayerBarUI : aucun BuildSystem trouvé dans la scène.");
            enabled = false;
            return;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildMenuUI.EnsureEventSystem();
        BuildCanvas();

        buildSystem.LayersChanged += OnLayersChanged;
        Rebuild();
    }

    void OnDestroy()
    {
        if (buildSystem != null) buildSystem.LayersChanged -= OnLayersChanged;
    }

    void Update()
    {
        // La demande de confirmation de suppression expire au bout de 3 secondes.
        if (removeArmed && Time.unscaledTime > removeArmedUntil)
        {
            removeArmed = false;
            Rebuild();
        }
    }

    void OnLayersChanged()
    {
        removeArmed = false;
        Rebuild();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void BuildCanvas()
    {
        var canvasGO = new GameObject("LayerBarCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // au-dessus du menu de construction

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvasT = canvasGO.transform;

        // Conteneur des boutons (pas de fond : il ne bloque pas les clics).
        barRoot = NewRect("Bar", canvasT);
        barRoot.anchorMin = Vector2.zero;
        barRoot.anchorMax = Vector2.one;
        barRoot.offsetMin = Vector2.zero;
        barRoot.offsetMax = Vector2.zero;

        BuildSizePanel(canvasT);
    }

    void BuildSizePanel(Transform canvasT)
    {
        var panelRT = NewRect("SizePanel", canvasT);
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(1f, 1f);
        panelRT.sizeDelta = new Vector2(300f, 232f);
        panelRT.anchoredPosition = new Vector2(-margin.x, -(margin.y + buttonSize.y + spacing));
        panelRT.gameObject.AddComponent<Image>().color = panelColor;
        sizePanel = panelRT.gameObject;

        sizeTitle = CreateText(panelRT, "Title", "", 22, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
        SetTopLeft(sizeTitle.rectTransform, 16f, 10f, 268f, 32f);

        var wl = CreateText(panelRT, "WidthLabel", "Largeur", 20, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
        SetTopLeft(wl.rectTransform, 16f, 56f, 120f, 36f);
        widthInput = CreateInput(panelRT, "WidthInput", 140f, 56f, 144f, 36f);

        var hl = CreateText(panelRT, "HeightLabel", "Hauteur", 20, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
        SetTopLeft(hl.rectTransform, 16f, 104f, 120f, 36f);
        heightInput = CreateInput(panelRT, "HeightInput", 140f, 104f, 144f, 36f);

        var hint = CreateText(panelRT, "Hint", "Redimensionner efface les objets de cette couche.",
                              14, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f), FontStyle.Italic);
        SetTopLeft(hint.rectTransform, 16f, 148f, 268f, 28f);

        var apply = CreateButton(panelRT, "Apply", "Appliquer", addColor);
        SetTopLeft((RectTransform)apply.transform, 16f, 184f, 268f, 40f);
        apply.onClick.AddListener(ApplySize);

        sizePanel.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Contenu dynamique
    // ------------------------------------------------------------------

    void Rebuild()
    {
        if (barRoot == null) return;

        for (int i = barRoot.childCount - 1; i >= 0; i--)
            Destroy(barRoot.GetChild(i).gameObject);

        int count = buildSystem.LayerCount;
        int active = buildSystem.ActiveLayerIndex;
        var buttons = new List<Button>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            var b = CreateButton(barRoot, "Layer" + (i + 1), (i + 1).ToString(), i == active ? activeColor : layerColor);
            b.onClick.AddListener(() => buildSystem.SetActiveLayer(index));
            buttons.Add(b);
        }

        if (count < buildSystem.MaxLayers)
        {
            var add = CreateButton(barRoot, "Add", "+", addColor);
            add.onClick.AddListener(() => buildSystem.AddLayer());
            buttons.Add(add);
        }

        if (count > 1)
        {
            var remove = CreateButton(barRoot, "Remove", removeArmed ? "Sûr ?" : "-", removeColor);
            remove.onClick.AddListener(OnRemoveClicked);
            buttons.Add(remove);
        }

        var size = CreateButton(barRoot, "Size", "L×H", toolColor);
        size.onClick.AddListener(ToggleSizePanel);
        buttons.Add(size);

        // Alignés à droite : le dernier bouton est collé au bord, les autres s'étendent vers la gauche.
        int total = buttons.Count;
        for (int k = 0; k < total; k++)
        {
            var rt = (RectTransform)buttons[k].transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = buttonSize;
            rt.anchoredPosition = new Vector2(-(margin.x + (total - 1 - k) * (buttonSize.x + spacing)), -margin.y);
        }

        RefreshSizePanel();
    }

    void OnRemoveClicked()
    {
        if (!removeArmed)
        {
            removeArmed = true;
            removeArmedUntil = Time.unscaledTime + 3f;
            Rebuild();
            return;
        }

        removeArmed = false;
        buildSystem.RemoveLayer(buildSystem.ActiveLayerIndex); // déclenche LayersChanged -> Rebuild
    }

    void ToggleSizePanel()
    {
        sizePanel.SetActive(!sizePanel.activeSelf);
        RefreshSizePanel();
    }

    void RefreshSizePanel()
    {
        if (sizePanel == null || !sizePanel.activeSelf) return;

        var grid = buildSystem.ActiveGrid;
        sizeTitle.text = "Grille - couche " + (buildSystem.ActiveLayerIndex + 1);
        widthInput.text = grid.Width.ToString();
        heightInput.text = grid.Height.ToString();
    }

    void ApplySize()
    {
        if (!int.TryParse(widthInput.text, out int w)) w = buildSystem.ActiveGrid.Width;
        if (!int.TryParse(heightInput.text, out int h)) h = buildSystem.ActiveGrid.Height;

        w = Mathf.Clamp(w, 1, maxGridSize);
        h = Mathf.Clamp(h, 1, maxGridSize);
        buildSystem.ResizeLayer(buildSystem.ActiveLayerIndex, w, h); // déclenche LayersChanged -> Rebuild
    }

    // ------------------------------------------------------------------
    // Petits utilitaires d'interface
    // ------------------------------------------------------------------

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

        var text = CreateText(rt, "Label", label, 26, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(4f, 4f);
        text.rectTransform.offsetMax = new Vector2(-4f, -4f);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = 26;
        return button;
    }

    Text CreateText(Transform parent, string name, string content, int fontSize,
                    TextAnchor alignment, Color color, FontStyle style)
    {
        var rt = NewRect(name, parent);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        return text;
    }

    InputField CreateInput(Transform parent, string name, float x, float y, float w, float h)
    {
        var rt = NewRect(name, parent);
        SetTopLeft(rt, x, y, w, h);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.14f);

        var input = rt.gameObject.AddComponent<InputField>();
        input.targetGraphic = bg;

        var text = CreateText(rt, "Text", "", 20, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
        StretchWithPadding(text.rectTransform, 8f, 4f);
        text.supportRichText = false;

        var placeholder = CreateText(rt, "Placeholder", "...", 20, TextAnchor.MiddleLeft,
                                     new Color(1f, 1f, 1f, 0.4f), FontStyle.Italic);
        StretchWithPadding(placeholder.rectTransform, 8f, 4f);

        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = InputField.ContentType.IntegerNumber;
        input.characterLimit = 4;
        return input;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Position et taille depuis le coin haut-gauche du parent.
    static void SetTopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void StretchWithPadding(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}