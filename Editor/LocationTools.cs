using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;


#region Blueprint & LocationTool helpers

[ScriptedImporter(1, "blueprint")]
public class BlueprintImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        TextAsset subAsset = new TextAsset(File.ReadAllText(ctx.assetPath));
        ctx.AddObjectToAsset("text", subAsset);
        ctx.SetMainObject(subAsset);
    }
}

[ScriptedImporter(1, "vbuild")]
public class VBuildImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        TextAsset subAsset = new TextAsset(File.ReadAllText(ctx.assetPath));
        ctx.AddObjectToAsset("text", subAsset);
        ctx.SetMainObject(subAsset);
    }
}

public static class LocationToolExtensionMethods
{
    public static List<string> ToList(this TextAsset textAsset)
    {
        return new List<string>(textAsset.text.Split(
            new string[] { "\r\n", "\r", "\n" },
            StringSplitOptions.None
        ));
    }
}

[Serializable]
public struct BlueprintPrefab
{
    public string prefabName;
    public string type;
    public float posX;
    public float posY;
    public float posZ;
    public float rotW;
    public float rotX;
    public float rotY;
    public float rotZ;

    public static bool IsDefault<TValue>(TValue value) =>
        EqualityComparer<TValue>.Default.Equals(value, default(TValue));
}

#endregion

public class LocationToolsWindow : EditorWindow
{
    private GUIStyle foldoutBoldStyle = null;
    private GUIStyle greyLabelStyle = null;

    [SerializeField]
    private Vector2 m_ScrollPosition;

    [MenuItem("Window/Location Tools")]
    static void ShowWindow()
    {
        // Get existing open window or if none, make a new one:
        var window = GetWindow<LocationToolsWindow>();
        window.titleContent = new GUIContent("Location Tools");
        window.Show();

        // Foldout style with bold font
        window.foldoutBoldStyle = new GUIStyle(EditorStyles.foldout);
        window.foldoutBoldStyle.fontStyle = FontStyle.Bold;

        // Grey label style
        window.greyLabelStyle = new GUIStyle(EditorStyles.label);
        window.greyLabelStyle.normal.textColor = Color.grey;
        window.greyLabelStyle.hover.textColor = Color.grey;
    }

    void OnEnable()
    {
        // Nothing to initialize currently
    }

    // Called when the instance is destroyed or before a domain reload.
    void OnDisable()
    {
        // Clean up stability visuals
        ClearStabilityVisuals();

        // Destroy highlight material
        if (highlightMaterial != null)
        {
            DestroyImmediate(highlightMaterial);
            highlightMaterial = null;
        }
    }

    void OnGUI()
    {
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
        EditorGUILayout.Space(1f);
        DoImportGUI();
        EditorGUILayout.Space(1f);
        DoExportGUI();
        EditorGUILayout.Space(1f);
        DoStabilityGUI();

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Made with Love by Balrond & probablykory", greyLabelStyle);
        GUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
    }

    #region Import Tool
    #region Utils

    private void ClearParse()
    {
        blueprintPrefabCount = 0;
        blueprintsList.Clear();
        cachedPrefabs.Clear();
        structuresList.Clear();
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

	private GameObject FindPrefab(BlueprintPrefab blueprintPrefab)
    {
        if (cachedPrefabs.Count > 0)
        {
            GameObject foundOne = cachedPrefabs.Find(x => 
                x != null && 
                x.name.Equals(blueprintPrefab.prefabName, StringComparison.OrdinalIgnoreCase));
            if (foundOne != null)
                return foundOne;
        }

        string[] guids = AssetDatabase.FindAssets(blueprintPrefab.prefabName + " t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            //  Skip any prefab under a "_res" folder because these prefabs are just no-material (i.e. white) prefabs
            if (path.Contains("/_res/", StringComparison.OrdinalIgnoreCase))
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab != null && prefab.name.Equals(blueprintPrefab.prefabName, StringComparison.OrdinalIgnoreCase))
            {
                cachedPrefabs.Add(prefab);
                return prefab;
            }
        }

        return null;
    }

    private void GenerateStructureList()
    {
        var prefab = default(BlueprintPrefab);

        // Debug.Log("Detected PlanBuild");
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
                        prefab.posZ = InvariantFloat(array, 4, 0f);;
                        prefab.rotX = InvariantFloat(array, 5, 0f);
                        prefab.rotY = InvariantFloat(array, 6, 0f);
                        prefab.rotZ = InvariantFloat(array, 7, 0f);
                        prefab.rotW = InvariantFloat(array, 8, 0f);
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
        blueprintPrefabCount = structuresList.Count;
        // Debug.Log("Amount of objects: " + planBuildPrefabs.Count);
    }

