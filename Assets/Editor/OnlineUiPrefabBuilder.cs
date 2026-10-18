using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OnlineUiPrefabBuilder
{
    private const string waitingPrefabPath = "Assets/Prefabs/Waiting For Opponent Panel.prefab";
    private const string lobbyPrefabPath = "Assets/Prefabs/Online Lobby Panel.prefab";
    private const string activeGameRowPrefabPath = "Assets/Prefabs/Active Game Row.prefab";

    [MenuItem("Elemento/Build Online UI Prefabs")]
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
        GameObject lobbyRoot = PrefabUtility.LoadPrefabContents(lobbyPrefabPath);
        try
        {
            RectTransform backButton = FindRect(lobbyRoot.transform, "Back Button");
            TextMeshProUGUI buttonText = backButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText == null)
                throw new InvalidOperationException("[OnlineUiPrefabBuilder] Lobby Back Button text was not found.");

            UIActiveGameRow rowPrefab = BuildActiveGameRowPrefab(backButton.gameObject, buttonText);
            ConfigureWaitingPrefab(backButton.gameObject);
            ConfigureLobbyPrefab(lobbyRoot, rowPrefab, backButton.gameObject, buttonText);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(lobbyRoot);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[OnlineUiPrefabBuilder] Built waiting overlay, Active Game Row, and lobby active-games list.");
    }

    private static UIActiveGameRow BuildActiveGameRowPrefab(GameObject buttonSource,
        TextMeshProUGUI textSource)
    {
        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(activeGameRowPrefabPath) != null;
        GameObject root = prefabExists
            ? PrefabUtility.LoadPrefabContents(activeGameRowPrefabPath)
            : new GameObject("Active Game Row", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(HorizontalLayoutGroup), typeof(UIActiveGameRow));

        try
        {
            root.name = "Active Game Row";
            SetLayerRecursively(root, buttonSource.layer);
            RectTransform rootRect = GetOrAddComponent<RectTransform>(root);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(560f, 110f);

            Image background = GetOrAddComponent<Image>(root);
            background.color = new Color(0f, 0f, 0f, 0.45f);
            background.raycastTarget = false;

            HorizontalLayoutGroup layout = GetOrAddComponent<HorizontalLayoutGroup>(root);
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            RectTransform leftColumn = GetOrCreateRect(rootRect, "Match Text");
            VerticalLayoutGroup leftLayout = GetOrAddComponent<VerticalLayoutGroup>(leftColumn.gameObject);
            leftLayout.padding = new RectOffset(8, 8, 5, 5);
            leftLayout.spacing = 2f;
            leftLayout.childAlignment = TextAnchor.MiddleLeft;
            leftLayout.childControlWidth = true;
            leftLayout.childControlHeight = true;
            leftLayout.childForceExpandWidth = true;
            leftLayout.childForceExpandHeight = false;
            ConfigureLayoutElement(leftColumn.gameObject, 220f, 90f);
            leftColumn.SetSiblingIndex(0);

            TextMeshProUGUI codeText = GetOrCreateText(leftColumn, "Code Text", textSource);
            codeText.text = "Code: 52MMXK";
            codeText.alignment = TextAlignmentOptions.MidlineLeft;
            codeText.fontSize = 24f;
            ConfigureLayoutElement(codeText.gameObject, 220f, 38f);

            TextMeshProUGUI opponentText = GetOrCreateText(leftColumn, "Opponent Text", textSource);
            opponentText.text = "vs Opponent";
            opponentText.alignment = TextAlignmentOptions.MidlineLeft;
            opponentText.fontSize = 22f;
            ConfigureLayoutElement(opponentText.gameObject, 220f, 38f);

            GameObject rejoinObject = GetOrCreateButton(rootRect, buttonSource, "Rejoin Button");
            Button rejoinButton = RequireButton(rejoinObject, "Rejoin Button");
            TextMeshProUGUI rejoinText = RequireButtonText(rejoinObject, "Rejoin Button");
            rejoinText.text = "Rejoin";
            rejoinText.fontSize = 22f;
            ConfigureLayoutElement(rejoinObject, 150f, 80f);
            rejoinObject.transform.SetSiblingIndex(1);

            GameObject forfeitObject = GetOrCreateButton(rootRect, buttonSource, "Forfeit Button");
            Button forfeitButton = RequireButton(forfeitObject, "Forfeit Button");
            TextMeshProUGUI forfeitText = RequireButtonText(forfeitObject, "Forfeit Button");
            forfeitText.text = "Forfeit";
            forfeitText.fontSize = 22f;
            ConfigureLayoutElement(forfeitObject, 150f, 80f);
            forfeitObject.transform.SetSiblingIndex(2);

            UIActiveGameRow row = GetOrAddComponent<UIActiveGameRow>(root);
            SerializedObject serializedRow = new(row);
            Assign(serializedRow, "codeText", codeText);
            Assign(serializedRow, "opponentText", opponentText);
            Assign(serializedRow, "rejoinButton", rejoinButton);
            Assign(serializedRow, "rejoinText", rejoinText);
            Assign(serializedRow, "forfeitButton", forfeitButton);
            Assign(serializedRow, "forfeitText", forfeitText);
            serializedRow.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, activeGameRowPrefabPath);
        }
        finally
        {
            if (prefabExists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }

        GameObject prefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(activeGameRowPrefabPath);
        UIActiveGameRow prefab = prefabObject == null ? null : prefabObject.GetComponent<UIActiveGameRow>();
        if (prefab == null)
            throw new InvalidOperationException("[OnlineUiPrefabBuilder] Active Game Row prefab could not be loaded.");
        return prefab;
    }

    private static void ConfigureWaitingPrefab(GameObject buttonSource)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(waitingPrefabPath);
        try
        {
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;

            if (root.GetComponent<GraphicRaycaster>() == null)
                root.AddComponent<GraphicRaycaster>();

            RectTransform dim = FindRect(root.transform, "Dim Out");
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.anchoredPosition = Vector2.zero;
            dim.sizeDelta = Vector2.zero;
            Image dimImage = dim.GetComponent<Image>();
            CanvasGroup dimGroup = dim.GetComponent<CanvasGroup>();
            if (dimImage == null || dimGroup == null)
                throw new InvalidOperationException("[OnlineUiPrefabBuilder] Waiting Dim Out components were not found.");
            dimImage.raycastTarget = true;
            dimGroup.blocksRaycasts = true;
            dim.SetAsFirstSibling();

            RectTransform banner = FindRect(root.transform, "Banner");
            // Main.unity stretches the panel root full-screen, so the prefab offset is already the on-screen position.
            banner.anchoredPosition = new Vector2(0f, -45f);
            banner.SetSiblingIndex(1);

            GameObject leaveObject = GetOrCreateButton(rootRect, buttonSource, "Leave Button");
            RectTransform leaveRect = (RectTransform)leaveObject.transform;
            leaveRect.anchorMin = leaveRect.anchorMax = new Vector2(0.5f, 1f);
            leaveRect.pivot = new Vector2(0.5f, 0.5f);
            leaveRect.anchoredPosition = new Vector2(0f, -230f);
            leaveRect.sizeDelta = new Vector2(320f, 90f);
            leaveRect.SetAsLastSibling();
            Button leaveButton = RequireButton(leaveObject, "Leave Button");
            TextMeshProUGUI leaveText = RequireButtonText(leaveObject, "Leave Button");
            leaveText.text = "Leave";

            UIWaitingForOpponentPanel panel = root.GetComponent<UIWaitingForOpponentPanel>();
            if (panel == null)
                throw new InvalidOperationException("[OnlineUiPrefabBuilder] UIWaitingForOpponentPanel was not found.");
            SerializedObject serializedPanel = new(panel);
            Assign(serializedPanel, "leaveButton", leaveButton);
            Assign(serializedPanel, "leaveText", leaveText);
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, waitingPrefabPath);
            Debug.Log("[OnlineUiPrefabBuilder] Updated Waiting For Opponent Panel prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureLobbyPrefab(GameObject root, UIActiveGameRow rowPrefab,
        GameObject buttonSource, TextMeshProUGUI textSource)
    {
        RectTransform rootRect = (RectTransform)root.transform;
        RectTransform activeGames = GetOrCreateRect(rootRect, "Active Games");
        activeGames.anchorMin = activeGames.anchorMax = new Vector2(0.5f, 0.5f);
        activeGames.pivot = new Vector2(0.5f, 0.5f);
        activeGames.anchoredPosition = new Vector2(0f, -560f);
        activeGames.sizeDelta = new Vector2(600f, 380f);

        TextMeshProUGUI header = GetOrCreateText(activeGames, "Header", textSource);
        RectTransform headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, 50f);
        header.text = "Active games";
        header.alignment = TextAlignmentOptions.Center;
        header.fontSize = 30f;
        headerRect.SetSiblingIndex(0);

        RectTransform scrollRectTransform = GetOrCreateRect(activeGames, "Scroll View");
        scrollRectTransform.anchorMin = new Vector2(0f, 1f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.pivot = new Vector2(0.5f, 1f);
        scrollRectTransform.anchoredPosition = new Vector2(0f, -55f);
        scrollRectTransform.sizeDelta = new Vector2(0f, 325f);
        scrollRectTransform.SetSiblingIndex(1);
        ScrollRect scroll = GetOrAddComponent<ScrollRect>(scrollRectTransform.gameObject);

        RectTransform viewport = GetOrCreateRect(scrollRectTransform, "Viewport");
        Stretch(viewport);
        Image viewportImage = GetOrAddComponent<Image>(viewport.gameObject);
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        GetOrAddComponent<RectMask2D>(viewport.gameObject);

        RectTransform content = GetOrCreateRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = GetOrAddComponent<VerticalLayoutGroup>(content.gameObject);
        contentLayout.padding = new RectOffset(10, 10, 0, 0);
        contentLayout.spacing = 10f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter contentFitter = GetOrAddComponent<ContentSizeFitter>(content.gameObject);
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;

        UIOnlineLobbyPanel panel = root.GetComponent<UIOnlineLobbyPanel>();
        if (panel == null)
            throw new InvalidOperationException("[OnlineUiPrefabBuilder] UIOnlineLobbyPanel was not found.");
        SerializedObject serializedPanel = new(panel);
        Button createButton = serializedPanel.FindProperty("createButton").objectReferenceValue as Button;
        if (createButton == null)
            throw new InvalidOperationException("[OnlineUiPrefabBuilder] Lobby Create Match Button was not found.");

        GameObject cancelGameObject = GetOrCreateButton(rootRect, buttonSource, "Cancel Game Button");
        Button cancelGameButton = RequireButton(cancelGameObject, "Cancel Game Button");
        TextMeshProUGUI cancelGameText = RequireButtonText(cancelGameObject, "Cancel Game Button");
        cancelGameText.text = "Cancel game";
        RectTransform createRect = (RectTransform)createButton.transform;
        RectTransform cancelGameRect = (RectTransform)cancelGameObject.transform;
        cancelGameRect.anchorMin = createRect.anchorMin;
        cancelGameRect.anchorMax = createRect.anchorMax;
        cancelGameRect.pivot = createRect.pivot;
        cancelGameRect.anchoredPosition = createRect.anchoredPosition;
        cancelGameRect.sizeDelta = createRect.sizeDelta;

        Assign(serializedPanel, "activeGamesRoot", activeGames.gameObject);
        Assign(serializedPanel, "activeGamesHeaderText", header);
        Assign(serializedPanel, "activeGamesContent", content);
        Assign(serializedPanel, "activeGameRowPrefab", rowPrefab);
        Assign(serializedPanel, "cancelGameButton", cancelGameButton);
        Assign(serializedPanel, "cancelGameText", cancelGameText);
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();

        activeGames.gameObject.SetActive(false);
        cancelGameObject.SetActive(false);
        SavePrefab(root, lobbyPrefabPath);
        Debug.Log("[OnlineUiPrefabBuilder] Updated Online Lobby Panel prefab.");
    }

    private static GameObject GetOrCreateButton(RectTransform parent, GameObject buttonSource, string objectName)
    {
        RectTransform existing = FindDirectRect(parent, objectName);
        GameObject buttonObject = existing == null
            ? Object.Instantiate(buttonSource, parent, false)
            : existing.gameObject;
        buttonObject.name = objectName;
        buttonObject.SetActive(true);
        SetLayerRecursively(buttonObject, parent.gameObject.layer);
        Button button = RequireButton(buttonObject, objectName);
        button.onClick = new Button.ButtonClickedEvent();
        return buttonObject;
    }

    private static TextMeshProUGUI GetOrCreateText(RectTransform parent, string objectName,
        TextMeshProUGUI source)
    {
        RectTransform existing = FindDirectRect(parent, objectName);
        GameObject textObject = existing == null
            ? new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI))
            : existing.gameObject;
        if (existing == null)
            textObject.transform.SetParent(parent, false);
        textObject.name = objectName;
        textObject.layer = parent.gameObject.layer;
        TextMeshProUGUI text = GetOrAddComponent<TextMeshProUGUI>(textObject);
        CopyTextStyle(source, text);
        text.raycastTarget = false;
        text.richText = false;
        return text;
    }

    private static void CopyTextStyle(TextMeshProUGUI source, TextMeshProUGUI target)
    {
        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.color = source.color;
        target.fontStyle = source.fontStyle;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;
    }

    private static void ConfigureLayoutElement(GameObject target, float preferredWidth, float preferredHeight)
    {
        LayoutElement element = GetOrAddComponent<LayoutElement>(target);
        element.minWidth = 0f;
        element.minHeight = 0f;
        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static RectTransform GetOrCreateRect(RectTransform parent, string objectName)
    {
        RectTransform rect = FindDirectRect(parent, objectName);
        if (rect != null)
            return rect;
        GameObject child = new(objectName, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform FindDirectRect(RectTransform parent, string objectName)
    {
        for (int index = 0; index < parent.childCount; ++index)
        {
            RectTransform child = parent.GetChild(index) as RectTransform;
            if (child != null && child.name == objectName)
                return child;
        }
        return null;
    }

    private static RectTransform FindRect(Transform root, string objectName)
    {
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name == objectName)
                return rect;
        }
        throw new InvalidOperationException($"[OnlineUiPrefabBuilder] {objectName} was not found.");
    }

    private static Button RequireButton(GameObject root, string objectName)
    {
        Button button = root.GetComponentInChildren<Button>(true);
        if (button == null)
            throw new InvalidOperationException($"[OnlineUiPrefabBuilder] {objectName} Button was not found.");
        return button;
    }

    private static TextMeshProUGUI RequireButtonText(GameObject root, string objectName)
    {
        TextMeshProUGUI text = root.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
            throw new InvalidOperationException($"[OnlineUiPrefabBuilder] {objectName} text was not found.");
        text.richText = false;
        return text;
    }

    private static void Assign(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"[OnlineUiPrefabBuilder] Serialized property {propertyName} was not found.");
        property.objectReferenceValue = value;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path, out bool saveSucceeded);
        if (!saveSucceeded)
            throw new InvalidOperationException($"[OnlineUiPrefabBuilder] {path} could not be saved.");
    }
}
