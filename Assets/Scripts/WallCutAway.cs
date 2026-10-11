using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Raycast-driven wall cutaway. For each enabled CutawayBox, casts toward the camera
/// and hides every wall renderer whose collider the ray crosses. Evaluated only on camera
/// events (rotation start/complete, target slide complete) and when boxes are added or
/// removed, never per frame.
///
/// Requirements: walls need colliders (MeshCollider is fine) on a layer in wallMask.
/// Colliders stay active while their renderer is hidden, so gameplay collision and
/// later raycasts are unaffected.
///
/// Add one instance to the Main Camera. Add a CutawayBox to each box.
/// </summary>
[DisallowMultipleComponent]
public class WallCutaway : MonoBehaviour
{
    [SerializeField] private OrbitCameraController cameraController;

    [Tooltip("Layers containing wall colliders. Keep floors and ceilings off these layers.")]
    [SerializeField] private LayerMask wallMask = ~0;

    [Tooltip("Radius of a ring of extra rays around each center ray. Makes hits reliable when " +
             "the center ray exits through a wall seam or corner. 0 casts a single ray.")]
    [SerializeField, Min(0.0f)] private float sampleRadius = 0.25f;
    [SerializeField, Range(0, 16)] private int ringSamples = 0;

    [Tooltip("Hidden walls still cast shadows, so interior lighting doesn't change.")]
    [SerializeField] private bool keepShadows = true;

    [SerializeField] private bool drawDebugRays = false;

