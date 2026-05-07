using UnityEngine;
using UnityEditor;
using System.Net;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;

public class GitFontAgnostic : EditorWindow
{
    // --- REPO CONFIG ---
    private string repoOwner = "ryanoasis"; // Default changed for testing
    private string repoName = "nerd-fonts";
    private string branch = "master"; // Nerd fonts uses 'master', Google uses 'main'
    private string githubToken = "";

    // --- DATA STATE ---
    private List<FontData> allFonts = new List<FontData>();
    private List<FontData> filteredFonts = new List<FontData>();
    private string searchQuery = "";
    private Vector2 scrollPos;
    private bool isSearching = false;

    // --- FILTER & PAGINATION ---
    private string selectedVibe = "";
    private string selectedLetterGroup = "";
    private int currentPage = 0;
    private const int ItemsPerPage = 25;
    private readonly string[] vibes = { "Tech", "Retro", "Elegant", "Loud" };
    private readonly string[] letterGroups = { "A-D", "E-H", "I-L", "M-P", "Q-T", "U-Z" };

    // --- PREVIEW ---
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

    [MenuItem("Tools/Universal Git Font Verse")]
    public static void ShowWindow() => GetWindow<GitFontAgnostic>("Universal Git Font Verse");

    void OnGUI()
    {
        DrawHeader();
        DrawABPreview();
        EditorGUILayout.Space(5);
        DrawFilterControls();
        EditorGUILayout.Space(5);
        DrawFontList();
        DrawPagination();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("UNIVERSAL GIT SETTINGS", EditorStyles.boldLabel);
        repoOwner = EditorGUILayout.TextField("Owner", repoOwner);
        repoName = EditorGUILayout.TextField("Repo", repoName);
        branch = EditorGUILayout.TextField("Branch", branch);
        githubToken = EditorGUILayout.PasswordField("GitHub Token (Highly Recommended)", githubToken);

        if (GUILayout.Button(isSearching ? "Mapping Repository Structure..." : "Sync Any GitHub Repo", GUILayout.Height(30))) FetchFonts();
        EditorGUILayout.EndVertical();
    }

    private void DrawABPreview()
    {
        EditorGUILayout.BeginVertical("helpbox");
        EditorGUILayout.BeginHorizontal();
        compareMode = EditorGUILayout.ToggleLeft("A/B Mode", compareMode, GUILayout.Width(80));
        fontSize = EditorGUILayout.Slider(fontSize, 10, 80);
        EditorGUILayout.EndHorizontal();

        GUIStyle fontStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = (int)fontSize,
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        EditorGUILayout.BeginHorizontal(GUILayout.Height(150));
        DrawPreviewSlot("SLOT A: " + nameA, fontA, nameA, fontStyle);
        if (compareMode) DrawPreviewSlot("SLOT B: " + nameB, fontB, nameB, fontStyle);
        EditorGUILayout.EndHorizontal();

        testText = EditorGUILayout.TextField(testText);
        EditorGUILayout.EndVertical();
    }

