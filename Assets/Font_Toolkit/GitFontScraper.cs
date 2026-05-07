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
    private string repoOwner = "google";
    private string repoName = "fonts";
    private string branch = "main";
    private string githubToken = "";

    // --- DISCOVERY STATE ---
    private List<RepoInfo> discoveredRepos = new List<RepoInfo>();
    private bool showDiscovery = false;

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

    public class RepoInfo
    {
        public string owner;
        public string name;
        public string defaultBranch;
        public int stars;
    }

    [System.Serializable]
    public class FontData
    {
        public string name;
        public string downloadUrl;
    }

    [MenuItem("Tools/Master Font Verse")]
    public static void ShowWindow() => GetWindow<GitFontAgnostic>("Master Font Verse");

    void OnGUI()
    {
        DrawDiscoveryHeader();
        DrawHeader();
        DrawABPreview();

        EditorGUILayout.Space(5);
        DrawFilterControls();

        DrawFontList();
        DrawPagination();
    }

    private void DrawDiscoveryHeader()
    {
        EditorGUILayout.BeginVertical("box");
        if (GUILayout.Button(showDiscovery ? "▲ Hide Discovery" : "▼ Discover Trending Font Repos (GitHub Topics)", EditorStyles.toolbarButton))
        {
            showDiscovery = !showDiscovery;
            if (showDiscovery && discoveredRepos.Count == 0) DiscoverRepos();
        }

        if (showDiscovery)
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(120));
            foreach (var repo in discoveredRepos)
            {
                EditorGUILayout.BeginHorizontal("helpbox");
                EditorGUILayout.LabelField($"{repo.owner}/{repo.name}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"★ {repo.stars}", GUILayout.Width(60));
                if (GUILayout.Button("Load", GUILayout.Width(50)))
                {
                    repoOwner = repo.owner;
                    repoName = repo.name;
                    branch = repo.defaultBranch;
                    FetchFonts();
                    showDiscovery = false;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }

    private void DiscoverRepos()
    {
        string url = "https://api.github.com/search/repositories?q=topic:fonts&sort=stars&order=desc";
        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Unity-Font-Verse");
                if (!string.IsNullOrEmpty(githubToken)) wc.Headers.Add("Authorization", "token " + githubToken);
                string json = wc.DownloadString(url);
                JObject root = JObject.Parse(json);
                JArray items = (JArray)root["items"];

                discoveredRepos.Clear();
                foreach (var item in items)
                {
                    discoveredRepos.Add(new RepoInfo
                    {
                        owner = item["owner"]["login"].ToString(),
                        name = item["name"].ToString(),
                        defaultBranch = item["default_branch"].ToString(),
                        stars = (int)item["stargazers_count"]
                    });
                }
            }
        }
        catch { Debug.LogError("Discovery failed. Check Token."); }
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        repoOwner = EditorGUILayout.TextField("Owner", repoOwner);
        repoName = EditorGUILayout.TextField("Repo", repoName);
        EditorGUILayout.EndHorizontal();
        branch = EditorGUILayout.TextField("Branch", branch);
        githubToken = EditorGUILayout.PasswordField("GitHub Token (PAT)", githubToken);

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button(isSearching ? "Syncing..." : "Sync Repository Content", GUILayout.Height(25))) FetchFonts();
        GUI.backgroundColor = Color.white;
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

        EditorGUILayout.BeginHorizontal(GUILayout.Height(140));
        DrawPreviewSlot("SLOT A: " + nameA, fontA, nameA, fontStyle);
        if (compareMode) DrawPreviewSlot("SLOT B: " + nameB, fontB, nameB, fontStyle);
        EditorGUILayout.EndHorizontal();

        testText = EditorGUILayout.TextField("Preview Text", testText);
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
            if (GUILayout.Button("Import to Project")) ExportFont(fontName, f);
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
        searchQuery = EditorGUILayout.TextField("Search Name", searchQuery, "SearchTextField");

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
            EditorGUILayout.LabelField(font.name);
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
        EditorGUILayout.LabelField($"Page {currentPage + 1} / {maxPages} ({filteredFonts.Count} fonts)", EditorStyles.centeredGreyMiniLabel);
        if (GUILayout.Button(">>")) currentPage = Mathf.Min(maxPages - 1, currentPage + 1);
        EditorGUILayout.EndHorizontal();
    }

    // --- LOGIC ---

    private void FetchFonts()
    {
        isSearching = true;
        allFonts.Clear();
        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/git/trees/{branch}?recursive=1";

        try
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Unity-Master-Font-Verse");
                if (!string.IsNullOrEmpty(githubToken)) wc.Headers.Add("Authorization", "token " + githubToken);

                string json = wc.DownloadString(url);
                JObject root = JObject.Parse(json);
                JArray tree = (JArray)root["tree"];

                foreach (var node in tree)
                {
                    string path = node["path"].ToString();
                    if (path.EndsWith(".ttf") || path.EndsWith(".otf"))
                    {
                        if (path.Contains("Windows Compatible")) continue;
                        string fileName = Path.GetFileNameWithoutExtension(path);
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
            }
        }
        catch (System.Exception e) { Debug.LogError("Fetch Error: " + e.Message); }
        isSearching = false;
    }

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
            if (isA) { fontA = loadedFont; nameA = data.name; }
            else { fontB = loadedFont; nameB = data.name; }
            Repaint();
        }
    }

    private string DownloadToTemp(FontData data)
    {
        string ext = data.downloadUrl.EndsWith(".otf") ? ".otf" : ".ttf";
        string relPath = "Assets/Fonts/Previews/" + data.name.Replace(" ", "_") + "_P" + ext;
        string fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), relPath);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath))) Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

        if (!File.Exists(fullPath))
        {
            try { new WebClient().DownloadFile(data.downloadUrl, fullPath); } catch { return null; }
        }
        return relPath;
    }

    private void ExportFont(string name, Font font)
    {
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
        if (name.Contains("mono") || name.Contains("code")) return "Tech";
        if (name.Contains("pixel") || name.Contains("arcade")) return "Retro";
        if (name.Contains("script") || name.Contains("cursive")) return "Elegant";
        if (name.Contains("bold") || name.Contains("black") || name.Contains("ultra")) return "Loud";
        return "General";
    }
}