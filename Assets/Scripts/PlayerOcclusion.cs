using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Keeps the player visible through walls and reports when another box hides it.
///
/// Rendering: appends an OcclusionXRay material to the player's renderers, so any part
/// of the player behind geometry is drawn in a flat color.
///
/// Logic: each frame, casts a few rays from the player's bounds toward the camera against
/// occluderMask. A hit on any box other than the one the player is currently in counts
/// as occluded (the player's own box is excluded, since its camera-side walls are culled
/// by the inverted normals but still have colliders). On a change, optionally tints the
/// player's base color and raises OcclusionChanged.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[DisallowMultipleComponent]
public class PlayerOcclusion : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;

    [Tooltip("Layers whose colliders can hide the player: wall colliders, or each room's volume collider.")]
    [SerializeField] private LayerMask occluderMask;

    [Tooltip("Set to Collide if the occluders are trigger colliders (e.g. the rooms' movable-area boxes).")]
    [SerializeField] private QueryTriggerInteraction occluderTriggers = QueryTriggerInteraction.Ignore;

    [Tooltip("Material using Custom/OcclusionXRay. Appended to the player's renderers at runtime. " +
             "Its own color is overridden by Occluded Color.")]
    [SerializeField] private Material xRayMaterial;

    [Tooltip("Color of the hidden parts (alpha sets their opacity) and, with Tint When Occluded, " +
             "of the visible parts while occluded.")]
    [FormerlySerializedAs("occludedTint")]
    [SerializeField] private Color occludedColor = new Color(0.3f, 0.6f, 1.0f, 0.75f);

    [Tooltip("Replace the player's base color with Occluded Color while occluded.")]
    [SerializeField] private bool tintWhenOccluded = true;

    [Tooltip("How far toward the edges of the player's bounds the outer sample rays sit. 0 puts all rays at the center.")]
    [SerializeField, Range(0.0f, 1.0f)] private float sampleSpread = 0.8f;

    /// <summary>True when any sample ray is blocked by another box.</summary>
    public bool IsOccluded { get; private set; }

    /// <summary>Fraction of sample rays blocked, 0 to 1.</summary>
    public float OccludedFraction { get; private set; }

    public event Action<bool> OcclusionChanged;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit/Unlit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard
    private static readonly int XRayColorId = Shader.PropertyToID("_XRayColor");  // OcclusionXRay

    private const int SampleCount = 5;
    private readonly Vector3[] samples = new Vector3[SampleCount];
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private PlayerController player;
    private Renderer[] renderers;
    private int[] baseMaterialCounts;
    private int[] xRayIndices;
    private MaterialPropertyBlock tintBlock;
    private MaterialPropertyBlock xRayBlock;
    private MaterialPropertyBlock emptyBlock;

    private BoxCollider cachedArea;
    private Transform cachedOwnBox;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        List<Renderer> list = new();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                list.Add(r);
            }
        }
        renderers = list.ToArray();
        baseMaterialCounts = new int[renderers.Length];
        xRayIndices = new int[renderers.Length];

        tintBlock = new MaterialPropertyBlock();
        xRayBlock = new MaterialPropertyBlock();
        emptyBlock = new MaterialPropertyBlock();
    }

    private void OnValidate()
    {
        // Apply color edits live in Play mode.
        if (!Application.isPlaying || renderers == null || !isActiveAndEnabled)
        {
            return;
        }
        ApplyXRayColor();
        ApplyTint(IsOccluded && tintWhenOccluded);
    }

    private void OnEnable()
    {
        AddXRay();
    }

    private void OnDisable()
    {
        ApplyTint(false);
        RemoveXRay();
        if (IsOccluded)
        {
            IsOccluded = false;
            OccludedFraction = 0.0f;
            OcclusionChanged?.Invoke(false);
        }
    }

    private void LateUpdate()
    {
        // LateUpdate: after the player has moved and the camera has updated this frame.
        if (viewCamera == null)
        {
            return;
        }

        int blocked = CountBlockedSamples();
        OccludedFraction = blocked / (float)SampleCount;

        bool occluded = blocked > 0;
        if (occluded != IsOccluded)
        {
            IsOccluded = occluded;
            ApplyTint(occluded && tintWhenOccluded);
            OcclusionChanged?.Invoke(occluded);
        }
    }

    // ------------------------------------------------------------------
    // Occlusion test
    // ------------------------------------------------------------------

    private int CountBlockedSamples()
    {
        if (!TryGetPlayerBounds(out Bounds b))
        {
            return 0;
        }

        Transform cam = viewCamera.transform;
        Vector3 right = cam.right;
        Vector3 up = cam.up;

        // Half-size of the AABB along the camera's right and up axes.
        Vector3 e = b.extents;
        float halfW = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.y) * e.y + Mathf.Abs(right.z) * e.z;
        float halfH = Mathf.Abs(up.x) * e.x + Mathf.Abs(up.y) * e.y + Mathf.Abs(up.z) * e.z;

        Vector3 c = b.center;
        samples[0] = c;
        samples[1] = c + right * (halfW * sampleSpread);
        samples[2] = c - right * (halfW * sampleSpread);
        samples[3] = c + up * (halfH * sampleSpread);
        samples[4] = c - up * (halfH * sampleSpread);

        Transform ownBox = ResolveOwnBox();
        bool ortho = viewCamera.orthographic;
        int blocked = 0;

        // Rays from inside the player's box toward the camera can meet other boxes' walls
        // from behind; with inverted normals those are back faces, which PhysX skips by default.
        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            for (int s = 0; s < SampleCount; ++s)
            {
                Vector3 origin = samples[s];
                Vector3 dir;
                float length;

                if (ortho)
                {
                    // Ortho view rays are parallel to the camera axis.
                    dir = -cam.forward;
                    length = Vector3.Dot(cam.position - origin, dir);
                }
                else
                {
                    Vector3 delta = cam.position - origin;
                    length = delta.magnitude;
                    dir = length > 1e-5f ? delta / length : -cam.forward;
                }

                if (length > 0.0f && IsBlocked(origin, dir, length, ownBox))
                {
                    ++blocked;
                }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = prevBackfaces;
        }

        return blocked;
    }

    private bool IsBlocked(Vector3 origin, Vector3 dir, float length, Transform ownBox)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, hitBuffer, length, occluderMask, occluderTriggers);
        for (int i = 0; i < n; ++i)
        {
            Transform t = hitBuffer[i].collider.transform;
            if (ownBox != null && t.IsChildOf(ownBox))
            {
                continue;   // the player's own room never counts
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// The room the player is in: the area itself when it sits on the room root (it has the
    /// room's renderers under it), otherwise the area's parent (a MovableArea child under
    /// its room).
    /// </summary>
    private Transform ResolveOwnBox()
    {
        BoxCollider area = player.MovableArea;
        if (area != cachedArea)
        {
            cachedArea = area;
            if (area == null)
            {
                cachedOwnBox = null;
            }
            else
            {
                if (area.GetComponentInChildren<Renderer>(true) != null || area.transform.parent == null)
                {
                    cachedOwnBox = area.transform;
                }
                else
                {
                    cachedOwnBox = area.transform.parent;
                }
            }
        }
        return cachedOwnBox;
    }

    private bool TryGetPlayerBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled)
            {
                continue;
            }
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return any;
    }

    // ------------------------------------------------------------------
    // Materials
    // ------------------------------------------------------------------

    private void AddXRay()
    {
        for (int i = 0; i < renderers.Length; ++i)
        {
            Material[] mats = renderers[i].sharedMaterials;
            int existing = xRayMaterial != null ? Array.IndexOf(mats, xRayMaterial) : -1;
            baseMaterialCounts[i] = existing >= 0 ? mats.Length - 1 : mats.Length;
            xRayIndices[i] = xRayMaterial == null ? -1 : (existing >= 0 ? existing : mats.Length);

            if (xRayMaterial == null || existing >= 0)
            {
                continue;
            }

            // A material beyond the submesh count re-renders the last submesh with it.
            Material[] extended = new Material[mats.Length + 1];
            mats.CopyTo(extended, 0);
            extended[mats.Length] = xRayMaterial;
            renderers[i].sharedMaterials = extended;
        }

        ApplyXRayColor();
    }

    private void ApplyXRayColor()
    {
        xRayBlock.SetColor(XRayColorId, occludedColor);
        for (int i = 0; i < renderers.Length; ++i)
        {
            if (renderers[i] != null && xRayIndices[i] >= 0)
            {
                renderers[i].SetPropertyBlock(xRayBlock, xRayIndices[i]);
            }
        }
    }

    private void RemoveXRay()
    {
        if (xRayMaterial == null)
        {
            return;
        }
        for (int i = 0; i < renderers.Length; ++i)
        {
            Renderer r = renderers[i];
            if (r == null)
            {
                continue;
            }
            if (xRayIndices[i] >= 0)
            {
                r.SetPropertyBlock(emptyBlock, xRayIndices[i]);
                xRayIndices[i] = -1;
            }
            Material[] mats = r.sharedMaterials;
            if (Array.IndexOf(mats, xRayMaterial) < 0)
            {
                continue;
            }
            List<Material> kept = new(mats);
            kept.RemoveAll(m => m == xRayMaterial);
            r.sharedMaterials = kept.ToArray();
        }
    }

    private void ApplyTint(bool on)
    {
        // Alpha is only meaningful for the x-ray; the base color stays opaque.
        Color tint = occludedColor;
        tint.a = 1.0f;
        tintBlock.SetColor(BaseColorId, tint);
        tintBlock.SetColor(ColorId, tint);

        // Per-material blocks, so the x-ray material keeps its own color.
        for (int i = 0; i < renderers.Length; ++i)
        {
            if (renderers[i] == null)
            {
                continue;
            }
            for (int m = 0; m < baseMaterialCounts[i]; ++m)
            {
                renderers[i].SetPropertyBlock(on ? tintBlock : emptyBlock, m);
            }
        }
    }
}
