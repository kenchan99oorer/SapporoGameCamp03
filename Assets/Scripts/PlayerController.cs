using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerController : MonoBehaviour
{
    [SerializeField] private OrbitCameraController cameraController;
    [SerializeField, Min(0.0f)] private float moveSpeed = 4.0f;

    [Header("Movable Area")]
    [Tooltip("Box the player is constrained to. If empty, detected at Start using areaMask.")]
    [SerializeField] private BoxCollider movableArea;
    [Tooltip("Layers holding movable-area trigger boxes. Used by DetectMovableArea().")]
    [SerializeField] private LayerMask areaMask;
    [Tooltip("Keeps the player's edge inside the area, in world units (e.g. the player's half-width).")]
    [SerializeField, Min(0.0f)] private float edgeMargin = 0.5f;
    [Tooltip("Also clamp height. Off by default, since the pivot usually sits at the feet.")]
    [SerializeField] private bool clampHeight = false;

    [Header("Teleport")]
    [Tooltip("Seconds after a teleport during which further teleports are ignored.")]
    [SerializeField, Min(0.0f)] private float teleportCooldown = 0.25f;

#if ENABLE_INPUT_SYSTEM
    [Header("Input (Input System)")]
    [SerializeField] private Key rotateLeftKey = Key.LeftArrow;
    [SerializeField] private Key rotateRightKey = Key.RightArrow;
    [SerializeField] private Key moveLeftKey = Key.A;
    [SerializeField] private Key moveRightKey = Key.D;
#else
    [Header("Input (Legacy)")]
    [SerializeField] private KeyCode rotateLeftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rotateRightKey = KeyCode.RightArrow;
    [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode moveRightKey = KeyCode.D;
#endif

    private readonly Collider[] overlapBuffer = new Collider[8];
    private float lastTeleportTime = float.NegativeInfinity;

    public BoxCollider MovableArea => movableArea;
    public bool CanTeleport => Time.time - lastTeleportTime >= teleportCooldown;

    /// <summary>Horizontal direction toward the camera at its destination yaw.</summary>
    public Vector3 DirectionToCamera =>
        OrbitCameraController.DirectionToCamera(cameraController != null ? cameraController.TargetYaw : 0.0f);

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<OrbitCameraController>();
        }
    }

    private void Start()
    {
        if (movableArea == null)
        {
            if (!DetectMovableArea())
            {
                Debug.LogWarning($"{nameof(PlayerController)}: no movable area found at start; movement is unconstrained.", this);
            }
        }
        else
        {
            transform.position = ClampToArea(transform.position);
        }
    }

    private void Update()
    {
        int rotate = ReadRotateInput();
        if (rotate != 0 && cameraController != null)
        {
            cameraController.BeginStep(rotate);
        }

        int move = ReadMoveInput();
        if (move != 0)
        {
            // Move along the camera's screen-right axis so A/D stay left/right after the
            // view rotates. TargetYaw switches the axis as soon as the rotation key is
            // pressed rather than drifting with the swing.
            float yaw = cameraController != null ? cameraController.TargetYaw : 0.0f;
            Vector3 right = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.right;
            Vector3 next = transform.position + right * (move * moveSpeed * Time.deltaTime);
            transform.position = ClampToArea(next);
        }
    }

    // ------------------------------------------------------------------
    // Movable area
    // ------------------------------------------------------------------

    /// <summary>
    /// Constrain the player to a new area (e.g. after a teleport) and pull the player
    /// inside it immediately. Pass null to remove the constraint.
    /// </summary>
    public void SetMovableArea(BoxCollider area)
    {
        movableArea = area;
        if (movableArea != null)
        {
            transform.position = ClampToArea(transform.position);
        }
    }

    /// <summary>
    /// Find the area box containing the player's pivot and constrain to it. If several
    /// overlap at this point, the one whose center is nearest wins. An area passed as
    /// avoid is only chosen if nothing else contains the point.
    /// </summary>
    public bool DetectMovableArea(BoxCollider avoid = null)
    {
        Vector3 p = transform.position;
        int n = Physics.OverlapSphereNonAlloc(p, 0.01f, overlapBuffer, areaMask, QueryTriggerInteraction.Collide);

        BoxCollider best = null;
        float bestDist = float.MaxValue;
        bool avoidFound = false;
        for (int i = 0; i < n; ++i)
        {
            if (overlapBuffer[i] is BoxCollider box)
            {
                if (box == avoid)
                {
                    avoidFound = true;
                    continue;
                }
                float d = (box.bounds.center - p).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = box;
                }
            }
        }

        if (best == null && avoidFound)
        {
            best = avoid;
        }
        if (best == null)
        {
            return false;
        }
        SetMovableArea(best);
        return true;
    }

    // ------------------------------------------------------------------
    // Teleport
    // ------------------------------------------------------------------

    /// <summary>
    /// Teleport to a world position, re-detect the movable area there (preferring a
    /// different area from the one being left), and optionally move to the area's
    /// camera-side edge, turn to face the camera, and slide the camera over.
    /// </summary>
    public void TeleportTo(Vector3 destination, bool snapToFront = true, bool faceCamera = true, bool recenterCamera = true)
    {
        lastTeleportTime = Time.time;

        BoxCollider previous = movableArea;
        transform.position = destination;
        if (!DetectMovableArea(avoid: previous))
        {
            // Destination isn't inside any area; keep the old constraint.
            SetMovableArea(previous);
        }

        Vector3 toCamera = DirectionToCamera;

        if (snapToFront)
        {
            transform.position = SnapToFront(transform.position, toCamera);
        }
        if (faceCamera)
        {
            transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }
        if (recenterCamera && cameraController != null)
        {
            cameraController.RecenterOnPlayer();
        }
    }

    /// <summary>
    /// Clamp a world position into the movable area, inset by edgeMargin on the
    /// horizontal axes. Done in the box's local space, so rotated boxes are handled.
    /// </summary>
    private Vector3 ClampToArea(Vector3 world)
    {
        if (movableArea == null)
        {
            return world;
        }

        Transform t = movableArea.transform;
        Vector3 local = t.InverseTransformPoint(world) - movableArea.center;
        GetInsetExtents(out float hx, out float hz, out float hy);

        local.x = Mathf.Clamp(local.x, -hx, hx);
        local.z = Mathf.Clamp(local.z, -hz, hz);
        if (clampHeight)
        {
            local.y = Mathf.Clamp(local.y, -hy, hy);
        }

        return t.TransformPoint(local + movableArea.center);
    }

    /// <summary>
    /// Clamp into the area, then push to its edge nearest the camera along the view
    /// axis. Lateral position is kept.
    /// </summary>
    private Vector3 SnapToFront(Vector3 world, Vector3 toCamera)
    {
        if (movableArea == null)
        {
            return world;
        }

        world = ClampToArea(world);

        Transform t = movableArea.transform;
        Vector3 local = t.InverseTransformPoint(world) - movableArea.center;
        Vector3 d = t.InverseTransformDirection(toCamera);
        GetInsetExtents(out float hx, out float hz, out _);

        // With 90 degree yaw steps one local axis carries the whole camera direction.
        if (Mathf.Abs(d.x) > 0.5f)
        {
            local.x = Mathf.Sign(d.x) * hx;
        }
        if (Mathf.Abs(d.z) > 0.5f)
        {
            local.z = Mathf.Sign(d.z) * hz;
        }

        return t.TransformPoint(local + movableArea.center);
    }

    /// <summary>Area half-extents in its local units, with X/Z inset by edgeMargin.</summary>
    private void GetInsetExtents(out float hx, out float hz, out float hy)
    {
        Vector3 scale = movableArea.transform.lossyScale;
        Vector3 half = movableArea.size * 0.5f;

        // edgeMargin is in world units; convert per axis to the box's local units.
        hx = Mathf.Max(half.x - edgeMargin / Mathf.Max(Mathf.Abs(scale.x), 1e-5f), 0.0f);
        hz = Mathf.Max(half.z - edgeMargin / Mathf.Max(Mathf.Abs(scale.z), 1e-5f), 0.0f);
        hy = half.y;
    }

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------

    private int ReadRotateInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[rotateLeftKey].wasPressedThisFrame)  dir -= 1;
        if (kb[rotateRightKey].wasPressedThisFrame) dir += 1;
#else
        if (Input.GetKeyDown(rotateLeftKey))  dir -= 1;
        if (Input.GetKeyDown(rotateRightKey)) dir += 1;
#endif
        return dir;
    }

    private int ReadMoveInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[moveLeftKey].isPressed)  dir -= 1;
        if (kb[moveRightKey].isPressed) dir += 1;
#else
        // Held, not pressed-this-frame, to match isPressed above.
        if (Input.GetKey(moveLeftKey))  dir -= 1;
        if (Input.GetKey(moveRightKey)) dir += 1;
#endif
        return dir;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (movableArea == null)
        {
            return;
        }
        Gizmos.color = Color.green;
        Gizmos.matrix = movableArea.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(movableArea.center, movableArea.size);
    }
#endif
}
