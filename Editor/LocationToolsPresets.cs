using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CreateAssetMenu(fileName = "LocationToolsPresets", menuName = "Location Tools/Presets Config")]
public class LocationToolsPresets : ScriptableObject
{
    [Serializable]
    public class CreatureCategory
    {
        public string categoryName;
        public List<GameObject> prefabs = new List<GameObject>();
    }

    [Header("Loot Presets")]
    public List<GameObject> lootPrefabs = new List<GameObject>();

    [Header("Creature Presets")]
    public List<CreatureCategory> creatureCategories = new List<CreatureCategory>();
}
#endif
