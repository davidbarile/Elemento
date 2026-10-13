using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Keeps the Game Play tab's dropdowns in the right-column layout while placing
// its final two sliders directly from the matching left-column row geometry.
public static class SettingsLayoutFix
{
    private const string prefabPath = "Assets/Prefabs/Settings Screen.prefab";
    private const string bottomGroupName = "Bottom Toggles";
    private const string frozenHealthSliderName = "Freeze Health Slider";
    private const string lightningDamageSliderName = "Lightning Damage Slider";

    [MenuItem("Elemento/Fix Settings Layout")]
    public static void Run()
    {
        Apply();
    }

    public static void RunBatch()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            var panel = root.GetComponentInChildren<UISettingsPanel>(true);

            if (panel == null)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] UISettingsPanel was not found.");
            }

            var serializedPanel = new SerializedObject(panel);
            var discardDropdown = GetDropdown(serializedPanel, "discardModeDropdown");
            var playModeDropdown = GetDropdown(serializedPanel, "playModeDropdown");
            var aiDifficultyDropdown = GetDropdown(serializedPanel, "aiDifficultyDropdown");
            var discardParent = (RectTransform)discardDropdown.transform.parent;
            var playModeParent = (RectTransform)playModeDropdown.transform.parent;
            var aiDifficultyParent = (RectTransform)aiDifficultyDropdown.transform.parent;
            var rightGroup = (RectTransform)aiDifficultyParent.parent;

            if (playModeParent.parent != rightGroup || discardParent.parent != rightGroup)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Unexpected dropdown hierarchy.");
            }

            var gameplayCategory = rightGroup.parent as RectTransform;
            var leftGroup = gameplayCategory != null ? gameplayCategory.Find("Left Group") as RectTransform : null;

            if (leftGroup == null || leftGroup.childCount < 2)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Left-column slider references were not found.");
            }

            playModeParent.gameObject.SetActive(true);
            playModeDropdown.interactable = true;

            var discardIndex = discardParent.GetSiblingIndex();
            var spacerAfterDiscard = GetSpacer(rightGroup, discardIndex + 1);
            spacerAfterDiscard.gameObject.SetActive(true);
            playModeParent.SetSiblingIndex(discardIndex + 2);

            var spacerAfterPlayMode = GetSpacer(rightGroup, playModeParent.GetSiblingIndex() + 1);
            spacerAfterPlayMode.gameObject.SetActive(true);
            aiDifficultyParent.SetSiblingIndex(playModeParent.GetSiblingIndex() + 2);

            var spacerAfterAiIndex = aiDifficultyParent.GetSiblingIndex() + 1;

            if (spacerAfterAiIndex < rightGroup.childCount && rightGroup.GetChild(spacerAfterAiIndex).name == "Spacer")
            {
                UnityEngine.Object.DestroyImmediate(rightGroup.GetChild(spacerAfterAiIndex).gameObject);
            }

            var bottomGroup = FindRect(gameplayCategory, bottomGroupName);
            var frozenHealthSlider = FindRect(gameplayCategory, frozenHealthSliderName);
            var lightningDamageSlider = FindRect(gameplayCategory, lightningDamageSliderName);
            var frozenReference = (RectTransform)leftGroup.GetChild(leftGroup.childCount - 2);
            var lightningReference = (RectTransform)leftGroup.GetChild(leftGroup.childCount - 1);
            var leftLayout = leftGroup.GetComponent<VerticalLayoutGroup>();
            var bottomLayout = bottomGroup.GetComponent<VerticalLayoutGroup>();

            if (leftLayout == null || bottomLayout == null)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Expected slider layouts were not found.");
            }

            bottomGroup.SetParent(gameplayCategory, false);
            CopyRect(rightGroup, bottomGroup);
            bottomGroup.SetAsLastSibling();

            var bottomLayoutElement = bottomGroup.GetComponent<LayoutElement>();

            if (bottomLayoutElement != null)
            {
                bottomLayoutElement.enabled = false;
            }

            var bottomTopPadding = leftLayout.padding.top;

            for (var i = 0; i < leftGroup.childCount - 2; i++)
            {
                bottomTopPadding += Mathf.RoundToInt(((RectTransform)leftGroup.GetChild(i)).rect.height + leftLayout.spacing);
            }

            bottomLayout.enabled = true;
            bottomLayout.padding = new RectOffset(leftLayout.padding.left, leftLayout.padding.right, bottomTopPadding, leftLayout.padding.bottom);
            bottomLayout.spacing = leftLayout.spacing;
            bottomLayout.childAlignment = TextAnchor.UpperLeft;
            bottomLayout.childForceExpandWidth = leftLayout.childForceExpandWidth;
            bottomLayout.childForceExpandHeight = leftLayout.childForceExpandHeight;
            bottomLayout.childControlWidth = leftLayout.childControlWidth;
            bottomLayout.childControlHeight = leftLayout.childControlHeight;
            bottomLayout.childScaleWidth = leftLayout.childScaleWidth;
            bottomLayout.childScaleHeight = leftLayout.childScaleHeight;

            var internalSpacer = bottomGroup.Find("Spacer");

            if (internalSpacer != null)
            {
                UnityEngine.Object.DestroyImmediate(internalSpacer.gameObject);
            }

            PlaceSlider(frozenHealthSlider, frozenReference, bottomGroup, 0);
            PlaceSlider(lightningDamageSlider, lightningReference, bottomGroup, 1);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gameplayCategory);
            ValidateLayout(frozenHealthSlider, lightningDamageSlider, frozenReference, lightningReference);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var saveSucceeded);

            if (!saveSucceeded)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Settings Screen prefab could not be saved.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SettingsLayoutFix] Settings Screen prefab updated.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static TMP_Dropdown GetDropdown(SerializedObject serializedPanel, string propertyName)
    {
        var property = serializedPanel.FindProperty(propertyName);
        var dropdown = property != null ? property.objectReferenceValue as TMP_Dropdown : null;

        if (dropdown == null)
        {
            throw new InvalidOperationException($"[SettingsLayoutFix] {propertyName} was not assigned.");
        }

        return dropdown;
    }

    private static RectTransform GetSpacer(RectTransform parent, int index)
    {
        if (index >= parent.childCount || parent.GetChild(index).name != "Spacer")
        {
            throw new InvalidOperationException("[SettingsLayoutFix] Expected dropdown spacer was not found.");
        }

        return (RectTransform)parent.GetChild(index);
    }

    private static RectTransform FindRect(RectTransform root, string objectName)
    {
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name == objectName)
            {
                return rect;
            }
        }

        throw new InvalidOperationException($"[SettingsLayoutFix] {objectName} was not found.");
    }

    private static void PlaceSlider(RectTransform slider, RectTransform reference, RectTransform bottomGroup, int siblingIndex)
    {
        slider.SetParent(bottomGroup, false);
        CopyRect(reference, slider);
        slider.SetSiblingIndex(siblingIndex);
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition = source.anchoredPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void ValidateLayout(RectTransform frozenHealthSlider, RectTransform lightningDamageSlider,
        RectTransform frozenReference, RectTransform lightningReference)
    {
        const float tolerance = 0.5f;

        if (Mathf.Abs(frozenHealthSlider.rect.width - frozenReference.rect.width) > tolerance ||
            Mathf.Abs(lightningDamageSlider.rect.width - lightningReference.rect.width) > tolerance)
        {
            throw new InvalidOperationException("[SettingsLayoutFix] Right-column slider widths do not match the left column.");
        }

        var lightningCorners = new Vector3[4];
        var referenceCorners = new Vector3[4];
        lightningDamageSlider.GetWorldCorners(lightningCorners);
        lightningReference.GetWorldCorners(referenceCorners);

        if (Mathf.Abs(lightningCorners[0].y - referenceCorners[0].y) > tolerance)
        {
            throw new InvalidOperationException("[SettingsLayoutFix] Lightning Damage is not bottom-aligned with Total Points to Win.");
        }
    }
}
