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

    [MenuItem("Tools/Google Font Browser")]
    public static void ShowWindow() => GetWindow<GoogleFontImporter>("Font Browser");

    private void OnEnable()
    {
        if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_API_KEY_HERE")
        {
            FetchFontList();
        }
    }

    void OnGUI()
    {
        EditorGUILayout.BeginVertical("box");
        apiKey = EditorGUILayout.TextField("API Key", apiKey);
        if (GUILayout.Button("Refresh Font List")) FetchFontList();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Search Bar
        EditorGUI.BeginChangeCheck();
        searchQuery = EditorGUILayout.TextField("Search Fonts", searchQuery);
        if (EditorGUI.EndChangeCheck()) UpdateSearch();

        EditorGUILayout.Space(5);

        // Font List
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        foreach (var font in filteredFonts)
        {
            DrawFontRow(font);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawFontRow(JToken font)
    {
        string name = font["family"].ToString();
        string category = font["category"].ToString();
        var variants = font["variants"] as JArray;

        EditorGUILayout.BeginHorizontal("helpbox");

        VegiVertical(() => {
            EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{category} | {variants.Count} styles", EditorStyles.miniLabel);
        });

        if (GUILayout.Button("Download .TTF", GUILayout.Width(100), GUILayout.Height(30)))
        {
            DownloadFont(name, font["files"]["regular"].ToString());
        }

        EditorGUILayout.EndHorizontal();
    }

    private void VegiVertical(System.Action action)
    {
        EditorGUILayout.BeginVertical();
        action();
        EditorGUILayout.EndVertical();
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
            catch (System.Exception e)
            {
                Debug.LogError("Failed to fetch fonts: " + e.Message);
            }
        }
    }

    private void UpdateSearch()
    {
        if (allFonts == null) return;
        filteredFonts = allFonts
            .Where(f => f["family"].ToString().ToLower().Contains(searchQuery.ToLower()))
            .Take(50) // Limit display for performance
            .ToList();
    }

    private void DownloadFont(string name, string url)
    {
        string path = "Assets/Fonts/";
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        string filePath = Path.Combine(path, name.Replace(" ", "_") + ".ttf");

        using (WebClient wc = new WebClient())
        {
            wc.DownloadFile(url, filePath);
            AssetDatabase.ImportAsset(filePath);
            Debug.Log($"<b>{name}</b> downloaded to {filePath}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Font>(filePath));
        }
    }
}