using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PackageProfile))]
public class PackageProfileEditor : Editor
{
    int insertIndex; // índice donde insertar el package local

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var profile = (PackageProfile)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Paquete local (helper)", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            insertIndex = EditorGUILayout.IntField("Índice (packagesToAdd)", insertIndex, GUILayout.Width(220));
            if (GUILayout.Button("Seleccionar carpeta del package (local)"))
            {
                var folder = EditorUtility.OpenFolderPanel("Seleccionar carpeta del package (contiene package.json)", "", "");
                if (!string.IsNullOrEmpty(folder))
                {
                    // Normalizar a URL file: sin triple slash
                    var normalized = ToFileUrl(folder);

                    // Insertar en packagesToAdd
                    var list = profile.packagesToAdd != null ? new System.Collections.Generic.List<string>(profile.packagesToAdd) : new System.Collections.Generic.List<string>();
                    insertIndex = Mathf.Clamp(insertIndex, 0, list.Count);
                    list.Insert(insertIndex, normalized);
                    profile.packagesToAdd = list.ToArray();

                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[PackageProfile] Insertado package local en packagesToAdd[{insertIndex}]: {normalized}");
                }
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Aplicar este perfil (Add/Remove Packages)"))
        {
            PackageProfileApplier.Apply(profile);
        }
    }

    // Siempre devuelve formato file:C:/... (sin file:///)
    static string ToFileUrl(string absolutePath)
    {
        var p = absolutePath.Replace('\\', '/').Trim().Trim('"');

        // Si ya viene como file:, normalizar y quitar slashes extra tras el esquema
        if (p.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase))
        {
            var rest = p.Substring("file:".Length).Replace('\\', '/');
            while (rest.StartsWith("/")) rest = rest.Substring(1); // elimina /// -> /
            return "file:" + rest;
        }

        // Ruta absoluta -> file:C:/...
        if (System.IO.Path.IsPathRooted(p))
            return $"file:{p}";

        // Relativa -> file:relative/path
        return $"file:{p}";
    }
}