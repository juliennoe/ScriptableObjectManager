
// ScriptableObjectManagerEditor.cs
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JulienNoe.Tools.ScriptableObjectManager
{
    // Custom Unity Editor tool for browsing and filtering ScriptableObjects
    public class ScriptableObjectManagerEditor : EditorWindow
    {
        // Dictionary storing ScriptableObjects grouped by folder path
        private Dictionary<string, List<ScriptableObject>> objectsByFolder = new();

        // Foldout states per folder in the UI
        private Dictionary<string, bool> folderFoldouts = new();

        // Scroll position state for both UI columns
        private Vector2 scrollPos;
        private Vector2 rightScrollPos;

        // Search input, cached query, and matched results
        private string searchQuery = "";
        private string searchTriggered = "";
        private HashSet<ScriptableObject> matchingByContent = new();

        // Root folder to restrict the search (default: "Assets")
        private string folderRootPath = "Assets";

        // Toggle between searching in name only or serialized fields
        private bool onlyInName = true;

        // Currently selected ScriptableObject and its editor instance
        private ScriptableObject selectedSO;
        private Editor selectedEditor;

        // Help box visibility toggle
        private bool showHelp = false;

        // Adds a menu item to launch the tool from Unity Editor
        [MenuItem("Tools/Julien Noe/Scriptable Object Manager")]
        public static void ShowWindow()
        {
            GetWindow<ScriptableObjectManagerEditor>("ScriptableObject Browser");
        }

        // On opening the window, automatically scan assets
        private void OnEnable()
        {
            ScanAssets();
        }

        // Main GUI rendering function
        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space();

            if (showHelp)
            {
                EditorGUILayout.HelpBox(
                    "This tool scans ScriptableObjects in the specified Assets folder path, groups them by directory, and allows you to filter them by name or internal data. Select one to preview in detail.",
                    MessageType.Info
                );
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            DrawLeftColumn();
            DrawRightColumn();

            EditorGUILayout.EndHorizontal();
        }

        // Draws the top toolbar with search and filter options
        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            // Rescan button (green)
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Rescan", GUILayout.Width(70)))
            {
                ScanAssets();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.Space(8);
            GUILayout.Label("Search:", GUILayout.Width(50));
            searchQuery = GUILayout.TextField(searchQuery, GUILayout.Width(200));

            // Search button (green)
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Search", GUILayout.Width(70)))
            {
                searchTriggered = searchQuery;
                FilterByContentMatches();
                Repaint();
            }

            // Clean button (orange)
            GUI.backgroundColor = new Color(1.0f, 0.6f, 0.0f);
            if (GUILayout.Button("Clean", GUILayout.Width(70)))
            {
                searchQuery = "";
                searchTriggered = "";
                Repaint();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();

            // Help toggle
            showHelp = GUILayout.Toggle(
                showHelp,
                showHelp ? "Close Help ▲" : "❔ Help ▼",
                "Button",
                GUILayout.Width(110)
            );

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            // Custom root path for search
            GUILayout.Label("Specify Folder:", GUILayout.Width(100));
            folderRootPath = GUILayout.TextField(folderRootPath, GUILayout.Width(240));
            if (!folderRootPath.StartsWith("Assets")) folderRootPath = "Assets";

            // Search type toggle (data or name)
            onlyInName = EditorGUILayout.ToggleLeft("Search specific Data", !onlyInName, GUILayout.Width(160));
            onlyInName = !onlyInName;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // Displays the list of ScriptableObjects organized by folder
        private void DrawLeftColumn()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(position.width * 0.45f));

            foreach (var kvp in objectsByFolder)
            {
                string folder = kvp.Key;
                List<ScriptableObject> scriptableObjects = kvp.Value;

                var filteredObjects = scriptableObjects
                    .Where(so =>
                    {
                        if (so == null) return false;
                        if (string.IsNullOrEmpty(searchTriggered)) return true;

                        string lowerQuery = searchTriggered.ToLower();
                        if (so.name.ToLower().Contains(lowerQuery)) return true;

                        return !onlyInName && matchingByContent.Contains(so);
                    })
                    .ToList();

                if (filteredObjects.Count == 0) continue;
                if (!folderFoldouts.ContainsKey(folder)) folderFoldouts[folder] = true;

                folderFoldouts[folder] = EditorGUILayout.Foldout(folderFoldouts[folder], folder, true);
                if (folderFoldouts[folder])
                {
                    EditorGUI.indentLevel++;
                    foreach (var so in filteredObjects)
                    {
                        if (so == null) continue;
                        GUIStyle style = (so == selectedSO) ? EditorStyles.boldLabel : EditorStyles.label;
                        if (GUILayout.Button(so.name, style))
                        {
                            if (selectedSO != so)
                            {
                                selectedSO = so;
                                if (selectedEditor != null)
                                    DestroyImmediate(selectedEditor);
                                selectedEditor = Editor.CreateEditor(so);
                            }
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // Renders the right panel for showing ScriptableObject details
        private void DrawRightColumn()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);

            if (selectedSO != null && selectedEditor != null)
            {
                EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                selectedEditor.OnInspectorGUI();
            }
            else
            {
                EditorGUILayout.LabelField("Select a ScriptableObject from the list to view its details.", EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // Scans project assets to locate ScriptableObjects under the given root folder
        private void ScanAssets()
        {
            objectsByFolder.Clear();
            folderFoldouts.Clear();
            selectedSO = null;
            selectedEditor = null;

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.StartsWith(folderRootPath)) continue;

                ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so != null)
                {
                    string folderPath = Path.GetDirectoryName(assetPath).Replace("\\", "/");
                    if (!objectsByFolder.ContainsKey(folderPath))
                        objectsByFolder[folderPath] = new List<ScriptableObject>();
                    objectsByFolder[folderPath].Add(so);
                }
            }

            objectsByFolder = objectsByFolder
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        // Filters ScriptableObjects by scanning their serialized content
        private void FilterByContentMatches()
        {
            matchingByContent.Clear();
            if (onlyInName || string.IsNullOrEmpty(searchTriggered)) return;

            string lowerQuery = searchTriggered.ToLower();

            foreach (var folder in objectsByFolder.Values)
            {
                foreach (var so in folder)
                {
                    if (so == null) continue;

                    SerializedObject serializedObject = new SerializedObject(so);
                    SerializedProperty property = serializedObject.GetIterator();

                    while (property.NextVisible(true))
                    {
                        if (property.propertyType == SerializedPropertyType.String ||
                            property.propertyType == SerializedPropertyType.Integer ||
                            property.propertyType == SerializedPropertyType.Float ||
                            property.propertyType == SerializedPropertyType.Enum)
                        {
                            string value = property.displayName + " " +
                                        property.stringValue + " " +
                                        property.intValue + " " +
                                        property.floatValue + " " +
                                        property.enumDisplayNames?.FirstOrDefault();

                            if (value.ToLower().Contains(lowerQuery))
                            {
                                matchingByContent.Add(so);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
