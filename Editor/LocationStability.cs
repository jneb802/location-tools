using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor estimate of settled structural support. Does not simulate network ownership,
// update timing, damage modifiers, or mod patches applied only in the running game.
public static class LocationStability
{
    public sealed class Result
    {
        public readonly List<WearNTear> Failed = new List<WearNTear>();
        public readonly Dictionary<WearNTear, float> Support = new Dictionary<WearNTear, float>();
        public bool Complete;
        public string Detail;
        public int ExternalPieces;
        public int ProtectedPieces;
        public int Passes;
    }

    private struct Material
    {
        public float Max, Min, Horizontal, Vertical;
    }

    private sealed class Node
    {
        public WearNTear Piece;
        public Material Material;
        public Vector3 Com;
        public float Support;
        public bool Grounded, Removed;
        public readonly List<Contact> Contacts = new List<Contact>();
    }

    private struct Contact
    {
        public Node Other;
        public float Factor, VerticalFactor;
        public Vector3 Direction;
        public bool Below;
    }

    private const int MaxPasses = 8192;
    private const float SettleTolerance = 0.0001f;
    private static readonly MethodInfo MaterialMethod = typeof(WearNTear).GetMethod(
        "GetMaterialProperties", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public static Result Check(GameObject target, float? flatGroundHeight = null,
        Func<float, string, bool> cancel = null)
    {
        if (target == null || !target.scene.IsValid() || !target.scene.isLoaded)
            throw new ArgumentException("Select a location in a loaded scene or Prefab Mode.");
        string[] layers = { "piece", "Default", "static_solid", "Default_small", "terrain" };
        foreach (string layer in layers)
            if (LayerMask.NameToLayer(layer) < 0)
                throw new InvalidOperationException("Missing Valheim physics layer: " + layer);
        if (MaterialMethod == null)
            throw new InvalidOperationException("The loaded WearNTear has no material support method.");

        WearNTear[] targets = target.GetComponentsInChildren<WearNTear>();
        Result result = new Result();
        if (targets.Length == 0)
        {
            result.Detail = "No active WearNTear pieces found.";
            return result;
        }

        PhysicsScene physics = target.scene.GetPhysicsScene();
        GameObject ground = null;
        try
        {
            if (flatGroundHeight.HasValue)
            {
                if (float.IsNaN(flatGroundHeight.Value) || float.IsInfinity(flatGroundHeight.Value))
                    throw new ArgumentException("Ground height must be a finite number.");
                Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
                foreach (Collider collider in target.GetComponentsInChildren<Collider>())
                    bounds.Encapsulate(collider.bounds);
                float top = flatGroundHeight.Value;
                float bottom = Mathf.Min(bounds.min.y - 10f, top - 2f);
                ground = new GameObject("LocationStability_TemporaryGround");
                ground.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(ground, target.scene);
                ground.layer = LayerMask.NameToLayer("terrain");
                ground.transform.position = new Vector3(bounds.center.x, (top + bottom) * 0.5f, bounds.center.z);
                ground.AddComponent<BoxCollider>().size = new Vector3(bounds.size.x + 20f, top - bottom, bounds.size.z + 20f);
            }
            Physics.SyncTransforms();

            Dictionary<WearNTear.MaterialType, Material> materials = new Dictionary<WearNTear.MaterialType, Material>();
            Dictionary<WearNTear, Node> lookup = new Dictionary<WearNTear, Node>();
            List<Node> nodes = new List<Node>();
            foreach (WearNTear piece in targets)
                AddNode(piece, lookup, nodes, materials);

            int mask = LayerMask.GetMask(layers);
            int terrain = LayerMask.NameToLayer("terrain");
            Collider[] hits = new Collider[128];
            // Include connected pieces outside the selected hierarchy. Their support is
            // calculated as well, rather than treating them as absent or fully grounded.
            for (int index = 0; index < nodes.Count; index++)
            {
                if (index % 64 == 0 && cancel != null && cancel(0.4f * index / nodes.Count, "Reading piece contacts"))
                    throw new OperationCanceledException();
                Node node = nodes[index];
                Collider[] own = node.Piece.GetComponentsInChildren<Collider>(true);
                HashSet<Collider> excluded = new HashSet<Collider>(own);
                HashSet<Collider> seen = new HashSet<Collider>();
                foreach (Collider collider in own)
                {
                    if (collider.isTrigger || collider.attachedRigidbody != null)
                        continue;
                    Vector3 position;
                    Vector3 size;
                    Quaternion rotation;
                    if (collider is BoxCollider box)
                    {
                        position = box.transform.position + box.transform.TransformVector(box.center);
                        size = Vector3.Scale(box.transform.lossyScale, box.size);
                        rotation = box.transform.rotation;
                    }
                    else
                    {
                        position = collider.bounds.center;
                        size = collider.bounds.size;
                        rotation = Quaternion.identity;
                    }
                    size = (size + Vector3.one * 0.3f) * 0.5f;
                    if (size.x < 0 || size.y < 0 || size.z < 0)
                        throw new InvalidOperationException("Negative collider scale on " + node.Piece.name + ". Correct the scale before checking support.");
                    int count;
                    while (true)
                    {
                        count = physics.OverlapBox(position, size, hits, rotation, mask, QueryTriggerInteraction.Ignore);
                        if (count < hits.Length) break;
                        if (hits.Length >= 65536)
                            throw new InvalidOperationException("Too many overlapping colliders to finish the check.");
                        hits = new Collider[hits.Length * 2];
                    }
                    for (int hitIndex = 0; hitIndex < count; hitIndex++)
                    {
                        Collider hit = hits[hitIndex];
                        if (hit.isTrigger || hit.attachedRigidbody != null || excluded.Contains(hit) || !seen.Add(hit))
                            continue;
                        WearNTear other = hit.GetComponentInParent<WearNTear>();
                        if (hit.gameObject.layer == terrain || other == null)
                        {
                            node.Grounded = true;
                            continue;
                        }
                        if (!other.m_supports) continue;
                        Node neighbor = AddNode(other, lookup, nodes, materials);
                        float distance = Vector3.Distance(node.Com, neighbor.Com) + 0.1f;
                        float originDistance = Vector3.Distance(node.Com, other.transform.position) + 0.1f;
                        if (!node.Piece.m_forceCorrectCOMCalculation)
                            distance = Mathf.Min(distance, originDistance);
                        Vector3 point = FindSupportPoint(node.Com, neighbor.Com, hit);
                        bool below = point.y < node.Com.y + 0.05f;
                        Vector3 direction = point - node.Com;
                        float loss = node.Material.Horizontal;
                        if (below && direction.normalized.y < 0f)
                        {
                            float angle = Mathf.Acos(1f - Mathf.Abs(direction.normalized.y)) / (Mathf.PI / 2f);
                            loss = Mathf.Min(loss, Mathf.Lerp(node.Material.Horizontal, node.Material.Vertical, angle));
                        }
                        direction.y = 0f;
                        node.Contacts.Add(new Contact
                        {
                            Other = neighbor,
                            Factor = 1f - loss * distance,
                            VerticalFactor = 1f - node.Material.Vertical * distance,
                            Direction = direction,
                            Below = below
                        });
                    }
                }
            }

            result.ExternalPieces = nodes.Count - targets.Length;
            foreach (WearNTear piece in targets)
                if (!piece.m_noSupportWear) result.ProtectedPieces++;

            // WearNTear starts at max support. Descend from that state so disconnected
            // loops cannot retain invented support. Remove failures only after settling,
            // then settle again to include collapse of dependent pieces.
            bool finished = false;
            while (result.Passes < MaxPasses)
            {
                float delta = 0f;
                foreach (Node node in nodes)
                {
                    float next = Calculate(node);
                    delta = Mathf.Max(delta, Mathf.Abs(next - node.Support));
                    node.Support = next;
                }
                result.Passes++;
                if (result.Passes % 16 == 0 && cancel != null && cancel(0.7f, "Resolving support and collapse"))
                    throw new OperationCanceledException();
                if (delta > SettleTolerance) continue;
                bool removed = false;
                bool thresholdUncertain = false;
                foreach (Node node in nodes)
                {
                    if (node.Removed || !node.Piece.m_noSupportWear) continue;
                    if (node.Support < node.Material.Min)
                    {
                        node.Removed = true;
                        node.Support = 0f;
                        removed = true;
                    }
                    else if (node.Support - node.Material.Min < 0.001f && delta > 0f)
                        thresholdUncertain = true;
                }
                if (removed) continue;
                if (thresholdUncertain) continue;
                finished = true;
                break;
            }
            foreach (WearNTear piece in targets)
            {
                Node node = lookup[piece];
                result.Support.Add(piece, node.Support);
                if (node.Removed) result.Failed.Add(piece);
            }
            result.Complete = finished;
            result.Detail = finished ? "Settled support estimate; game timing and runtime mod changes are not simulated."
                : "Support did not settle within the calculation limit. Remaining pieces are not confirmed stable.";
            return result;
        }
        finally
        {
            if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
            Physics.SyncTransforms();
        }
    }

    private static Node AddNode(WearNTear piece, Dictionary<WearNTear, Node> lookup,
        List<Node> nodes, Dictionary<WearNTear.MaterialType, Material> materials)
    {
        if (lookup.TryGetValue(piece, out Node existing)) return existing;
        if (!materials.TryGetValue(piece.m_materialType, out Material material))
        {
            object[] args = { 0f, 0f, 0f, 0f };
            MaterialMethod.Invoke(piece, args);
            material = new Material { Max = (float)args[0], Min = (float)args[1], Horizontal = (float)args[2], Vertical = (float)args[3] };
            if (material.Max <= 0 || material.Min <= 0)
                throw new InvalidOperationException("Unsupported WearNTear material: " + piece.m_materialType);
            materials.Add(piece.m_materialType, material);
        }
        Node node = new Node
        {
            Piece = piece, Material = material, Support = material.Max,
            Com = piece.transform.position + piece.transform.rotation * piece.m_comOffset
        };
        lookup.Add(piece, node);
        nodes.Add(node);
        return node;
    }

    private static Vector3 FindSupportPoint(Vector3 com, Vector3 otherCom, Collider collider)
    {
        if (collider is MeshCollider mesh && !mesh.convex)
            return mesh.Raycast(new Ray(com, Vector3.down), out RaycastHit hit, 10f) ? hit.point : (com + otherCom) * 0.5f;
        return collider.ClosestPoint(com);
    }

    private static float Calculate(Node node)
    {
        if (node.Removed) return 0f;
        if (node.Grounded || !node.Piece.m_noSupportWear) return node.Material.Max;
        float best = 0f;
        foreach (Contact contact in node.Contacts)
            if (!contact.Other.Removed)
                best = Mathf.Max(best, contact.Other.Support * contact.Factor);
        for (int first = 0; first < node.Contacts.Count - 1; first++)
        {
            Contact a = node.Contacts[first];
            if (!a.Below || a.Other.Removed) continue;
            for (int second = first + 1; second < node.Contacts.Count; second++)
            {
                Contact b = node.Contacts[second];
                if (!b.Below || b.Other.Removed) continue;
                float average = (a.Other.Support * a.VerticalFactor + b.Other.Support * b.VerticalFactor) * 0.5f;
                if (average > best && Vector3.Angle(a.Direction, b.Direction) >= 100f)
                    best = average;
            }
        }
        return Mathf.Min(best, node.Material.Max);
    }
}