    private const int MaxHitsPerRay = 32;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxHitsPerRay];
    private readonly Dictionary<Collider, Renderer> rendererCache = new();
    private readonly Dictionary<Renderer, ShadowCastingMode> originalShadows = new();
    private HashSet<Renderer> hidden = new();
    private HashSet<Renderer> scratch = new();
    private bool registryDirty;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        if (cameraController == null && !TryGetComponent(out cameraController))
        {
            cameraController = FindFirstObjectByType<OrbitCameraController>();
        }
        if (cameraController == null)
        {
            Debug.LogWarning($"{nameof(WallCutaway)}: no OrbitCameraController found.", this);
            enabled = false;
            return;
        }

        cameraController.RotationStarted += HandleRotationStarted;
        cameraController.RotationCompleted += HandleRotationCompleted;
        cameraController.TargetSlideCompleted += HandleSlideCompleted;
        CutawayBox.RegistryChanged += HandleRegistryChanged;

        // Boxes that enabled before us are already in the registry; evaluate them.
        registryDirty = true;
    }

    private void OnDisable()
    {
        if (cameraController != null)
        {
            cameraController.RotationStarted -= HandleRotationStarted;
            cameraController.RotationCompleted -= HandleRotationCompleted;
            cameraController.TargetSlideCompleted -= HandleSlideCompleted;
        }
        CutawayBox.RegistryChanged -= HandleRegistryChanged;
        RestoreAll();
    }

    private void LateUpdate()
    {
        // Coalesces registry changes, so a stage enabling many boxes in one frame
        // costs one evaluation.
        if (registryDirty)
        {
            registryDirty = false;
            Refresh();
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>Re-evaluate against the camera's destination pose.</summary>
    public void Refresh()
    {
        if (cameraController == null)
        {
            return;
        }
        Evaluate(cameraController.TargetYaw, cameraController.TargetDestination, cameraController.IsRotating);
    }

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    // Rotation start: add the walls blocking the destination view, but keep the ones
    // blocking the current view hidden, so nothing swings across the screen mid-turn.
    private void HandleRotationStarted(float destinationYaw) =>
        Evaluate(destinationYaw, cameraController.TargetDestination, additive: true);

    // Rotation complete: settle to exactly the walls blocking the final view.
    private void HandleRotationCompleted(float yaw) =>
        Evaluate(yaw, cameraController.TargetDestination, additive: false);

    // The camera moves with its target, so re-evaluate after a slide too.
    private void HandleSlideCompleted(Vector3 _) => Refresh();

    private void HandleRegistryChanged() => registryDirty = true;

    // ------------------------------------------------------------------
    // Evaluation
    // ------------------------------------------------------------------

    private void Evaluate(float yaw, Vector3 target, bool additive)
    {
        Quaternion camRot = cameraController.RotationForYaw(yaw);
        Vector3 camPos = cameraController.PositionFor(yaw, target);
        bool ortho = cameraController.Camera.orthographic;

        scratch.Clear();

        // Rays start inside each box and exit through the walls' inner faces. With
        // single-sided wall meshes those are back faces, which PhysX skips by default.
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            IReadOnlyList<CutawayBox> boxes = CutawayBox.Active;
            for (int b = 0; b < boxes.Count; ++b)
            {
                Vector3 center = boxes[b].WorldCenter;
                Vector3 dir;
                float length;

                if (ortho)
                {
                    // Ortho view rays are parallel to the camera axis, so the occluders of a
                    // point lie along -forward up to the camera plane, not on the line to the
                    // camera position. The two only coincide for a box at the camera target.
                    dir = camRot * Vector3.back;
                    length = Vector3.Dot(camPos - center, dir);
                }
                else
                {
                    Vector3 delta = camPos - center;
                    length = delta.magnitude;
                    dir = length > 1e-5f ? delta / length : camRot * Vector3.back;
                }

                if (length > 0.0f)
                {
                    CastSamples(center, dir, length);
                }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = prevBackfaces;
        }

        if (additive)
        {
            scratch.UnionWith(hidden);
        }
        Apply();
    }

    private void CastSamples(Vector3 origin, Vector3 dir, float length)
    {
        CastOne(origin, dir, length);

        if (sampleRadius <= 0.0f || ringSamples == 0)
        {
            return;
        }

        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 1e-6f)
        {
            right = Vector3.Cross(Vector3.forward, dir);
        }
        right.Normalize();
        Vector3 up = Vector3.Cross(dir, right);

        float step = Mathf.PI * 2.0f / ringSamples;
        for (int i = 0; i < ringSamples; ++i)
        {
            float a = i * step;
            Vector3 offset = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * sampleRadius;
            CastOne(origin + offset, dir, length);
        }
    }

    private void CastOne(Vector3 origin, Vector3 dir, float length)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, hitBuffer, length, wallMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; ++i)
        {
            Renderer r = ResolveRenderer(hitBuffer[i].collider);
            if (r != null)
            {
                scratch.Add(r);
            }
        }

        if (drawDebugRays)
        {
            Debug.DrawRay(origin, dir * length, n > 0 ? Color.red : Color.green, 2.0f);
        }
    }

    private Renderer ResolveRenderer(Collider c)
    {
        if (!rendererCache.TryGetValue(c, out Renderer r))
        {
            if (!c.TryGetComponent(out r))
            {
                r = c.GetComponentInParent<Renderer>();
            }
            rendererCache[c] = r;   // cache misses too
        }
        return r;
    }

    // ------------------------------------------------------------------
    // Visibility
    // ------------------------------------------------------------------

    private void Apply()
    {
        // Drop walls destroyed along with their box.
        hidden.RemoveWhere(r => r == null);

        foreach (Renderer r in hidden)
        {
            if (!scratch.Contains(r))
            {
                SetHidden(r, false);
            }
        }
        foreach (Renderer r in scratch)
        {
            if (!hidden.Contains(r))
            {
                SetHidden(r, true);
            }
        }
        (hidden, scratch) = (scratch, hidden);
    }

    private void RestoreAll()
    {
        foreach (Renderer r in hidden)
        {
            if (r != null)
            {
                SetHidden(r, false);
            }
        }
        hidden.Clear();
    }

    private void SetHidden(Renderer r, bool hide)
    {
        if (!originalShadows.TryGetValue(r, out ShadowCastingMode original))
        {
            original = r.shadowCastingMode;
            originalShadows[r] = original;
        }

        if (keepShadows)
        {
            r.enabled = true;
            r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : original;
        }
        else
        {
            r.shadowCastingMode = original;
            r.enabled = !hide;
        }
    }
}
