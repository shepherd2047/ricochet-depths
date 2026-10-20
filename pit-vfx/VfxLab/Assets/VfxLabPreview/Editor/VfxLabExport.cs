// Exports the shippable part (Assets/RicochetVfx only, no preview scene) as pit-vfx/RicochetVfx.unitypackage:
//   Unity -batchmode -quit -projectPath pit-vfx/VfxLab -executeMethod VfxLabExport.Run
using System.IO;
using UnityEditor;

public static class VfxLabExport
{
    [MenuItem("Ricochet VFX/Export Package")]
    public static void Run()
    {
        var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "RicochetVfx.unitypackage"));
        AssetDatabase.ExportPackage("Assets/RicochetVfx", path, ExportPackageOptions.Recurse);
        UnityEngine.Debug.Log("VfxLab: exported " + path);
    }
}
