using System.IO;
using UnityEngine;

namespace DBarile
{
    public class ScreenShotManager : MonoBehaviour
    {
        [Header("<Your Custom Filename Here>")]
        [SerializeField] private string fileName = "Screenshot";
        [Header("Assets/Screenshots")]
        [SerializeField] private string filePath = "Assets/Screenshots";
        [SerializeField, Range(1, 5)] private int scaleFactor = 1;
        [Space]
        [SerializeField] private bool putInSubfolders;
        [Space]
        [SerializeField] private bool labelScreenDimensions;
        [SerializeField] private bool labelTimeStamp;

        [Header("HotKey To take Screenshot")]
        [SerializeField] private KeyCode screenShotKey = KeyCode.F1;

        [Tooltip("If using new Unity Input system,\nyou can just click this\ninstead of using the hotkey")]
        [Header("Check This To take Screenshot")]
        [SerializeField] private bool takeScreenshot;

        private void Start()
        {
            this.takeScreenshot = false;
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                this.takeScreenshot = false;
        }

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        private void Update()
        {
            //If using the new Unity Input system, comment out these two lines and just click the checkbox in the inspector
            if (Input.GetKeyDown(this.screenShotKey))
                TakeScreenshot();

            if (this.takeScreenshot)
                TakeScreenshot();

            this.takeScreenshot = false;
        }
#endif

        public void TakeScreenshot()
        {
            var screenDims = $"{Screen.width * this.scaleFactor}x{Screen.height * this.scaleFactor}";
            var subFolderName = this.putInSubfolders ? $"{screenDims}/" : string.Empty;
            var directoryName = $"{this.filePath}/{subFolderName}";

            if (this.putInSubfolders)
                Directory.CreateDirectory(directoryName);

            var timeStampString = this.labelTimeStamp ? System.DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss") : string.Empty;
            var dimsString = this.labelScreenDimensions ? $"_{screenDims}" : string.Empty;

            float numFiles = Directory.GetFiles(directoryName).Length;
            numFiles = Mathf.RoundToInt(numFiles / 2);//remove meta files
            var fileName = this.labelTimeStamp ? $"{this.fileName}{dimsString}_{timeStampString}.png" : $"{this.fileName}{dimsString}_{numFiles}.png";
            ScreenCapture.CaptureScreenshot($"{this.filePath}/{subFolderName}{fileName}", this.scaleFactor);

            Debug.Log($"<color=yellow>Screen Capture:</color> <color=#999>{this.filePath}/{subFolderName}</color><color=white>{fileName}</color>");
        }
    }
}