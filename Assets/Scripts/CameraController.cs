using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Orbits the attached camera around a world-space target in 90 degree yaw steps.
/// Left/Right input rotates counterclockwise/clockwise (as seen from above).
/// The target does not follow the player continuously; call RecenterOnPlayer(),
/// MoveTargetTo() or SnapTargetTo() from your own events to slide it.
/// Attach to the Main Camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class OrbitCameraController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Initial target position. Overridden by the player position on start if recenterOnStart is set.")]
    [SerializeField] private Vector3 targetPosition = Vector3.zero;
    [SerializeField] private Transform player;
    [Tooltip("Offset applied to the player position when recentering (e.g. aim at torso height).")]
    [SerializeField] private Vector3 playerOffset = Vector3.zero;
    [Tooltip("Keep the current target height when recentering, so jumps/slopes don't bob the camera.")]
    [SerializeField] private bool lockTargetHeight = false;
    [SerializeField] private bool recenterOnStart = true;

    [Header("Placement")]
    [SerializeField, Min(0.01f)] private float distance = 8.0f;
    [Tooltip("Downward pitch in degrees. 35.264 gives a true isometric view.")]
    [SerializeField, Range(0.0f, 90.0f)] private float pitch = 35.264f;
    [Tooltip("Initial yaw in degrees.")]
    [SerializeField] private float startYaw = 45.0f;

    [Header("Projection")]
    [SerializeField, Min(0.01f)] private float orthographicSize = 5.0f;

    [Header("Rotation")]
    [SerializeField] private float stepDegrees = 90.0f;
    [Tooltip("Seconds per step. 0 snaps instantly.")]
    [SerializeField, Min(0.0f)] private float rotationDuration = 0.25f;
    [Tooltip("Swap the direction associated with left/right.")]
    [SerializeField] private bool invertDirection = false;

    [Header("Target Slide")]
    [Tooltip("Default seconds for a target slide. 0 snaps instantly.")]
    [SerializeField, Min(0.0f)] private float slideDuration = 0.5f;

