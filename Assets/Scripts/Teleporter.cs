using UnityEngine;

/// <summary>
/// Teleporter trigger spanning two rooms. When the player enters it, they land just past
/// the box's camera-side face, i.e. in the room closer to the camera, then (optionally)
/// step to that room's camera-side edge, face the camera, and the camera slides over.
/// Because the landing is always toward the camera, the direction of travel follows the
/// view: rotate the camera 180 degrees and the same box takes you the other way.
///
/// The player needs a kinematic Rigidbody for trigger events to fire.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class Teleporter : MonoBehaviour
{
    [Tooltip("Distance past the box's camera-side face where the player lands, in world units. " +
             "Negative lands inside the box.")]
    [SerializeField] private float landingOffset = 0.25f;
    [SerializeField] private bool snapToFront = true;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private bool recenterCamera = true;

    private BoxCollider box;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.CanTeleport)
        {
            return;
        }

        Vector3 landing = GetLanding(player.transform.position, player.DirectionToCamera);
        player.TeleportTo(landing, snapToFront, faceCamera, recenterCamera);
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
