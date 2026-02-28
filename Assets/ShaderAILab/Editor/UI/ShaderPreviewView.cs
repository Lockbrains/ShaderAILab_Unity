using System.Reflection;
using UnityEditor;
using UnityEngine;
using ShaderAILab.Editor.Core;

namespace ShaderAILab.Editor.UI
{
    public class ShaderPreviewView
    {
        const string kEditorPrefKey = "DefaultMaterialPreviewMesh";

        // Built-in MaterialEditor mesh indices: 0=Sphere, 1=Cube, 2=Cylinder, 3=Torus, 4=Quad
        static readonly string[] IconNames =
        {
            "PreMatSphere", "PreMatCube", "PreMatCylinder", "PreMatTorus", "PreMatQuad"
        };
        static readonly string[] Tooltips =
        {
            "Sphere", "Cube", "Cylinder", "Torus", "Plane"
        };

        static GUIContent[] _icons;
        static FieldInfo _selectedMeshField;
        static FieldInfo _staticMeshesField;
        static PropertyInfo _firstInspectedProp;

        Material _previewMaterial;
        MaterialEditor _materialEditor;
        int _meshIndex;
        Mesh _customMesh;
        bool _usingCustom;

        static ShaderPreviewView()
        {
            var t = typeof(MaterialEditor);
            _selectedMeshField = t.GetField("m_SelectedMesh",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _staticMeshesField = t.GetField("s_Meshes",
                BindingFlags.NonPublic | BindingFlags.Static);

            // Editor.firstInspectedEditor controls whether DefaultPreviewGUI
            // respects m_SelectedMesh or forces sphere override.
            _firstInspectedProp = typeof(UnityEditor.Editor).GetProperty("firstInspectedEditor",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        void EnsureIcons()
        {
            if (_icons != null && _icons.Length == IconNames.Length) return;
            _icons = new GUIContent[IconNames.Length];
            for (int i = 0; i < IconNames.Length; i++)
            {
                _icons[i] = EditorGUIUtility.IconContent(IconNames[i]);
                if (_icons[i] == null || _icons[i].image == null)
                    _icons[i] = new GUIContent(Tooltips[i][0].ToString());
                _icons[i].tooltip = Tooltips[i];
            }
        }

        public void SetShader(ShaderDocument doc)
        {
            if (doc == null) return;

            Shader shader = ResolveShader(doc);

            if (_previewMaterial == null)
                _previewMaterial = new Material(shader);
            else
                _previewMaterial.shader = shader;

            foreach (var prop in doc.Properties)
                ApplyPropertyToMaterial(prop);

            _meshIndex = EditorPrefs.GetInt(kEditorPrefKey, 0);
            RecreateMaterialEditor();
        }

        public void UpdateProperty(string propertyName, object value)
        {
            if (_previewMaterial == null) return;

            if (value is float f)
                _previewMaterial.SetFloat(propertyName, f);
            else if (value is int i)
                _previewMaterial.SetInt(propertyName, i);
            else if (value is Color c)
                _previewMaterial.SetColor(propertyName, c);
            else if (value is Vector4 v)
                _previewMaterial.SetVector(propertyName, v);

            RecreateMaterialEditor();
        }

        public void OnPreviewGUI()
        {
            if (_previewMaterial == null) return;
            if (_materialEditor == null) RecreateMaterialEditor();
            if (_materialEditor == null) return;

            DrawToolbar();

            var rect = GUILayoutUtility.GetRect(200, 200,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 10 || rect.height < 10) return;

            if (_materialEditor.HasPreviewGUI())
                _materialEditor.OnInteractivePreviewGUI(rect, EditorStyles.helpBox);
            else
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "Preview not available", EditorStyles.centeredGreyMiniLabel);
            }
        }

        void DrawToolbar()
        {
            EnsureIcons();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            for (int i = 0; i < _icons.Length; i++)
            {
                bool wasSelected = !_usingCustom && _meshIndex == i;
                bool nowSelected = GUILayout.Toggle(wasSelected, _icons[i],
                    EditorStyles.toolbarButton, GUILayout.Width(24), GUILayout.Height(18));
                if (nowSelected && !wasSelected)
                {
                    _usingCustom = false;
                    _meshIndex = i;
                    ApplyMeshIndex(i);
                }
            }

            GUILayout.Space(2);

            var customIcon = EditorGUIUtility.IconContent("Mesh Icon");
            if (customIcon == null || customIcon.image == null)
                customIcon = new GUIContent("M");
            customIcon.tooltip = "Custom Mesh";

            bool wasCustom = _usingCustom;
            bool nowCustom = GUILayout.Toggle(wasCustom, customIcon,
                EditorStyles.toolbarButton, GUILayout.Width(24), GUILayout.Height(18));
            if (nowCustom && !wasCustom)
                _usingCustom = true;
            if (!nowCustom && wasCustom && _meshIndex >= 0)
            {
                _usingCustom = false;
                ApplyMeshIndex(_meshIndex);
            }

            if (_usingCustom)
            {
                GUILayout.Space(4);
                var newMesh = (Mesh)EditorGUILayout.ObjectField(
                    _customMesh, typeof(Mesh), false,
                    GUILayout.MinWidth(60), GUILayout.MaxWidth(120));
                if (newMesh != _customMesh)
                {
                    _customMesh = newMesh;
                    if (_customMesh != null)
                        ApplyCustomMesh(_customMesh);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void ApplyMeshIndex(int index)
        {
            EditorPrefs.SetInt(kEditorPrefKey, index);

            if (_materialEditor != null && _selectedMeshField != null)
                _selectedMeshField.SetValue(_materialEditor, index);

            RestoreStaticMeshes();
        }

        void ApplyCustomMesh(Mesh mesh)
        {
            if (mesh == null) return;

            int slot = 4; // Quad slot
            _meshIndex = slot;
            EditorPrefs.SetInt(kEditorPrefKey, slot);

            if (_materialEditor != null)
            {
                // Trigger Init() so s_Meshes is populated
                _materialEditor.HasPreviewGUI();

                if (_selectedMeshField != null)
                    _selectedMeshField.SetValue(_materialEditor, slot);

                if (_staticMeshesField != null)
                {
                    var meshes = _staticMeshesField.GetValue(null) as Mesh[];
                    if (meshes != null && meshes.Length > slot)
                    {
                        if (_originalQuadMesh == null)
                            _originalQuadMesh = meshes[slot];
                        meshes[slot] = mesh;
                    }
                }
            }
        }

        static Mesh _originalQuadMesh;

        void RestoreStaticMeshes()
        {
            if (_originalQuadMesh == null) return;
            if (_staticMeshesField == null) return;

            var meshes = _staticMeshesField.GetValue(null) as Mesh[];
            if (meshes != null && meshes.Length > 4)
                meshes[4] = _originalQuadMesh;
            _originalQuadMesh = null;
        }

        void RecreateMaterialEditor()
        {
            if (_materialEditor != null)
                Object.DestroyImmediate(_materialEditor);

            if (_previewMaterial != null)
            {
                _materialEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(
                    _previewMaterial, typeof(MaterialEditor));

                // Without this, DefaultPreviewGUI always overrides mesh to sphere
                if (_firstInspectedProp != null)
                    _firstInspectedProp.SetValue(_materialEditor, true);

                if (_selectedMeshField != null)
                    _selectedMeshField.SetValue(_materialEditor, _meshIndex);
            }
        }

        static Shader ResolveShader(ShaderDocument doc)
        {
            Shader shader = null;

            if (!string.IsNullOrEmpty(doc.FilePath))
            {
                string relativePath = doc.FilePath;
                if (relativePath.StartsWith(Application.dataPath))
                    relativePath = "Assets" + relativePath.Substring(Application.dataPath.Length);
                relativePath = relativePath.Replace('\\', '/');
                shader = AssetDatabase.LoadAssetAtPath<Shader>(relativePath);
            }

            if (shader == null)
                shader = Shader.Find(doc.ShaderName);

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");

            return shader;
        }

        void ApplyPropertyToMaterial(ShaderProperty prop)
        {
            if (_previewMaterial == null || !_previewMaterial.HasProperty(prop.Name)) return;

            switch (prop.PropertyType)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    if (float.TryParse(prop.DefaultValue, out float fval))
                        _previewMaterial.SetFloat(prop.Name, fval);
                    break;
                case ShaderPropertyType.Color:
                    if (TryParseColor(prop.DefaultValue, out Color col))
                        _previewMaterial.SetColor(prop.Name, col);
                    break;
                case ShaderPropertyType.Int:
                    if (int.TryParse(prop.DefaultValue, out int ival))
                        _previewMaterial.SetInt(prop.Name, ival);
                    break;
            }
        }

        static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(str)) return false;
            str = str.Trim('(', ')');
            var parts = str.Split(',');
            if (parts.Length >= 3)
            {
                float.TryParse(parts[0].Trim(), out float r);
                float.TryParse(parts[1].Trim(), out float g);
                float.TryParse(parts[2].Trim(), out float b);
                float a = parts.Length >= 4 && float.TryParse(parts[3].Trim(), out float pa) ? pa : 1f;
                color = new Color(r, g, b, a);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            RestoreStaticMeshes();

            if (_materialEditor != null)
                Object.DestroyImmediate(_materialEditor);
            _materialEditor = null;

            if (_previewMaterial != null)
                Object.DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }
    }
}
