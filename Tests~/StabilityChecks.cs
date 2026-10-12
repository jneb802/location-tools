using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run with Unity Pipeline run_script. Kept outside asset imports so no test framework
// or runtime assembly dependency is added to projects that install Location Tools.
public static class StabilityChecks
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Offset = new Vector3(10000, 1000, 10000);
    private static GameObject root;
    private static readonly List<GameObject> fixtures = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();

    public static object Run()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        LocationToolsWindow window = ScriptableObject.CreateInstance<LocationToolsWindow>();
        passed.Clear();
        try
        {
            Reset();
            WearNTear b = Piece("B", new Vector3(2, 0, 0), WearNTear.MaterialType.Wood);
            WearNTear a = Piece("A", Vector3.zero, WearNTear.MaterialType.Wood);
            Box("Anchor", new Vector3(-2, 0, 0));
            typeof(LocationToolsWindow).GetField("stabilityTarget", Private).SetValue(window, root);
            for (int order = 0; order < 2; order++)
            {
                if (order == 1) a.transform.SetAsFirstSibling();
                typeof(LocationToolsWindow).GetMethod("RunStabilityCheck", Private).Invoke(window, null);
                List<GameObject> failed = (List<GameObject>)typeof(LocationToolsWindow).GetField("unstablePieces", Private).GetValue(window);
                Assert(failed.Count == 0, "Window command is independent of hierarchy order " + order);
            }
            Near(LocationStability.Check(root).Support[b], 58f, "Horizontal wood support");

            Reset();
            Piece("Protected wood", Vector3.zero, WearNTear.MaterialType.Wood, true);
            WearNTear iron = Piece("Iron", new Vector3(2, 0, 0), WearNTear.MaterialType.Iron);
            Near(LocationStability.Check(root).Support[iron], 100f * (1f - 2.1f / 13f), "Receiving material controls loss");

            Reset();
            WearNTear stone = Piece("Stone", Vector3.zero, WearNTear.MaterialType.Stone);
            Piece("Right support", new Vector3(2, 0, 0), WearNTear.MaterialType.Stone, true);
            Assert(LocationStability.Check(root).Failed.Contains(stone), "Single side contact gets no vertical bonus");
            Piece("Left support", new Vector3(-2, 0, 0), WearNTear.MaterialType.Stone, true);
            Near(LocationStability.Check(root).Support[stone], 737.5f, "Opposite contacts receive two-point support");

            Reset();
            WearNTear floating = Piece("Floating", Vector3.zero, WearNTear.MaterialType.Wood);
            GameObject ignored = Box("Excluded collider", new Vector3(2, 0, 0));
            ignored.layer = LayerMask.NameToLayer("Ignore Raycast");
            Assert(LocationStability.Check(root).Failed.Contains(floating), "Excluded physics layers cannot ground pieces");
            ignored.layer = LayerMask.NameToLayer("Default");
            Near(LocationStability.Check(root).Support[floating], 100f, "Eligible static collider grants full support");

            Reset();
            WearNTear groundPiece = Piece("Ground option", new Vector3(0, -1000, 0), WearNTear.MaterialType.Wood);
            Assert(LocationStability.Check(root).Failed.Contains(groundPiece), "No invented ground by default");
            Near(LocationStability.Check(root, 0f).Support[groundPiece], 100f, "Explicit flat ground supplies support");
            Assert(!UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Any(c => c.name == "LocationStability_TemporaryGround"), "Temporary ground cleaned after success");
            try { LocationStability.Check(root, 0f, (p, m) => true); }
            catch (OperationCanceledException) { }
            Assert(!UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Any(c => c.name == "LocationStability_TemporaryGround"), "Temporary ground cleaned after cancellation");

            Reset();
            WearNTear external = Piece("External protected wood", Vector3.zero, WearNTear.MaterialType.Wood, true);
            external.transform.SetParent(null);
            WearNTear dependent = Piece("Dependent", new Vector3(2, 0, 0), WearNTear.MaterialType.Wood);
            LocationStability.Result externalResult = LocationStability.Check(root);
            Assert(externalResult.ExternalPieces == 1 && externalResult.Failed.Count == 0, "Connected external pieces included");
            external.m_supports = false;
            Assert(LocationStability.Check(root).Failed.Contains(dependent), "m_supports=false does not support neighbors");

            Reset();
            WearNTear protectedPiece = Piece("Protected floating", Vector3.zero, WearNTear.MaterialType.Wood, true);
            LocationStability.Result protectedResult = LocationStability.Check(root);
            Assert(protectedResult.ProtectedPieces == 1 && protectedResult.Failed.Count == 0, "Support wear disabled remains intact");

            Reset();
            Piece("Foundation", Vector3.zero, WearNTear.MaterialType.Wood, true);
            WearNTear weakStone = Piece("Weak stone", new Vector3(0, 2, 0), WearNTear.MaterialType.Stone);
            WearNTear upperWood = Piece("Upper wood", new Vector3(0, 4, 0), WearNTear.MaterialType.Wood);
            LocationStability.Result collapse = LocationStability.Check(root);
            Assert(collapse.Complete && collapse.Failed.Contains(weakStone) && collapse.Failed.Contains(upperWood), "Failure removes support from dependent pieces");

            Reset();
            Piece("Floating cycle A", Vector3.zero, WearNTear.MaterialType.Iron);
            Piece("Floating cycle B", new Vector3(2, 0, 0), WearNTear.MaterialType.Iron);
            LocationStability.Result cycle = LocationStability.Check(root);
            Assert(cycle.Complete && cycle.Failed.Count == 2, "Floating cycles lose support");

            Reset();
            WearNTear meshReceiver = Piece("Mesh receiver", Vector3.zero, WearNTear.MaterialType.Stone);
            WearNTear meshSupport = Piece("Mesh support", new Vector3(1.5f, -1.5f, 0), WearNTear.MaterialType.Stone, true);
            UnityEngine.Object.DestroyImmediate(meshSupport.GetComponent<BoxCollider>());
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cube);
            MeshCollider meshCollider = meshSupport.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshSupport.transform.localScale = Vector3.one * 2;
            Physics.SyncTransforms();
            MethodInfo gamePoint = typeof(WearNTear).GetMethod("FindSupportPoint", BindingFlags.Static | BindingFlags.NonPublic);
            Vector3 point = (Vector3)gamePoint.Invoke(null, new object[] { meshReceiver.transform.position, meshSupport, meshCollider });
            float t = Mathf.Acos(1f - Mathf.Abs((point - meshReceiver.transform.position).normalized.y)) / (Mathf.PI / 2f);
            float expected = 1000f * (1f - Mathf.Lerp(1f, 0.125f, t) * (Mathf.Sqrt(4.5f) + 0.1f));
            Near(LocationStability.Check(root).Support[meshReceiver], expected, "Non-convex contact matches loaded game helper");

            return new { count = passed.Count, passed = passed.ToArray() };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
            foreach (GameObject item in fixtures) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            fixtures.Clear();
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(original);
        }
    }

    private static void Reset()
    {
        foreach (GameObject item in fixtures) if (item != null) UnityEngine.Object.DestroyImmediate(item);
        fixtures.Clear();
        root = new GameObject("Stability regression fixtures");
        fixtures.Add(root);
    }

    private static GameObject Box(string name, Vector3 offset)
    {
        GameObject item = new GameObject(name);
        fixtures.Add(item);
        item.transform.SetParent(root.transform);
        item.transform.position = Offset + offset;
        item.AddComponent<BoxCollider>().size = Vector3.one * 2;
        return item;
    }

    private static WearNTear Piece(string name, Vector3 offset, WearNTear.MaterialType material, bool protect = false)
    {
        WearNTear piece = Box(name, offset).AddComponent<WearNTear>();
        piece.m_materialType = material;
        piece.m_noSupportWear = !protect;
        return piece;
    }

    private static void Near(float actual, float expected, string label)
    {
        Assert(Mathf.Abs(actual - expected) < 0.01f, label + " (actual=" + actual + ", expected=" + expected + ")");
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        passed.Add(label);
    }
}
