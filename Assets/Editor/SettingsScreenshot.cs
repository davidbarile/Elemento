using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SettingsScreenshot
{
    private const string prefabPath = "Assets/Prefabs/Settings Screen.prefab";
    private const string outputPath = "/tmp/settings-check.png";
    private const int width = 1280;
    private const int height = 720;

    public static void Capture()
    {
        var exitCode = 0;
        RenderTexture renderTexture = null;
        RenderTexture previousRenderTexture = null;
        Texture2D screenshot = null;

        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                throw new InvalidOperationException("[SettingsScreenshot] Settings Screen prefab was not found.");
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;

            if (instance == null)
            {
                throw new InvalidOperationException("[SettingsScreenshot] Settings Screen prefab could not be instantiated.");
            }

            var canvas = instance.GetComponentInChildren<Canvas>(true);
            var gameplayCategory = FindTransform(instance.transform, "Gameplay Category");

            if (canvas == null || gameplayCategory == null)
            {
                throw new InvalidOperationException("[SettingsScreenshot] Canvas or Game Play panel was not found.");
            }

            foreach (Transform category in gameplayCategory.parent)
            {
                category.gameObject.SetActive(category == gameplayCategory);
            }

            gameplayCategory.gameObject.SetActive(true);

            var cameraObject = new GameObject("Settings Screenshot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
            Canvas.ForceUpdateCanvases();
            camera.Render();

            previousRenderTexture = RenderTexture.active;
            RenderTexture.active = renderTexture;
            screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            screenshot.Apply();
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            RenderTexture.active = previousRenderTexture;

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new IOException("[SettingsScreenshot] Screenshot file was not written.");
            }

            Debug.Log($"[SettingsScreenshot] Saved {outputPath}.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Debug.LogException(exception);
        }
        finally
        {
            RenderTexture.active = previousRenderTexture;

            if (screenshot != null)
            {
                UnityEngine.Object.DestroyImmediate(screenshot);
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        EditorApplication.Exit(exitCode);
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }
}
