using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class PackageProfilesWindow : EditorWindow
{
    Vector2 _scrollLeft, _scrollRight;
    Dictionary<string, string> _currentPackages; // name -> versionOrPath
    PackageProfile[] _profiles;
    bool _batchRunning;

    [MenuItem("Tools/Packages/Profiles")]
    public static void Open() => GetWindow<PackageProfilesWindow>("Package Profiles");

    void OnEnable()
    {
        RefreshData();
        PackageProfileApplier.OnBatchStarted += OnBatchStarted;
        PackageProfileApplier.OnBatchCompleted += OnBatchCompleted;
    }

    void OnDisable()
    {
        PackageProfileApplier.OnBatchStarted -= OnBatchStarted;
        PackageProfileApplier.OnBatchCompleted -= OnBatchCompleted;
    }

    void OnBatchStarted() { _batchRunning = true; Repaint(); }
    void OnBatchCompleted() { _batchRunning = false; RefreshCurrentPackages(); Repaint(); }

    void OnFocus() { RefreshData(); }

    void OnGUI()
    {
        // Toolbar
        EditorGUILayout.LabelField("Gestor de Perfiles de Paquetes", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_batchRunning))
            {
                if (GUILayout.Button("Refrescar")) RefreshData();
                if (GUILayout.Button("Encolar SOLO adds en TODOS"))
                {
                    foreach (var p in _profiles ?? Array.Empty<PackageProfile>()) PackageProfileApplier.ApplyAddsOnly(p);
                    Debug.Log("[Packages] Encolados adds de todos los perfiles. Ejecuta 'Final Apply'.");
                }
            }
            using (new EditorGUI.DisabledScope(!_batchRunning && PackageProfileApplier.IsRunning))
            {
                if (GUILayout.Button("Final Apply")) PackageProfileApplier.FinalApply();
            }
        }

        EditorGUILayout.Space(6);

        // Dos paneles con scroll que se adaptan al tamaño
        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            // IZQUIERDA: Perfiles
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(260f, position.width * 0.5f))))
            {
                EditorGUILayout.LabelField("Perfiles disponibles", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft, GUILayout.ExpandHeight(true));
                    DrawProfilesList();
                    EditorGUILayout.EndScrollView();
                }
            }

            // Separador
            GUILayout.Box(GUIContent.none, GUILayout.Width(2), GUILayout.ExpandHeight(true));

            // DERECHA: Paquetes actuales
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField("Paquetes actuales (manifest.json)", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.ExpandHeight(true));
                    DrawCurrentPackages();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        // Indicador de “en proceso” ocupando toda la ventana y bloqueando interacción
        if (PackageProfileApplier.IsRunning)
        {
            var fullRect = new Rect(0, 0, position.width, position.height);

            // Capa oscura
            EditorGUI.DrawRect(fullRect, new Color(0f, 0f, 0f, 0.45f));

            // Caja central con mensaje
            var boxSize = new Vector2(Mathf.Min(360f, position.width * 0.8f), 90f);
            var boxRect = new Rect(
                (position.width - boxSize.x) * 0.5f,
                (position.height - boxSize.y) * 0.5f,
                boxSize.x, boxSize.y);

            GUILayout.BeginArea(boxRect, GUI.skin.window);
            GUILayout.Label("Procesando paquetes...", EditorStyles.boldLabel);
            var progressRect = GUILayoutUtility.GetRect(boxSize.x - 20f, 18f);
            EditorGUI.ProgressBar(progressRect, 0.5f, "");
            GUILayout.EndArea();

            // Bloquear completamente la interacción de la ventana
            EditorGUIUtility.AddCursorRect(fullRect, MouseCursor.Link);
            GUI.enabled = false; // deshabilitar todos los controles debajo del overlay
        }
        else
        {
            GUI.enabled = true;
        }
    }

    void DrawProfilesList()
    {
        if (_profiles == null || _profiles.Length == 0)
        {
            EditorGUILayout.HelpBox("No se encontraron PackageProfile assets.\nCrea uno con Create > Build > Package Profile.", MessageType.Info);
            return;
        }

        foreach (var p in _profiles)
        {
            bool isApplied = IsProfileApplied(p, _currentPackages);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var label = isApplied ? $"{p.profileName} (Aplicado)" : p.profileName;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(_batchRunning || isApplied))
                    {
                        if (GUILayout.Button("Encolar (Add+Remove)", GUILayout.Width(150))) PackageProfileApplier.Apply(p);
                        if (GUILayout.Button("Encolar (Solo Add)", GUILayout.Width(130))) PackageProfileApplier.ApplyAddsOnly(p);
                    }
                }

                DrawList("Añadir:", p.packagesToAdd);
                DrawList("Quitar:", p.packagesToRemove);
            }
            GUILayout.Space(4);
        }
    }

    void DrawCurrentPackages()
    {
        if (_currentPackages == null || _currentPackages.Count == 0)
        {
            EditorGUILayout.HelpBox("No se pudieron leer paquetes del manifest.json.", MessageType.Warning);
            return;
        }

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

    void DrawList(string title, string[] items)
    {
        if (items == null || items.Length == 0) return;
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        foreach (var it in items.Where(s => !string.IsNullOrWhiteSpace(s)))
            EditorGUILayout.LabelField("• " + it, EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(2);
    }

    void RefreshData() { RefreshProfiles(); RefreshCurrentPackages(); }
    void RefreshProfiles()
    {
        var guids = AssetDatabase.FindAssets("t:PackageProfile");
        _profiles = guids.Select(g => AssetDatabase.LoadAssetAtPath<PackageProfile>(AssetDatabase.GUIDToAssetPath(g)))
                         .Where(p => p != null)
                         .OrderBy(p => p.profileName)
                         .ToArray();
    }
    void RefreshCurrentPackages() => _currentPackages = PackageUtils.ReadManifestPackages();

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
                if (!current.TryGetValue(guessed, out var inst) || !string.Equals(inst?.Trim(), norm.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                if (string.IsNullOrEmpty(name)) return false;
                if (!current.TryGetValue(name, out var inst)) return false;
                if (!string.IsNullOrEmpty(exact) && !string.Equals(inst?.Trim(), exact.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
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