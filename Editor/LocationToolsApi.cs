#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Serializable]
public sealed class BlueprintImportResult
{
    public int rootInstanceId;
    public string rootName;
    public int importedCount;
    public Dictionary<string, int> missingPrefabs;
}

public static class LocationToolsApi
{
    public static BlueprintImportResult ImportBlueprint(string filePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Import blueprints in Edit mode.");
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
            throw new ArgumentException("Provide an absolute blueprint file path.", nameof(filePath));
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("An active loaded scene is required.");
        List<BlueprintPrefab> structuresList = Parse(filePath);
        if (structuresList.Count == 0)
            throw new InvalidDataException("The file contains no blueprint objects.");

        // Get the blueprint name (without extension) for naming
        string blueprintName = Path.GetFileNameWithoutExtension(filePath);
        string locationName = "MWL_" + blueprintName;

        Dictionary<string, GameObject> cachedPrefabs = new Dictionary<string, GameObject>();

        // Create parent GameObject "MWL_Location1" at (0,0,0)
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Import Blueprint");
        GameObject locationParent = new GameObject(locationName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(locationParent, scene);
        try
        {
            locationParent.transform.position = Vector3.zero;
            locationParent.transform.rotation = Quaternion.identity;

            // Add Location component with m_noBuild = false
            Location location = locationParent.AddComponent<Location>();
            location.m_noBuild = false;

            // Create child "Blueprint" (empty GameObject) - this is the import target
            GameObject blueprintChild = new GameObject("Blueprint");
            blueprintChild.transform.SetParent(locationParent.transform);
            blueprintChild.transform.localPosition = Vector3.zero;
            blueprintChild.transform.localRotation = Quaternion.identity;

            // Create child for TerrainModifier
            string terrainName = locationName + "_Terrain1";
            GameObject terrainChild = new GameObject(terrainName);
            terrainChild.transform.SetParent(locationParent.transform);
            terrainChild.transform.localPosition = Vector3.zero;
            terrainChild.transform.localRotation = Quaternion.identity;

            // Add TerrainModifier component with m_smooth = true, m_square = false
            TerrainModifier terrainMod = terrainChild.AddComponent<TerrainModifier>();
            terrainMod.m_smooth = true;
            terrainMod.m_square = false;

            // Import prefabs as children of the Blueprint object
            int counter = 0;
            Dictionary<string, int> notFound = new Dictionary<string, int>();

            foreach (BlueprintPrefab blueprintPrefab in structuresList)
            {
                if (blueprintPrefab.prefabName == null)
                {
                    continue;
                }

                GameObject prefab = FindPrefab(blueprintPrefab, cachedPrefabs);
                if (prefab != null)
                {
                    Quaternion quat = new Quaternion(blueprintPrefab.rotX, blueprintPrefab.rotY, blueprintPrefab.rotZ, blueprintPrefab.rotW);

                    // Always maintain prefab connections
                    GameObject result = PrefabUtility.InstantiatePrefab(prefab, blueprintChild.transform) as GameObject;
                    if (result != null)
                    {
                        result.transform.position = new Vector3(blueprintPrefab.posX, blueprintPrefab.posY, blueprintPrefab.posZ);
                        result.transform.rotation = quat;
                        if (blueprintPrefab.hasScale)
                            result.transform.localScale = new Vector3(blueprintPrefab.scaleX, blueprintPrefab.scaleY, blueprintPrefab.scaleZ);
                    }
                    if (result == null)
                        throw new InvalidOperationException("Could not instantiate prefab: " + blueprintPrefab.prefabName);
                    counter++;
                }
                else
                {
                    string name = blueprintPrefab.prefabName;
                    if (notFound.ContainsKey(name))
                        notFound[name]++;
                    else
                        notFound[name] = 1;
                }
            }

            // Register undo for the created hierarchy
            Undo.RegisterCreatedObjectUndo(locationParent, "Import Blueprint");

            // Select the created location in the hierarchy
            Selection.activeGameObject = locationParent;

            Debug.Log($"Imported {counter} objects into {locationName}");

            if (notFound.Count > 0)
            {
                Debug.LogWarning("Import was unable to find: " + string.Join(", ", notFound.Select(i => i.Value.ToString() + "x " + i.Key)));
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
            return new BlueprintImportResult
            {
                rootInstanceId = locationParent.GetInstanceID(),
                rootName = locationName,
                importedCount = counter,
                missingPrefabs = notFound
            };
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(locationParent);
            throw;
        }
    }

    private static float InvariantFloat(string[] row, int index, float defaultValue = 0f)
    {
        if (index >= row.Length)
        {
            return defaultValue;
        }
        string text = row[index];
        if (string.IsNullOrEmpty(text))
        {
            return defaultValue;
        }
        return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HasValue(string[] row, int index) =>
        index < row.Length && !string.IsNullOrWhiteSpace(row[index]);

    // Prefers a prefab whose name matches exactly. Ripped projects can also contain a model prefab whose
    // name differs only in letter case, such as model/swamptree1 next to SwampTree1.
    private static GameObject FindPrefab(BlueprintPrefab blueprintPrefab, Dictionary<string, GameObject> cachedPrefabs)
    {
        if (cachedPrefabs.TryGetValue(blueprintPrefab.prefabName, out GameObject cached))
            return cached;

        GameObject caseInsensitiveMatch = null;
        string[] guids = AssetDatabase.FindAssets(blueprintPrefab.prefabName + " t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            //  Skip any prefab under a "_res" folder because these prefabs are just no-material (i.e. white) prefabs
            //  Exceptions: some _res subfolders contain valid textured prefabs
            bool isResPath = path.Contains("/_res/", StringComparison.OrdinalIgnoreCase);
            bool isResException = path.Contains("/Ashlands_Build/", StringComparison.OrdinalIgnoreCase)
                               || path.Contains("/Charred_Balista/", StringComparison.OrdinalIgnoreCase);
            if (isResPath && !isResException)
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                continue;
            if (prefab.name.Equals(blueprintPrefab.prefabName, StringComparison.Ordinal))
            {
                cachedPrefabs[blueprintPrefab.prefabName] = prefab;
                return prefab;
            }
            if (caseInsensitiveMatch == null && prefab.name.Equals(blueprintPrefab.prefabName, StringComparison.OrdinalIgnoreCase))
                caseInsensitiveMatch = prefab;
        }

        cachedPrefabs[blueprintPrefab.prefabName] = caseInsensitiveMatch;
        return caseInsensitiveMatch;
    }

    internal static List<BlueprintPrefab> Parse(string filePath)
    {
        List<BlueprintPrefab> structuresList = new List<BlueprintPrefab>();
        string[] blueprintsList = File.ReadAllLines(filePath);
        BlueprintPrefab prefab = default(BlueprintPrefab);

        foreach (string str in blueprintsList)
        {
            if (!string.IsNullOrEmpty(str) && str.IndexOf('#') == -1)
            {
                if (str.IndexOf(';') >= 0) {
                    // .blueprint
                    prefab = default(BlueprintPrefab);
                    string[] array = str.Split(';');
                    if (array.Length >= 9)
                    {
                        prefab = new BlueprintPrefab();
                        prefab.prefabName = array[0];
                        prefab.type = array[1] != null ? array[1] : "NONE";
                        prefab.posX = InvariantFloat(array, 2, 0f);
                        prefab.posY = InvariantFloat(array, 3, 0f);
                        prefab.posZ = InvariantFloat(array, 4, 0f);
                        prefab.rotX = InvariantFloat(array, 5, 0f);
                        prefab.rotY = InvariantFloat(array, 6, 0f);
                        prefab.rotZ = InvariantFloat(array, 7, 0f);
                        prefab.rotW = InvariantFloat(array, 8, 0f);
                        // Columns 10-12 hold scale. A missing column defaults to 1, as in Infinity Hammer.
                        prefab.hasScale = HasValue(array, 10) || HasValue(array, 11) || HasValue(array, 12);
                        prefab.scaleX = InvariantFloat(array, 10, 1f);
                        prefab.scaleY = InvariantFloat(array, 11, 1f);
                        prefab.scaleZ = InvariantFloat(array, 12, 1f);
                    }
                } else {
                    // .vbuild
                    prefab = default(BlueprintPrefab);
                    string[] array = str.Split(' ');
                    if (array.Length >= 8) {
                        prefab = new BlueprintPrefab();
                        prefab.prefabName = array[0];
                        prefab.rotX = InvariantFloat(array, 1, 0f);
                        prefab.rotY = InvariantFloat(array, 2, 0f);
                        prefab.rotZ = InvariantFloat(array, 3, 0f);
                        prefab.rotW = InvariantFloat(array, 4, 0f);
                        prefab.posX = InvariantFloat(array, 5, 0f);
                        prefab.posY = InvariantFloat(array, 6, 0f);
                        prefab.posZ = InvariantFloat(array, 7, 0f);
                        prefab.type = "NONE";
                    }
                }
                if (!BlueprintPrefab.IsDefault(prefab))
                {
                    structuresList.Add(prefab);
                }
            }
        }
        return structuresList;
    }

}
#endif
