using UnityEngine;

/// <summary>
/// Teleporter trigger spanning two rooms. When the player enters it (or, with
/// requireFullyInside, once the player's collider is entirely inside it), they land just
/// past the box's camera-side face, i.e. in the room closer to the camera, then
/// (optionally) step to that room's camera-side edge, face the camera, and the camera
/// slides over. Because the landing is always toward the camera, the direction of travel
/// follows the view: rotate the camera 180 degrees and the same box takes you the other way.
///
/// The player needs a kinematic Rigidbody for trigger events to fire.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class Teleporter : MonoBehaviour
{
    [Tooltip("Only teleport once the player's collider is entirely inside the box, rather than on first contact. " +
             "The box must be at least as wide and tall as the player's collider.")]
    [SerializeField] private bool requireFullyInside = true;

    [Tooltip("Distance past the box's camera-side face where the player lands, in world units. " +
             "Negative lands inside the box, which is what you want when the box spans both rooms " +
             "wall to wall. The landing point must fall inside the front room's movable area.")]
    [SerializeField] private float landingOffset = -0.25f;
    [SerializeField] private bool snapToFront = true;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private bool recenterCamera = true;

    [Tooltip("Log trigger events, containment changes and teleports to the Console.")]
    [SerializeField] private bool debugLog = false;

    private BoxCollider box;

    // Containment tracking (requireFullyInside).
    private PlayerController trackedPlayer;
    private Collider[] trackedColliders;
    private int overlapCount;
    private bool armed = true;
    private bool lastInside;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnDisable()
    {
        ClearTracking();
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        Log($"Trigger enter: '{other.name}' (layer {LayerMask.LayerToName(other.gameObject.layer)}, " +
            $"trigger={other.isTrigger}), PlayerController {(player != null ? "found" : "NOT found")}");
        if (player == null)
        {
            return;
        }

        if (!requireFullyInside)
        {
            if (player.CanTeleport)
            {
                Teleport(player);
            }
            return;
        }

        // Track the overlap and test containment in Update. Counted per collider, in
        // case the player has more than one.
        if (trackedPlayer != player)
        {
            trackedPlayer = player;
            trackedColliders = CollectSolidColliders(player);
            overlapCount = 0;
        }
        ++overlapCount;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player != trackedPlayer)
        {
            return;
        }
        Log($"Trigger exit: '{other.name}'");
        if (--overlapCount <= 0)
        {
            ClearTracking();
        }
    }

    private void Update()
    {
        if (trackedPlayer == null)
        {
            return;
        }

        bool inside = IsFullyInside(trackedColliders);
        if (inside != lastInside)
        {
            lastInside = inside;
            Log(inside ? "Player fully inside" : $"Player overlapping but not fully inside ({DescribeOverhang()})");
        }

        if (!inside)
        {
            // Re-arm once the player is no longer fully inside, so a landing point that
            // is still inside the box can't retrigger on every cooldown.
            armed = true;
            return;
        }

        if (armed && !trackedPlayer.CanTeleport)
        {
            Log("Fully inside but teleport cooldown is active");
        }

        if (armed && trackedPlayer.CanTeleport)
        {
            armed = false;
            Teleport(trackedPlayer);
        }
    }

    private void Teleport(PlayerController player)
    {
        Vector3 landing = GetLanding(player.transform.position, player.DirectionToCamera);
        Log($"Teleporting to {landing}, toward camera {player.DirectionToCamera}. " +
            $"Colliders at landing point: {DescribeCollidersAt(landing)}");
        if (player.TeleportTo(landing, snapToFront, faceCamera, recenterCamera))
        {
            Log($"Landed at {player.transform.position}, movable area: " +
                $"{(player.MovableArea != null ? player.MovableArea.name : "none")}");
        }
        else
        {
            Log("Not teleported: the landing point isn't inside a different room's movable area " +
                "(already in the front room, or the landing point misses the room's collider)");
        }
    }

    private static string DescribeCollidersAt(Vector3 point)
    {
        Collider[] found = Physics.OverlapSphere(point, 0.01f, ~0, QueryTriggerInteraction.Collide);
        if (found.Length == 0)
        {
            return "none";
        }
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < found.Length; ++i)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append($"'{found[i].name}' ({LayerMask.LayerToName(found[i].gameObject.layer)}");
            sb.Append(found[i].isTrigger ? ", trigger)" : ")");
        }
        return sb.ToString();
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[Teleporter '{name}'] {message}", this);
        }
    }

    /// <summary>Which local axes of the box the player's colliders stick out of, for logging.</summary>
    private string DescribeOverhang()
    {
        if (trackedColliders == null || trackedColliders.Length == 0)
        {
            return "player has no solid collider";
        }

        Vector3 half = box.size * 0.5f;
        bool x = false, y = false, z = false;
        foreach (Collider c in trackedColliders)
        {
            if (c == null || !c.enabled)
            {
                continue;
            }
            Bounds b = c.bounds;
            for (int i = 0; i < 8; ++i)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = transform.InverseTransformPoint(corner) - box.center;
                x |= Mathf.Abs(local.x) > half.x;
                y |= Mathf.Abs(local.y) > half.y;
                z |= Mathf.Abs(local.z) > half.z;
            }
        }
        return $"sticks out on local {(x ? "X " : "")}{(y ? "Y " : "")}{(z ? "Z" : "")}".TrimEnd();
    }

    private void ClearTracking()
    {
        lastInside = false;
        trackedPlayer = null;
        trackedColliders = null;
        overlapCount = 0;
        armed = true;
    }

    private static Collider[] CollectSolidColliders(PlayerController player)
    {
        Collider[] all = player.GetComponentsInChildren<Collider>();
        int count = 0;
        for (int i = 0; i < all.Length; ++i)
        {
            if (!all[i].isTrigger)
            {
                all[count++] = all[i];
            }
        }
        System.Array.Resize(ref all, count);
        return all;
    }

    /// <summary>
    /// True if every corner of each collider's world bounds lies inside the box. Tested in
    /// the box's local space, so rotated boxes work. World AABBs are slightly conservative
    /// for rotated shapes, which only makes the test stricter.
    /// </summary>
    private bool IsFullyInside(Collider[] colliders)
    {
        if (colliders == null || colliders.Length == 0)
        {
            return false;
        }

        Vector3 half = box.size * 0.5f;
        foreach (Collider c in colliders)
        {
            if (c == null || !c.enabled)
            {
                continue;
            }

            Bounds b = c.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;
            for (int i = 0; i < 8; ++i)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                Vector3 local = transform.InverseTransformPoint(corner) - box.center;
                if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Landing point just past the box face that looks toward the camera, keeping the
    /// player's height and lateral position. Uses the box's local horizontal axis most
    /// aligned with the camera direction, so rotated boxes work.
    /// </summary>
    public Vector3 GetLanding(Vector3 from, Vector3 toCamera)
    {
        if (box == null)
        {
            box = GetComponent<BoxCollider>();
        }

        Vector3 local = transform.InverseTransformPoint(from) - box.center;
        Vector3 d = transform.InverseTransformDirection(toCamera);
        Vector3 half = box.size * 0.5f;
        Vector3 scale = transform.lossyScale;

        if (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
        {
            float offset = landingOffset / Mathf.Max(Mathf.Abs(scale.x), 1e-5f);
            local.x = Mathf.Sign(d.x) * (half.x + offset);
        }
        else
        {
            float offset = landingOffset / Mathf.Max(Mathf.Abs(scale.z), 1e-5f);
            local.z = Mathf.Sign(d.z) * (half.z + offset);
        }

        return transform.TransformPoint(local + box.center);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (box == null)
        {
            box = GetComponent<BoxCollider>();
        }
        if (box == null)
        {
            return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1.0f, 0.4f, 1.0f, 0.15f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1.0f, 0.4f, 1.0f, 1.0f);
        Gizmos.DrawWireCube(box.center, box.size);

        // Landing points for each of the four camera directions.
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 c = transform.TransformPoint(box.center);
        Vector3[] axes = { transform.right, -transform.right, transform.forward, -transform.forward };
        foreach (Vector3 a in axes)
        {
            Gizmos.DrawWireSphere(GetLanding(c, a), 0.15f);
        }
    }
#endif
}
