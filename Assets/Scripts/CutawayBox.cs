using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a box/room for WallCutaway. Add to each box root (e.g. box1).
/// The cast origin is the center of the box's renderers' combined bounds, so a pivot
/// placed at the floor doesn't matter. The center is stored in local space, so moving
/// or rotating the box at runtime is handled.
/// </summary>
[DisallowMultipleComponent]
public class CutawayBox : MonoBehaviour
{
    [Tooltip("Added to the computed center, in local space. Use it if props inside the box skew the bounds.")]
    [SerializeField] private Vector3 centerOffset = Vector3.zero;

    private static readonly List<CutawayBox> active = new();

    /// <summary>Boxes currently enabled in the scene.</summary>
    public static IReadOnlyList<CutawayBox> Active => active;

    /// <summary>Raised when a box is enabled, disabled, or recalculates its center.</summary>
    public static event Action RegistryChanged;

    private Vector3 localCenter;

    public Vector3 WorldCenter => transform.TransformPoint(localCenter + centerOffset);

    private void Awake()
    {
        // Computed before WallCutaway can hide anything, so hidden walls never skew it.
        localCenter = ComputeLocalCenter();
    }

    private void OnEnable()
    {
        active.Add(this);
        RegistryChanged?.Invoke();
    }

    private void OnDisable()
    {
        active.Remove(this);
        RegistryChanged?.Invoke();
    }

    /// <summary>Recompute the center, e.g. after swapping the box's geometry.</summary>
    public void RecalculateCenter()
    {
        localCenter = ComputeLocalCenter();
        RegistryChanged?.Invoke();
    }

    private Vector3 ComputeLocalCenter()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return Vector3.zero;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; ++i)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return transform.InverseTransformPoint(b.center);
    }

    // Statics survive play mode entry when domain reload is disabled; reset them explicitly.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        RegistryChanged = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 local = Application.isPlaying ? localCenter : ComputeLocalCenter();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(local + centerOffset), 0.2f);
    }
#endif
}
