using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ShaderAILab.Editor
{
    /// <summary>
    /// Custom ShaderGUI for all ShaderAILab-generated shaders.
    /// Reads AILab_Property metadata from the shader source to build
    /// a grouped, user-friendly Material Inspector.
    /// </summary>
    public class ShaderAILabGUI : ShaderGUI
    {
        public struct PropertyMeta
        {
            public string Name;
            public string DisplayName;
            public string Role;
            public string Type;
        }

        static readonly Regex RePropertyTag = new Regex(
            @"//\s*\[AILab_Property:\s*(.*?)\]", RegexOptions.Compiled);
        static readonly Regex ReKV = new Regex(
            @"(\w+)=""([^""]*)""", RegexOptions.Compiled);

        static readonly Dictionary<string, string> RoleGroupNames = new Dictionary<string, string>
        {
            { "base_color",  "Base Color" },
            { "albedo",      "Base Color" },
            { "color",       "Base Color" },
            { "normal",      "Normal" },
            { "normalmap",   "Normal" },
            { "emission",    "Emission" },
            { "emissive",    "Emission" },
            { "roughness",   "Surface" },
            { "smoothness",  "Surface" },
            { "metallic",    "Surface" },
            { "specular",    "Surface" },
            { "occlusion",   "Surface" },
            { "ao",          "Surface" },
            { "alpha",       "Transparency" },
            { "cutoff",      "Transparency" },
            { "opacity",     "Transparency" },
            { "tiling",      "UV / Tiling" },
            { "offset",      "UV / Tiling" },
            { "uv",          "UV / Tiling" },
        };

        Dictionary<string, PropertyMeta> _metaCache;
        string _cachedShaderName;

        bool _nlFoldout;
        string _nlPrompt = "";
        bool _nlProcessing;

        // Foldout states per group
        readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            var material = materialEditor.target as Material;
            if (material == null || material.shader == null)
            {
                base.OnGUI(materialEditor, properties);
                return;
            }

            EnsureMetaCache(material.shader);

            EditorGUILayout.Space(2);

            // Header
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUILayout.LabelField("Shader AILab Material", headerStyle);
            EditorGUILayout.LabelField(material.shader.name, EditorStyles.miniLabel);

            DrawSeparator();

            // Group properties by role
            var groups = GroupProperties(properties);

            foreach (var group in groups)
            {
                if (!_foldouts.ContainsKey(group.Key))
                    _foldouts[group.Key] = true;

                _foldouts[group.Key] = EditorGUILayout.BeginFoldoutHeaderGroup(
                    _foldouts[group.Key], group.Key);

                if (_foldouts[group.Key])
                {
                    EditorGUI.indentLevel++;
                    foreach (var prop in group.Value)
                        DrawProperty(materialEditor, prop);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            DrawSeparator();

            // Natural language adjustment section
            DrawNLAdjustSection(materialEditor, properties, material);

            DrawSeparator();

            // Standard settings
            materialEditor.RenderQueueField();
            materialEditor.EnableInstancingField();
            materialEditor.DoubleSidedGIField();
        }

        void DrawProperty(MaterialEditor materialEditor, MaterialProperty prop)
        {
            string displayName = prop.displayName;

            if (_metaCache != null && _metaCache.TryGetValue(prop.name, out var meta))
            {
                if (!string.IsNullOrEmpty(meta.DisplayName))
                    displayName = meta.DisplayName;
            }

            switch (prop.type)
            {
                case MaterialProperty.PropType.Texture:
                    materialEditor.TexturePropertySingleLine(
                        new GUIContent(displayName), prop);
                    break;

                case MaterialProperty.PropType.Color:
                    materialEditor.ColorProperty(prop, displayName);
                    break;

                case MaterialProperty.PropType.Range:
                    materialEditor.RangeProperty(prop, displayName);
                    break;

                case MaterialProperty.PropType.Float:
                    materialEditor.FloatProperty(prop, displayName);
                    break;

                case MaterialProperty.PropType.Vector:
                    materialEditor.VectorProperty(prop, displayName);
                    break;

#if UNITY_2021_1_OR_NEWER
                case MaterialProperty.PropType.Int:
                    materialEditor.IntegerProperty(prop, displayName);
                    break;
#endif

                default:
                    materialEditor.ShaderProperty(prop, displayName);
                    break;
            }
        }

        List<KeyValuePair<string, List<MaterialProperty>>> GroupProperties(MaterialProperty[] properties)
        {
            var groups = new Dictionary<string, List<MaterialProperty>>();

            foreach (var prop in properties)
            {
                if ((prop.flags & MaterialProperty.PropFlags.HideInInspector) != 0)
                    continue;

                string groupName = "Other";

                if (_metaCache != null && _metaCache.TryGetValue(prop.name, out var meta)
                    && !string.IsNullOrEmpty(meta.Role))
                {
                    string roleLower = meta.Role.ToLowerInvariant().Replace(" ", "_");
                    if (RoleGroupNames.TryGetValue(roleLower, out string mapped))
                        groupName = mapped;
                    else
                        groupName = CultureCapitalize(meta.Role);
                }
                else
                {
                    groupName = InferGroup(prop);
                }

                if (!groups.ContainsKey(groupName))
                    groups[groupName] = new List<MaterialProperty>();
                groups[groupName].Add(prop);
            }

            // Sort: Base Color first, Other last
            var sorted = groups.OrderBy(g =>
            {
                if (g.Key == "Base Color") return 0;
                if (g.Key == "Normal") return 1;
                if (g.Key == "Surface") return 2;
                if (g.Key == "Emission") return 3;
                if (g.Key == "Transparency") return 4;
                if (g.Key == "UV / Tiling") return 5;
                if (g.Key == "Other") return 99;
                return 10;
            }).ToList();

            return sorted;
        }

        static string InferGroup(MaterialProperty prop)
        {
            string lower = prop.name.ToLowerInvariant();

            if (lower.Contains("color") || lower.Contains("albedo") || lower.Contains("tint"))
                return "Base Color";
            if (lower.Contains("normal") || lower.Contains("bump"))
                return "Normal";
            if (lower.Contains("metal") || lower.Contains("rough") || lower.Contains("smooth")
                || lower.Contains("specular") || lower.Contains("occlusion"))
                return "Surface";
            if (lower.Contains("emiss"))
                return "Emission";
            if (lower.Contains("alpha") || lower.Contains("cutoff") || lower.Contains("opac"))
                return "Transparency";

            return "Other";
        }

        void EnsureMetaCache(Shader shader)
        {
            if (_metaCache != null && _cachedShaderName == shader.name)
                return;

            _cachedShaderName = shader.name;
            _metaCache = new Dictionary<string, PropertyMeta>();

            string path = UnityEditor.AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path)) return;

            string fullPath = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(fullPath)) return;

            string content = System.IO.File.ReadAllText(fullPath);
            foreach (Match m in RePropertyTag.Matches(content))
            {
                var meta = new PropertyMeta();
                foreach (Match kv in ReKV.Matches(m.Groups[1].Value))
                {
                    string key = kv.Groups[1].Value.ToLowerInvariant();
                    string val = kv.Groups[2].Value;
                    switch (key)
                    {
                        case "name":    meta.Name = val; break;
                        case "display": meta.DisplayName = val; break;
                        case "role":    meta.Role = val; break;
                        case "type":    meta.Type = val; break;
                    }
                }

                if (!string.IsNullOrEmpty(meta.Name))
                    _metaCache[meta.Name] = meta;
            }
        }

        // ---- Natural Language Adjustment Section ----

        void DrawNLAdjustSection(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            _nlFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_nlFoldout, "AI Parameter Adjustment");
            if (_nlFoldout)
            {
                EditorGUILayout.HelpBox(
                    "Describe the change you want in natural language, e.g. \"make the color warmer\" or \"set roughness to 0.8\".",
                    MessageType.Info);

                EditorGUI.BeginDisabledGroup(_nlProcessing);

                _nlPrompt = EditorGUILayout.TextField("Instruction", _nlPrompt);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_nlProcessing ? "Processing..." : "Adjust", GUILayout.Width(100)))
                {
                    if (!string.IsNullOrEmpty(_nlPrompt))
                        RunNLAdjust(materialEditor, properties, material);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        async void RunNLAdjust(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            _nlProcessing = true;

            try
            {
                var llmService = LLM.LLMService.Instance;
                if (!llmService.ActiveProvider.ValidateSettings())
                {
                    Debug.LogWarning("[ShaderAILab] LLM not configured. Open LLM Settings.");
                    _nlProcessing = false;
                    return;
                }

                string systemPrompt = LLM.PromptTemplates.BuildMaterialAdjustSystemPrompt();
                string userPrompt = LLM.PromptTemplates.BuildMaterialAdjustUserPrompt(
                    _nlPrompt, properties, _metaCache);

                string response = await llmService.GenerateAsync(systemPrompt, userPrompt);
                var changes = LLM.PromptTemplates.ParseMaterialAdjustResponse(response);

                if (changes != null && changes.Count > 0)
                {
                    Undo.RecordObject(material, "AI Parameter Adjustment");

                    foreach (var kvp in changes)
                    {
                        var prop = properties.FirstOrDefault(p => p.name == kvp.Key);
                        if (prop == null) continue;

                        ApplyPropertyValue(prop, kvp.Value);
                    }

                    EditorUtility.SetDirty(material);
                    materialEditor.Repaint();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ShaderAILab] NL Adjust Error: {ex.Message}");
            }

            _nlProcessing = false;
            _nlPrompt = "";
        }

        static void ApplyPropertyValue(MaterialProperty prop, string valueStr)
        {
            switch (prop.type)
            {
                case MaterialProperty.PropType.Float:
                case MaterialProperty.PropType.Range:
                    if (float.TryParse(valueStr, out float fVal))
                        prop.floatValue = fVal;
                    break;

#if UNITY_2021_1_OR_NEWER
                case MaterialProperty.PropType.Int:
                    if (int.TryParse(valueStr, out int iVal))
                        prop.intValue = iVal;
                    break;
#endif

                case MaterialProperty.PropType.Color:
                    if (TryParseColor(valueStr, out Color c))
                        prop.colorValue = c;
                    break;

                case MaterialProperty.PropType.Vector:
                    if (TryParseVector(valueStr, out Vector4 v))
                        prop.vectorValue = v;
                    break;
            }
        }

        static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            str = str.Trim('(', ')');
            var parts = str.Split(',');
            if (parts.Length >= 3)
            {
                float.TryParse(parts[0].Trim(), out float r);
                float.TryParse(parts[1].Trim(), out float g);
                float.TryParse(parts[2].Trim(), out float b);
                float a = 1f;
                if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out a);
                color = new Color(r, g, b, a);
                return true;
            }
            return false;
        }

        static bool TryParseVector(string str, out Vector4 vec)
        {
            vec = Vector4.zero;
            str = str.Trim('(', ')');
            var parts = str.Split(',');
            if (parts.Length >= 2)
            {
                float.TryParse(parts[0].Trim(), out float x);
                float.TryParse(parts[1].Trim(), out float y);
                float z = 0, w = 0;
                if (parts.Length >= 3) float.TryParse(parts[2].Trim(), out z);
                if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out w);
                vec = new Vector4(x, y, z, w);
                return true;
            }
            return false;
        }

        static void DrawSeparator()
        {
            EditorGUILayout.Space(4);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 1f));
            EditorGUILayout.Space(4);
        }

        static string CultureCapitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
