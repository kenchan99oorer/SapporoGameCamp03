using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hides the walls of a room/box that face the OrbitCameraController, so the interior
/// stays visible after each 90 degree step. Attach to the box root (e.g. box1).
/// Wall facing is derived from each wall's bounds relative to the box center, so node
/// names and export order don't matter.
/// </summary>
[DisallowMultipleComponent]
public class WallCutaway : MonoBehaviour
{
    [SerializeField] private OrbitCameraController cameraController;

    [Tooltip("Explicit wall renderers. If empty, child renderers whose name starts with the prefix are used.")]
    [SerializeField] private List<Renderer> walls = new();
    [SerializeField] private string wallNamePrefix = "側面";

    [Tooltip("Hide walls whose outward direction is within this angle of the camera direction. " +
             "80 hides one wall when axis-aligned and two walls on a 45 degree diagonal.")]
    [SerializeField, Range(0.0f, 180.0f)] private float hideAngle = 80.0f;

    [Tooltip("Hidden walls still cast shadows, so interior lighting doesn't change when they hide.")]
    [SerializeField] private bool keepShadows = true;

    [Tooltip("While rotating, also hide walls facing the destination angle so nothing swings across the view.")]
    [SerializeField] private bool hideDestinationDuringRotation = true;

    private struct Wall
    {
        public Renderer renderer;
        public Vector3 localOutward;   // outward XZ direction in box-local space
        public ShadowCastingMode originalShadows;
        public bool hidden;
    }

    private readonly List<Wall> wallStates = new();
    private float cosThreshold;

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<OrbitCameraController>();
        }
        if (cameraController == null)
        {
            Debug.LogWarning($"{nameof(WallCutaway)} on {name}: no OrbitCameraController found.", this);
            enabled = false;
            return;
        }

        CollectWalls();
    }

    private void OnValidate()
    {
        cosThreshold = Mathf.Cos(hideAngle * Mathf.Deg2Rad);
    }

    private void OnDisable()
    {
        // Restore everything so disabling the component never leaves walls missing.
        for (int i = 0; i < wallStates.Count; ++i)
        {
            Wall w = wallStates[i];
            if (w.hidden)
            {
                SetHidden(ref w, false);
                wallStates[i] = w;
            }
        }
    }

    private void LateUpdate()
    {
        // LateUpdate runs after the camera controller's Update, so yaw is current.
        Vector3 toCamNow = OrbitCameraController.DirectionToCamera(cameraController.CurrentYaw);
        Vector3 toCamDest = (hideDestinationDuringRotation && cameraController.IsRotating)
            ? OrbitCameraController.DirectionToCamera(cameraController.TargetYaw)
            : toCamNow;

        for (int i = 0; i < wallStates.Count; ++i)
        {
            Wall w = wallStates[i];
            if (w.renderer == null)
            {
                continue;
            }

            // Re-derive world outward each frame so moved/rotated boxes still work.
            Vector3 outward = transform.TransformDirection(w.localOutward);
            outward.y = 0.0f;
            outward.Normalize();

            bool hide = Vector3.Dot(outward, toCamNow) > cosThreshold ||
                        Vector3.Dot(outward, toCamDest) > cosThreshold;

            if (hide != w.hidden)
            {
                SetHidden(ref w, hide);
                wallStates[i] = w;
            }
        }
    }

    /// <summary>Rebuild the wall list, e.g. after swapping the box's mesh at runtime.</summary>
    public void CollectWalls()
    {
        OnDisable();
        wallStates.Clear();
        cosThreshold = Mathf.Cos(hideAngle * Mathf.Deg2Rad);

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        if (all.Length == 0)
        {
            return;
        }

        // Box center from the combined bounds of every renderer in the box.
        Bounds combined = all[0].bounds;
        for (int i = 1; i < all.Length; ++i)
        {
            combined.Encapsulate(all[i].bounds);
        }
        Vector3 center = combined.center;

        IEnumerable<Renderer> source = walls.Count > 0 ? walls : FindByPrefix(all);
        foreach (Renderer r in source)
        {
            if (r == null)
            {
                continue;
            }

            Vector3 d = r.bounds.center - center;
            d.y = 0.0f;
            if (d.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning($"{nameof(WallCutaway)}: '{r.name}' is centered on the box, can't determine facing. Skipped.", r);
                continue;
            }

            wallStates.Add(new Wall
            {
                renderer = r,
                localOutward = transform.InverseTransformDirection(d.normalized),
                originalShadows = r.shadowCastingMode,
                hidden = false,
            });
        }
    }

    private IEnumerable<Renderer> FindByPrefix(Renderer[] all)
    {
        foreach (Renderer r in all)
        {
            if (r.name.StartsWith(wallNamePrefix))
            {
                yield return r;
            }
        }
    }

    private void SetHidden(ref Wall w, bool hide)
    {
        if (keepShadows)
        {
            w.renderer.enabled = true;
            w.renderer.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : w.originalShadows;
        }
        else
        {
            w.renderer.shadowCastingMode = w.originalShadows;
            w.renderer.enabled = !hide;
        }
        w.hidden = hide;
    }
}