using UnityEngine;
using UnityEditor;
using System.Net;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;

public class GitFontAgnostic : EditorWindow
{
    private string repoOwner = "google";
    private string repoName = "fonts";
    private string branch = "main";
    private string githubToken = "";

    private List<FontData> allFonts = new List<FontData>();
    private List<FontData> filteredFonts = new List<FontData>();
    private string searchQuery = "";
    private Vector2 scrollPos;
    private bool isSearching = false;

    private Font fontA;
    private Font fontB;
    private string nameA = "None";
    private string nameB = "None";
    private string testText = "The quick brown fox jumps over the lazy dog.";
    private float fontSize = 36;
    private bool compareMode = true;

    [System.Serializable]
    public class FontData
    {
        public string name;
        public string downloadUrl;
    }

    [MenuItem("Tools/Git Font Verse")]
    public static void ShowWindow() => GetWindow<GitFontAgnostic>("Git Font Verse");

    void OnGUI()
    {
        DrawHeader();
        DrawABPreview();

        EditorGUILayout.Space(5);
        EditorGUI.BeginChangeCheck();
        searchQuery = EditorGUILayout.TextField("Search Results", searchQuery, "SearchTextField");
        if (EditorGUI.EndChangeCheck()) FilterList();

        DrawFontList();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("GIT REPOSITORY SETTINGS", EditorStyles.boldLabel);
        repoOwner = EditorGUILayout.TextField("Owner", repoOwner);
        repoName = EditorGUILayout.TextField("Repo", repoName);
        githubToken = EditorGUILayout.PasswordField("GitHub Token (PAT)", githubToken);

        if (GUILayout.Button(isSearching ? "Mapping Tree..." : "Sync Repository (Safe Mode)", GUILayout.Height(30))) FetchFonts();
        EditorGUILayout.EndVertical();
    }

    private void DrawABPreview()
    {
        EditorGUILayout.BeginVertical("helpbox");
        EditorGUILayout.BeginHorizontal();
        compareMode = EditorGUILayout.ToggleLeft("A/B Mode", compareMode, GUILayout.Width(100));
        fontSize = EditorGUILayout.Slider(fontSize, 10, 100);
        EditorGUILayout.EndHorizontal();

        GUIStyle fontStyle = new GUIStyle(EditorStyles.label);
        fontStyle.fontSize = (int)fontSize;
        fontStyle.normal.textColor = Color.white;
        fontStyle.alignment = TextAnchor.MiddleCenter;
        fontStyle.wordWrap = true;

        EditorGUILayout.BeginHorizontal(GUILayout.Height(150));

        // Slot A
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SLOT A: " + nameA, EditorStyles.centeredGreyMiniLabel);
        if (fontA != null)
        {
            fontStyle.font = fontA;
            EditorGUILayout.LabelField(testText, fontStyle, GUILayout.ExpandHeight(true));
        }
        else
        {
            EditorGUILayout.LabelField("[Select a font below]", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndVertical();

        if (compareMode)
        {
            // Slot B
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("SLOT B: " + nameB, EditorStyles.centeredGreyMiniLabel);
            if (fontB != null)
            {
                fontStyle.font = fontB;
                EditorGUILayout.LabelField(testText, fontStyle, GUILayout.ExpandHeight(true));
            }
            else
            {
                EditorGUILayout.LabelField("[Select a font below]", EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true));
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        testText = EditorGUILayout.TextArea(testText, GUILayout.Height(40));
        EditorGUILayout.EndVertical();
    }

    private void DrawFontList()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        foreach (var font in filteredFonts)
        {
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(font.name, EditorStyles.boldLabel);
            if (GUILayout.Button("Set A", GUILayout.Width(50))) LoadToSlot(font, true);
            if (compareMode && GUILayout.Button("Set B", GUILayout.Width(50))) LoadToSlot(font, false);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void FetchFonts()
    {
        isSearching = true;
        allFonts.Clear();

        // BIG BRAIN: Use the Recursive Tree API to get ALL file paths in 1 call.
        // We target the 'ofl' folder path if possible, or just the whole repo.
        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/git/trees/{branch}?recursive=1";

        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Unity-Tool");
                if (!string.IsNullOrEmpty(githubToken)) wc.Headers.Add("Authorization", "token " + githubToken);

                string json = wc.DownloadString(url);
                JObject root = JObject.Parse(json);
                JArray tree = root["tree"] as JArray;

                foreach (var node in tree)
                {
                    string path = node["path"].ToString();

                    // Logic: Must be in 'ofl' folder, end in .ttf, and we prefer "Regular"
                    if (path.StartsWith("ofl/") && path.EndsWith(".ttf"))
                    {
                        // We only want to show the font family once
                        string familyName = path.Split('/')[1];

                        // Check if we already added this family (avoiding Bold/Italic duplicates)
                        if (!allFonts.Any(f => f.name == familyName))
                        {
                            string downloadUrl = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/{branch}/{path}";
                            allFonts.Add(new FontData { name = familyName, downloadUrl = downloadUrl });
                        }
                    }
                }
                FilterList();
                Debug.Log($"<b>[FontVerse]</b> Mapped {allFonts.Count} fonts accurately.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Git Error: " + e.Message);
            if (e.Message.Contains("403")) Debug.LogError("Rate Limit hit. Use a Token!");
        }
        isSearching = false;
    }

    private void LoadToSlot(FontData data, bool isA)
    {
        string relativePath = DownloadToTemp(data);
        if (string.IsNullOrEmpty(relativePath)) return;

        AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Font loadedFont = AssetDatabase.LoadAssetAtPath<Font>(relativePath);

        if (loadedFont != null)
        {
            loadedFont.RequestCharactersInTexture(testText, (int)fontSize, FontStyle.Normal);
            if (isA) { fontA = loadedFont; nameA = data.name; }
            else { fontB = loadedFont; nameB = data.name; }
            this.Repaint();
        }
    }

    private string DownloadToTemp(FontData data)
    {
        string folderPath = Application.dataPath + "/Fonts/Previews";
        string relativePath = "Assets/Fonts/Previews/" + data.name.Replace(" ", "_") + "_P.ttf";
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        string fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), relativePath);

        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile(data.downloadUrl, fullPath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"404 on: {data.downloadUrl}. The file path in the repo might be different.");
            return null;
        }
        return relativePath;
    }

    private void FilterList()
    {
        filteredFonts = allFonts.Where(f => string.IsNullOrEmpty(searchQuery) || f.name.ToLower().Contains(searchQuery.ToLower())).Take(100).ToList();
    }
}