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
        EditorGUILayout.Space(1f);
        DoLootGUI();
        EditorGUILayout.Space(1f);
        DoCreaturesGUI();

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
    [SerializeField] private bool useFlatStabilityGround;
    [SerializeField] private float stabilityGroundHeight;
    private bool stabilityCheckIncomplete;
    private List<GameObject> unstablePieces = new List<GameObject>();
    private List<GameObject> highlightObjects = new List<GameObject>();
    private Material highlightMaterial;
    private string stabilityResultMessage = "";
    private Vector2 unstablePiecesScrollPos;
    private bool showUnstableList = true;

    // Presets config
    [SerializeField]
    private LocationToolsPresets presetsConfig;

    // Loot Section
    private bool showLoot = true;
    private Vector2 lootScrollPos;

    // Creature Section
    private bool showCreatures = true;
    private Vector2 creatureScrollPos;
    private Dictionary<string, bool> creatureCategoryExpanded = new Dictionary<string, bool>();

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

    private void ReloadBlueprintFolder()
    {
        if (string.IsNullOrEmpty(blueprintFolder) || !Directory.Exists(blueprintFolder))
            return;

        string currentFile = blueprintFiles.Count > 0 && selectedBlueprintIndex < blueprintFiles.Count
            ? blueprintFiles[selectedBlueprintIndex]
            : null;

        blueprintFiles = Directory.GetFiles(blueprintFolder, "*.blueprint")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        // Try to maintain selection on the same file
        if (currentFile != null && blueprintFiles.Contains(currentFile))
        {
            selectedBlueprintIndex = blueprintFiles.IndexOf(currentFile);
        }
        else
        {
            selectedBlueprintIndex = Mathf.Clamp(selectedBlueprintIndex, 0, Mathf.Max(0, blueprintFiles.Count - 1));
        }

        if (blueprintFiles.Count > 0)
        {
            LoadSelectedBlueprint();
        }
        else
        {
            ClearParse();
        }
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
                
                GUILayout.Space(10f);
                if (GUILayout.Button("Reload", GUILayout.Width(60f)))
                {
                    ReloadBlueprintFolder();
                }
                
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

    private void DrawDraggablePrefabRow(GameObject prefab, Event evt)
    {
        if (prefab == null) return;

        Rect rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
        GUIContent content = EditorGUIUtility.ObjectContent(prefab, typeof(GameObject));
        GUILayout.Label(content, GUILayout.Height(18));
        EditorGUILayout.EndHorizontal();

        // Make row draggable
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { prefab };
            DragAndDrop.StartDrag(prefab.name);
            evt.Use();
        }
    }

    private void DoLootGUI()
    {
        // Toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        showLoot = EditorGUILayout.Foldout(showLoot, "Loot Presets", true, foldoutBoldStyle);
        GUILayout.EndHorizontal();

        if (showLoot)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();

            // Config assignment field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Presets Config", GUILayout.MaxWidth(125f));
            presetsConfig = (LocationToolsPresets)EditorGUILayout.ObjectField(presetsConfig, typeof(LocationToolsPresets), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (presetsConfig == null)
            {
                EditorGUILayout.HelpBox("Assign a LocationToolsPresets asset to enable loot presets.", MessageType.Info);
            }
            else if (presetsConfig.lootPrefabs.Count == 0)
            {
                EditorGUILayout.HelpBox("No loot prefabs configured. Add prefabs in the LocationToolsPresets asset.", MessageType.Info);
            }
            else
            {
                Event evt = Event.current;

                // Scrollable list with max height
                float listHeight = Mathf.Min(presetsConfig.lootPrefabs.Count * 22f, 200f);
                lootScrollPos = EditorGUILayout.BeginScrollView(
                    lootScrollPos,
                    GUILayout.Height(listHeight + 5f)
                );

                foreach (GameObject prefab in presetsConfig.lootPrefabs)
                {
                    DrawDraggablePrefabRow(prefab, evt);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(18);
            EditorGUILayout.EndVertical();
        }
    }

    private void DoCreaturesGUI()
    {
        // Toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        showCreatures = EditorGUILayout.Foldout(showCreatures, "Creature Presets", true, foldoutBoldStyle);
        GUILayout.EndHorizontal();

        if (showCreatures)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();

            // Config assignment field (only show if not already shown in Loot section)
            if (!showLoot)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Presets Config", GUILayout.MaxWidth(125f));
                presetsConfig = (LocationToolsPresets)EditorGUILayout.ObjectField(presetsConfig, typeof(LocationToolsPresets), false);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }

            if (presetsConfig == null)
            {
                EditorGUILayout.HelpBox("Assign a LocationToolsPresets asset to enable creature presets.", MessageType.Info);
            }
            else if (presetsConfig.creatureCategories.Count == 0)
            {
                EditorGUILayout.HelpBox("No creature categories configured. Add categories in the LocationToolsPresets asset.", MessageType.Info);
            }
            else
            {
                Event evt = Event.current;

                // Scrollable list with max height
                float listHeight = Mathf.Min(presetsConfig.creatureCategories.Count * 50f, 300f);
                creatureScrollPos = EditorGUILayout.BeginScrollView(
                    creatureScrollPos,
                    GUILayout.Height(listHeight + 5f)
                );

                foreach (var category in presetsConfig.creatureCategories)
                {
                    if (string.IsNullOrEmpty(category.categoryName)) continue;

                    // Initialize expanded state if not present
                    if (!creatureCategoryExpanded.ContainsKey(category.categoryName))
                    {
                        creatureCategoryExpanded[category.categoryName] = false;
                    }

                    // Category foldout
                    creatureCategoryExpanded[category.categoryName] = EditorGUILayout.Foldout(
                        creatureCategoryExpanded[category.categoryName],
                        category.categoryName,
                        true
                    );

                    if (creatureCategoryExpanded[category.categoryName])
                    {
                        EditorGUI.indentLevel++;
                        foreach (GameObject prefab in category.prefabs)
                        {
                            DrawDraggablePrefabRow(prefab, evt);
                        }
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(18);
            EditorGUILayout.EndVertical();
        }
    }

    #endregion

    #region Stability Tool

    #region Stability Utils

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

    private void RunStabilityCheck()
    {
        ClearStabilityVisuals();
        if (stabilityTarget == null)
        {
            stabilityResultMessage = "No target selected.";
            return;
        }
        try
        {
            LocationStability.Result result = LocationStability.Check(stabilityTarget,
                useFlatStabilityGround ? (float?)stabilityGroundHeight : null,
                (progress, message) => EditorUtility.DisplayCancelableProgressBar("Location stability", message, progress));
            foreach (WearNTear piece in result.Failed)
                unstablePieces.Add(piece.gameObject);
            if (unstablePieces.Count > 0) HighlightUnstablePieces();
            stabilityCheckIncomplete = !result.Complete;
            string ground = useFlatStabilityGround ? $"Assumed flat ground at world Y={stabilityGroundHeight}." : "Using scene colliders only.";
            string outcome = result.Complete
                ? $"{result.Failed.Count} of {result.Support.Count} pieces predicted to fail after support settles."
                : "Check incomplete.";
            stabilityResultMessage = $"{outcome} {ground}\n{result.Detail}\n"
                + $"{result.ExternalPieces} connected pieces outside the target included; {result.ProtectedPieces} target pieces have support wear disabled.";
        }
        catch (OperationCanceledException)
        {
            stabilityCheckIncomplete = true;
            stabilityResultMessage = "Check canceled. No stability result is available.";
        }
        catch (Exception exception)
        {
            stabilityCheckIncomplete = true;
            stabilityResultMessage = "Check incomplete: " + exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            SceneView.RepaintAll();
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

        // Clear unstable pieces list
        unstablePieces.Clear();

        // Clear result message
        stabilityResultMessage = "";
        stabilityCheckIncomplete = false;
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
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target", GUILayout.MaxWidth(125f));
            stabilityTarget = (GameObject)EditorGUILayout.ObjectField(stabilityTarget, typeof(GameObject), true);
            EditorGUILayout.EndHorizontal();

            useFlatStabilityGround = EditorGUILayout.Toggle("Assume flat ground", useFlatStabilityGround);
            if (useFlatStabilityGround)
                stabilityGroundHeight = EditorGUILayout.FloatField("Ground world Y", stabilityGroundHeight);
            if (EditorGUI.EndChangeCheck()) ClearStabilityVisuals();
            EditorGUILayout.HelpBox("Checks settled support against scene colliders. Enable flat ground only when it matches the location placement. This estimate does not reproduce game update timing or runtime mod changes.", MessageType.Info);

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
                MessageType msgType = unstablePieces.Count > 0 || stabilityCheckIncomplete ? MessageType.Warning : MessageType.Info;
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
