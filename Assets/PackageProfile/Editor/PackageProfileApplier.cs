using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class PackageProfileApplier
{
    static bool _isRunning;
    public static Action OnBatchStarted;
    public static Action OnBatchCompleted;
    public static bool IsRunning => _isRunning;

    // Intención acumulada
    static readonly List<string> _pendingRemoves = new List<string>();
    static readonly List<string> _pendingAdds = new List<string>();

    public static void Apply(PackageProfile profile) => Enqueue(profile, includeRemoves: true);
    public static void ApplyAddsOnly(PackageProfile profile) => Enqueue(profile, includeRemoves: false);

    static void Enqueue(PackageProfile profile, bool includeRemoves)
    {
        if (profile == null)
        {
            Debug.LogError("[PackageProfile] Perfil nulo.");
            return;
        }

        if (includeRemoves)
        {
            foreach (var s in (profile.packagesToRemove ?? Array.Empty<string>()))
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var name = s.Split('@')[0].Trim();
                if (!string.IsNullOrEmpty(name)) _pendingRemoves.Add(name);
            }
        }

        foreach (var s in (profile.packagesToAdd ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var id = PackageUtils.NormalizeAddId(s);
            if (!string.IsNullOrEmpty(id)) _pendingAdds.Add(id);
        }

        Debug.Log("[Packages] Intención encolada. Usa FinalApply() para resolver en una sola operación.");
    }

    public static void FinalApply()
    {
        if (_isRunning)
        {
            Debug.LogWarning("[Packages] Ya hay un batch en curso.");
            return;
        }

        if (_pendingAdds.Count == 0 && _pendingRemoves.Count == 0)
        {
            Debug.Log("[Packages] No hay cambios por aplicar.");
            return;
        }

        _isRunning = true;
        OnBatchStarted?.Invoke();

        var listReq = Client.List(true);
        EditorApplication.update += TickList;

        void TickList()
        {
            if (!listReq.IsCompleted) return;
            EditorApplication.update -= TickList;

            var installed = BuildInstalledMap(listReq);
            var (adds, removes) = BuildFinalChanges(installed);

            if (adds.Length == 0 && removes.Length == 0)
            {
                Debug.Log("[Packages] No hay cambios efectivos tras pruning.");
                _isRunning = false;
                OnBatchCompleted?.Invoke();
                return;
            }

            var req = Client.AddAndRemove(adds, removes);
            EditorApplication.update += TickApply;

            void TickApply()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= TickApply;

                if (req.Status == StatusCode.Failure)
                {
                    Debug.LogError($"[Packages] AddAndRemove error: {req.Error?.message ?? "Error desconocido"}");
                }
                else
                {
                    Debug.Log("[Packages] AddAndRemove completado.");
                    _pendingAdds.Clear();
                    _pendingRemoves.Clear();
                }

                _isRunning = false;
                OnBatchCompleted?.Invoke();
            }
        }
    }

    static Dictionary<string, string> BuildInstalledMap(ListRequest listReq)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (listReq.Status == StatusCode.Success && listReq.Result != null)
        {
            foreach (var p in listReq.Result)
            {
                if (string.IsNullOrEmpty(p?.name)) continue;
                var value = !string.IsNullOrEmpty(p?.resolvedPath)
                    ? $"file:{p.resolvedPath.Replace('\\', '/')}"
                    : p.version;
                map[p.name] = value;
            }
        }
        else
        {
            Debug.LogWarning($"[Packages] Snapshot falló: {listReq.Error?.message}");
        }

        return map;
    }

    static (string[] adds, string[] removes) BuildFinalChanges(Dictionary<string, string> installed)
    {
        var desiredRemoves = _pendingRemoves
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var desiredAdds = _pendingAdds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var removes = (installed == null || installed.Count == 0)
            ? desiredRemoves.ToArray()
            : desiredRemoves.Where(installed.ContainsKey).ToArray();

        var addsList = new List<string>();
        if (installed == null || installed.Count == 0)
        {
            addsList.AddRange(desiredAdds);
        }
        else
        {
            foreach (var id in desiredAdds)
            {
                if (id.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    var guessed = PackageUtils.TryGetLocalPackageNameFromFolder(id);
                    if (!string.IsNullOrEmpty(guessed) &&
                        installed.TryGetValue(guessed, out var curFile) &&
                        string.Equals(curFile?.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    addsList.Add(id);
                    continue;
                }

                var (name, exact) = PackageUtils.ParseRequirement(id);
                if (!string.IsNullOrEmpty(name) && installed.TryGetValue(name, out var curVal))
                {
                    if (!string.IsNullOrEmpty(exact) &&
                        string.Equals(curVal?.Trim(), exact.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                addsList.Add(id);
            }
        }

        return (addsList.ToArray(), removes);
    }
}