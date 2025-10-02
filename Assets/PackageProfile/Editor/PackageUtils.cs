using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class PackageUtils
{
    public static string NormalizeAddId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim().Trim('"');
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = s.Substring("file:".Length).Replace('\\', '/');
            while (rest.StartsWith("/")) rest = rest.Substring(1);
            return "file:" + rest;
        }

        var looksLikePath = s.Contains("\\") || s.Contains("/") || (s.Length > 1 && s[1] == ':');
        return looksLikePath ? $"file:{s.Replace('\\', '/')}" : s;
    }

    // Devuelve (name, exactValue). Para file: retorna (null, null) ya que requiere leer package.json para saber el nombre.
    public static (string name, string exactValue) ParseRequirement(string idOrPath)
    {
        idOrPath = idOrPath?.Trim() ?? string.Empty;
        if (idOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return (null, null);
        var at = idOrPath.IndexOf('@');
        if (at > 0) return (idOrPath.Substring(0, at), idOrPath.Substring(at + 1));
        return (idOrPath, null);
    }

    public static string TryGetLocalPackageNameFromFolder(string fileUri)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileUri) || !fileUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
            var folder = fileUri.Substring("file:".Length);
            var pkgJson = Path.Combine(folder, "package.json");
            if (!File.Exists(pkgJson)) return null;
            var json = File.ReadAllText(pkgJson);
            return ExtractJsonString(json, "name");
        }
        catch
        {
            return null;
        }
    }

    public static string ToFileUrl(string absoluteOrRelativePath)
    {
        var p = (absoluteOrRelativePath ?? string.Empty).Replace('\\', '/').Trim().Trim('"');
        if (p.Length == 0) return string.Empty;

        if (p.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = p.Substring("file:".Length).Replace('\\', '/');
            while (rest.StartsWith("/")) rest = rest.Substring(1);
            return "file:" + rest;
        }

        if (Path.IsPathRooted(p)) return $"file:{p}";
        return $"file:{p}";
    }

    public static Dictionary<string, string> ReadManifestPackages()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) return dict;

            var projectRoot = Directory.GetParent(dataPath);
            if (projectRoot == null) return dict;

            var manifestPath = Path.Combine(projectRoot.FullName, "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return dict;

            var json = File.ReadAllText(manifestPath);
            var depsObj = ExtractJsonObject(json, "dependencies");
            if (string.IsNullOrEmpty(depsObj)) return dict;

            foreach (var kv in ParseSimpleStringDict(depsObj)) dict[kv.Key] = kv.Value;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Packages] No se pudo leer manifest.json: {e.Message}");
        }

        return dict;
    }

    public static string ExtractJsonObject(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

        var i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf('{', i);
        if (i < 0) return null;

        int depth = 0;
        for (int j = i; j < json.Length; j++)
        {
            var c = json[j];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return json.Substring(i, j - i + 1);
            }
        }

        return null;
    }

    public static IEnumerable<KeyValuePair<string, string>> ParseSimpleStringDict(string jsonObject)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(jsonObject)) return result;

        int i = 0;
        while (i < jsonObject.Length)
        {
            var q1 = jsonObject.IndexOf('"', i);
            if (q1 < 0) break;
            var q2 = jsonObject.IndexOf('"', q1 + 1);
            if (q2 < 0) break;
            var key = jsonObject.Substring(q1 + 1, q2 - q1 - 1);

            var colon = jsonObject.IndexOf(':', q2);
            if (colon < 0) break;
            int vStart = colon + 1;
            while (vStart < jsonObject.Length && char.IsWhiteSpace(jsonObject[vStart])) vStart++;

            string value;
            if (vStart < jsonObject.Length && jsonObject[vStart] == '"')
            {
                var vq2 = jsonObject.IndexOf('"', vStart + 1);
                if (vq2 < 0) break;
                value = jsonObject.Substring(vStart + 1, vq2 - vStart - 1);
                i = jsonObject.IndexOf(',', vq2);
                i = i < 0 ? jsonObject.Length : i + 1;
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

    static string ExtractJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

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
}