    private void ImportObjects()
    {
        if (blueprintFiles.Count == 0 || structuresList.Count == 0)
        {
            Debug.LogError("No blueprint selected or no prefabs found.");
            return;
        }

        // Get the blueprint name (without extension) for naming
        string blueprintName = Path.GetFileNameWithoutExtension(blueprintFiles[selectedBlueprintIndex]);
        string locationName = "MWL_" + blueprintName;

        // Create parent GameObject "MWL_Location1" at (0,0,0)
        GameObject locationParent = new GameObject(locationName);
        locationParent.transform.position = Vector3.zero;
        locationParent.transform.rotation = Quaternion.identity;

        // Add Location component with m_noBuild = false
        var location = locationParent.AddComponent<Location>();
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
        var terrainMod = terrainChild.AddComponent<TerrainModifier>();
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

            GameObject prefab = FindPrefab(blueprintPrefab);
            if (prefab != null)
            {
                Quaternion quat = new Quaternion(blueprintPrefab.rotX, blueprintPrefab.rotY, blueprintPrefab.rotZ, blueprintPrefab.rotW);

                // Always maintain prefab connections
                GameObject result = PrefabUtility.InstantiatePrefab(prefab, blueprintChild.transform) as GameObject;
                if (result != null)
                {
                    result.transform.position = new Vector3(blueprintPrefab.posX, blueprintPrefab.posY, blueprintPrefab.posZ);
                    result.transform.rotation = quat;
                }
                counter++;
            }
            else
            {
                var name = blueprintPrefab.prefabName;
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
    }
    
    private void ExportObjects()
    {
        if (exportTarget == null)
        {
            Debug.LogError("No export target set.");
            return;
        }

        // Always export as .blueprint
        var defaultName = (exportTarget != null ? exportTarget.name : "Blueprint") + ".blueprint";
        var defaultDir = exportSettings != null && exportSettings.Count > 0 && !string.IsNullOrEmpty(exportSettings[0])
            ? exportSettings[0]
            : "Assets/";
        var path = EditorUtility.SaveFilePanel("Export Blueprint", defaultDir, defaultName, "blueprint");
        if (string.IsNullOrEmpty(path)) return;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var lines = new List<string>();

        // ---- Header (match example format) ----
        lines.Add($"#Name:{exportTarget.name}");
        lines.Add("#Creator:LocationTools");
        lines.Add("#Description:");
        lines.Add("#Category:LocationTools");
        lines.Add($"#Center:{exportTarget.name}");
        var c = exportTarget.transform.position;
        lines.Add($"#Coordinates:{c.x.ToString(inv)},{c.y.ToString(inv)},{c.z.ToString(inv)}");
        lines.Add("#Rotation:0,0,0");
        lines.Add("#SnapPoints"); // none for now
        lines.Add("#Pieces");

        // ---- One layer deep: only direct children of exportTarget ----
        for (int i = 0; i < exportTarget.transform.childCount; i++)
        {
            var t = exportTarget.transform.GetChild(i);
            if (t == null) continue;

            // Resolve prefab name (prefer source prefab asset name; fallback to GameObject name)
            GameObject sourcePrefab = null;
            try
            {
    #if UNITY_2018_3_OR_NEWER
                sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
    #else
                sourcePrefab = PrefabUtility.GetPrefabParent(t.gameObject) as GameObject;
    #endif
            }
            catch { /* ignore */ }

            var prefabName = (sourcePrefab != null ? sourcePrefab.name : t.gameObject.name);

            var p = t.position;
            var r = t.rotation;
            var s = t.localScale;

            // Example .blueprint row format:
            // name;;posX;posY;posZ;rotX;rotY;rotZ;rotW;;scaleX;scaleY;scaleZ;[optional blob]
            // We'll omit the optional trailing blob but keep the ending semicolon.
            lines.Add(string.Join(";",
                prefabName,
                "", // empty 'type' field -> produces the double semicolon after name
                p.x.ToString(inv),
                p.y.ToString(inv),
                p.z.ToString(inv),
                r.x.ToString(inv),
                r.y.ToString(inv),
                r.z.ToString(inv),
                r.w.ToString(inv),
                "", // empty field to create the second double-semicolon block
                s.x.ToString(inv),
                s.y.ToString(inv),
                s.z.ToString(inv)
            ) + ";");
        }

        // Write file and refresh AssetDatabase
        try
        {
            File.WriteAllLines(path, lines);
            Debug.Log($"Exported {exportTarget.transform.childCount} objects (one layer) to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write blueprint: {e.Message}");
            return;
        }

        AssetDatabase.Refresh();

        // If saved inside the project, ping/select it
        var projectPath = Application.dataPath.Replace("/Assets", "");
        if (path.StartsWith(projectPath))
        {
            var assetPath = "Assets" + path.Substring(projectPath.Length).Replace('\\', '/');
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }



    #endregion

    #region State

    private static string defaultExportPath = "Assets/PrefabInstance/";
    private List<string> exportSettings = new List<string>() { defaultExportPath };

    private bool showImports = true;
    private bool showExports = true;
    private bool showExportSettings = false;

    // Import tool state - folder-based selection
    private string blueprintFolder = "";
    private List<string> blueprintFiles = new List<string>();
    private int selectedBlueprintIndex = 0;

    // Import parsing state
    private int blueprintPrefabCount = 0;
    private List<string> blueprintsList = new List<string>();
    private List<GameObject> cachedPrefabs = new List<GameObject>();
    private List<BlueprintPrefab> structuresList = new List<BlueprintPrefab>();

    // Export state
    private GameObject exportTarget;

    // Stability tool state
    private bool showStability = true;
    private GameObject stabilityTarget;
    private GameObject groundPlane;
    private List<GameObject> unstablePieces = new List<GameObject>();
    private List<GameObject> highlightObjects = new List<GameObject>();
    private Material highlightMaterial;
    private string stabilityResultMessage = "";
    private Vector2 unstablePiecesScrollPos;
    private bool showUnstableList = true;

    #endregion

    #region GUI

    private void SelectBlueprintFolder()
    {
        string startPath = string.IsNullOrEmpty(blueprintFolder) ? "Assets/" : blueprintFolder;
        string folder = EditorUtility.OpenFolderPanel("Select Blueprint Folder", startPath, "");

        if (!string.IsNullOrEmpty(folder))
        {
            blueprintFolder = folder;
            blueprintFiles = Directory.GetFiles(blueprintFolder, "*.blueprint")
                .OrderBy(f => Path.GetFileName(f))
                .ToList();
            selectedBlueprintIndex = 0;

            if (blueprintFiles.Count > 0)
            {
                LoadSelectedBlueprint();
            }
            else
            {
                ClearParse();
            }
        }
    }

    private void LoadSelectedBlueprint()
    {
        ClearParse();

        if (blueprintFiles.Count == 0 || selectedBlueprintIndex < 0 || selectedBlueprintIndex >= blueprintFiles.Count)
            return;

        string filePath = blueprintFiles[selectedBlueprintIndex];
        if (File.Exists(filePath))
        {
            string content = File.ReadAllText(filePath);
            blueprintsList = new List<string>(content.Split(
                new string[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            ));
            GenerateStructureList();
        }
    }

    private void NavigateBlueprint(int direction)
    {
        if (blueprintFiles.Count == 0)
            return;

        selectedBlueprintIndex = Mathf.Clamp(selectedBlueprintIndex + direction, 0, blueprintFiles.Count - 1);
        LoadSelectedBlueprint();
    }

    private void DoImportGUI()
    {
        // Toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        showImports = EditorGUILayout.Foldout(showImports, "Import", true, foldoutBoldStyle);
        GUILayout.EndHorizontal();

        if (showImports)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();

            // Blueprint Folder selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Blueprint Folder", GUILayout.MaxWidth(125f));
            EditorGUILayout.TextField(blueprintFolder, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Select", GUILayout.Width(60f)))
            {
                SelectBlueprintFolder();
            }
            EditorGUILayout.EndHorizontal();

            // Selected file display and navigation
            if (blueprintFiles.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer

                string currentFileName = Path.GetFileName(blueprintFiles[selectedBlueprintIndex]);
                string displayText = $"{currentFileName} ({selectedBlueprintIndex + 1}/{blueprintFiles.Count})";
                if (blueprintPrefabCount > 0)
                {
                    displayText += $" - {blueprintPrefabCount} objs";
                }
                EditorGUILayout.LabelField(displayText);
                EditorGUILayout.EndHorizontal();

                // Navigation buttons
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer
                GUI.enabled = selectedBlueprintIndex > 0;
                if (GUILayout.Button("\u25B2", GUILayout.Width(30f))) // Up arrow
                {
                    NavigateBlueprint(-1);
                }
                GUI.enabled = selectedBlueprintIndex < blueprintFiles.Count - 1;
                if (GUILayout.Button("\u25BC", GUILayout.Width(30f))) // Down arrow
                {
                    NavigateBlueprint(1);
                }
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else if (!string.IsNullOrEmpty(blueprintFolder))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("No .blueprint files found in the selected folder.", MessageType.Warning, true);
            }

            EditorGUILayout.Space(10);

            // Buttons (Import / Reset)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(2f);
            GUI.enabled = blueprintFiles.Count > 0 && blueprintPrefabCount > 0;
            if (GUILayout.Button("Import", EditorStyles.miniButtonLeft))
            {
                ImportObjects();
            }
            GUI.enabled = true;
            // Buttons (Reset)
            if (GUILayout.Button("Reset", EditorStyles.miniButtonRight))
            {
                ClearParse();
                blueprintFolder = "";
                blueprintFiles.Clear();
                selectedBlueprintIndex = 0;
                Repaint();
            }
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(18);
            EditorGUILayout.EndVertical();
        }
    }
    
    private void DoExportGUI()
    {
        // Toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        showExports = EditorGUILayout.Foldout(showExports, "Export", true, foldoutBoldStyle);
        GUILayout.EndHorizontal();

        if (showExports)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();

            // Export Target
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Export Target", GUILayout.MaxWidth(125f));
            exportTarget = (GameObject)EditorGUILayout.ObjectField(exportTarget, typeof(GameObject), true);
            EditorGUILayout.EndHorizontal();

            // Buttons (Export)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(2f);
            if (GUILayout.Button("Export", EditorStyles.miniButtonLeft))
            {
                ExportObjects();
            }
            // Buttons (Reset)
            if (GUILayout.Button("Reset", EditorStyles.miniButtonRight))
            {
                ClearParse();
                exportTarget = (GameObject)EditorGUILayout.ObjectField(null, typeof(GameObject), true);
                Repaint();
            }
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // Export Settings folder and list
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer
            showExportSettings = EditorGUILayout.Foldout(showExportSettings, "Export path settings", true);
            
            EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(6f)); // spacer
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(18);
            EditorGUILayout.EndVertical();
        }
    }

    #endregion

    #region Stability Tool

    #region Stability Utils

    private struct MaterialProps
    {
        public float maxSupport;
        public float minSupport;
        public float verticalLoss;
        public float horizontalLoss;
    }

    private MaterialProps GetMaterialProperties(WearNTear.MaterialType material)
    {
        switch (material)
        {
            case WearNTear.MaterialType.Wood:
                return new MaterialProps { maxSupport = 100, minSupport = 10, verticalLoss = 0.125f, horizontalLoss = 0.2f };
            case WearNTear.MaterialType.HardWood:
                return new MaterialProps { maxSupport = 140, minSupport = 10, verticalLoss = 0.1f, horizontalLoss = 1f / 6f };
            case WearNTear.MaterialType.Stone:
                return new MaterialProps { maxSupport = 1000, minSupport = 100, verticalLoss = 0.125f, horizontalLoss = 1.0f };
            case WearNTear.MaterialType.Iron:
                return new MaterialProps { maxSupport = 1500, minSupport = 20, verticalLoss = 1f / 13f, horizontalLoss = 1f / 13f };
            case WearNTear.MaterialType.Marble:
                return new MaterialProps { maxSupport = 1500, minSupport = 100, verticalLoss = 0.125f, horizontalLoss = 0.5f };
            case WearNTear.MaterialType.Ashstone:
                return new MaterialProps { maxSupport = 2000, minSupport = 100, verticalLoss = 0.1f, horizontalLoss = 1f / 3f };
            case WearNTear.MaterialType.Ancient:
                return new MaterialProps { maxSupport = 5000, minSupport = 100, verticalLoss = 1f / 15f, horizontalLoss = 0.25f };
            default:
                return new MaterialProps { maxSupport = 100, minSupport = 10, verticalLoss = 0.125f, horizontalLoss = 0.2f };
        }
    }

    private void CreateHighlightMaterial()
    {
        if (highlightMaterial != null) return;

        highlightMaterial = new Material(Shader.Find("Unlit/Color"));
        highlightMaterial.color = new Color(1f, 0f, 0f, 0.5f);
        highlightMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        highlightMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        highlightMaterial.SetInt("_ZWrite", 0);
        highlightMaterial.renderQueue = 3000;
        highlightMaterial.hideFlags = HideFlags.HideAndDontSave;
    }

    private Bounds CalculateTargetBounds(GameObject target)
    {
        Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
        bool boundsInitialized = false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (!boundsInitialized)
            {
                bounds = r.bounds;
                boundsInitialized = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        // Fallback if no renderers
        if (!boundsInitialized)
        {
            bounds = new Bounds(target.transform.position, Vector3.one * 10f);
        }

        return bounds;
    }

    private void CreateGroundPlane(Bounds targetBounds)
    {
        if (groundPlane != null)
        {
            DestroyImmediate(groundPlane);
        }

        // Use a cube instead of a plane - OverlapBox needs volume to detect intersection
        // A thin plane won't register properly with Physics.OverlapBox
        groundPlane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundPlane.name = "StabilityCheck_Ground";
        groundPlane.layer = 15; // Valheim terrain layer

        // Size the ground box to cover entire build with margin
        float size = Mathf.Max(targetBounds.size.x, targetBounds.size.z) + 20f;
        float thickness = 2f; // Thick enough for reliable detection

        // Position ground at world y=0 (like Valheim terrain)
        // Top surface at y=0, extending downward
        float groundTop = 0f;
        groundPlane.transform.position = new Vector3(targetBounds.center.x, groundTop - thickness / 2f, targetBounds.center.z);
        groundPlane.transform.localScale = new Vector3(size, thickness, size);

        // Set material to semi-transparent green for visibility
        var renderer = groundPlane.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material groundMat = new Material(Shader.Find("Unlit/Color"));
            groundMat.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            groundMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            groundMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            groundMat.SetInt("_ZWrite", 0);
            groundMat.renderQueue = 2999;
            groundMat.hideFlags = HideFlags.HideAndDontSave;
            renderer.material = groundMat;
        }

        // Hide in hierarchy and mark as not saved
        groundPlane.hideFlags = HideFlags.HideAndDontSave;
    }

    // Structure to hold collider bound data (mirrors Valheim's WearNTear.BoundData)
    private struct BoundData
    {
        public Vector3 m_pos;
        public Quaternion m_rot;
        public Vector3 m_size;
    }

    private List<BoundData> GetPieceBounds(WearNTear piece)
    {
        List<BoundData> bounds = new List<BoundData>();
        Collider[] colliders = piece.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            if (collider.isTrigger || collider.attachedRigidbody != null)
                continue;

            BoundData bd = new BoundData();
            if (collider is BoxCollider box)
            {
                bd.m_rot = box.transform.rotation;
                bd.m_pos = box.transform.position + box.transform.TransformVector(box.center);
                bd.m_size = new Vector3(
                    box.transform.lossyScale.x * box.size.x,
                    box.transform.lossyScale.y * box.size.y,
                    box.transform.lossyScale.z * box.size.z
                );
            }
            else
            {
                bd.m_rot = Quaternion.identity;
                bd.m_pos = collider.bounds.center;
                bd.m_size = collider.bounds.size;
            }

            // Expand bounds like Valheim does (0.3f tolerance)
            bd.m_size.x += 0.3f;
            bd.m_size.y += 0.3f;
            bd.m_size.z += 0.3f;
            bd.m_size *= 0.5f; // Half extents for OverlapBox

            bounds.Add(bd);
        }

        return bounds;
    }