    private void DrawPreviewSlot(string label, Font f, string fontName, GUIStyle style)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
        if (f != null)
        {
            style.font = f;
            EditorGUILayout.LabelField(testText, style, GUILayout.ExpandHeight(true));
            GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("Import to Project")) ExportFont(fontName, f);
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.LabelField("[Empty]", style, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawFilterControls()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUI.BeginChangeCheck();
        searchQuery = EditorGUILayout.TextField("Search", searchQuery, "SearchTextField");

        EditorGUILayout.BeginHorizontal();
        foreach (var v in vibes)
        {
            GUI.backgroundColor = selectedVibe == v ? Color.cyan : Color.white;
            if (GUILayout.Button(v, EditorStyles.miniButton)) { selectedVibe = (selectedVibe == v) ? "" : v; currentPage = 0; }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        foreach (var group in letterGroups)
        {
            GUI.backgroundColor = selectedLetterGroup == group ? Color.yellow : Color.white;
            if (GUILayout.Button(group, EditorStyles.miniButton)) { selectedLetterGroup = (selectedLetterGroup == group) ? "" : group; currentPage = 0; }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck()) FilterList();
        EditorGUILayout.EndVertical();
    }

    private void DrawFontList()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        var pageItems = filteredFonts.Skip(currentPage * ItemsPerPage).Take(ItemsPerPage);
        foreach (var font in pageItems)
        {
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(font.name, EditorStyles.boldLabel);
            if (GUILayout.Button("Set A", GUILayout.Width(50))) LoadToSlot(font, true);
            if (compareMode && GUILayout.Button("Set B", GUILayout.Width(50))) LoadToSlot(font, false);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawPagination()
    {
        int maxPages = Mathf.Max(1, Mathf.CeilToInt((float)filteredFonts.Count / ItemsPerPage));
        EditorGUILayout.BeginHorizontal("box");
        if (GUILayout.Button("<<")) currentPage = Mathf.Max(0, currentPage - 1);
        EditorGUILayout.LabelField($"Page {currentPage + 1}/{maxPages} ({filteredFonts.Count} fonts)", EditorStyles.centeredGreyMiniLabel);
        if (GUILayout.Button(">>")) currentPage = Mathf.Min(maxPages - 1, currentPage + 1);
        EditorGUILayout.EndHorizontal();
    }

    private void FetchFonts()
    {
        isSearching = true;
        allFonts.Clear();
        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/git/trees/{branch}?recursive=1";

        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Unity-Universal-Font-Tool");
                if (!string.IsNullOrEmpty(githubToken)) wc.Headers.Add("Authorization", "token " + githubToken);

                string json = wc.DownloadString(url);
                JObject root = JObject.Parse(json);
                JArray tree = root["tree"] as JArray;

                foreach (var node in tree)
                {
                    string path = node["path"].ToString();

                    // UNIVERSAL LOGIC: Look for any .ttf or .otf
                    if (path.EndsWith(".ttf") || path.EndsWith(".otf"))
                    {
                        // Skip Nerd Font specific "Windows Compatible" duplicates to keep list clean
                        if (path.Contains("Windows Compatible")) continue;

                        string fileName = Path.GetFileNameWithoutExtension(path);

                        // Try to get a clean Family Name from the path
                        string[] parts = path.Split('/');
                        string familyName = parts.Length > 1 ? parts[parts.Length - 2] : fileName;

                        // Only add unique families or unique files
                        if (!allFonts.Any(f => f.name == fileName))
                        {
                            allFonts.Add(new FontData
                            {
                                name = fileName,
                                downloadUrl = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/{branch}/{path}"
                            });
                        }
                    }
                }
                FilterList();
                Debug.Log($"Successfully mapped {allFonts.Count} fonts from {repoName}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Repo Access Error: " + e.Message);
            if (e.Message.Contains("403")) Debug.LogError("GitHub Rate Limit hit. You MUST use a Token for large repos like Nerd Fonts.");
        }
        isSearching = false;
    }

    // ... [LoadToSlot, DownloadToTemp, ExportFont, FilterList, CheckLetterGroup, GuessVibe same as previous version] ...
    // (Ensure you include the logic methods from the previous response here)

    private void LoadToSlot(FontData data, bool isA)
    {
        string path = DownloadToTemp(data);
        if (string.IsNullOrEmpty(path)) return;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        Font loadedFont = AssetDatabase.LoadAssetAtPath<Font>(path);
        if (loadedFont != null)
        {
            loadedFont.RequestCharactersInTexture(testText, (int)fontSize, FontStyle.Normal);
            if (isA) { fontA = loadedFont; nameA = data.name; } else { fontB = loadedFont; nameB = data.name; }
            Repaint();
        }
    }

    private string DownloadToTemp(FontData data)
    {
        string ext = data.downloadUrl.EndsWith(".otf") ? ".otf" : ".ttf";
        string relPath = "Assets/Fonts/Previews/" + data.name.Replace(" ", "_") + "_P" + ext;
        string fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), relPath);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath))) Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        try { new WebClient().DownloadFile(data.downloadUrl, fullPath); } catch { return null; }
        return relPath;
    }

    private void ExportFont(string name, Font font)
    {
        if (font == null) return;
        string sourcePath = AssetDatabase.GetAssetPath(font);
        string ext = Path.GetExtension(sourcePath);
        string destDir = "Assets/Fonts/Imported";
        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
        string destPath = Path.Combine(destDir, name.Replace(" ", "_") + ext);
        File.Copy(sourcePath, destPath, true);
        AssetDatabase.ImportAsset(destPath);
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Font>(destPath));
    }

    private void FilterList()
    {
        filteredFonts = allFonts.Where(f => {
            bool s = string.IsNullOrEmpty(searchQuery) || f.name.ToLower().Contains(searchQuery.ToLower());
            bool v = string.IsNullOrEmpty(selectedVibe) || GuessVibe(f.name) == selectedVibe;
            bool l = string.IsNullOrEmpty(selectedLetterGroup) || CheckLetterGroup(f.name);
            return s && v && l;
        }).ToList();
    }

    private bool CheckLetterGroup(string name)
    {
        char first = name.ToUpper()[0];
        return selectedLetterGroup switch
        {
            "A-D" => first >= 'A' && first <= 'D',
            "E-H" => first >= 'E' && first <= 'H',
            "I-L" => first >= 'I' && first <= 'L',
            "M-P" => first >= 'M' && first <= 'P',
            "Q-T" => first >= 'Q' && first <= 'T',
            "U-Z" => first >= 'U' && first <= 'Z',
            _ => true
        };
    }

    private string GuessVibe(string name)
    {
        name = name.ToLower();
        if (name.Contains("mono") || name.Contains("code") || name.Contains("nerd")) return "Tech";
        if (name.Contains("pixel") || name.Contains("arcade")) return "Retro";
        if (name.Contains("script") || name.Contains("light")) return "Elegant";
        if (name.Contains("bold") || name.Contains("black")) return "Loud";
        return "General";
    }
}