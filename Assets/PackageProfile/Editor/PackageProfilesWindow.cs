using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class PackageProfilesWindow : EditorWindow
{
    Vector2 _scrollLeft, _scrollRight;
    Dictionary<string, string> _currentPackages; // name -> versionOrPath
    PackageProfile[] _profiles;
    float _leftWidth = 380f;
    bool _dragging;

    [MenuItem("Tools/Packages/Profiles")]
    public static void Open() => GetWindow<PackageProfilesWindow>("Package Profiles");

    void OnEnable() { RefreshProfiles(); RefreshCurrentPackages(); }
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

    void RefreshCurrentPackages() => _currentPackages = ReadManifestPackages();

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
                    using (new EditorGUI.DisabledScope(isApplied))
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(p.profileName, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            var btnText = isApplied ? "Aplicado" : "Aplicar";
                            if (GUILayout.Button(btnText, GUILayout.Width(90))) ApplyFromWindow(p);
                        }

                        DrawList("Añadir:", p.packagesToAdd);
                        DrawList("Quitar:", p.packagesToRemove);
                    }
                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(4);
        if (GUILayout.Button("Refrescar perfiles")) RefreshProfiles();
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

    void ApplyFromWindow(PackageProfile profile)
    {
        var removes = (profile.packagesToRemove ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Split('@')[0].Trim())
            .Where(name => _currentPackages != null && _currentPackages.ContainsKey(name))
            .ToList();

        var adds = (profile.packagesToAdd ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeAddId) // solo normalización de entrada
            .ToList();

        ExecuteRemovesThenAdds(removes, adds);
    }

    static string NormalizeAddId(string s)
    {
        s = s.Trim().Trim('"');
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = s.Substring("file:".Length).Replace('\\', '/');
            while (rest.StartsWith("/")) rest = rest[1..];
            return "file:" + rest;
        }
        var looksLikePath = s.Contains("\\") || s.Contains("/") || (s.Length > 1 && s[1] == ':');
        return looksLikePath ? $"file:{s.Replace('\\', '/')}" : s;
    }

    void ExecuteRemovesThenAdds(List<string> removes, List<string> adds)
    {
        void RemoveNext()
        {
            if (removes.Count == 0) { RefreshCurrentPackages(); AddNext(); return; }
            var name = removes[0]; removes.RemoveAt(0);

            var req = UnityEditor.PackageManager.Client.Remove(name);
            EditorApplication.update += Tick;
            void Tick()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Tick;
                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    Debug.LogError($"[Packages] Remove error {name}: {req.Error?.message}");
                RefreshCurrentPackages();
                Repaint();
                RemoveNext();
            }
        }

        void AddNext()
        {
            if (adds.Count == 0) { RefreshCurrentPackages(); Repaint(); Debug.Log("[Packages] Cambios aplicados."); return; }
            var id = adds[0]; adds.RemoveAt(0);

            var req = UnityEditor.PackageManager.Client.Add(id);
            EditorApplication.update += Tick;
            void Tick()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Tick;
                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    Debug.LogError($"[Packages] Add error {id}: {req.Error?.message}");
                RefreshCurrentPackages();
                Repaint();
                AddNext();
            }
        }

        RemoveNext();
    }

    static bool IsProfileApplied(PackageProfile profile, Dictionary<string, string> current)
    {
        if (current == null) return false;

        foreach (var entry in (profile.packagesToAdd ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var req = ParseRequirement(entry);
            if (!current.TryGetValue(req.name, out var installedValue)) return false;
            if (!string.IsNullOrEmpty(req.exactValue) && !string.Equals(installedValue?.Trim(), req.exactValue.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        }

        foreach (var entry in (profile.packagesToRemove ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var req = ParseRequirement(entry);
            if (current.ContainsKey(req.name)) return false;
        }

        return true;
    }

    struct PackageReq { public string name; public string exactValue; }

    static PackageReq ParseRequirement(string idOrPath)
    {
        idOrPath = idOrPath.Trim();
        if (idOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var name = TryGetNameFromFilePackage(idOrPath) ?? new DirectoryInfo(idOrPath.Substring("file:".Length)).Name;
            return new PackageReq { name = name, exactValue = idOrPath };
        }
        var at = idOrPath.IndexOf('@');
        if (at > 0) return new PackageReq { name = idOrPath[..at], exactValue = idOrPath[(at + 1)..] };
        return new PackageReq { name = idOrPath, exactValue = null };
    }

    static string TryGetNameFromFilePackage(string fileUri)
    {
        try
        {
            var folder = fileUri.Substring("file:".Length);
            var pkgJson = Path.Combine(folder, "package.json");
            if (!File.Exists(pkgJson)) return null;
            var json = File.ReadAllText(pkgJson);
            return ExtractJsonString(json, "name");
        }
        catch { return null; }
    }

    static Dictionary<string, string> ReadManifestPackages()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manifestPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return dict;

            var json = File.ReadAllText(manifestPath);
            var depsObj = ExtractJsonObject(json, "dependencies");
            if (string.IsNullOrEmpty(depsObj)) return dict;

            foreach (var kv in ParseSimpleStringDict(depsObj)) dict[kv.Key] = kv.Value;
        }
        catch (Exception e) { Debug.LogWarning($"[Packages] No se pudo leer manifest.json: {e.Message}"); }
        return dict;
    }

    static string ExtractJsonObject(string json, string key)
    {
        var i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf('{', i);
        if (i < 0) return null;

        int depth = 0;
        for (int j = i; j < json.Length; j++)
        {
            if (json[j] == '{') depth++;
            else if (json[j] == '}')
            {
                depth--;
                if (depth == 0) return json.Substring(i, j - i + 1);
            }
        }
        return null;
    }

    static string ExtractJsonString(string json, string key)
    {
        var i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf(':', i);
        if (i < 0) return null;
        i = json.IndexOf('"', i);
        if (i < 0) return null;
        var j = json.IndexOf('"', i + 1);
        if (j < 0) return null;
        return json.Substring(i + 1, j - i - 1);
    }

    static IEnumerable<KeyValuePair<string, string>> ParseSimpleStringDict(string jsonObject)
    {
        var result = new List<KeyValuePair<string, string>>();
        int i = 0;
        while (i < jsonObject.Length)
        {
            var q1 = jsonObject.IndexOf('"', i); if (q1 < 0) break;
            var q2 = jsonObject.IndexOf('"', q1 + 1); if (q2 < 0) break;
            var key = jsonObject.Substring(q1 + 1, q2 - q1 - 1);

            var colon = jsonObject.IndexOf(':', q2); if (colon < 0) break;
            int vStart = colon + 1; while (vStart < jsonObject.Length && char.IsWhiteSpace(jsonObject[vStart])) vStart++;

            string value;
            if (vStart < jsonObject.Length && jsonObject[vStart] == '"')
            {
                var vq2 = jsonObject.IndexOf('"', vStart + 1); if (vq2 < 0) break;
                value = jsonObject.Substring(vStart + 1, vq2 - vStart - 1);
                i = jsonObject.IndexOf(',', vq2); i = i < 0 ? jsonObject.Length : i + 1;
            }
            else
            {
                int vend = jsonObject.IndexOfAny(new[] { ',', '}' }, vStart);
                if (vend < 0) vend = jsonObject.Length;
                value = jsonObject.Substring(vStart, vend - vStart).Trim();
                i = vend + 1;
            }

            if (!string.IsNullOrEmpty(key)) result.Add(new KeyValuePair<string, string>(key, value));
        }
        return result;
    }
}