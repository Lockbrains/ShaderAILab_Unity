using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using ShaderAILab.Editor.Core;
using ShaderAILab.Editor.UI;

namespace ShaderAILab.Editor.Integration
{
    /// <summary>
    /// Intercepts double-click on .shader files that contain AILab tags
    /// and opens them in the ShaderAILabWindow instead of the default text editor.
    /// </summary>
    public static class ShaderAILabAssetHandler
    {
        [OnOpenAsset(0)]
        static bool OnOpenAsset(int instanceID, int line)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            if (obj == null) return false;

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader"))
                return false;

            string fullPath = System.IO.Path.GetFullPath(path);
            string content = System.IO.File.ReadAllText(fullPath);

            if (!content.Contains("[AILab_"))
                return false;

            ShaderAILabWindow.OpenShader(fullPath);
            return true;
        }
    }
}