    private Vector3 GetPieceCOM(WearNTear piece)
    {
        // Valheim uses: transform.position + transform.rotation * m_comOffset
        return piece.transform.position + piece.transform.rotation * piece.m_comOffset;
    }

    private float CalculatePieceSupport(WearNTear piece, Dictionary<WearNTear, float> supportValues, HashSet<Collider> pieceOwnColliders, Collider groundCollider)
    {
        MaterialProps props = GetMaterialProperties(piece.m_materialType);

        // Use all physics layers - named layers like "piece" may not exist in the project
        // We filter manually by checking WearNTear components and ground collider
        int rayMask = ~0; // All layers

        // Get all bounds for this piece (all child colliders)
        List<BoundData> bounds = GetPieceBounds(piece);
        if (bounds.Count == 0)
        {
            // No valid colliders - piece might still be supported if touching ground
            return 0f;
        }

        Vector3 com = GetPieceCOM(piece);
        float bestSupport = 0f;
        bool touchingGround = false;

        // Manual ground check - Physics.OverlapBox may not detect newly created colliders in Editor
        // Check if any piece collider intersects with the ground plane bounds
        if (groundCollider != null)
        {
            Bounds groundBounds = groundCollider.bounds;

            // Use actual collider bounds (AABB) for more reliable intersection
            Collider[] pieceColliders = piece.GetComponentsInChildren<Collider>(true);
            foreach (Collider col in pieceColliders)
            {
                if (col.isTrigger || col.attachedRigidbody != null)
                    continue;

                if (col.bounds.Intersects(groundBounds))
                {
                    touchingGround = true;
                    break;
                }
            }
        }

        // Track support points for two-point angle bonus (Valheim's c_ComMinAngle = 100°)
        List<Vector3> supportPoints = new List<Vector3>();
        List<float> supportPointValues = new List<float>();

        // Check each bound for overlapping colliders (mirrors Valheim's UpdateSupport)
        foreach (BoundData bound in bounds)
        {
            Collider[] nearbyColliders = Physics.OverlapBox(bound.m_pos, bound.m_size, bound.m_rot, rayMask);

            foreach (Collider col in nearbyColliders)
            {
                // Skip triggers and rigidbodies
                if (col.isTrigger || col.attachedRigidbody != null)
                    continue;

                // Skip own colliders
                if (pieceOwnColliders.Contains(col))
                    continue;

                // Check if this is the ground plane collider
                if (groundCollider != null && col == groundCollider)
                {
                    touchingGround = true;
                    continue;
                }

                // Check if this collider belongs to a WearNTear we've already processed
                WearNTear supporterWnt = col.GetComponentInParent<WearNTear>();
                if (supporterWnt == null || supporterWnt == piece)
                {
                    // Non-WearNTear collider that's not terrain = grounded (like static objects)
                    if (supporterWnt == null)
                    {
                        return props.maxSupport;
                    }
                    continue;
                }

                // Skip if supporter doesn't provide support
                if (!supporterWnt.m_supports)
                    continue;

                if (!supportValues.ContainsKey(supporterWnt))
                    continue;

                float supporterSupport = supportValues[supporterWnt];
                if (supporterSupport <= 0)
                    continue;

                // Calculate distance using COM (like Valheim)
                Vector3 supporterCOM = GetPieceCOM(supporterWnt);
                float distance = Vector3.Distance(com, supporterCOM) + 0.1f;

                // Also check distance to supporter's transform position (Valheim uses smaller of the two)
                float distToTransform = Vector3.Distance(com, supporterWnt.transform.position) + 0.1f;
                if (distToTransform < distance && !piece.m_forceCorrectCOMCalculation)
                {
                    distance = distToTransform;
                }

                // Get supporter's material properties for loss calculation
                MaterialProps supporterProps = GetMaterialProperties(supporterWnt.m_materialType);

                // Calculate horizontal loss (baseline)
                float horizontalSupport = supporterSupport - supporterProps.horizontalLoss * distance * supporterSupport;
                bestSupport = Mathf.Max(bestSupport, horizontalSupport);

                // Check if supporter is below us for vertical support
                Vector3 supportPoint = col.ClosestPoint(com);
                if (supportPoint.y < com.y + 0.05f)
                {
                    Vector3 toSupport = (supportPoint - com).normalized;
                    if (toSupport.y < 0f)
                    {
                        // Calculate angle-based interpolation between horizontal and vertical loss
                        float t = Mathf.Acos(1f - Mathf.Abs(toSupport.y)) / (Mathf.PI / 2f);
                        float lossRate = Mathf.Lerp(supporterProps.horizontalLoss, supporterProps.verticalLoss, t);
                        float angledSupport = supporterSupport - lossRate * distance * supporterSupport;
                        bestSupport = Mathf.Max(bestSupport, angledSupport);
                    }

                    // Pure vertical support - track for two-point angle bonus
                    float verticalSupport = supporterSupport - supporterProps.verticalLoss * distance * supporterSupport;
                    bestSupport = Mathf.Max(bestSupport, verticalSupport);

                    // Store support point for angle calculation
                    supportPoints.Add(supportPoint);
                    supportPointValues.Add(verticalSupport);
                }
            }
        }

        // If touching ground, piece is fully supported
        if (touchingGround)
        {
            return props.maxSupport;
        }

        // Two-point angle support bonus (Valheim's c_ComMinAngle = 100°)
        // If two support points are >= 100° apart horizontally, use average of their support values
        if (supportPoints.Count > 1)
        {
            for (int i = 0; i < supportPoints.Count - 1; i++)
            {
                Vector3 dir1 = supportPoints[i] - com;
                dir1.y = 0f; // Project to horizontal plane

                for (int j = i + 1; j < supportPoints.Count; j++)
                {
                    float avgSupport = (supportPointValues[i] + supportPointValues[j]) * 0.5f;

                    // Only consider if this could improve our support
                    if (avgSupport > bestSupport)
                    {
                        Vector3 dir2 = supportPoints[j] - com;
                        dir2.y = 0f; // Project to horizontal plane

                        // Check if supports are >= 100° apart
                        if (Vector3.Angle(dir1, dir2) >= 100f)
                        {
                            bestSupport = avgSupport;
                        }
                    }
                }
            }
        }

        // Cap at this piece's max support
        return Mathf.Min(bestSupport, props.maxSupport);
    }

