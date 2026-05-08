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

    // --- ACTIVE TAB ---
    private enum TabMode { All, Liked, Recent }
    private TabMode activeTab = TabMode.All;

    // --- FAVORITES ---
    private HashSet<string> likedFonts = new HashSet<string>();
    private const string LikedPrefsKey = "MFV_LikedFonts";

    // --- RECENTLY VIEWED ---
    private List<string> recentFontNames = new List<string>();
    private const int MaxRecent = 10;
    private const string RecentPrefsKey = "MFV_RecentFonts";

    // --- SHUFFLE ---
    private bool shuffleMode = false;
    private double shuffleInterval = 3.0;
    private double lastShuffleTime = 0;
    private System.Random rng = new System.Random();

    // --- PREVIEW ---
    private Font fontA;
    private Font fontB;
    private string nameA = "None";
    private string nameB = "None";
    private string testText = "The quick brown fox jumps over the lazy dog.";
    private float fontSize = 36;
    private bool compareMode = true;

    // --- COLLECTIONS (Pairings) ---
    private List<FontPairing> savedPairings = new List<FontPairing>();
    private bool showCollections = false;
    private string newPairingLabel = "My Pairing";
    private const string PairingsPrefsKey = "MFV_Pairings";

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

    [System.Serializable]
    public class FontPairing
    {
        public string label;
        public string fontAName;
        public string fontBName;
    }

    [MenuItem("Tools/Master Font Verse")]
    public static void ShowWindow() => GetWindow<GitFontAgnostic>("Master Font Verse");

    void OnEnable()
    {
        LoadLiked();
        LoadRecent();
        LoadPairings();
    }

    void OnDisable()
    {
        shuffleMode = false;
        EditorApplication.update -= OnShuffleUpdate;
    }

    void OnGUI()
    {
        DrawDiscoveryHeader();
        DrawHeader();
        DrawABPreview();
        DrawCollectionsPanel();

        EditorGUILayout.Space(5);
        DrawTabBar();
        DrawFilterControls();
        DrawFontList();
        DrawPagination();
    }

    // ─────────────────────────────────────────────────────────────────
    //  TAB BAR
    // ─────────────────────────────────────────────────────────────────
    private void DrawTabBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        DrawTab("🎵 All Fonts", TabMode.All);
        DrawTab("❤️  Liked", TabMode.Liked);
        DrawTab("🕓 Recent", TabMode.Recent);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTab(string label, TabMode mode)
    {
        bool active = activeTab == mode;
        GUI.backgroundColor = active ? Color.white : new Color(0.7f, 0.7f, 0.7f);
        if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.ExpandWidth(true)))
        {
            activeTab = mode;
            currentPage = 0;
            FilterList();
        }
        GUI.backgroundColor = Color.white;
    }

    // ─────────────────────────────────────────────────────────────────
    //  DISCOVERY
    // ─────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────
    //  HEADER / REPO CONFIG
    // ─────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────
    //  A/B PREVIEW + RANDOMIZE + SHUFFLE
    // ─────────────────────────────────────────────────────────────────
    private void DrawABPreview()
    {
        EditorGUILayout.BeginVertical("helpbox");

        // --- Top controls row ---
        EditorGUILayout.BeginHorizontal();
        compareMode = EditorGUILayout.ToggleLeft("A/B Mode", compareMode, GUILayout.Width(80));
        fontSize = EditorGUILayout.Slider(fontSize, 10, 80);
        EditorGUILayout.EndHorizontal();

        // --- Randomize row ---
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1f, 0.6f, 0.1f); // warm orange
        if (GUILayout.Button("🎲 Random → A", GUILayout.Height(22)))
            RandomizeSlot(true);

        if (compareMode)
        {
            if (GUILayout.Button("🎲 Random → B", GUILayout.Height(22)))
                RandomizeSlot(false);

            if (GUILayout.Button("🎲🎲 Random Both", GUILayout.Height(22)))
            {
                RandomizeSlot(true);
                RandomizeSlot(false);
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();

        // --- Shuffle row ---
        EditorGUILayout.BeginHorizontal();
        bool newShuffle = EditorGUILayout.ToggleLeft("🔁 Auto-Shuffle A", shuffleMode, GUILayout.Width(130));
        if (newShuffle != shuffleMode) ToggleShuffle(newShuffle);
        EditorGUILayout.LabelField("Every", GUILayout.Width(38));
        shuffleInterval = EditorGUILayout.Slider((float)shuffleInterval, 1f, 10f, GUILayout.Width(120));
        EditorGUILayout.LabelField("sec", GUILayout.Width(28));
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

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⬇ Import")) ExportFont(fontName, f);

            bool isLiked = likedFonts.Contains(fontName);
            GUI.backgroundColor = isLiked ? new Color(1f, 0.4f, 0.5f) : Color.white;
            if (GUILayout.Button(isLiked ? "❤️ Liked" : "♡ Like", GUILayout.Width(65)))
                ToggleLike(fontName);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.LabelField("[Empty]", style, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────────────────────────
    //  COLLECTIONS (SAVED PAIRINGS)
    // ─────────────────────────────────────────────────────────────────
    private void DrawCollectionsPanel()
    {
        EditorGUILayout.BeginVertical("box");
        if (GUILayout.Button(showCollections ? "▲ Hide Collections" : "▼ 📋 Collections (Saved Pairings)", EditorStyles.toolbarButton))
            showCollections = !showCollections;

        if (showCollections)
        {
            // Save current pairing
            EditorGUILayout.BeginHorizontal();
            newPairingLabel = EditorGUILayout.TextField(newPairingLabel);
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Save Current A/B", GUILayout.Width(120)) && (fontA != null || fontB != null))
            {
                savedPairings.Add(new FontPairing { label = newPairingLabel, fontAName = nameA, fontBName = nameB });
                SavePairings();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // List saved pairings
            for (int i = savedPairings.Count - 1; i >= 0; i--)
            {
                var p = savedPairings[i];
                EditorGUILayout.BeginHorizontal("helpbox");
                EditorGUILayout.LabelField($"📋 {p.label}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{p.fontAName} / {p.fontBName}", EditorStyles.miniLabel);
                if (GUILayout.Button("Load", GUILayout.Width(45))) LoadPairing(p);
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(22))) { savedPairings.RemoveAt(i); SavePairings(); }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────────────────────────
    //  FILTERS
    // ─────────────────────────────────────────────────────────────────
    private void DrawFilterControls()
    {
        if (activeTab != TabMode.All) return; // filters only apply to All tab

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

    // ─────────────────────────────────────────────────────────────────
    //  FONT LIST
    // ─────────────────────────────────────────────────────────────────
    private void DrawFontList()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        var pageItems = filteredFonts.Skip(currentPage * ItemsPerPage).Take(ItemsPerPage);
        foreach (var font in pageItems)
        {
            bool liked = likedFonts.Contains(font.name);
            EditorGUILayout.BeginHorizontal("box");

            // Like indicator
            GUI.color = liked ? new Color(1f, 0.5f, 0.6f) : Color.gray;
            EditorGUILayout.LabelField(liked ? "❤" : "♡", GUILayout.Width(16));
            GUI.color = Color.white;

            EditorGUILayout.LabelField(font.name);

            if (GUILayout.Button("Set A", GUILayout.Width(50))) LoadToSlot(font, true);
            if (compareMode && GUILayout.Button("Set B", GUILayout.Width(50))) LoadToSlot(font, false);

            GUI.backgroundColor = liked ? new Color(1f, 0.4f, 0.5f) : Color.white;
            if (GUILayout.Button(liked ? "❤" : "♡", GUILayout.Width(26))) ToggleLike(font.name);
            GUI.backgroundColor = Color.white;

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

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — RANDOMIZE & SHUFFLE
    // ─────────────────────────────────────────────────────────────────
    private void RandomizeSlot(bool isA)
    {
        if (allFonts.Count == 0) { Debug.LogWarning("No fonts loaded yet — sync a repo first."); return; }

        // Prefer filtered list if non-trivial; fall back to allFonts
        var pool = filteredFonts.Count > 1 ? filteredFonts : allFonts;

        FontData pick = null;
        // Avoid repeating the same font in the same slot
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var candidate = pool[rng.Next(pool.Count)];
            bool sameAsSlot = isA ? candidate.name == nameA : candidate.name == nameB;
            if (!sameAsSlot) { pick = candidate; break; }
        }
        pick ??= pool[rng.Next(pool.Count)]; // fallback

        LoadToSlot(pick, isA);
    }

    private void ToggleShuffle(bool enable)
    {
        shuffleMode = enable;
        if (enable)
        {
            lastShuffleTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnShuffleUpdate;
        }
        else
        {
            EditorApplication.update -= OnShuffleUpdate;
        }
    }

    private void OnShuffleUpdate()
    {
        if (!shuffleMode) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - lastShuffleTime >= shuffleInterval)
        {
            lastShuffleTime = now;
            RandomizeSlot(true);
            Repaint();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — LIKED FONTS
    // ─────────────────────────────────────────────────────────────────
    private void ToggleLike(string fontName)
    {
        if (string.IsNullOrEmpty(fontName) || fontName == "None") return;
        if (likedFonts.Contains(fontName)) likedFonts.Remove(fontName);
        else likedFonts.Add(fontName);
        SaveLiked();
        if (activeTab == TabMode.Liked) FilterList();
        Repaint();
    }

    private void SaveLiked() => EditorPrefs.SetString(LikedPrefsKey, string.Join("|", likedFonts));
    private void LoadLiked()
    {
        string raw = EditorPrefs.GetString(LikedPrefsKey, "");
        likedFonts = new HashSet<string>(raw.Split('|').Where(s => !string.IsNullOrEmpty(s)));
    }

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — RECENTLY VIEWED
    // ─────────────────────────────────────────────────────────────────
    private void PushRecent(string fontName)
    {
        recentFontNames.Remove(fontName);
        recentFontNames.Insert(0, fontName);
        if (recentFontNames.Count > MaxRecent) recentFontNames.RemoveAt(recentFontNames.Count - 1);
        SaveRecent();
    }

    private void SaveRecent() => EditorPrefs.SetString(RecentPrefsKey, string.Join("|", recentFontNames));
    private void LoadRecent()
    {
        string raw = EditorPrefs.GetString(RecentPrefsKey, "");
        recentFontNames = raw.Split('|').Where(s => !string.IsNullOrEmpty(s)).ToList();
    }

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — COLLECTIONS
    // ─────────────────────────────────────────────────────────────────
    private void LoadPairing(FontPairing p)
    {
        var fontAData = allFonts.FirstOrDefault(f => f.name == p.fontAName);
        var fontBData = allFonts.FirstOrDefault(f => f.name == p.fontBName);
        if (fontAData != null) LoadToSlot(fontAData, true);
        if (fontBData != null) LoadToSlot(fontBData, false);
    }

    private void SavePairings()
    {
        // Simple serialization: "label~A~B" per entry, joined by §
        var parts = savedPairings.Select(p => $"{p.label}~{p.fontAName}~{p.fontBName}");
        EditorPrefs.SetString(PairingsPrefsKey, string.Join("§", parts));
    }

    private void LoadPairings()
    {
        string raw = EditorPrefs.GetString(PairingsPrefsKey, "");
        savedPairings = raw.Split('§')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => { var t = s.Split('~'); return new FontPairing { label = t[0], fontAName = t.Length > 1 ? t[1] : "", fontBName = t.Length > 2 ? t[2] : "" }; })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — FETCH & FILTER
    // ─────────────────────────────────────────────────────────────────
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

    private void FilterList()
    {
        IEnumerable<FontData> source;

        switch (activeTab)
        {
            case TabMode.Liked:
                source = allFonts.Where(f => likedFonts.Contains(f.name));
                break;
            case TabMode.Recent:
                // Keep recent order
                source = recentFontNames
                    .Select(n => allFonts.FirstOrDefault(f => f.name == n))
                    .Where(f => f != null);
                break;
            default:
                source = allFonts.Where(f => {
                    bool s = string.IsNullOrEmpty(searchQuery) || f.name.ToLower().Contains(searchQuery.ToLower());
                    bool v = string.IsNullOrEmpty(selectedVibe) || GuessVibe(f.name) == selectedVibe;
                    bool l = string.IsNullOrEmpty(selectedLetterGroup) || CheckLetterGroup(f.name);
                    return s && v && l;
                });
                break;
        }

        filteredFonts = source.ToList();
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

    // ─────────────────────────────────────────────────────────────────
    //  LOGIC — LOAD / DOWNLOAD / EXPORT
    // ─────────────────────────────────────────────────────────────────
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
            PushRecent(data.name);
            if (activeTab == TabMode.Recent) FilterList();
            Repaint();
        }
    }

    private string DownloadToTemp(FontData data)
    {
        string ext = data.downloadUrl.EndsWith(".otf") ? ".otf" : ".ttf";
        string relPath = "Assets/Fonts/Previews/" + data.name.Replace(" ", "_") + "_P" + ext;
        string fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), relPath);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

        if (!File.Exists(fullPath))
        {
            try { new WebClient().DownloadFile(data.downloadUrl, fullPath); }
            catch { return null; }
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
}