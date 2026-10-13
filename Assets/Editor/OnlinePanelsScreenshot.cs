using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class OnlinePanelsScreenshot
{
    private const string scenePath = "Assets/Scenes/Main.unity";
    private const string inviteCode = "ABC123";
    private const int width = 1920;
    private const int height = 1080;

    public static void Capture()
    {
        bool shouldExit = Array.IndexOf(Environment.GetCommandLineArgs(), "-executeMethod") >= 0;
        int exitCode = 0;
        GameObject cameraObject = null;
        RenderTexture renderTexture = null;
        RenderTexture previousRenderTexture = RenderTexture.active;

        try
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var usernamePanels = UnityEngine.Object.FindObjectsByType<UIUsernamePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var lobbyPanels = UnityEngine.Object.FindObjectsByType<UIOnlineLobbyPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (usernamePanels.Length == 0 || lobbyPanels.Length == 0)
            {
                throw new InvalidOperationException("[OnlinePanelsScreenshot] Username Panel or Online Lobby Panel was not found.");
            }

            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                throw new InvalidOperationException("[OnlinePanelsScreenshot] HOME is not set.");
            }

            string outputDirectory = Path.Combine(home, "unity-mcp-out");
            Directory.CreateDirectory(outputDirectory);

            cameraObject = new GameObject("Online Panels Screenshot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            CapturePanel(usernamePanels[0], camera, renderTexture, Path.Combine(outputDirectory, "username-panel.png"));
            CapturePanel(lobbyPanels[0], camera, renderTexture, Path.Combine(outputDirectory, "online-lobby-panel.png"));
            SetInviteCodeState(lobbyPanels[0]);
            CapturePanel(lobbyPanels[0], camera, renderTexture, Path.Combine(outputDirectory, "online-lobby-code.png"));

            Debug.Log("S1_DONE");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Debug.LogException(exception);
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;

            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        if (shouldExit)
        {
            EditorApplication.Exit(exitCode);
        }
    }

    private static void CapturePanel(UIPanelBase panel, Camera camera, RenderTexture renderTexture, string outputPath)
    {
        var canvases = panel.GetComponentsInParent<Canvas>(true);
        if (canvases.Length == 0)
        {
            throw new InvalidOperationException($"[OnlinePanelsScreenshot] Canvas for {panel.name} was not found.");
        }

        var panelCanvas = canvases[0];
        var rootCanvas = canvases[canvases.Length - 1];
        foreach (Transform child in rootCanvas.transform)
        {
            child.gameObject.SetActive(child == panel.transform || panel.transform.IsChildOf(child));
        }

        panel.gameObject.SetActive(true);
        panel.SetVisible(true);
        panelCanvas.enabled = true;
        rootCanvas.enabled = true;
        camera.cullingMask = 1 << panel.gameObject.layer;
        rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        rootCanvas.worldCamera = camera;
        rootCanvas.planeDistance = 1f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rootCanvas.transform);
        Canvas.ForceUpdateCanvases();
        camera.Render();

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        Texture2D screenshot = null;
        RenderTexture previousRenderTexture = RenderTexture.active;
        try
        {
            RenderTexture.active = renderTexture;
            screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            screenshot.Apply();
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;
            if (screenshot != null)
            {
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new IOException($"[OnlinePanelsScreenshot] Screenshot file was not written: {outputPath}");
        }

        Debug.Log($"[OnlinePanelsScreenshot] Saved {outputPath}.");
    }

    private static void SetInviteCodeState(UIOnlineLobbyPanel lobbyPanel)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo inviteCodeField = typeof(UIOnlineLobbyPanel).GetField("inviteCode", flags);
        FieldInfo statusTextField = typeof(UIOnlineLobbyPanel).GetField("statusText", flags);

        if (inviteCodeField == null || statusTextField == null)
        {
            throw new MissingFieldException("[OnlinePanelsScreenshot] Lobby invite-code fields were not found.");
        }

        inviteCodeField.SetValue(lobbyPanel, inviteCode);
        var statusText = statusTextField.GetValue(lobbyPanel) as TMP_Text;
        var localization = UnityEngine.Object.FindFirstObjectByType<LocalizationManager>(FindObjectsInactive.Include);
        string waitingText = localization != null && localization.OnlineWaiting != null && localization.OnlineWaiting.Length > 0
            && !string.IsNullOrEmpty(localization.OnlineWaiting[0])
            ? localization.OnlineWaiting[0]
            : "Invite code: {0} — waiting for opponent";

        if (statusText == null)
        {
            throw new InvalidOperationException("[OnlinePanelsScreenshot] Lobby status text was not found.");
        }

        statusText.text = string.Format(waitingText, inviteCode);
    }
}
