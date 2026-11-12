using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The list of stages the MAIN MENU shows. The menu scene can't see the Puzzle scene's objects, so this small asset
/// (Assets/Resources/StageCatalog.asset) carries the list across.
///
/// YOU NEVER EDIT THIS BY HAND. It is rebuilt from the StageDefinition objects in the Puzzle scene every time you SAVE that scene
/// (see StageCatalogSync in the Editor folder), or from the menu  Tools > Stages > Rebuild Stage Catalog From Open Scene.
///   Add a stage     duplicate a Stage object in the Puzzle scene (or add Stage Definition to a new empty object), save the scene.
///   Remove a stage  delete its object, save the scene.
/// </summary>
[CreateAssetMenu(fileName = "StageCatalog", menuName = "Stages/Stage Catalog")]
public class StageCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        public StageMode mode;
        public int order;
        public Sprite thumbnail;
        [TextArea(1, 3)] public string description;
    }

    public const string ResourceName = "StageCatalog";

    [Tooltip("Exact name of the scene that holds the stages. It must be in Build Settings. Filled in by the sync tool.")]
    public string stageScene = "Puzzle";

    public List<Entry> stages = new List<Entry>();

    public static StageCatalog Load()
    {
        return Resources.Load<StageCatalog>(ResourceName);
    }

    /// <summary>The stages of one mode, in menu order (Order first, then name).</summary>
    public List<Entry> Get(StageMode mode)
    {
        List<Entry> list = new List<Entry>();
        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i] != null && stages[i].mode == mode) list.Add(stages[i]);
        }

        list.Sort((a, b) =>
        {
            if (a.order != b.order) return a.order.CompareTo(b.order);
            return string.CompareOrdinal(a.displayName, b.displayName);
        });
        return list;
    }
}
