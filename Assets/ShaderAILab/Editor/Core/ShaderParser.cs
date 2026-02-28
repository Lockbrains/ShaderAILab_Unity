using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Parses .shader files that contain AILab metadata tags into a ShaderDocument.
    /// </summary>
    public static class ShaderParser
    {
        static readonly Regex ReShaderName =
            new Regex(@"Shader\s+""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex RePropertyTag =
            new Regex(@"//\s*\[AILab_Property:\s*(.*?)\]", RegexOptions.Compiled);

        static readonly Regex ReBlockStart =
            new Regex(@"//\s*\[AILab_Block_Start:\s*""([^""]+)""\]", RegexOptions.Compiled);

        static readonly Regex ReBlockEnd =
            new Regex(@"//\s*\[AILab_Block_End\]", RegexOptions.Compiled);

        static readonly Regex ReIntent =
            new Regex(@"//\s*\[AILab_Intent:\s*""([^""]+)""\]", RegexOptions.Compiled);

        static readonly Regex ReParamRef =
            new Regex(@"//\s*\[AILab_Param:\s*""([^""]+)""\s+role=""([^""]*)""\]", RegexOptions.Compiled);

        static readonly Regex ReGlobal =
            new Regex(@"//\s*\[AILab_Global:\s*(.*?)\]", RegexOptions.Compiled);

        static readonly Regex ReSection =
            new Regex(@"//\s*\[AILab_Section:\s*""([^""]+)""\]", RegexOptions.Compiled);

        static readonly Regex ReDisabled =
            new Regex(@"//\s*\[AILab_Disabled\]", RegexOptions.Compiled);

        static readonly Regex ReKV =
            new Regex(@"(\w+)=""([^""]*?)""", RegexOptions.Compiled);

        static readonly Regex ReStructStart =
            new Regex(@"struct\s+(Attributes|Varyings)\s*\{", RegexOptions.Compiled);

        static readonly Regex ReStructField =
            new Regex(@"^\s*(float[234]?|half[234]?|int|uint)\s+(\w+)\s*:\s*(\w+)\s*;", RegexOptions.Compiled);

        // -------------------------------------------------------------------

        public static ShaderDocument ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Shader file not found: {filePath}");

            string content = File.ReadAllText(filePath);
            var doc = ParseContent(content);
            doc.FilePath = filePath;
            doc.LastModified = File.GetLastWriteTimeUtc(filePath);
            return doc;
        }

        public static ShaderDocument ParseContent(string content)
        {
            var doc = new ShaderDocument { RawContent = content };

            var nameMatch = ReShaderName.Match(content);
            if (nameMatch.Success)
                doc.ShaderName = nameMatch.Groups[1].Value;

            string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            ParseGlobalSettings(lines, doc);
            ParseProperties(lines, doc);
            ParseBlocks(lines, doc);
            ParseStructs(lines, doc);

            return doc;
        }

        // -------------------------------------------------------------------
        // Global settings
        // -------------------------------------------------------------------

        static void ParseGlobalSettings(string[] lines, ShaderDocument doc)
        {
            foreach (string line in lines)
            {
                var m = ReGlobal.Match(line);
                if (!m.Success) continue;

                foreach (Match kv in ReKV.Matches(m.Groups[1].Value))
                {
                    string key = kv.Groups[1].Value.ToLowerInvariant();
                    string val = kv.Groups[2].Value;
                    switch (key)
                    {
                        case "cull":       doc.GlobalSettings.CullMode   = val; break;
                        case "blend":      doc.GlobalSettings.BlendMode  = val; break;
                        case "zwrite":     doc.GlobalSettings.ZWriteMode = val; break;
                        case "rendertype": doc.GlobalSettings.RenderType = val; break;
                        case "queue":      doc.GlobalSettings.RenderQueue = val; break;
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------

        static void ParseProperties(string[] lines, ShaderDocument doc)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var pm = RePropertyTag.Match(lines[i]);
                if (!pm.Success) continue;

                var prop = new ShaderProperty();
                foreach (Match kv in ReKV.Matches(pm.Groups[1].Value))
                {
                    string key = kv.Groups[1].Value.ToLowerInvariant();
                    string val = kv.Groups[2].Value;
                    switch (key)
                    {
                        case "name":    prop.Name        = val; break;
                        case "display": prop.DisplayName = val; break;
                        case "type":    prop.PropertyType = ParsePropertyType(val); break;
                        case "default": prop.DefaultValue = val; break;
                        case "min":     if (float.TryParse(val, out float mn)) prop.MinValue = mn; break;
                        case "max":     if (float.TryParse(val, out float mx)) prop.MaxValue = mx; break;
                        case "role":    prop.Role = val; break;
                    }
                }

                // Grab the raw Unity property declaration on the next non-comment line
                for (int j = i + 1; j < lines.Length && j <= i + 3; j++)
                {
                    string trimmed = lines[j].Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("//"))
                    {
                        prop.RawDeclaration = trimmed;
                        break;
                    }
                }

                doc.Properties.Add(prop);
            }
        }

        // -------------------------------------------------------------------
        // Blocks
        // -------------------------------------------------------------------

        static void ParseBlocks(string[] lines, ShaderDocument doc)
        {
            int i = 0;
            while (i < lines.Length)
            {
                var sm = ReBlockStart.Match(lines[i]);
                if (!sm.Success) { i++; continue; }

                var block = new ShaderBlock
                {
                    Title = sm.Groups[1].Value,
                    StartLine = i,
                    Section = DetermineSection(lines, i)
                };

                var codeLines = new List<string>();
                bool isDisabled = false;
                i++;

                while (i < lines.Length)
                {
                    if (ReBlockEnd.IsMatch(lines[i]))
                    {
                        block.EndLine = i;
                        i++;
                        break;
                    }

                    var im = ReIntent.Match(lines[i]);
                    if (im.Success)
                    {
                        block.Intent = im.Groups[1].Value;
                        i++;
                        continue;
                    }

                    var prm = ReParamRef.Match(lines[i]);
                    if (prm.Success)
                    {
                        block.ReferencedParams.Add(prm.Groups[1].Value);
                        i++;
                        continue;
                    }

                    if (ReDisabled.IsMatch(lines[i]))
                    {
                        isDisabled = true;
                        i++;
                        continue;
                    }

                    // If block is disabled, code lines are prefixed with "// "
                    if (isDisabled)
                    {
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("// "))
                        {
                            int commentIdx = lines[i].IndexOf("// ");
                            codeLines.Add(lines[i].Substring(0, commentIdx) + lines[i].Substring(commentIdx + 3));
                        }
                        else
                            codeLines.Add(lines[i]);
                    }
                    else
                    {
                        codeLines.Add(lines[i]);
                    }
                    i++;
                }

                block.IsEnabled = !isDisabled;
                block.Code = DedentLines(codeLines);
                doc.Blocks.Add(block);
            }
        }

        // -------------------------------------------------------------------
        // Struct parsing → DataFlowGraph
        // -------------------------------------------------------------------

        static void ParseStructs(string[] lines, ShaderDocument doc)
        {
            var graph = DataFlowGraph.CreateDefault();

            var parsedAttrs = new List<StructFieldInfo>();
            var parsedVarys = new List<StructFieldInfo>();
            string currentStruct = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var sm = ReStructStart.Match(lines[i]);
                if (sm.Success)
                {
                    currentStruct = sm.Groups[1].Value;
                    continue;
                }

                if (currentStruct != null && lines[i].Trim().Contains("}"))
                {
                    currentStruct = null;
                    continue;
                }

                if (currentStruct != null)
                {
                    var fm = ReStructField.Match(lines[i]);
                    if (fm.Success)
                    {
                        var info = new StructFieldInfo
                        {
                            Type = fm.Groups[1].Value,
                            Name = fm.Groups[2].Value,
                            Semantic = fm.Groups[3].Value
                        };

                        if (currentStruct == "Attributes")
                            parsedAttrs.Add(info);
                        else
                            parsedVarys.Add(info);
                    }
                }
            }

            if (parsedAttrs.Count > 0)
                ApplyParsedFields(graph, DataFlowStage.Attributes, parsedAttrs);
            if (parsedVarys.Count > 0)
                ApplyParsedFields(graph, DataFlowStage.Varyings, parsedVarys);

            doc.DataFlow = graph;
        }

        struct StructFieldInfo
        {
            public string Type;
            public string Name;
            public string Semantic;
        }

        static void ApplyParsedFields(DataFlowGraph graph, DataFlowStage stage, List<StructFieldInfo> parsed)
        {
            var list = stage == DataFlowStage.Attributes
                ? graph.AttributeFields
                : graph.VaryingFields;

            var matchedNames = new HashSet<string>();

            foreach (var info in parsed)
            {
                bool found = false;
                foreach (var field in list)
                {
                    if (field.Name == info.Name)
                    {
                        field.IsActive = true;
                        matchedNames.Add(field.Name);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    var custom = new DataFlowField(
                        info.Name, info.Type, info.Semantic,
                        info.Name,
                        stage,
                        false);
                    custom.IsActive = true;
                    list.Add(custom);
                    matchedNames.Add(info.Name);
                }
            }

            foreach (var field in list)
            {
                if (!field.IsRequired && !matchedNames.Contains(field.Name))
                    field.IsActive = false;
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Strip the common leading whitespace from parsed code lines so
        /// the block stores only relative indentation.
        /// </summary>
        static string DedentLines(List<string> codeLines)
        {
            if (codeLines.Count == 0) return "";

            int minIndent = int.MaxValue;
            foreach (string raw in codeLines)
            {
                string line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                int spaces = 0;
                foreach (char c in line)
                {
                    if (c == ' ') spaces++;
                    else if (c == '\t') spaces += 4;
                    else break;
                }
                if (spaces < minIndent) minIndent = spaces;
            }

            if (minIndent <= 0 || minIndent == int.MaxValue)
                return string.Join("\n", codeLines).Trim();

            var result = new List<string>();
            foreach (string raw in codeLines)
            {
                string line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    result.Add("");
                    continue;
                }
                int removed = 0;
                int idx = 0;
                while (removed < minIndent && idx < line.Length)
                {
                    if (line[idx] == ' ') { removed++; idx++; }
                    else if (line[idx] == '\t') { removed += 4; idx++; }
                    else break;
                }
                result.Add(line.Substring(idx));
            }

            return string.Join("\n", result).Trim();
        }

        static ShaderSectionType DetermineSection(string[] lines, int blockLine)
        {
            for (int i = blockLine - 1; i >= 0 && i > blockLine - 30; i--)
            {
                var sm = ReSection.Match(lines[i]);
                if (sm.Success)
                {
                    string s = sm.Groups[1].Value.ToLowerInvariant();
                    if (s.Contains("constant")) return ShaderSectionType.Constants;
                    if (s.Contains("vertex"))   return ShaderSectionType.Vertex;
                    if (s.Contains("fragment")) return ShaderSectionType.Fragment;
                    if (s.Contains("helper"))   return ShaderSectionType.Helper;
                }

                // If another block start is found first, stop looking
                if (ReBlockStart.IsMatch(lines[i])) break;
            }

            // Fallback: guess from block title (will be set after parse)
            return ShaderSectionType.Unknown;
        }

        static ShaderPropertyType ParsePropertyType(string raw)
        {
            switch (raw.ToLowerInvariant())
            {
                case "float":     return ShaderPropertyType.Float;
                case "range":     return ShaderPropertyType.Range;
                case "color":     return ShaderPropertyType.Color;
                case "vector":    return ShaderPropertyType.Vector;
                case "2d":
                case "texture2d": return ShaderPropertyType.Texture2D;
                case "3d":
                case "texture3d": return ShaderPropertyType.Texture3D;
                case "cube":
                case "cubemap":   return ShaderPropertyType.Cubemap;
                case "int":
                case "integer":   return ShaderPropertyType.Int;
                default:          return ShaderPropertyType.Float;
            }
        }
    }
}