    private void RunStabilityCheck()
    {
        if (stabilityTarget == null)
        {
            stabilityResultMessage = "No target selected.";
            return;
        }

        // Clear previous results
        ClearStabilityVisuals();

        // Calculate bounds and create ground plane
        Bounds targetBounds = CalculateTargetBounds(stabilityTarget);
        CreateGroundPlane(targetBounds);

        // CRITICAL: Sync physics transforms so OverlapBox can detect colliders in Editor
        Physics.SyncTransforms();

        // Find all WearNTear components under target
        WearNTear[] allPieces = stabilityTarget.GetComponentsInChildren<WearNTear>();

        if (allPieces.Length == 0)
        {
            stabilityResultMessage = "No building pieces found (no WearNTear components).";
            return;
        }

        // Build a lookup of each piece's colliders (to skip self-collision checks)
        Dictionary<WearNTear, HashSet<Collider>> pieceColliders = new Dictionary<WearNTear, HashSet<Collider>>();
        foreach (WearNTear piece in allPieces)
        {
            Collider[] colliders = piece.GetComponentsInChildren<Collider>(true);
            pieceColliders[piece] = new HashSet<Collider>(colliders);
        }

        // Sort by Y position (bottom to top) for proper support propagation
        var sortedPieces = allPieces.OrderBy(p => p.transform.position.y).ToList();

        // Get the ground plane's collider for direct comparison
        Collider groundCollider = groundPlane != null ? groundPlane.GetComponent<Collider>() : null;

        // Calculate support for each piece
        Dictionary<WearNTear, float> supportValues = new Dictionary<WearNTear, float>();
        unstablePieces.Clear();

        foreach (WearNTear piece in sortedPieces)
        {
            HashSet<Collider> ownColliders = pieceColliders[piece];
            float support = CalculatePieceSupport(piece, supportValues, ownColliders, groundCollider);
            supportValues[piece] = support;

            MaterialProps props = GetMaterialProperties(piece.m_materialType);

            // Check if piece would break (support < minSupport)
            if (support < props.minSupport)
            {
                unstablePieces.Add(piece.gameObject);
            }
        }

        // Apply visual highlighting
        if (unstablePieces.Count > 0)
        {
            HighlightUnstablePieces();
            stabilityResultMessage = $"{unstablePieces.Count} unstable piece(s) found (highlighted in red).";
        }
        else
        {
            stabilityResultMessage = "All pieces are stable!";
        }
    }

