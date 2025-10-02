using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PackageProfileApplier
{
    public static void Apply(PackageProfile profile)
    {
        if (profile == null) { Debug.LogError("[PackageProfile] Perfil nulo."); return; }

        var removes = (profile.packagesToRemove ?? System.Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Split('@')[0].Trim())
            .ToList();

        var adds = (profile.packagesToAdd ?? System.Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeAddId)
            .ToList();

        ExecuteRemovesThenAdds(removes, adds);
    }

    static string NormalizeAddId(string s)
    {
        s = s.Trim().Trim('"');
        if (s.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase))
        {
            var rest = s.Substring("file:".Length).Replace('\\', '/');
            while (rest.StartsWith("/")) rest = rest.Substring(1);
            return "file:" + rest;
        }
        var looksLikePath = s.Contains("\\") || s.Contains("/") || (s.Length > 1 && s[1] == ':');
        return looksLikePath ? $"file:{s.Replace('\\', '/')}" : s;
    }

    static void ExecuteRemovesThenAdds(List<string> removes, List<string> adds)
    {
        void RemoveNext()
        {
            if (removes.Count == 0) { AddNext(); return; }
            var name = removes[0]; removes.RemoveAt(0);

            var req = UnityEditor.PackageManager.Client.Remove(name);
            EditorApplication.update += Tick;
            void Tick()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Tick;
                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    Debug.LogError($"[Packages] Remove error {name}: {req.Error?.message}");
                RemoveNext();
            }
        }

        void AddNext()
        {
            if (adds.Count == 0) { Debug.Log("[Packages] Cambios aplicados."); return; }
            var id = adds[0]; adds.RemoveAt(0);

            var req = UnityEditor.PackageManager.Client.Add(id);
            EditorApplication.update += Tick;
            void Tick()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Tick;
                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    Debug.LogError($"[Packages] Add error {id}: {req.Error?.message}");
                AddNext();
            }
        }

        RemoveNext();
    }
}