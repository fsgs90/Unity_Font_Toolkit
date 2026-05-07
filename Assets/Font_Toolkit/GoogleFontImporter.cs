using UnityEngine;
using UnityEditor;
using System.Net;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;

public class GoogleFontImporter : EditorWindow
{
    private string apiKey = "YOUR_API_KEY_HERE";
    private string searchQuery = "";
    private string selectedCategory = "All";
    private JArray allFonts = new JArray();
    private List<JToken> filteredFonts = new List<JToken>();
    private Vector2 scrollPos;

    private Font previewFont;
    private string previewText = "The quick brown fox jumps over the lazy dog.";
    private string currentPreviewName = "None (Select a font)";

    private readonly string[] categories = { "All", "sans-serif", "serif", "display", "handwriting", "monospace" };

    [MenuItem("Tools/Google Font Browser")]
    public static void ShowWindow() => GetWindow<GoogleFontImporter>("Font Browser");

    void OnGUI()
    {
        // --- PREVIEW HEADER ---
        EditorGUILayout.BeginVertical("helpbox");
        EditorGUILayout.LabelField("LIVE PREVIEW: " + currentPreviewName, EditorStyles.boldLabel);

        GUIStyle previewStyle = new GUIStyle(EditorStyles.label);
        previewStyle.fontSize = 30; // Made it bigger to see clearly
        previewStyle.wordWrap = true;
        previewStyle.alignment = TextAnchor.MiddleCenter;

        if (previewFont != null)
        {
            previewStyle.font = previewFont;
        }

        // Fixed height box for the preview
        Rect previewRect = GUILayoutUtility.GetRect(100, 80);
        EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f, 1f));
        EditorGUI.LabelField(previewRect, previewText, previewStyle);

        previewText = EditorGUILayout.TextField("Preview Text", previewText);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // --- SETTINGS ---
        apiKey = EditorGUILayout.TextField("API Key", apiKey);

        EditorGUILayout.BeginHorizontal();
        searchQuery = EditorGUILayout.TextField("Search", searchQuery);
        selectedCategory = categories[EditorGUILayout.Popup(System.Array.IndexOf(categories, selectedCategory), categories, GUILayout.Width(100))];
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Refresh Font Library")) FetchFontList();

        EditorGUILayout.Space(5);

        // --- SCROLLABLE LIST ---
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        if (filteredFonts != null)
        {
            foreach (var font in filteredFonts)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField(font["family"].ToString(), GUILayout.Width(150));

                if (GUILayout.Button("Preview", GUILayout.Width(70)))
                    LoadPreview(font["family"].ToString(), font["files"]["regular"].ToString());

                if (GUILayout.Button("Import", GUILayout.ExpandWidth(true)))
                    DownloadFont(font["family"].ToString(), font["files"]["regular"].ToString(), false);

                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void LoadPreview(string family, string url)
    {
        string filePath = DownloadFont(family, url, true);

        if (!string.IsNullOrEmpty(filePath))
        {
            // IMPORTANT: This line forces Unity to wait until the file is fully imported
            AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceUpdate);

            previewFont = AssetDatabase.LoadAssetAtPath<Font>(filePath);
            currentPreviewName = family;

            if (previewFont == null) Debug.LogWarning("Font imported but not yet loaded. Try clicking Preview again.");

            Repaint();
        }
    }

    private string DownloadFont(string name, string url, bool isPreview)
    {
        // Removed the unused 'previewDir' variable to fix your warning
        string folder = isPreview ? "Assets/Fonts/Previews" : "Assets/Fonts";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = name.Replace(" ", "_") + (isPreview ? "_Preview" : "") + ".ttf";
        string filePath = Path.Combine(folder, fileName);

        if (!File.Exists(filePath))
        {
            using (WebClient wc = new WebClient())
            {
                try
                {
                    wc.DownloadFile(url, filePath);
                    AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceSynchronousImport);
                }
                catch
                {
                    Debug.LogError("Download failed for " + name);
                    return null;
                }
            }
        }

        if (!isPreview)
        {
            Debug.Log($"<b>{name}</b> ready in Assets/Fonts");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Font>(filePath));
        }

        return filePath;
    }

    private void FetchFontList()
    {
        string url = $"https://www.googleapis.com/webfonts/v1/webfonts?key={apiKey}&sort=popularity";
        using (WebClient wc = new WebClient())
        {
            try
            {
                string json = wc.DownloadString(url);
                allFonts = JObject.Parse(json)["items"] as JArray;
                UpdateSearch();
            }
            catch { Debug.LogError("Check API Key."); }
        }
    }

    private void UpdateSearch()
    {
        if (allFonts == null) return;
        filteredFonts = allFonts
            .Where(f => (selectedCategory == "All" || f["category"].ToString() == selectedCategory) &&
                        f["family"].ToString().ToLower().Contains(searchQuery.ToLower()))
            .Take(40).ToList();
    }
}