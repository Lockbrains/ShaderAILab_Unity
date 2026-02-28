using System.IO;
using UnityEditor;
using UnityEngine;
using ShaderAILab.Editor.UI;

namespace ShaderAILab.Editor.Integration
{
    public static class ShaderAILabMenuItems
    {
        [MenuItem("Assets/Open in Shader AILab", true)]
        static bool ValidateOpenInAILab()
        {
            var obj = Selection.activeObject;
            if (obj == null) return false;
            string path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader");
        }

        [MenuItem("Assets/Open in Shader AILab")]
        static void OpenInAILab()
        {
            var obj = Selection.activeObject;
            if (obj == null) return;
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return;

            string fullPath = Path.GetFullPath(path);
            ShaderAILabWindow.OpenShader(fullPath);
        }

        [MenuItem("ShaderAILab/New Unlit Shader")]
        static void NewUnlitShader()
        {
            CreateFromTemplate("URPUnlit.shader.txt", "NewUnlitShader");
        }

        [MenuItem("ShaderAILab/New Lit Shader")]
        static void NewLitShader()
        {
            CreateFromTemplate("URPLit.shader.txt", "NewLitShader");
        }

        static void CreateFromTemplate(string templateName, string defaultName)
        {
            string templateDir = Path.Combine(Application.dataPath, "ShaderAILab", "Templates");
            string templatePath = Path.Combine(templateDir, templateName);

            if (!File.Exists(templatePath))
            {
                EditorUtility.DisplayDialog("Template Missing",
                    $"Template not found: {templatePath}", "OK");
                return;
            }

            string savePath = EditorUtility.SaveFilePanel(
                "Create New AILab Shader", "Assets", defaultName, "shader");

            if (string.IsNullOrEmpty(savePath)) return;

            string content = File.ReadAllText(templatePath);
            string shaderName = "AILab/" + Path.GetFileNameWithoutExtension(savePath);
            content = content.Replace("AILab/Template", shaderName);

            File.WriteAllText(savePath, content);
            AssetDatabase.Refresh();
            ShaderAILabWindow.OpenShader(savePath);
        }
    }
}
