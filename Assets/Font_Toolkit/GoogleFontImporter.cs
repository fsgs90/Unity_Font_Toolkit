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
    private JArray allFonts = new JArray();
    private List<JToken> filteredFonts = new List<JToken>();
    private Vector2 scrollPos;

    // Preview Logic
    private Font previewFont;
    private string previewText = "The quick brown fox jumps over the lazy dog.";
    private string currentPreviewName = "None (Select a font)";
    private float previewFontSize = 30;
    private Vector2 previewScrollPos;

    // Filters
    private List<string> selectedTags = new List<string>();
    private readonly string[] feelingTags = { "Elegant", "Playful", "Retro", "Tech", "Kids", "Loud", "Vintage" };
    private readonly string[] appearanceTags = { "Sans Serif", "Serif", "Display", "Handwriting", "Monospace" };

    [MenuItem("Tools/Google Font Browser")]
    public static void ShowWindow() => GetWindow<GoogleFontImporter>("Font Browser");

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_API_KEY_HERE") FetchFontList();
    }

    void OnGUI()
    {
        // 1. THE NEW DYNAMIC PREVIEW HEADER
        DrawPreviewHeader();

        EditorGUILayout.Space(10);

        // 2. SEARCH AND API
        EditorGUILayout.BeginVertical("box");
        apiKey = EditorGUILayout.TextField("API Key", apiKey);

        EditorGUI.BeginChangeCheck();
        searchQuery = EditorGUILayout.TextField("Search Fonts", searchQuery);
        if (EditorGUI.EndChangeCheck()) UpdateSearch();

        if (GUILayout.Button("Refresh Font Library")) FetchFontList();
        EditorGUILayout.EndVertical();

        // 3. TAG FILTERS
        EditorGUILayout.Space(5);
        DrawTagSection("Feeling", feelingTags);
        DrawTagSection("Appearance", appearanceTags);

        if (selectedTags.Count > 0)
        {
            if (GUILayout.Button("Clear Filters", GUILayout.Width(100))) { selectedTags.Clear(); UpdateSearch(); }
        }

        EditorGUILayout.Space(10);

        // 4. SCROLLABLE LIST
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        if (filteredFonts != null && filteredFonts.Count > 0)
        {
            foreach (var font in filteredFonts)
            {
                DrawFontRow(font);
            }
        }
        else
        {
            EditorGUILayout.LabelField("No fonts found. Try changing your filters.", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawPreviewHeader()
    {
        EditorGUILayout.BeginVertical("helpbox");

        // Header Line
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("LIVE PREVIEW: " + currentPreviewName, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Size", GUILayout.Width(35));
        previewFontSize = EditorGUILayout.Slider(previewFontSize, 10, 120, GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();

        // Style Setup
        GUIStyle previewStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        previewStyle.fontSize = (int)previewFontSize;
        previewStyle.alignment = TextAnchor.UpperLeft;
        previewStyle.normal.textColor = Color.white;
        previewStyle.padding = new RectOffset(10, 10, 10, 10);
        if (previewFont != null) previewStyle.font = previewFont;

        // Calculate height for the content
        float calculatedHeight = previewStyle.CalcHeight(new GUIContent(previewText), position.width - 40);

        // Scrollable Preview Area
        previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, "box", GUILayout.Height(Mathf.Min(calculatedHeight + 30, 250)));
        EditorGUILayout.SelectableLabel(previewText, previewStyle, GUILayout.Height(calculatedHeight + 10));
        EditorGUILayout.EndScrollView();

        // Text Input
        EditorGUILayout.Space(2);
        previewText = EditorGUILayout.TextField(previewText);

        EditorGUILayout.EndVertical();
    }

    private void DrawTagSection(string title, string[] tags)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        int cols = 2;
        for (int i = 0; i < tags.Length; i += cols)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = 0; j < cols; j++)
            {
                if (i + j < tags.Length) DrawTagButton(tags[i + j]);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space(5);
    }

    private void DrawTagButton(string tag)
    {
        bool isActive = selectedTags.Contains(tag);
        GUI.backgroundColor = isActive ? new Color(0.2f, 0.5f, 1f) : Color.white;

        if (GUILayout.Button(tag, GUILayout.Height(22)))
        {
            if (isActive) selectedTags.Remove(tag);
            else selectedTags.Add(tag);
            UpdateSearch();
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawFontRow(JToken font)
    {
        string family = font["family"].ToString();
        EditorGUILayout.BeginHorizontal("box");
        EditorGUILayout.LabelField(family, EditorStyles.label, GUILayout.Width(180));

        if (GUILayout.Button("Preview", GUILayout.Width(70)))
            LoadPreview(family, font["files"]["regular"].ToString());

        if (GUILayout.Button("Import TTF", GUILayout.ExpandWidth(true)))
            DownloadFont(family, font["files"]["regular"].ToString(), false);

        EditorGUILayout.EndHorizontal();
    }

    private void UpdateSearch()
    {
        if (allFonts == null) return;
        filteredFonts = allFonts.Where(f => {
            string family = f["family"].ToString().ToLower();
            string cat = f["category"].ToString().ToLower();
            bool matchesSearch = string.IsNullOrEmpty(searchQuery) || family.Contains(searchQuery.ToLower());
            if (!matchesSearch) return false;
            if (selectedTags.Count == 0) return true;
            return selectedTags.Any(tag => {
                string t = tag.ToLower();
                if (t == "sans serif" && cat == "sans-serif") return true;
                if (t == "serif" && cat == "serif") return true;
                if (t == "display" && cat == "display") return true;
                if (t == "handwriting" && cat == "handwriting") return true;
                if (t == "monospace" && cat == "monospace") return true;
                if (t == "tech" && (family.Contains("mono") || family.Contains("code") || family.Contains("robot"))) return true;
                if (t == "retro" && (family.Contains("old") || family.Contains("retro") || family.Contains("vibe"))) return true;
                if (t == "elegant" && (cat == "serif" && family.Contains("display"))) return true;
                if (t == "playful" && cat == "display") return true;
                if (t == "loud" && (family.Contains("black") || family.Contains("bold") || family.Contains("ultra"))) return true;
                return false;
            });
        }).Take(40).ToList();
    }

    private void LoadPreview(string family, string url)
    {
        string filePath = DownloadFont(family, url, true);
        if (!string.IsNullOrEmpty(filePath))
        {
            AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceSynchronousImport);
            previewFont = AssetDatabase.LoadAssetAtPath<Font>(filePath);
            currentPreviewName = family;
            Repaint();
        }
    }

    private string DownloadFont(string name, string url, bool isPreview)
    {
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
                catch { return null; }
            }
        }
        if (!isPreview)
        {
            Debug.Log($"Imported: {name}");
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
            catch { Debug.LogError("API Error. Check your Key."); }
        }
    }
}