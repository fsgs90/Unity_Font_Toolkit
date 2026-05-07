# Google Font Browser for Unity
A high-performance, studio-grade Editor Tool designed to bridge the gap between typography design and game implementation. Browse, filter, and compare the entire Google Fonts library directly within the Unity Editor.

# Why this tool?
Typographic choices are often a "blind" process in game development—downloading, importing, and checking fonts one by one. This tool transforms that into a live prototyping workflow, allowing you to audition over 1,500 fonts without ever leaving your project.

# Key Features
Live A/B Comparison: Toggle "Compare Mode" to view two different fonts side-by-side. Perfect for testing UI readability vs. stylistic flair.

Vibe-Based Filtering: Unlike the technical API, this tool includes a custom mapping engine that lets you filter fonts by "feeling" (e.g., Retro, Elegant, Tech, Playful).

Scalable Preview Header: A dynamic, scrollable preview area that uses CalcHeight logic to ensure long strings and various font sizes never clip.

One-Click Import: Found a winner? The Import button handles the download and AssetDatabase registration automatically, placing the font in your project instantly.

Live Search: Reactive search filtering to find specific families by name as you type.

# Installation & Setup
Place the Script: Ensure the GoogleFontImporter.cs file is located within an Editor folder (e.g., Assets/Scripts/Editor/).

Dependencies: This script requires the Newtonsoft JSON package (available via Unity Package Manager).

API Key: * Go to the Google Cloud Console.

Enable the Google Fonts Developer API.

Generate an API Key and paste it into the tool's API field.

Open the Window: Go to Tools > Google Font Browser.

# How to Use
1. Finding Fonts
Use the Feeling tags to narrow down the art direction. For example, selecting "Retro" will utilize the internal keyword engine to find pixel, arcade, and neon-style fonts.

2. A/B Testing
Enable the A/B Compare checkbox.

In the font list, you will now see A and B buttons.

Click A on one font and B on another to lock them into the side-by-side view.

Type custom text into the text field to see how your specific game strings (like "GAME OVER" or "Level 1") look in both styles.

3. Finalizing
Once you’ve decided on a font, click Import. The tool downloads the .ttf directly into Assets/Fonts/.

# Technical Details
Caching: Preview fonts are stored in Assets/Fonts/Previews with a _P suffix to prevent project clutter and asset name collisions.

Unity 6 Compatible: Optimized for the latest Unity Editor versions with support for high-DPI displays and word-wrapped labels.

Performance: Uses LINQ-based filtering and Take(40) caps to ensure the Editor UI remains responsive even when browsing thousands of entries.

Note: This tool demonstrates proficiency in REST API Integration, Custom Editor Scripting, Asynchronous Asset Management, and UX-driven Tool Development.

Created for developers who are tired of the "Import, Test, Delete" cycle.