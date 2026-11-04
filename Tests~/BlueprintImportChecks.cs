using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Run with Unity Pipeline run_script. Kept outside asset imports so no test framework
// dependency is added to projects that install Location Tools.
public static class BlueprintImportChecks
{
    private const string Folder = "Assets/LocationToolsImportChecks";
    private const string ScalePiece = "LocationToolsScaleCheckPiece";
    private const string CasePiece = "LocationToolsCaseCheckPiece";
    private static readonly Vector3 PrefabScale = new Vector3(2f, 3f, 4f);
    private static readonly Vector3 ModelScale = new Vector3(100f, 100f, 100f);
    private static readonly List<string> passed = new List<string>();

    public static object Run()
    {
        if (AssetDatabase.IsValidFolder(Folder))
            throw new InvalidOperationException(Folder + " already exists. Remove it before running the checks.");
        // The import goes into the active scene and is reverted with Undo, because Unity
        // cannot open an additive scene while the active scene is untitled and unsaved.
        UnityEngine.Object previousSelection = Selection.activeObject;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        string blueprintPath = Path.Combine(Path.GetTempPath(), "LocationToolsImportChecks.blueprint");
        passed.Clear();
        try
        {
            AssetDatabase.CreateFolder("Assets", "LocationToolsImportChecks");
            AssetDatabase.CreateFolder(Folder, "a_model");
            AssetDatabase.CreateFolder(Folder, "b_prefab");
            SavePrefab(Folder + "/" + ScalePiece + ".prefab", ScalePiece, PrefabScale);
            // A model prefab whose name differs only in letter case, found before the real prefab.
            SavePrefab(Folder + "/a_model/" + CasePiece.ToLowerInvariant() + ".prefab", CasePiece.ToLowerInvariant(), ModelScale);
            SavePrefab(Folder + "/b_prefab/" + CasePiece + ".prefab", CasePiece, Vector3.one);

            File.WriteAllLines(blueprintPath, new string[]
            {
                "#Name:ImportChecks",
                "#Pieces",
                ScalePiece + ";;0;0;0;0;0;0;1;;1.5;2.5;3.5;",
                ScalePiece + ";;1;0;0;0;0;0;1",
                ScalePiece + ";;2;0;0;0;0;0;1;;;;;",
                ScalePiece + ";;3;0;0;0;0;0;1;;0.5",
                CasePiece + ";;4;0;0;0;0;0;1;;2;2;2;",
            });

            BlueprintImportResult result = LocationToolsApi.ImportBlueprint(blueprintPath);
            Assert(result.importedCount == 5 && result.missingPrefabs.Count == 0, "All rows import");
            GameObject root = (GameObject)EditorUtility.InstanceIDToObject(result.rootInstanceId);
            Transform pieces = root.transform.Find("Blueprint");

            Near(pieces.GetChild(0).localScale, new Vector3(1.5f, 2.5f, 3.5f), "Scale columns replace the prefab scale");
            Near(pieces.GetChild(1).localScale, PrefabScale, "Rows without scale columns keep the prefab scale");
            Near(pieces.GetChild(2).localScale, PrefabScale, "Empty scale columns keep the prefab scale");
            Near(pieces.GetChild(3).localScale, new Vector3(0.5f, 1f, 1f), "Missing scale columns default to 1");
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(pieces.GetChild(4).gameObject);
            Assert(source != null && source.name == CasePiece, "Exact-case prefab name is preferred", "got " + (source == null ? "null" : source.name));
            return new Dictionary<string, object> { { "passed", passed } };
        }
        finally
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Selection.activeObject = previousSelection;
            AssetDatabase.DeleteAsset(Folder);
            File.Delete(blueprintPath);
        }
    }

    private static void SavePrefab(string path, string name, Vector3 scale)
    {
        GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        source.name = name;
        source.transform.localScale = scale;
        PrefabUtility.SaveAsPrefabAsset(source, path);
        UnityEngine.Object.DestroyImmediate(source);
    }

    private static void Near(Vector3 actual, Vector3 expected, string name)
    {
        Assert(Vector3.Distance(actual, expected) < 0.0001f, name, "expected " + expected.ToString("F3") + ", got " + actual.ToString("F3"));
    }

    private static void Assert(bool condition, string name, string detail = "")
    {
        if (!condition)
            throw new Exception("Check failed: " + name + (detail == "" ? "" : ": " + detail));
        passed.Add(name);
    }
}
