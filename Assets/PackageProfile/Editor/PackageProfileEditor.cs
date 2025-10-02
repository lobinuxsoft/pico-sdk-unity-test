using System.Collections.Generic;
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
                    InsertLocalPackage(profile, PackageUtils.ToFileUrl(folder), ref insertIndex);
                }
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Encolar: Aplicar y Remover")) PackageProfileApplier.Apply(profile);
            if (GUILayout.Button("Encolar: Solo Aplicar")) PackageProfileApplier.ApplyAddsOnly(profile);
        }

        if (GUILayout.Button("Final Apply (resolver cola única)"))
        {
            PackageProfileApplier.FinalApply();
        }
    }

    static void InsertLocalPackage(PackageProfile profile, string normalizedFileUrl, ref int index)
    {
        var list = new List<string>(profile.packagesToAdd ?? System.Array.Empty<string>());
        index = Mathf.Clamp(index, 0, list.Count);
        list.Insert(index, normalizedFileUrl);
        profile.packagesToAdd = list.ToArray();

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PackageProfile] Insertado package local en packagesToAdd[{index}]: {normalizedFileUrl}");
    }
}