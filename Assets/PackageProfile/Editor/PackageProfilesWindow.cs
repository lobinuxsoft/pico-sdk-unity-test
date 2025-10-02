using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class PackageProfilesWindow : EditorWindow
{
    Vector2 _scrollLeft, _scrollRight;
    Dictionary<string, string> _currentPackages; // name -> versionOrPath (desde manifest)
    PackageProfile[] _profiles;
    float _leftWidth = 380f;
    bool _dragging;
    bool _batchRunning;

    [MenuItem("Tools/Packages/Profiles")]
    public static void Open() => GetWindow<PackageProfilesWindow>("Package Profiles");

    void OnEnable()
    {
        RefreshProfiles(); RefreshCurrentPackages();
        PackageProfileApplier.OnBatchStarted += HandleBatchStarted;
        PackageProfileApplier.OnBatchCompleted += HandleBatchCompleted;
    }

    void OnDisable()
    {
        PackageProfileApplier.OnBatchStarted -= HandleBatchStarted;
        PackageProfileApplier.OnBatchCompleted -= HandleBatchCompleted;
    }

    void HandleBatchStarted() { _batchRunning = true; Repaint(); }
    void HandleBatchCompleted() { _batchRunning = false; RefreshCurrentPackages(); Repaint(); }

    void OnFocus() { RefreshProfiles(); RefreshCurrentPackages(); }

    void OnGUI()
    {
        GUILayout.Space(6);
        EditorGUILayout.LabelField("Gestor de Perfiles de Paquetes", EditorStyles.boldLabel);
        GUILayout.Space(6);

        var rect = EditorGUILayout.GetControlRect(false, 0);
        var splitterRect = new Rect(_leftWidth, rect.y, 4f, position.height);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftWidth)); DrawProfilesPanel(); EditorGUILayout.EndVertical();
        GUILayout.Box(GUIContent.none, GUILayout.Width(2), GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginVertical(); DrawPackagesPanel(); EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        HandleSplitter(splitterRect);
    }

    void HandleSplitter(Rect splitterRect)
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition)) _dragging = true;
        if (_dragging && e.type == EventType.MouseDrag) { _leftWidth = Mathf.Clamp(e.mousePosition.x, 260f, position.width - 260f); Repaint(); }
        if (e.type == EventType.MouseUp) _dragging = false;
    }

    void RefreshProfiles()
    {
        var guids = AssetDatabase.FindAssets("t:PackageProfile");
        _profiles = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<PackageProfile>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null)
            .OrderBy(p => p.profileName)
            .ToArray();
    }

    void RefreshCurrentPackages() => _currentPackages = PackageUtils.ReadManifestPackages();

    void DrawProfilesPanel()
    {
        EditorGUILayout.LabelField("Perfiles disponibles", EditorStyles.boldLabel);
        GUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft, GUILayout.ExpandHeight(true));
            if (_profiles == null || _profiles.Length == 0)
            {
                EditorGUILayout.HelpBox("No se encontraron PackageProfile assets.\nCrea uno con Create > Build > Package Profile.", MessageType.Info);
            }
            else
            {
                foreach (var p in _profiles)
                {
                    var isApplied = IsProfileApplied(p, _currentPackages);
                    using (new EditorGUI.DisabledScope(_batchRunning))
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(p.profileName, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Encolar (Add+Remove)", GUILayout.Width(150))) PackageProfileApplier.Apply(p);
                            if (GUILayout.Button("Encolar (Solo Add)", GUILayout.Width(130))) PackageProfileApplier.ApplyAddsOnly(p);
                        }

                        DrawList("Añadir:", p.packagesToAdd);
                        DrawList("Quitar:", p.packagesToRemove);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(isApplied ? "Estado: Aplicado" : "Estado: No aplicado", EditorStyles.miniLabel);
                        }
                    }
                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_batchRunning))
            {
                if (GUILayout.Button("Refrescar perfiles")) RefreshProfiles();
                if (GUILayout.Button("Encolar SOLO adds en TODOS"))
                {
                    foreach (var p in _profiles ?? Array.Empty<PackageProfile>())
                        PackageProfileApplier.ApplyAddsOnly(p);
                    Debug.Log("[Packages] Encolados adds de todos los perfiles. Ejecuta 'Final Apply (resolver cola)'.");
                }
            }

            using (new EditorGUI.DisabledScope(!_batchRunning && PackageProfileApplier.IsRunning))
            {
                if (GUILayout.Button("Final Apply (resolver cola)"))
                {
                    PackageProfileApplier.FinalApply();
                }
            }
        }
    }

    void DrawList(string title, string[] items)
    {
        if (items == null || items.Length == 0) return;
        GUILayout.Space(2);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        foreach (var it in items.Where(s => !string.IsNullOrWhiteSpace(s)))
            EditorGUILayout.LabelField("• " + it, EditorStyles.wordWrappedLabel);
    }

    void DrawPackagesPanel()
    {
        EditorGUILayout.LabelField("Paquetes actuales (manifest.json)", EditorStyles.boldLabel);
        GUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.ExpandHeight(true));

            if (_currentPackages == null || _currentPackages.Count == 0)
            {
                EditorGUILayout.HelpBox("No se pudieron leer paquetes del manifest.json.", MessageType.Warning);
            }
            else
            {
                var mono = new GUIStyle(EditorStyles.label) { font = EditorStyles.miniFont };
                foreach (var kv in _currentPackages.OrderBy(k => k.Key))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(kv.Key, mono, GUILayout.Height(16), GUILayout.MinWidth(160), GUILayout.ExpandWidth(true));
                        GUILayout.Label("→", GUILayout.Width(16));
                        EditorGUILayout.SelectableLabel(kv.Value, mono, GUILayout.Height(16), GUILayout.MinWidth(140), GUILayout.ExpandWidth(true));
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refrescar paquetes")) RefreshCurrentPackages();
            if (GUILayout.Button("Refrescar todo")) { RefreshProfiles(); RefreshCurrentPackages(); }
        }
    }

    static bool IsProfileApplied(PackageProfile profile, Dictionary<string, string> current)
    {
        if (current == null) return false;

        foreach (var entry in (profile.packagesToAdd ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var norm = PackageUtils.NormalizeAddId(entry);
            var (name, exact) = PackageUtils.ParseRequirement(norm);
            if (norm.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var guessed = PackageUtils.TryGetLocalPackageNameFromFolder(norm);
                if (string.IsNullOrEmpty(guessed)) return false;
                if (!current.TryGetValue(guessed, out var inst) || !string.Equals(inst?.Trim(), norm.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            }
            else
            {
                if (string.IsNullOrEmpty(name)) return false;
                if (!current.TryGetValue(name, out var inst)) return false;
                if (!string.IsNullOrEmpty(exact) && !string.Equals(inst?.Trim(), exact.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            }
        }

        foreach (var entry in (profile.packagesToRemove ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var toRemove = entry.Split('@')[0].Trim();
            if (current.ContainsKey(toRemove)) return false;
        }

        return true;
    }
}