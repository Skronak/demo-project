using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fond de ciel animé : un seul quad, enfant de la caméra, placé derrière toute la scène,
/// qui affiche le shader Building/SkyClouds (dégradé + nuages qui défilent lentement).
///
/// Utilisation : ajouter ce composant à un objet vide (ex. "Sky"). Le shader est assigné
/// automatiquement à l'ajout du composant ; sinon glissez SkyClouds.shader dans « Sky Shader ».
/// Le quad suit la caméra et s'adapte au zoom : les nuages bougent doucement, avec un léger parallaxe
/// quand la caméra se déplace.
/// </summary>
public class SkyBackground : MonoBehaviour
{
    [SerializeField] Camera cam;

    [Tooltip("Shader Building/SkyClouds. Référencer le shader ici garantit qu'il est inclus dans le build.")]
    [SerializeField] Shader skyShader;

    [Tooltip("Distance du quad devant la caméra (limitée à 90 % du far clip). Doit être plus loin que la scène.")]
    [SerializeField] float distance = 150f;

    [Header("Ciel")]
    [SerializeField] Color topColor = new Color(0.25f, 0.55f, 0.95f, 1f);
    [SerializeField] Color horizonColor = new Color(0.78f, 0.90f, 1f, 1f);

    [Header("Nuages")]
    [SerializeField] Color cloudColor = Color.white;
    [SerializeField] Color cloudShadeColor = new Color(0.78f, 0.84f, 0.93f, 1f);
    [Tooltip("Taille des nuages : plus la valeur est petite, plus les nuages sont grands.")]
    [SerializeField, Range(0.01f, 0.5f)] float cloudScale = 0.08f;
    [Tooltip("Vitesse de défilement (unités monde par seconde). Valeurs faibles = nuages lents.")]
    [SerializeField] Vector2 cloudSpeed = new Vector2(0.4f, 0f);
    [Tooltip("Quantité de nuages : 0 = ciel dégagé, 1 = très couvert.")]
    [SerializeField, Range(0f, 1f)] float coverage = 0.5f;
    [Tooltip("Douceur des bords des nuages.")]
    [SerializeField, Range(0.01f, 0.5f)] float softness = 0.2f;
    [Tooltip("0 = nuages fixes par rapport à l'écran, 1 = nuages qui suivent le déplacement de la scène. " +
             "Une valeur faible donne l'impression qu'ils sont loin.")]
    [SerializeField, Range(0f, 1f)] float parallax = 0.3f;

    static readonly int TopColorId = Shader.PropertyToID("_TopColor");
    static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
    static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
    static readonly int CloudShadeColorId = Shader.PropertyToID("_CloudShadeColor");
    static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
    static readonly int CloudSpeedId = Shader.PropertyToID("_CloudSpeed");
    static readonly int CoverageId = Shader.PropertyToID("_Coverage");
    static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    static readonly int OffsetId = Shader.PropertyToID("_Offset");

    Transform quad;
    Material material;
    Mesh mesh;

#if UNITY_EDITOR
    // À l'ajout du composant dans l'éditeur : référence le shader pour qu'il soit sérialisé (donc inclus dans les builds).
    void Reset()
    {
        skyShader = Shader.Find("Building/SkyClouds");
    }
#endif

    void Start()
    {
        if (cam == null)
        {
            var rig = FindFirstObjectByType<IsometricCameraController>();
            cam = rig != null ? rig.Camera : Camera.main;
        }
        if (cam == null)
        {
            Debug.LogError("SkyBackground : aucune caméra trouvée.");
            enabled = false;
            return;
        }

        if (skyShader == null) skyShader = Shader.Find("Building/SkyClouds");
        if (skyShader == null)
        {
            Debug.LogError("SkyBackground : shader « Building/SkyClouds » introuvable. Ajoutez SkyClouds.shader au projet.");
            enabled = false;
            return;
        }

        // La caméra ne doit rien dessiner d'autre que notre ciel en arrière-plan.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = horizonColor;

        material = new Material(skyShader) { name = "SkyClouds (instance)" };
        mesh = CreateQuadMesh();

        var go = new GameObject("SkyQuad");
        go.transform.SetParent(cam.transform, false);
        go.transform.localRotation = Quaternion.identity;
        quad = go.transform;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        ApplyProperties();
        UpdateQuad();
    }

    void LateUpdate()
    {
        if (quad == null) return;
        UpdateQuad();
    }

    // Placé après le déplacement de la caméra : le quad couvre toujours exactement la vue.
    void UpdateQuad()
    {
        float dist = Mathf.Min(distance, cam.farClipPlane * 0.9f);
        quad.localPosition = new Vector3(0f, 0f, dist);

        float h = cam.orthographic
            ? 2f * cam.orthographicSize
            : 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        quad.localScale = new Vector3(w * 1.02f, h * 1.02f, 1f); // petite marge pour éviter les bords

        // Parallaxe : on décale les nuages d'une fraction du déplacement de la caméra, dans le plan de l'écran.
        Transform ct = cam.transform;
        Vector3 p = ct.position;
        float ox = Vector3.Dot(p, ct.right) * parallax;
        float oy = Vector3.Dot(p, ct.up) * parallax;
        material.SetVector(OffsetId, new Vector4(ox, oy, 0f, 0f));
    }

    void ApplyProperties()
    {
        if (material == null) return;
        material.SetColor(TopColorId, topColor);
        material.SetColor(HorizonColorId, horizonColor);
        material.SetColor(CloudColorId, cloudColor);
        material.SetColor(CloudShadeColorId, cloudShadeColor);
        material.SetFloat(CloudScaleId, cloudScale);
        material.SetVector(CloudSpeedId, new Vector4(cloudSpeed.x, cloudSpeed.y, 0f, 0f));
        material.SetFloat(CoverageId, coverage);
        material.SetFloat(SoftnessId, softness);
    }

    // Permet de régler les paramètres dans l'inspecteur pendant que le jeu tourne.
    void OnValidate()
    {
        ApplyProperties();
        if (cam != null && Application.isPlaying) cam.backgroundColor = horizonColor;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }

    // Quad centré de 1x1 dans le plan XY, uv de (0,0) à (1,1).
    static Mesh CreateQuadMesh()
    {
        var m = new Mesh { name = "SkyQuad" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }
}
