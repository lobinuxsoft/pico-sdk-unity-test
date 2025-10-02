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
                    var normalized = PackageUtils.ToFileUrl(folder);

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
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Encolar: Aplicar y Remover"))
            {
                PackageProfileApplier.Apply(profile); // encola
            }
            if (GUILayout.Button("Encolar: Solo Aplicar"))
            {
                PackageProfileApplier.ApplyAddsOnly(profile); // encola
            }
        }
        if (GUILayout.Button("Final Apply (resolver cola)"))
        {
            PackageProfileApplier.FinalApply(); // ejecuta la cola
        }
    }
}