//#if ENABLE_INPUT_SYSTEM
//    [Header("Input (Input System)")]
//    [SerializeField] private Key rotateLeftKey = Key.LeftArrow;
//    [SerializeField] private Key rotateRightKey = Key.RightArrow;
//#else
//    [Header("Input (Legacy)")]
//    [SerializeField] private KeyCode rotateLeftKey = KeyCode.LeftArrow;
//    [SerializeField] private KeyCode rotateRightKey = KeyCode.RightArrow;
//#endif

    /// <summary>Raised when a target slide reaches its destination.</summary>
    public event Action<Vector3> TargetSlideCompleted;

    private Camera cam;

    // Rotation state.
    private float currentYaw;
    private float fromYaw;
    private float toYaw;
    private float rotElapsed;
    private bool rotating;

    // Target slide state.
    private Vector3 currentTarget;
    private Vector3 slideFrom;
    private Vector3 slideTo;
    private float slideElapsed;
    private float activeSlideDuration;
    private bool sliding;

    public bool IsRotating => rotating;
    public bool IsSliding => sliding;
    public Vector3 CurrentTarget => currentTarget;

    /// <summary>Yaw the camera is currently at (interpolated during a rotation).</summary>
    public float CurrentYaw => currentYaw;

    /// <summary>Yaw the camera is rotating toward. Equals CurrentYaw when settled.</summary>
    public float TargetYaw => toYaw;

    /// <summary>
    /// Horizontal (XZ) unit direction from the target toward a camera at the given yaw.
    /// </summary>
    public static Vector3 DirectionToCamera(float yaw)
    {
        float r = yaw * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(r), 0.0f, -Mathf.Cos(r));
    }

    public Transform Player
    {
        get => player;
        set => player = value;
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        ConfigureProjection();

        currentYaw = fromYaw = toYaw = startYaw;
        currentTarget = slideFrom = slideTo = targetPosition;
    }

    private void Start()
    {
        // Done in Start so the player has had a chance to spawn/position in Awake.
        if (recenterOnStart && player != null)
        {
            SnapTargetTo(ResolvePlayerTarget());
        }
        ApplyTransform(currentYaw, currentTarget);
    }

    private void Update()
    {
        bool dirty = TickRotation(Time.unscaledDeltaTime);
        dirty |= TickSlide(Time.unscaledDeltaTime);

        if (dirty)
        {
            ApplyTransform(currentYaw, currentTarget);
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// direction: +1 = clockwise (right), -1 = counterclockwise (left), viewed from above.
    /// Presses during an active rotation queue onto the destination yaw.
    /// </summary>
    public void BeginStep(int direction)
    {
        if (invertDirection)
        {
            direction = -direction;
        }

        fromYaw = currentYaw;
        toYaw += direction * stepDegrees;
        rotElapsed = 0.0f;
        rotating = true;
    }

    /// <summary>
    /// Slide the target to the player's current position (plus offset).
    /// Parameterless so it can be wired directly to a UnityEvent in the inspector.
    /// </summary>
    public void RecenterOnPlayer()
    {
        if (player == null)
        {
            Debug.LogWarning($"{nameof(OrbitCameraController)}: RecenterOnPlayer called with no player assigned.", this);
            return;
        }
        MoveTargetTo(ResolvePlayerTarget(), slideDuration);
    }

    /// <summary>Slide the target to the player using a custom duration.</summary>
    public void RecenterOnPlayer(float duration)
    {
        if (player == null)
        {
            return;
        }
        MoveTargetTo(ResolvePlayerTarget(), duration);
    }

    /// <summary>
    /// Slide the target to an arbitrary world position. A negative duration uses
    /// the default slideDuration. Retriggering mid-slide restarts from the current
    /// interpolated position, so there is no visible pop.
    /// </summary>
    public void MoveTargetTo(Vector3 worldPosition, float duration = -1.0f)
    {
        activeSlideDuration = duration < 0.0f ? slideDuration : duration;

        if (activeSlideDuration <= 0.0f)
        {
            SnapTargetTo(worldPosition);
            return;
        }

        slideFrom = currentTarget;
        slideTo = worldPosition;
        slideElapsed = 0.0f;
        sliding = true;
    }

    /// <summary>Move the target instantly, cancelling any slide in progress.</summary>
    public void SnapTargetTo(Vector3 worldPosition)
    {
        currentTarget = slideFrom = slideTo = worldPosition;
        sliding = false;
        ApplyTransform(currentYaw, currentTarget);
        TargetSlideCompleted?.Invoke(currentTarget);
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private Vector3 ResolvePlayerTarget()
    {
        Vector3 p = player.position + playerOffset;
        if (lockTargetHeight)
        {
            p.y = currentTarget.y;
        }
        return p;
    }

    private bool TickRotation(float dt)
    {
        if (!rotating)
        {
            return false;
        }

        rotElapsed += dt;
        float t = rotationDuration > 0.0f ? Mathf.Clamp01(rotElapsed / rotationDuration) : 1.0f;
        currentYaw = Mathf.LerpUnclamped(fromYaw, toYaw, Smoothstep(t));

        if (t >= 1.0f)
        {
            // Keep yaw bounded so repeated presses don't accumulate large values.
            toYaw = Mathf.Repeat(toYaw, 360.0f);
            currentYaw = fromYaw = toYaw;
            rotating = false;
        }
        return true;
    }

    private bool TickSlide(float dt)
    {
        if (!sliding)
        {
            return false;
        }

        slideElapsed += dt;
        float t = Mathf.Clamp01(slideElapsed / activeSlideDuration);
        currentTarget = Vector3.LerpUnclamped(slideFrom, slideTo, Smoothstep(t));

        if (t >= 1.0f)
        {
            currentTarget = slideFrom = slideTo;
            sliding = false;
            TargetSlideCompleted?.Invoke(currentTarget);
        }
        return true;
    }

    private static float Smoothstep(float t) => t * t * (3.0f - 2.0f * t);

//    private int ReadInput()
//    {
//        int dir = 0;
//#if ENABLE_INPUT_SYSTEM
//        Keyboard kb = Keyboard.current;
//        if (kb == null)
//        {
//            return 0;
//        }
//        if (kb[rotateLeftKey].wasPressedThisFrame) dir -= 1;
//        if (kb[rotateRightKey].wasPressedThisFrame) dir += 1;
//#else
//        if (Input.GetKeyDown(rotateLeftKey))  dir -= 1;
//        if (Input.GetKeyDown(rotateRightKey)) dir += 1;
//#endif
//        return dir;
//    }

    private void ConfigureProjection()
    {
        cam.orthographic = true;
        cam.orthographicSize = orthographicSize;

        // Distance doesn't affect apparent scale in ortho, but the clip planes must
        // still bracket the scene around the target.
        cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.1f);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, distance * 2.0f + 50.0f);
    }

    private void ApplyTransform(float yaw, Vector3 target)
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0.0f);
        transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Live preview of placement/projection in the editor.
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }
        if (cam != null)
        {
            ConfigureProjection();
        }
        if (!Application.isPlaying)
        {
            Vector3 preview = (recenterOnStart && player != null) ? player.position + playerOffset : targetPosition;
            ApplyTransform(startYaw, preview);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 t = Application.isPlaying ? currentTarget : targetPosition;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(t, 0.25f);
        Gizmos.DrawLine(transform.position, t);

        if (Application.isPlaying && sliding)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(slideTo, 0.2f);
        }
    }
#endif
}