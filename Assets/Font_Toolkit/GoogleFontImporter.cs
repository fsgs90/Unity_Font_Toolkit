using UnityEngine;
using UnityEditor;
using System.Net;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;

/// <summary>
/// An Editor Tool to browse, filter, and compare Google Fonts directly within Unity.
/// Features A/B testing and category filtering based on visual "Feelings".
/// </summary>
public class GoogleFontImporter : EditorWindow
{
    // --- API & DATA ---
    private string apiKey = "YOUR_API_KEY_HERE";
    private string searchQuery = "";
    private JArray allFonts = new JArray();
    private List<JToken> filteredFonts = new List<JToken>();
    private Vector2 scrollPos;

    // --- PREVIEW STATE ---
    private Font previewFontA;
    private Font previewFontB;
    private string currentPreviewNameA = "None";
    private string currentPreviewNameB = "None";
    private string previewText = "The quick brown fox jumps over the lazy dog.";
    private float previewFontSize = 30;
    private Vector2 previewScrollPos;
    private bool compareMode = false;

    // --- FILTER CATEGORIES ---
    // These lists mimic the visual filtering found on the Google Fonts website.
    private List<string> selectedTags = new List<string>();
    private readonly string[] feelingTags = { "Elegant", "Playful", "Retro", "Tech", "Kids", "Loud", "Vintage" };
    private readonly string[] appearanceTags = { "Sans Serif", "Serif", "Display", "Handwriting", "Monospace" };

    [MenuItem("Tools/Google Font Browser")]
    public static void ShowWindow() => GetWindow<GoogleFontImporter>("Font Browser");