    private void HighlightUnstablePieces()
    {
        CreateHighlightMaterial();

        foreach (GameObject piece in unstablePieces)
        {
            // Find all MeshFilters in this piece (there may be multiple)
            MeshFilter[] meshFilters = piece.GetComponentsInChildren<MeshFilter>();

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                // Parent highlight directly to the mesh's GameObject for correct positioning
                GameObject highlight = new GameObject("_StabilityHighlight");
                highlight.hideFlags = HideFlags.HideAndDontSave;
                highlight.transform.SetParent(meshFilter.transform);
                highlight.transform.localPosition = Vector3.zero;
                highlight.transform.localRotation = Quaternion.identity;
                highlight.transform.localScale = Vector3.one * 1.02f;

                MeshFilter hMeshFilter = highlight.AddComponent<MeshFilter>();
                hMeshFilter.sharedMesh = meshFilter.sharedMesh;

                MeshRenderer hRenderer = highlight.AddComponent<MeshRenderer>();
                hRenderer.material = highlightMaterial;

                highlightObjects.Add(highlight);
            }
        }
    }

    private void ClearStabilityVisuals()
    {
        // Destroy highlight objects
        foreach (GameObject highlight in highlightObjects)
        {
            if (highlight != null)
            {
                DestroyImmediate(highlight);
            }
        }
        highlightObjects.Clear();

        // Destroy ground plane
        if (groundPlane != null)
        {
            DestroyImmediate(groundPlane);
            groundPlane = null;
        }

        // Clear unstable pieces list
        unstablePieces.Clear();

        // Clear result message
        stabilityResultMessage = "";
    }

    #endregion

    #region Stability GUI

    private void DoStabilityGUI()
    {
        // Toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        showStability = EditorGUILayout.Foldout(showStability, "Stability", true, foldoutBoldStyle);
        GUILayout.EndHorizontal();

        if (showStability)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();

            // Auto-select target if null
            if (stabilityTarget == null)
            {
                // Try to find most recent MWL_ location in scene
                Location[] locations = FindObjectsByType<Location>(FindObjectsSortMode.None);
                if (locations.Length > 0)
                {
                    stabilityTarget = locations[locations.Length - 1].gameObject;
                }
                else if (Selection.activeGameObject != null)
                {
                    stabilityTarget = Selection.activeGameObject;
                }
            }

            // Target field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target", GUILayout.MaxWidth(125f));
            stabilityTarget = (GameObject)EditorGUILayout.ObjectField(stabilityTarget, typeof(GameObject), true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Run Stability Check button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(2f);
            GUI.enabled = stabilityTarget != null;
            if (GUILayout.Button("Run Stability Check", EditorStyles.miniButtonLeft))
            {
                RunStabilityCheck();
            }
            GUI.enabled = true;

            // Clear Highlights button
            if (GUILayout.Button("Clear Highlights", EditorStyles.miniButtonRight))
            {
                ClearStabilityVisuals();
            }
            EditorGUILayout.Space(2f);
            EditorGUILayout.EndHorizontal();

            // Results message
            if (!string.IsNullOrEmpty(stabilityResultMessage))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer
                MessageType msgType = unstablePieces.Count > 0 ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(stabilityResultMessage, msgType, true);
                EditorGUILayout.EndHorizontal();
            }

            // Unstable pieces list
            if (unstablePieces.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer
                showUnstableList = EditorGUILayout.Foldout(showUnstableList, $"Unstable Pieces ({unstablePieces.Count})", true);
                EditorGUILayout.EndHorizontal();

                if (showUnstableList)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(" ", GUILayout.MaxWidth(125f)); // spacer
                    EditorGUILayout.BeginVertical();

                    // Scrollable list with max height
                    float listHeight = Mathf.Min(unstablePieces.Count * 20f, 150f);
                    unstablePiecesScrollPos = EditorGUILayout.BeginScrollView(
                        unstablePiecesScrollPos,
                        GUILayout.Height(listHeight + 5f)
                    );

                    for (int i = 0; i < unstablePieces.Count; i++)
                    {
                        GameObject piece = unstablePieces[i];
                        if (piece == null) continue;

                        EditorGUILayout.BeginHorizontal();

                        // Clickable button with piece name
                        string displayName = piece.name;
                        if (displayName.Length > 35)
                        {
                            displayName = displayName.Substring(0, 32) + "...";
                        }

                        bool isSelected = Selection.activeGameObject == piece;
                        GUIStyle buttonStyle = isSelected ? EditorStyles.toolbarButton : EditorStyles.miniButton;

                        if (GUILayout.Button(displayName, buttonStyle))
                        {
                            // Select and frame the piece in scene view
                            Selection.activeGameObject = piece;
                            SceneView.lastActiveSceneView?.FrameSelected();
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(18);
            EditorGUILayout.EndVertical();
        }
    }

    #endregion

    #endregion
    #endregion

    // #region Misc Tools

    // private void SetImmortal(WearNTear wearNTear)
    // {
    //     wearNTear.m_damages.m_slash = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_blunt = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_pierce = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_fire = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_spirit = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_lightning = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_poison = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_frost = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_pickaxe = HitData.DamageModifier.Immune;
    //     wearNTear.m_damages.m_chop = HitData.DamageModifier.Immune;
    //     wearNTear.m_noRoofWear = false;
    //     wearNTear.m_noSupportWear = false;

    //     wearNTear.m_destroyedEffect = null;
    // }

    // private void SetSelectionsToImmortal()
    // {
    //     Type wntType = typeof(WearNTear);

    //     foreach (var go in Selection.gameObjects)
    //     {
    //         var wnt = go.GetComponent(wntType) as WearNTear;
    //         if (wnt == null)
    //         {
    //             wnt = go.AddComponent(wntType) as WearNTear;
    //         }
    //         SetImmortal(wnt);
    //     }
    // }

    // private bool showMisc = false;
    // private void DoMiscGUI()
    // {

    //     // Toolbar
    //     GUILayout.BeginHorizontal(EditorStyles.toolbar);
    //     showMisc = EditorGUILayout.Foldout(showMisc, "Misc", true, foldoutBoldStyle);
    //     GUILayout.EndHorizontal();

    //     if (showMisc)
    //     {
    //         GUILayout.BeginHorizontal();
    //         GUILayout.Space(6);
    //         GUI.enabled = selectionCount > 0;
    //         if (GUILayout.Button("Set WearNTear to immortal"))
    //         {
    //             SetSelectionsToImmortal();
    //         }
    //         GUI.enabled = true;

    //         GUILayout.Space(4);
    //         GUILayout.EndHorizontal();

    //         GUILayout.BeginHorizontal();
    //         GUILayout.Space(6);
    //         EditorGUILayout.HelpBox("  Creates a WearNTear if one isn't present.", MessageType.Info, true);
    //         GUILayout.Space(4);
    //         GUILayout.EndHorizontal();

    //     }
    // }

    // #endregion
}
#endif