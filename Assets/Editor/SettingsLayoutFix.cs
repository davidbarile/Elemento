using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Restructures the Game Play tab's right column of the Settings Screen prefab:
// Discard Mode, Play Mode, AI Difficulty stacked at the top by the Right Group's
// VerticalLayoutGroup, and Bottom Toggles (Frozen Zone Health, Lightning Damage)
// anchored to the bottom so extra dropdowns never push them offscreen.
public static class SettingsLayoutFix
{
    private const string PREFAB_PATH = "Assets/Prefabs/Settings Screen.prefab";
    private const string BOTTOM_GROUP_NAME = "Bottom Toggles";

    [MenuItem("Elemento/Fix Settings Layout")]
    public static void Run()
    {
        var root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

        try
        {
            var panel = root.GetComponentInChildren<UISettingsPanel>(true);
            var serializedPanel = new SerializedObject(panel);

            var discardDropdown = (TMP_Dropdown)serializedPanel.FindProperty("discardModeDropdown").objectReferenceValue;
            var playModeDropdown = (TMP_Dropdown)serializedPanel.FindProperty("playModeDropdown").objectReferenceValue;
            var aiDifficultyDropdown = (TMP_Dropdown)serializedPanel.FindProperty("aiDifficultyDropdown").objectReferenceValue;

            var discardParent = (RectTransform)discardDropdown.transform.parent;
            var playModeParent = (RectTransform)playModeDropdown.transform.parent;
            var aiDifficultyParent = (RectTransform)aiDifficultyDropdown.transform.parent;
            var column = (RectTransform)aiDifficultyParent.parent;

            if (playModeParent.parent != column || discardParent.parent != column)
            {
                Debug.LogError("[SettingsLayoutFix] Unexpected hierarchy; aborting.");
                return;
            }

            // Top group order: Discard, spacer, Play Mode, spacer, AI Difficulty.
            playModeParent.gameObject.SetActive(true);
            playModeDropdown.interactable = true;

            var discardIndex = discardParent.GetSiblingIndex();
            var spacerAfterDiscard = column.GetChild(discardIndex + 1);

            if (spacerAfterDiscard.name == "Spacer")
                spacerAfterDiscard.gameObject.SetActive(true);

            playModeParent.SetSiblingIndex(discardIndex + 2);

            var spacerAfterPlayMode = column.GetChild(playModeParent.GetSiblingIndex() + 1);

            if (spacerAfterPlayMode.name == "Spacer")
                spacerAfterPlayMode.gameObject.SetActive(true);

            aiDifficultyParent.SetSiblingIndex(playModeParent.GetSiblingIndex() + 2);

            // Bottom group: pull out of the layout and anchor to the column's bottom.
            var bottomGroup = (RectTransform)column.Find(BOTTOM_GROUP_NAME);
            var layout = column.GetComponent<VerticalLayoutGroup>();

            // The fixed 80px spacer that sat between AI Difficulty and the bottom group is no longer needed.
            for (var i = column.childCount - 1; i > aiDifficultyParent.GetSiblingIndex(); i--)
            {
                var child = column.GetChild(i);

                if (child.name == "Spacer" && child.GetComponents<Component>().Length == 1)
                    Object.DestroyImmediate(child.gameObject);
            }

            var layoutElement = bottomGroup.GetComponent<LayoutElement>();

            if (layoutElement == null)
                layoutElement = bottomGroup.gameObject.AddComponent<LayoutElement>();

            layoutElement.ignoreLayout = true;

            var height = bottomGroup.sizeDelta.y;
            bottomGroup.anchorMin = new Vector2(0f, 0f);
            bottomGroup.anchorMax = new Vector2(1f, 0f);
            bottomGroup.pivot = new Vector2(0.5f, 0f);
            bottomGroup.sizeDelta = new Vector2(-(layout.padding.left + layout.padding.right), height);
            bottomGroup.anchoredPosition = new Vector2((layout.padding.left - layout.padding.right) * 0.5f, layout.padding.bottom);
            bottomGroup.SetAsLastSibling();

            PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            Debug.Log("[SettingsLayoutFix] Settings Screen prefab updated.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