    /// <summary>
    /// Called when the window is opened or scripts are recompiled.
    /// Automatically attempts to fetch fonts if an API key is present.
    /// </summary>
    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_API_KEY_HERE") FetchFontList();
    }

    /// <summary>
    /// Main UI Loop.
    /// </summary>
    void OnGUI()
    {
        // 1. TOP SECTION: Visual Font Previews
        DrawPreviewHeader();
        EditorGUILayout.Space(10);

        // 2. MIDDLE SECTION: API Management and Text Search
        EditorGUILayout.BeginVertical("box");
        apiKey = EditorGUILayout.TextField("API Key", apiKey);

        EditorGUI.BeginChangeCheck();
        searchQuery = EditorGUILayout.TextField("Search Fonts", searchQuery);
        if (EditorGUI.EndChangeCheck()) UpdateSearch(); // Live filter as you type

        if (GUILayout.Button("Refresh Font Library")) FetchFontList();
        EditorGUILayout.EndVertical();

        // 3. FILTER SECTION: Feeling & Appearance Tags
        EditorGUILayout.Space(5);
        DrawTagSection("Feeling", feelingTags);
        DrawTagSection("Appearance", appearanceTags);

        // Utility to reset the list
        if (selectedTags.Count > 0)
        {
            if (GUILayout.Button("Clear Filters", GUILayout.Width(100)))
            {
                selectedTags.Clear();
                UpdateSearch();
            }
        }

        EditorGUILayout.Space(10);

        // 4. BOTTOM SECTION: The Actual Font List
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        if (filteredFonts != null && filteredFonts.Count > 0)
        {
            foreach (var font in filteredFonts) DrawFontRow(font);
        }
        else
        {
            EditorGUILayout.LabelField("No fonts found. Try changing your filters.", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Renders the Scalable Preview Area. 
    /// Handles the height calculation for the font display so it never clips.
    /// </summary>
    private void DrawPreviewHeader()
    {
        EditorGUILayout.BeginVertical("helpbox");

        // UI Controls for the preview area
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("LIVE PREVIEW", EditorStyles.boldLabel);
        compareMode = EditorGUILayout.ToggleLeft("A/B Compare", compareMode, GUILayout.Width(110));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Size", GUILayout.Width(35));
        previewFontSize = EditorGUILayout.Slider(previewFontSize, 10, 120, GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();

        // Setup the font style based on user settings
        GUIStyle pStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        pStyle.fontSize = (int)previewFontSize;
        pStyle.alignment = TextAnchor.UpperLeft;
        pStyle.normal.textColor = Color.white;
        pStyle.padding = new RectOffset(10, 10, 10, 10);

        // Calculate layout width to prevent text from overflowing horizontally
        float viewWidth = compareMode ? (position.width / 2) - 30 : position.width - 40;

        // Measure how high the text box needs to be at this specific width and font size
        float hA = pStyle.CalcHeight(new GUIContent(previewText), viewWidth);
        float hB = compareMode ? pStyle.CalcHeight(new GUIContent(previewText), viewWidth) : 0;
        float maxHeight = Mathf.Max(hA, hB);

        // Draw the preview box with a capped height to avoid UI overcrowding
        previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, "box", GUILayout.Height(Mathf.Min(maxHeight + 45, 250)));
        EditorGUILayout.BeginHorizontal();

        // Slot A Display
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("A: " + currentPreviewNameA, EditorStyles.miniLabel);
        pStyle.font = previewFontA;
        EditorGUILayout.SelectableLabel(previewText, pStyle, GUILayout.Height(maxHeight + 10));
        EditorGUILayout.EndVertical();

        if (compareMode)
        {
            // Vertical Divider line for A/B testing
            EditorGUILayout.BeginVertical(GUILayout.Width(2));
            EditorGUI.DrawRect(EditorGUILayout.GetControlRect(GUILayout.Width(1), GUILayout.ExpandHeight(true)), Color.gray);
            EditorGUILayout.EndVertical();

            // Slot B Display
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("B: " + currentPreviewNameB, EditorStyles.miniLabel);
            pStyle.font = previewFontB;
            EditorGUILayout.SelectableLabel(previewText, pStyle, GUILayout.Height(maxHeight + 10));
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        // User input field for the preview sentence
        previewText = EditorGUILayout.TextField(previewText);
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Renders a section of toggle-able tag buttons.
    /// </summary>
    private void DrawTagSection(string title, string[] tags)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        for (int i = 0; i < tags.Length; i += 2)
        {
            EditorGUILayout.BeginHorizontal();
            DrawTagButton(tags[i]);
            if (i + 1 < tags.Length) DrawTagButton(tags[i + 1]);
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawTagButton(string tag)
    {
        bool isActive = selectedTags.Contains(tag);
        // Highlight active tags with a specific blue color
        GUI.backgroundColor = isActive ? new Color(0.2f, 0.5f, 1f) : Color.white;
        if (GUILayout.Button(tag, GUILayout.Height(22)))
        {
            if (isActive) selectedTags.Remove(tag);
            else selectedTags.Add(tag);
            UpdateSearch();
        }
        GUI.backgroundColor = Color.white; // Reset color for other UI elements
    }

    /// <summary>
    /// Renders a single row in the font browser list.
    /// </summary>
    private void DrawFontRow(JToken font)
    {
        string family = font["family"].ToString();
        EditorGUILayout.BeginHorizontal("box");
        EditorGUILayout.LabelField(family, GUILayout.ExpandWidth(true));

        if (compareMode)
        {
            // Load into specific slots during comparison
            if (GUILayout.Button("A", GUILayout.Width(30))) LoadPreview(family, font["files"]["regular"].ToString(), true);
            if (GUILayout.Button("B", GUILayout.Width(30))) LoadPreview(family, font["files"]["regular"].ToString(), false);
        }
        else
        {
            if (GUILayout.Button("Preview", GUILayout.Width(70))) LoadPreview(family, font["files"]["regular"].ToString(), true);
        }

        if (GUILayout.Button("Import", GUILayout.Width(60))) DownloadFont(family, font["files"]["regular"].ToString(), false);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Downloads the .ttf file to a temporary location and assigns it to the preview slots.
    /// </summary>
    private void LoadPreview(string family, string url, bool isSlotA)
    {
        string path = DownloadFont(family, url, true);
        if (!string.IsNullOrEmpty(path))
        {
            // Force Unity to realize a new asset has been added immediately
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (isSlotA) { previewFontA = AssetDatabase.LoadAssetAtPath<Font>(path); currentPreviewNameA = family; }
            else { previewFontB = AssetDatabase.LoadAssetAtPath<Font>(path); currentPreviewNameB = family; }
            Repaint();
        }
    }

    /// <summary>
    /// Handles the physical download and AssetDatabase registration of the font file.
    /// </summary>
    private string DownloadFont(string name, string url, bool isPreview)
    {
        string folder = isPreview ? "Assets/Fonts/Previews" : "Assets/Fonts";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        // We add a "_P" suffix to preview files to avoid conflicts with main assets
        string path = Path.Combine(folder, name.Replace(" ", "_") + (isPreview ? "_P" : "") + ".ttf");

        if (!File.Exists(path))
        {
            try
            {
                new WebClient().DownloadFile(url, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            catch { return null; }
        }

        if (!isPreview)
        {
            Debug.Log($"<b>[FontBrowser]</b> Imported: {name}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Font>(path));
        }
        return path;
    }

    /// <summary>
    /// Communicates with the Google Fonts Web API.
    /// </summary>
    private void FetchFontList()
    {
        try
        {
            string url = $"https://www.googleapis.com/webfonts/v1/webfonts?key={apiKey}&sort=popularity";
            string json = new WebClient().DownloadString(url);
            allFonts = JObject.Parse(json)["items"] as JArray;
            UpdateSearch();
        }
        catch
        {
            Debug.LogError("<b>[FontBrowser]</b> Failed to fetch font list. Check API Key/Internet.");
        }
    }

    /// <summary>
    /// Filters the full list based on Search Query and curated 'Vibe' keywords.
    /// Since the API doesn't provide these, we map them manually to font names and categories.
    /// </summary>
    private void UpdateSearch()
    {
        if (allFonts == null) return;

        filteredFonts = allFonts.Where(f => {
            string fam = f["family"].ToString().ToLower();
            string cat = f["category"].ToString().ToLower();

            // 1. Basic Search Query
            if (!string.IsNullOrEmpty(searchQuery) && !fam.Contains(searchQuery.ToLower())) return false;

            // 2. If no tags, show all
            if (selectedTags.Count == 0) return true;

            // 3. Expanded Keyword Mapping
            return selectedTags.Any(t => {
                string tag = t.ToLower();

                // Standard Google Categories
                if (tag == "sans serif" && cat == "sans-serif") return true;
                if (tag == "serif" && cat == "serif") return true;
                if (tag == "display" && cat == "display") return true;
                if (tag == "handwriting" && cat == "handwriting") return true;
                if (tag == "monospace" && cat == "monospace") return true;

                // --- Vibe Logic (The "Magic" part) ---
                if (tag == "kids")
                    return cat == "handwriting" || fam.Contains("kid") || fam.Contains("school") ||
                           fam.Contains("child") || fam.Contains("cute") || fam.Contains("doodle") ||
                           fam.Contains("baby") || fam.Contains("bubb") || fam.Contains("jolly");

                if (tag == "retro")
                    return fam.Contains("retro") || fam.Contains("pixel") || fam.Contains("arcade") ||
                           fam.Contains("neon") || fam.Contains("disco") || fam.Contains("80s") ||
                           fam.Contains("90s") || fam.Contains("vhs") || fam.Contains("vapor");

                if (tag == "vintage")
                    return fam.Contains("old") || fam.Contains("antique") || fam.Contains("classic") ||
                           fam.Contains("century") || fam.Contains("western") || fam.Contains("rust") ||
                           fam.Contains("typewriter") || fam.Contains("victorian");

                if (tag == "tech")
                    return fam.Contains("mono") || fam.Contains("code") || fam.Contains("robot") ||
                           fam.Contains("data") || fam.Contains("orbitron") || fam.Contains("future") ||
                           fam.Contains("scifi") || fam.Contains("grid");

                if (tag == "loud")
                    return fam.Contains("black") || fam.Contains("bold") || fam.Contains("ultra") ||
                           fam.Contains("heavy") || fam.Contains("fat") || fam.Contains("press") ||
                           fam.Contains("impact");

                if (tag == "elegant")
                    return (cat == "serif" && (fam.Contains("light") || fam.Contains("display"))) ||
                           fam.Contains("script") || fam.Contains("formal") || fam.Contains("thin") ||
                           fam.Contains("grace");

                if (tag == "playful")
                    return fam.Contains("round") || fam.Contains("bubble") || fam.Contains("comic") ||
                           fam.Contains("bounce") || fam.Contains("jolly") || fam.Contains("funny");

                return false;
            });
        }).Take(40).ToList(); // Increased limit slightly to show more variety
    }
}