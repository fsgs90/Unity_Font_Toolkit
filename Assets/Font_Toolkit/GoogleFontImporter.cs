using UnityEngine;
using UnityEditor;
using System.Net;
using System.IO;
using TMPro;
using Newtonsoft.Json.Linq;
using System.Linq;

public class GoogleFontImporter : EditorWindow
{
    private string apiKey = "YOUR_API_KEY_HERE";
    private string fontName = "Roboto";
    private string savePath = "Assets/Fonts/";

    [MenuItem("Tools/Google Font to TMP")]
    public static void ShowWindow() => GetWindow<GoogleFontImporter>("Font Downloader");

    void OnGUI()
    {
        apiKey = EditorGUILayout.TextField("API Key", apiKey);
        fontName = EditorGUILayout.TextField("Font Name", fontName);

        if (GUILayout.Button("Import and Convert"))
        {
            FetchAndConvert();
        }
    }

    private void FetchAndConvert()
    {
        string url = $"https://www.googleapis.com/webfonts/v1/webfonts?key={apiKey}";

        using (WebClient wc = new WebClient())
        {
            string json = wc.DownloadString(url);
            JObject data = JObject.Parse(json);

            // Find the specific font
            var fontEntry = data["items"].FirstOrDefault(f => f["family"].ToString() == fontName);

            if (fontEntry != null)
            {
                // Get the regular 'ttf' URL
                string downloadUrl = fontEntry["files"]["regular"].ToString();
                DownloadFont(downloadUrl);
            }
            else
            {
                Debug.LogError("Font not found in Google database.");
            }
        }
    }

    private void DownloadFont(string url)
    {
        if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

        string filePath = Path.Combine(savePath, fontName + ".ttf");

        using (WebClient wc = new WebClient())
        {
            wc.DownloadFile(url, filePath);
            AssetDatabase.ImportAsset(filePath);
            Debug.Log($"Downloaded: {filePath}");

            // Now trigger TMP Conversion
            CreateTMPAsset(filePath);
        }
    }

    private void CreateTMPAsset(string fontFilePath)
    {
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(fontFilePath);

        if (sourceFont == null) return;

        // Create the TMP Asset
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);

        string assetPath = Path.Combine(savePath, fontName + " - TMP.asset");
        AssetDatabase.CreateAsset(fontAsset, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"TMP Asset created at: {assetPath}");
        Selection.activeObject = fontAsset;
    }
}