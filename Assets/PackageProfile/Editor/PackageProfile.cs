using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Build/Package Profile", fileName = "PackageProfile")]
public class PackageProfile : ScriptableObject
{
    [Tooltip("Nombre del perfil (solo informativo)")]
    public string profileName = "Profile";

    [Tooltip("Paquetes a añadir. Formatos válidos:\n- nombre@version\n- file:C:/ruta/al/package")]
    public string[] packagesToAdd;

    [Tooltip("Paquetes a quitar. Usar solo el nombre (sin @version)")]
    public string[] packagesToRemove;
    
    // Método helper para usar desde editor code
    public void ApplyProfileInEditor()
    {
        PackageProfileApplier.Apply(this);
    }
}