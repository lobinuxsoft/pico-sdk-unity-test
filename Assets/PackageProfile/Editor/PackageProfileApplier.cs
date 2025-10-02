using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PackageProfileApplier
{
    enum OpType { Add, Remove }
    struct Op { public OpType type; public string idOrName; }

    static readonly Queue<Op> _ops = new Queue<Op>();
    static bool _isRunning;
    static bool _snapshotReady;
    static Dictionary<string, string> _installedMap; // name -> versionOrFile
    const int RetryDelayFrames = 30;
    const int MaxRetries = 3;

    public static System.Action OnBatchStarted;
    public static System.Action OnBatchCompleted;
    public static bool IsRunning => _isRunning;

    public static void Apply(PackageProfile profile)
    {
        if (profile == null) { Debug.LogError("[PackageProfile] Perfil nulo."); return; }
        EnqueueFromProfile(profile, includeRemoves: true);
        Debug.Log("[Packages] Operaciones encoladas. Ejecuta FinalApply() para procesar.");
    }

    public static void ApplyAddsOnly(PackageProfile profile)
    {
        if (profile == null) { Debug.LogError("[PackageProfile] Perfil nulo."); return; }
        EnqueueFromProfile(profile, includeRemoves: false);
        Debug.Log("[Packages] Operaciones encoladas (solo adds). Ejecuta FinalApply() para procesar.");
    }

    public static void FinalApply()
    {
        if (_isRunning) { Debug.LogWarning("[Packages] Ya hay un procesamiento en curso; espera a que termine."); return; }
        if (_ops.Count == 0) { Debug.Log("[Packages] No hay operaciones encoladas."); return; }
        _isRunning = true;
        OnBatchStarted?.Invoke();

        // Asegurar snapshot antes de procesar
        if (!_snapshotReady) BeginSnapshot();
        else ProcessNext(0, 0);
    }

    static void EnqueueFromProfile(PackageProfile profile, bool includeRemoves)
    {
        var removes = includeRemoves
            ? (profile.packagesToRemove ?? System.Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Split('@')[0].Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        var adds = (profile.packagesToAdd ?? System.Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(PackageUtils.NormalizeAddId)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Sin snapshot aún: encolamos "a ciegas". Al tener snapshot, filtraremos redundancias justo antes de ejecutar.
        foreach (var r in removes) _ops.Enqueue(new Op { type = OpType.Remove, idOrName = r });
        foreach (var a in adds) _ops.Enqueue(new Op { type = OpType.Add, idOrName = a });
    }

    static void BeginSnapshot()
    {
        var listReq = UnityEditor.PackageManager.Client.List(true);
        EditorApplication.update += TickList;
        void TickList()
        {
            if (!listReq.IsCompleted) return;
            EditorApplication.update -= TickList;

            _installedMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (listReq.Status == UnityEditor.PackageManager.StatusCode.Success && listReq.Result != null)
            {
                foreach (var p in listReq.Result)
                {
                    if (string.IsNullOrEmpty(p?.name)) continue;
                    var value = !string.IsNullOrEmpty(p?.resolvedPath)
                        ? $"file:{p.resolvedPath.Replace('\\','/')}"
                        : p.version;
                    _installedMap[p.name] = value;
                }
            }
            else
            {
                Debug.LogWarning($"[Packages] No se pudo obtener snapshot de paquetes: {listReq.Error?.message}");
            }

            _snapshotReady = true;
            // Filtrar la cola para eliminar redundancias basadas en snapshot
            PruneQueueWithSnapshot();
            ProcessNext(0, 0);
        }
    }

    static void PruneQueueWithSnapshot()
    {
        if (_installedMap == null || _installedMap.Count == 0) return;
        if (_ops.Count == 0) return;

        var filtered = new Queue<Op>();
        foreach (var op in _ops)
        {
            if (op.type == OpType.Remove)
            {
                // Solo tiene sentido remover si está instalado
                if (_installedMap.ContainsKey(op.idOrName)) filtered.Enqueue(op);
            }
            else
            {
                var id = op.idOrName;
                var at = id.IndexOf('@');
                string name = null;
                string desiredValue = null;

                if (id.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Intentar deducir nombre para evitar duplicados exactos
                    var guessed = PackageUtils.TryGetLocalPackageNameFromFolder(id);
                    if (!string.IsNullOrEmpty(guessed))
                    {
                        name = guessed;
                        if (_installedMap.TryGetValue(name, out var cur) && string.Equals(cur?.Trim(), id.Trim(), System.StringComparison.OrdinalIgnoreCase))
                            continue; // ya instalado mismo file:
                    }
                    // Si no se pudo deducir nombre, no podemos asegurar redundancia -> lo dejamos pasar
                }
                else if (at > 0)
                {
                    name = id.Substring(0, at);
                    desiredValue = id.Substring(at + 1);
                    if (_installedMap.TryGetValue(name, out var cur) &&
                        !string.IsNullOrEmpty(desiredValue) &&
                        string.Equals(cur?.Trim(), desiredValue.Trim(), System.StringComparison.OrdinalIgnoreCase))
                        continue; // misma versión -> no encolar
                }
                else
                {
                    name = id;
                    // si ya está instalado con cualquier versión y no pedimos una exacta, permitir (por si queremos actualizar luego con otra op)
                }

                filtered.Enqueue(op);
            }
        }

        _ops.Clear();
        foreach (var f in filtered) _ops.Enqueue(f);
    }

    static void ProcessNext(int retryCount, int waitFrames)
    {
        if (waitFrames > 0)
        {
            int framesLeft = waitFrames;
            void DelayTick()
            {
                if (--framesLeft > 0) return;
                EditorApplication.update -= DelayTick;
                ProcessNext(retryCount, 0);
            }
            EditorApplication.update += DelayTick;
            return;
        }

        if (_ops.Count == 0)
        {
            _isRunning = false;
            Debug.Log("[Packages] Cola completada.");
            OnBatchCompleted?.Invoke();
            return;
        }

        var op = _ops.Peek();
        if (op.type == OpType.Remove)
        {
            var req = UnityEditor.PackageManager.Client.Remove(op.idOrName);
            EditorApplication.update += TickRemove;
            void TickRemove()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= TickRemove;

                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                {
                    var msg = req.Error?.message ?? string.Empty;
                    if (!string.IsNullOrEmpty(msg) && msg.IndexOf("exclusive access", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (retryCount < MaxRetries)
                        {
                            Debug.LogWarning($"[Packages] Remove '{op.idOrName}' reintentando por acceso exclusivo...");
                            ProcessNext(retryCount + 1, RetryDelayFrames);
                            return;
                        }
                    }
                    if (!string.IsNullOrEmpty(msg) &&
                        msg.IndexOf("cannot be found in the project manifest", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.LogWarning($"[Packages] Remove aviso {op.idOrName}: {msg}");
                    }
                    else
                    {
                        Debug.LogError($"[Packages] Remove error {op.idOrName}: {msg}");
                    }
                }
                else
                {
                    // Actualizar snapshot en memoria
                    if (_installedMap != null) _installedMap.Remove(op.idOrName);
                }

                _ops.Dequeue();
                ProcessNext(0, 0);
            }
        }
        else // Add
        {
            var req = UnityEditor.PackageManager.Client.Add(op.idOrName);
            EditorApplication.update += TickAdd;
            void TickAdd()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= TickAdd;

                if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                {
                    var msg = req.Error?.message ?? string.Empty;
                    if (!string.IsNullOrEmpty(msg) && msg.IndexOf("exclusive access", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (retryCount < MaxRetries)
                        {
                            Debug.LogWarning($"[Packages] Add '{op.idOrName}' reintentando por acceso exclusivo...");
                            ProcessNext(retryCount + 1, RetryDelayFrames);
                            return;
                        }
                    }
                    Debug.LogError($"[Packages] Add error {op.idOrName}: {msg}");
                }
                else
                {
                    // Mejor esfuerzo para actualizar snapshot
                    if (_installedMap != null)
                    {
                        var id = op.idOrName;
                        var (name, exact) = PackageUtils.ParseRequirement(id);
                        if (id.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var guessed = PackageUtils.TryGetLocalPackageNameFromFolder(id);
                            if (!string.IsNullOrEmpty(guessed)) _installedMap[guessed] = id;
                        }
                        else if (!string.IsNullOrEmpty(name))
                        {
                            _installedMap[name] = string.IsNullOrEmpty(exact) ? ( _installedMap.TryGetValue(name, out var cur) ? cur : exact ) : exact;
                        }
                    }
                }

                _ops.Dequeue();
                ProcessNext(0, 0);
            }
        }
    }
}