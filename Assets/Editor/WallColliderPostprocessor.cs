using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds a MeshCollider (and optionally the wall layer) to wall nodes of imported models.
/// Runs on every import/reimport, so the colliders become part of the model asset and
/// every instance gets them automatically.
/// Must live in an Editor folder (e.g. Assets/Editor/).
/// </summary>
public class WallColliderPostprocessor : AssetPostprocessor
{
    private const string WallPrefix = "側面";
    private const string WallLayerName = "Walls";   // must exist in Tags & Layers; ignored if not

    // Leave empty to process every model, or set a folder like "Assets/Stages/".
    private const string ScopeFolder = "";

    // Bump this whenever the logic below changes, so Unity reimports affected models.
    public override uint GetVersion() => 1;

    private void OnPostprocessModel(GameObject root)
    {
        if (ScopeFolder.Length > 0 && !assetPath.StartsWith(ScopeFolder))
        {
            return;
        }

        int layer = LayerMask.NameToLayer(WallLayerName);

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.StartsWith(WallPrefix) || mf.sharedMesh == null)
            {
                continue;
            }

            if (!mf.TryGetComponent(out MeshCollider mc))
            {
                mc = mf.gameObject.AddComponent<MeshCollider>();
            }
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;

            if (layer >= 0)
            {
                mf.gameObject.layer = layer;
            }
        }
    }
}
