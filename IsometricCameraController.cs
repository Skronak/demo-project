using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Caméra isométrique orthographique.
/// À placer sur un objet "CameraRig" (pivot) ; la caméra en devient l'enfant.
///   - WASD (= ZQSD en AZERTY, les touches sont lues par position physique) / flèches : déplacement
///   - Clic molette + glisser : déplacement à la souris
///   - Molette : zoom
///   - Touches Q / E (position physique, = A / E en AZERTY) : rotation de 90° de la vue
/// </summary>
public class IsometricCameraController : MonoBehaviour
{
    [SerializeField] Camera cam;

    [Header("Angle")]
    [Tooltip("35.264 = vraie isométrie, 30 = isométrique « jeu vidéo ».")]
    [SerializeField] float pitch = 35.264f;
    [SerializeField] float startYaw = 45f;
    [SerializeField] float rotateSmooth = 10f;

    [Header("Zoom")]
    [SerializeField] float startZoom = 10f;
    [SerializeField] float minZoom = 3f;
    [SerializeField] float maxZoom = 60f;
    [SerializeField] float zoomStep = 0.12f; // proportion par cran de molette

    [Header("Déplacement")]
    [Tooltip("Vitesse en multiple de la taille de zoom par seconde.")]
    [SerializeField] float moveSpeed = 1.2f;

    const float CameraDistance = 100f;

    float currentYaw, targetYaw;
    Vector3 boundsMin = new Vector3(-1000, 0, -1000);
    Vector3 boundsMax = new Vector3(1000, 0, 1000);

    public Camera Camera => cam;

    void Awake()
    {
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;

        cam.orthographic = true;
        cam.orthographicSize = Mathf.Clamp(startZoom, minZoom, maxZoom);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = CameraDistance * 3f;
        cam.transform.SetParent(transform, false);
        cam.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);
        cam.transform.localRotation = Quaternion.identity;

        currentYaw = targetYaw = startYaw;
        ApplyRotation();
    }

    /// <summary>Centre la caméra sur un point du monde.</summary>
    public void FocusOn(Vector3 worldPoint)
    {
        transform.position = new Vector3(worldPoint.x, 0f, worldPoint.z);
    }

    /// <summary>Limite le déplacement du pivot (en coordonnées monde).</summary>
    public void SetBounds(Vector3 min, Vector3 max)
    {
        boundsMin = min;
        boundsMax = max;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        Quaternion flatRot = Quaternion.Euler(0f, currentYaw, 0f);
        Vector3 right = flatRot * Vector3.right;
        Vector3 forward = flatRot * Vector3.forward;

        // --- Clavier ---
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

        Vector3 move = (right * input.x + forward * input.y) * (moveSpeed * cam.orthographicSize * Time.deltaTime);

        // --- Glisser à la molette ---
        if (mouse.middleButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue();
            float unitsPerPixel = 2f * cam.orthographicSize / Screen.height;
            float sinPitch = Mathf.Max(0.1f, Mathf.Sin(pitch * Mathf.Deg2Rad));
            move -= right * (d.x * unitsPerPixel);
            move -= forward * (d.y * unitsPerPixel / sinPitch);
        }

        Vector3 p = transform.position + move;
        p.x = Mathf.Clamp(p.x, boundsMin.x, boundsMax.x);
        p.z = Mathf.Clamp(p.z, boundsMin.z, boundsMax.z);
        transform.position = p;

        // --- Zoom ---
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float size = cam.orthographicSize * (1f - Mathf.Sign(scroll) * zoomStep);
            cam.orthographicSize = Mathf.Clamp(size, minZoom, maxZoom);
        }

        // --- Rotation par pas de 90° ---
        if (kb.qKey.wasPressedThisFrame) targetYaw += 90f;
        if (kb.eKey.wasPressedThisFrame) targetYaw -= 90f;

        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, 1f - Mathf.Exp(-rotateSmooth * Time.deltaTime));
        ApplyRotation();
    }

    void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0f);
    }
}
