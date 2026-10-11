using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Keeps the player visible through walls and reports when another room hides it.
///
/// Rendering:
///  - MeshRenderer / SkinnedMeshRenderer (3D models, billboard quads): an OcclusionXRay
///    material is appended, so hidden parts draw in a flat color. For alpha-tested or
///    transparent base materials (typical billboards), the base texture is passed to the
///    x-ray so the silhouette follows the visible shape instead of the whole quad.
///  - SpriteRenderer: a sprite can't take a second material, so a child mirror
///    SpriteRenderer using OcclusionXRaySprite copies the sprite every frame.
///
/// Logic: each frame, casts a few rays from the player's bounds toward the camera against
/// occluderMask. A hit on any room other than the one the player is in counts as
/// occluded. On a change, optionally tints the player and raises OcclusionChanged.
///
/// Renderers are collected in Awake. Call RefreshRenderers() if the player's visuals
/// change at runtime.
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

    [Header("X-Ray")]
    [Tooltip("Material using Custom/OcclusionXRay, for mesh renderers (3D models, billboard quads).")]
    [SerializeField] private Material xRayMaterial;

    [Tooltip("Material using Custom/OcclusionXRaySprite, for SpriteRenderers.")]
    [SerializeField] private Material xRaySpriteMaterial;

    [Tooltip("Color of the hidden parts (alpha sets their opacity) and, with Tint When Occluded, " +
             "of the visible parts while occluded.")]
    [FormerlySerializedAs("occludedTint")]
    [SerializeField] private Color occludedColor = new Color(0.3f, 0.6f, 1.0f, 0.75f);

    [Tooltip("Tint the visible parts of the player with Occluded Color while occluded.")]
    [SerializeField] private bool tintWhenOccluded = true;

    [Header("Detection")]
    [Tooltip("How far toward the edges of the player's bounds the outer sample rays sit. 0 puts all rays at the center.")]
    [SerializeField, Range(0.0f, 1.0f)] private float sampleSpread = 0.8f;

    /// <summary>True when any sample ray is blocked by another room.</summary>
    public bool IsOccluded { get; private set; }

    /// <summary>Fraction of sample rays blocked, 0 to 1.</summary>
    public float OccludedFraction { get; private set; }

    public event Action<bool> OcclusionChanged;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP Lit/Unlit
    private static readonly int ColorId = Shader.PropertyToID("_Color");           // Built-in
    private static readonly int XRayColorId = Shader.PropertyToID("_XRayColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
    private const int AlphaTestQueue = 2450;

    private const int SampleCount = 5;
    private readonly Vector3[] samples = new Vector3[SampleCount];
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private struct MeshEntry
    {
        public Renderer renderer;
        public int baseCount;      // materials excluding the x-ray
        public int xRayIndex;      // -1 when no x-ray slot
        public Texture shapeTex;   // base texture for alpha silhouettes, or null
        public Vector4 shapeSt;
    }

    private struct SpriteEntry
    {
        public SpriteRenderer source;
        public SpriteRenderer mirror;
        public Color originalColor;
    }

    private readonly List<MeshEntry> meshes = new();
    private readonly List<SpriteEntry> sprites = new();
    private readonly HashSet<SpriteRenderer> mirrorSet = new();

    private PlayerController player;
    private MaterialPropertyBlock block;
    private MaterialPropertyBlock emptyBlock;
    private bool tinted;

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
        block = new MaterialPropertyBlock();
        emptyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        Attach();
    }

    private void OnDisable()
    {
        Detach();
        if (IsOccluded)
        {
            IsOccluded = false;
            OccludedFraction = 0.0f;
            OcclusionChanged?.Invoke(false);
        }
    }

    private void OnValidate()
    {
        // Apply color edits live in Play mode.
        if (!Application.isPlaying || block == null || !isActiveAndEnabled)
        {
            return;
        }
        ApplyXRayColor();
        SetTint(IsOccluded && tintWhenOccluded, force: true);
    }

    private void LateUpdate()
    {
        // LateUpdate: after the player has moved/animated and the camera has updated.
        SyncSpriteMirrors();

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
            SetTint(occluded && tintWhenOccluded, force: false);
            OcclusionChanged?.Invoke(occluded);
        }
    }

    /// <summary>Re-collect the player's renderers, e.g. after swapping its visuals.</summary>
    public void RefreshRenderers()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        Detach();
        Attach();
        SetTint(IsOccluded && tintWhenOccluded, force: true);
    }

    private void Attach()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is SpriteRenderer sr)
            {
                // Skip our own mirrors, including ones from a Detach() whose deferred
                // Destroy hasn't run yet.
                bool isMirror = mirrorSet.Contains(sr) ||
                                (xRaySpriteMaterial != null && sr.sharedMaterial == xRaySpriteMaterial);
                if (!isMirror)
                {
                    AddSprite(sr);
                }
            }
            else if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                AddMesh(r);
            }
        }
        ApplyXRayColor();
        SyncSpriteMirrors();
    }

    private void Detach()
    {
        SetTint(false, force: true);

        foreach (MeshEntry m in meshes)
        {
            if (m.renderer == null || m.xRayIndex < 0)
            {
                continue;
            }
            m.renderer.SetPropertyBlock(emptyBlock, m.xRayIndex);
            List<Material> kept = new(m.renderer.sharedMaterials);
            kept.RemoveAll(mat => mat == xRayMaterial);
            m.renderer.sharedMaterials = kept.ToArray();
        }
        meshes.Clear();

        foreach (SpriteEntry s in sprites)
        {
            if (s.mirror != null)
            {
                Destroy(s.mirror.gameObject);
            }
        }
        sprites.Clear();
        mirrorSet.Clear();
    }

    // ------------------------------------------------------------------
    // Mesh renderers
    // ------------------------------------------------------------------

    private void AddMesh(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        int existing = xRayMaterial != null ? Array.IndexOf(mats, xRayMaterial) : -1;

        MeshEntry e = new MeshEntry
        {
            renderer = r,
            baseCount = existing >= 0 ? mats.Length - 1 : mats.Length,
            xRayIndex = -1,
        };

        if (xRayMaterial != null)
        {
            if (existing >= 0)
            {
                e.xRayIndex = existing;
            }
            else
            {
                // A material beyond the submesh count re-renders the last submesh with it.
                Material[] extended = new Material[mats.Length + 1];
                mats.CopyTo(extended, 0);
                extended[mats.Length] = xRayMaterial;
                r.sharedMaterials = extended;
                e.xRayIndex = mats.Length;
            }

            // The x-ray re-renders the last submesh, so take the shape from its material.
            if (e.baseCount > 0)
            {
                TryGetShapeTexture(mats[e.baseCount - 1], out e.shapeTex, out e.shapeSt);
            }
        }

        meshes.Add(e);
    }

    /// <summary>
    /// Base texture of alpha-tested or transparent materials, so billboards get a shaped
    /// silhouette. Opaque materials are skipped: their texture alpha often holds other
    /// data (e.g. smoothness) and would punch holes in the x-ray.
    /// </summary>
    private static bool TryGetShapeTexture(Material m, out Texture tex, out Vector4 st)
    {
        tex = null;
        st = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
        if (m == null || m.renderQueue < AlphaTestQueue)
        {
            return false;
        }

        string prop = m.HasProperty("_BaseMap") ? "_BaseMap" : m.HasProperty("_MainTex") ? "_MainTex" : null;
        if (prop == null)
        {
            return false;
        }

        tex = m.GetTexture(prop);
        if (tex == null)
        {
            return false;
        }
        Vector2 scale = m.GetTextureScale(prop);
        Vector2 offset = m.GetTextureOffset(prop);
        st = new Vector4(scale.x, scale.y, offset.x, offset.y);
        return true;
    }

    // ------------------------------------------------------------------
    // Sprite renderers
    // ------------------------------------------------------------------

    private void AddSprite(SpriteRenderer source)
    {
        SpriteEntry e = new SpriteEntry { source = source, originalColor = source.color };

        if (xRaySpriteMaterial != null)
        {
            GameObject go = new GameObject(source.name + " (X-Ray)");
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);

            SpriteRenderer mirror = go.AddComponent<SpriteRenderer>();
            mirror.sharedMaterial = xRaySpriteMaterial;
            mirror.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mirror.receiveShadows = false;
            e.mirror = mirror;
            mirrorSet.Add(mirror);
        }

        sprites.Add(e);
    }

    private void SyncSpriteMirrors()
    {
        foreach (SpriteEntry s in sprites)
        {
            if (s.source == null || s.mirror == null)
            {
                continue;
            }
            SpriteRenderer src = s.source;
            SpriteRenderer m = s.mirror;

            m.enabled = src.enabled;
            m.sprite = src.sprite;
            m.flipX = src.flipX;
            m.flipY = src.flipY;
            m.drawMode = src.drawMode;
            if (src.drawMode != SpriteDrawMode.Simple)
            {
                m.size = src.size;
            }
            m.sortingLayerID = src.sortingLayerID;
            m.sortingOrder = src.sortingOrder;
            m.color = occludedColor;
        }
    }

    // ------------------------------------------------------------------
    // Colors
    // ------------------------------------------------------------------

    private void ApplyXRayColor()
    {
        foreach (MeshEntry m in meshes)
        {
            if (m.renderer == null || m.xRayIndex < 0)
            {
                continue;
            }
            block.Clear();
            block.SetColor(XRayColorId, occludedColor);
            if (m.shapeTex != null)
            {
                block.SetTexture(MainTexId, m.shapeTex);
                block.SetVector(MainTexStId, m.shapeSt);
            }
            m.renderer.SetPropertyBlock(block, m.xRayIndex);
        }
        // Sprite mirrors take their color in SyncSpriteMirrors.
    }

    private void SetTint(bool on, bool force)
    {
        if (on == tinted && !force)
        {
            return;
        }

        // Alpha only matters for the x-ray; the visible parts stay opaque.
        Color tint = occludedColor;
        tint.a = 1.0f;

        // Meshes: per-material blocks on the base materials, so the x-ray slot keeps its own.
        block.Clear();
        block.SetColor(BaseColorId, tint);
        block.SetColor(ColorId, tint);
        foreach (MeshEntry m in meshes)
        {
            if (m.renderer == null)
            {
                continue;
            }
            for (int i = 0; i < m.baseCount; ++i)
            {
                m.renderer.SetPropertyBlock(on ? block : emptyBlock, i);
            }
        }

        // Sprites: via SpriteRenderer.color, restoring the color they had before tinting.
        for (int i = 0; i < sprites.Count; ++i)
        {
            SpriteEntry s = sprites[i];
            if (s.source == null)
            {
                continue;
            }
            if (on)
            {
                if (!tinted)
                {
                    s.originalColor = s.source.color;
                    sprites[i] = s;
                }
                Color c = tint;
                c.a = s.originalColor.a;
                s.source.color = c;
            }
            else if (tinted)
            {
                s.source.color = s.originalColor;
            }
        }

        tinted = on;
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

        // Rays from inside the player's room toward the camera can meet other rooms' walls
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
            else if (area.GetComponentInChildren<Renderer>(true) != null || area.transform.parent == null)
            {
                cachedOwnBox = area.transform;
            }
            else
            {
                cachedOwnBox = area.transform.parent;
            }
        }
        return cachedOwnBox;
    }

    private bool TryGetPlayerBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        foreach (MeshEntry m in meshes)
        {
            Accumulate(m.renderer, ref bounds, ref any);
        }
        foreach (SpriteEntry s in sprites)
        {
            Accumulate(s.source, ref bounds, ref any);
        }
        return any;
    }

    private static void Accumulate(Renderer r, ref Bounds bounds, ref bool any)
    {
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
        {
            return;
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
}
