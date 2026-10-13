using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Keeps the Game Play tab's right column in one deterministic vertical layout.
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
            var discardParent = GetDropdownParent(serializedPanel, "discardModeDropdown");
            var playModeParent = GetDropdownParent(serializedPanel, "playModeDropdown");
            var aiDifficultyParent = GetDropdownParent(serializedPanel, "aiDifficultyDropdown");
            var rightGroup = aiDifficultyParent.parent as RectTransform;

            if (rightGroup == null || playModeParent.parent != rightGroup || discardParent.parent != rightGroup)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Unexpected dropdown hierarchy.");
            }

            var gameplayCategory = rightGroup.parent as RectTransform;
            var leftGroup = gameplayCategory != null ? gameplayCategory.Find("Left Group") as RectTransform : null;

            if (leftGroup == null || leftGroup.childCount < 2)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Left-column slider references were not found.");
            }

            var rightLayout = rightGroup.GetComponent<VerticalLayoutGroup>();
            var leftLayout = leftGroup.GetComponent<VerticalLayoutGroup>();
            var spacers = GetSpacers(rightGroup);

            if (rightLayout == null || leftLayout == null || spacers.Count < 2)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Expected column layouts or dropdown spacers were not found.");
            }

            var spacerAfterDiscard = spacers[0];
            var spacerAfterPlayMode = spacers[1];
            var flexibleSpacer = spacers.Count > 2 ? spacers[2] : CreateFlexibleSpacer(rightGroup);
            var bottomGroup = FindRect(gameplayCategory, bottomGroupName);
            var frozenHealthSlider = FindRect(gameplayCategory, frozenHealthSliderName);
            var lightningDamageSlider = FindRect(gameplayCategory, lightningDamageSliderName);
            var frozenReference = (RectTransform)leftGroup.GetChild(leftGroup.childCount - 2);
            var lightningReference = (RectTransform)leftGroup.GetChild(leftGroup.childCount - 1);
            var bottomLayout = bottomGroup.GetComponent<VerticalLayoutGroup>();

            if (bottomLayout == null)
            {
                throw new InvalidOperationException("[SettingsLayoutFix] Bottom Toggles layout was not found.");
            }

            playModeParent.gameObject.SetActive(true);
            GetDropdown(serializedPanel, "playModeDropdown").interactable = true;
            spacerAfterDiscard.gameObject.SetActive(true);
            spacerAfterPlayMode.gameObject.SetActive(true);
            flexibleSpacer.gameObject.SetActive(true);
            bottomGroup.gameObject.SetActive(true);

            bottomGroup.SetParent(rightGroup, false);
            discardParent.SetSiblingIndex(0);
            spacerAfterDiscard.SetSiblingIndex(1);
            playModeParent.SetSiblingIndex(2);
            spacerAfterPlayMode.SetSiblingIndex(3);
            aiDifficultyParent.SetSiblingIndex(4);
            flexibleSpacer.SetSiblingIndex(5);
            bottomGroup.SetAsLastSibling();

            var internalSpacer = bottomGroup.Find("Spacer");

            if (internalSpacer != null)
            {
                UnityEngine.Object.DestroyImmediate(internalSpacer.gameObject);
            }

            frozenHealthSlider.SetParent(bottomGroup, false);
            frozenHealthSlider.SetSiblingIndex(0);
            lightningDamageSlider.SetParent(bottomGroup, false);
            lightningDamageSlider.SetSiblingIndex(1);

            ConfigureRightLayout(rightLayout, leftLayout);
            ConfigureBottomLayout(bottomLayout, leftLayout);

            var frozenHeight = GetHeight(frozenReference);
            var lightningHeight = GetHeight(lightningReference);
            var bottomHeight = frozenHeight + leftLayout.spacing + lightningHeight;

            ConfigureLayoutElement(discardParent, GetHeight(discardParent), 0f);
            ConfigureLayoutElement(spacerAfterDiscard, GetHeight(spacerAfterDiscard), 0f);
            ConfigureLayoutElement(playModeParent, GetHeight(playModeParent), 0f);
            ConfigureLayoutElement(spacerAfterPlayMode, GetHeight(spacerAfterPlayMode), 0f);
            ConfigureLayoutElement(aiDifficultyParent, GetHeight(aiDifficultyParent), 0f);
            ConfigureLayoutElement(flexibleSpacer, 0f, 1f, 0f);
            ConfigureLayoutElement(bottomGroup, bottomHeight, 0f);
            ConfigureLayoutElement(frozenHealthSlider, frozenHeight, 0f);
            ConfigureLayoutElement(lightningDamageSlider, lightningHeight, 0f);

            bottomGroup.localScale = Vector3.one;
            bottomGroup.anchorMin = Vector2.zero;
            bottomGroup.anchorMax = Vector2.zero;
            bottomGroup.pivot = new Vector2(0.5f, 0.5f);
            bottomGroup.anchoredPosition = Vector2.zero;
            bottomGroup.sizeDelta = new Vector2(0f, bottomHeight);
            flexibleSpacer.sizeDelta = new Vector2(flexibleSpacer.sizeDelta.x, 0f);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gameplayCategory);
            ValidateLayout(rightGroup, bottomGroup, flexibleSpacer, frozenHealthSlider,
                lightningDamageSlider, frozenReference, lightningReference);

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

    private static RectTransform GetDropdownParent(SerializedObject serializedPanel, string propertyName)
    {
        return (RectTransform)GetDropdown(serializedPanel, propertyName).transform.parent;
    }

    private static List<RectTransform> GetSpacers(RectTransform parent)
    {
        var spacers = new List<RectTransform>();

        for (var i = 0; i < parent.childCount; i++)
        {
            var child = (RectTransform)parent.GetChild(i);

            if (child.name == "Spacer")
            {
                spacers.Add(child);
            }
        }

        return spacers;
    }

    private static RectTransform CreateFlexibleSpacer(RectTransform parent)
    {
        var spacerObject = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacerObject.layer = parent.gameObject.layer;
        var spacer = (RectTransform)spacerObject.transform;
        spacer.SetParent(parent, false);
        return spacer;
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

    private static void ConfigureRightLayout(VerticalLayoutGroup rightLayout, VerticalLayoutGroup leftLayout)
    {
        rightLayout.padding = new RectOffset(leftLayout.padding.left, leftLayout.padding.right,
            leftLayout.padding.top, leftLayout.padding.bottom);
        rightLayout.spacing = 0f;
        rightLayout.childAlignment = leftLayout.childAlignment;
        rightLayout.childForceExpandWidth = true;
        rightLayout.childForceExpandHeight = false;
        rightLayout.childControlWidth = true;
        rightLayout.childControlHeight = true;
        rightLayout.childScaleWidth = false;
        rightLayout.childScaleHeight = false;
    }

    private static void ConfigureBottomLayout(VerticalLayoutGroup bottomLayout, VerticalLayoutGroup leftLayout)
    {
        bottomLayout.enabled = true;
        bottomLayout.padding = new RectOffset();
        bottomLayout.spacing = leftLayout.spacing;
        bottomLayout.childAlignment = leftLayout.childAlignment;
        bottomLayout.childForceExpandWidth = true;
        bottomLayout.childForceExpandHeight = false;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childScaleWidth = false;
        bottomLayout.childScaleHeight = false;
    }

    private static void ConfigureLayoutElement(RectTransform rect, float preferredHeight, float flexibleHeight,
        float minHeight = -1f)
    {
        var layoutElement = rect.GetComponent<LayoutElement>();

        if (layoutElement == null)
        {
            layoutElement = rect.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.enabled = true;
        layoutElement.ignoreLayout = false;
        layoutElement.minHeight = minHeight;
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleHeight = flexibleHeight;
    }

    private static float GetHeight(RectTransform rect)
    {
        return rect.sizeDelta.y > 0f ? rect.sizeDelta.y : rect.rect.height;
    }

    private static void ValidateLayout(RectTransform rightGroup, RectTransform bottomGroup,
        RectTransform flexibleSpacer, RectTransform frozenHealthSlider, RectTransform lightningDamageSlider,
        RectTransform frozenReference, RectTransform lightningReference)
    {
        const float tolerance = 0.5f;

        if (bottomGroup.parent != rightGroup || bottomGroup.GetSiblingIndex() != rightGroup.childCount - 1 ||
            flexibleSpacer.GetSiblingIndex() != bottomGroup.GetSiblingIndex() - 1)
        {
            throw new InvalidOperationException("[SettingsLayoutFix] Bottom Toggles is not last after the flexible spacer.");
        }

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
