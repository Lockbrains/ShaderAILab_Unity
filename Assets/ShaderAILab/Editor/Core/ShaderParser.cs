using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ShaderAILab.Editor.Core
{
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

        static readonly Regex RePassTag =
            new Regex(@"//\s*\[AILab_Pass:\s*(.*?)\]", RegexOptions.Compiled);

        static readonly Regex RePassStart =
            new Regex(@"^\s*Pass\s*\{", RegexOptions.Compiled);

        static readonly Regex RePassName =
            new Regex(@"^\s*Name\s+""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex ReLightModeTag =
            new Regex(@"""LightMode""\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex ReUsePass =
            new Regex(@"^\s*UsePass\s+""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex RePragma =
            new Regex(@"^\s*(#pragma\s+.+)$", RegexOptions.Compiled);

        static readonly Regex ReInclude =
            new Regex(@"^\s*#include\s+""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex ReHLSLPROGRAM =
            new Regex(@"^\s*HLSLPROGRAM\s*$", RegexOptions.Compiled);

        static readonly Regex ReENDHLSL =
            new Regex(@"^\s*ENDHLSL\s*$", RegexOptions.Compiled);

        static readonly Regex ReCull =
            new Regex(@"^\s*Cull\s+(\w+)", RegexOptions.Compiled);

        static readonly Regex ReBlend =
            new Regex(@"^\s*Blend\s+(.+)$", RegexOptions.Compiled);

        static readonly Regex ReZWrite =
            new Regex(@"^\s*ZWrite\s+(\w+)", RegexOptions.Compiled);

        static readonly Regex ReZTest =
            new Regex(@"^\s*ZTest\s+(\w+)", RegexOptions.Compiled);

        static readonly Regex ReColorMask =
            new Regex(@"^\s*ColorMask\s+(\S+)", RegexOptions.Compiled);

        static readonly Regex ReStencilBlock =
            new Regex(@"^\s*Stencil\s*\{", RegexOptions.Compiled);

        static readonly Regex ReStencilKV =
            new Regex(@"^\s*(Ref|Comp|Pass|Fail|ZFail|ReadMask|WriteMask)\s+(\S+)", RegexOptions.Compiled);

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

            var passRanges = DetectPassRanges(lines);

            if (passRanges.Count > 0)
            {
                foreach (var range in passRanges)
                {
                    if (range.IsUsePass)
                    {
                        doc.Passes.Add(ShaderPass.CreateUsePass(range.UsePassPath));
                    }
                    else
                    {
                        var pass = ParseSinglePass(lines, range);
                        doc.Passes.Add(pass);
                    }
                }
            }
            else
            {
                var fallbackPass = ShaderPass.CreateForwardLit();
                ParseBlocksIntoPass(lines, 0, lines.Length - 1, fallbackPass);
                ParseStructsIntoPass(lines, 0, lines.Length - 1, fallbackPass);
                doc.Passes.Add(fallbackPass);
            }

            return doc;
        }

        // -------------------------------------------------------------------
        // Pass range detection
        // -------------------------------------------------------------------

        struct PassRange
        {
            public int StartLine;
            public int EndLine;
            public bool IsUsePass;
            public string UsePassPath;
        }

        static List<PassRange> DetectPassRanges(string[] lines)
        {
            var ranges = new List<PassRange>();

            for (int i = 0; i < lines.Length; i++)
            {
                var usePassMatch = ReUsePass.Match(lines[i]);
                if (usePassMatch.Success)
                {
                    ranges.Add(new PassRange
                    {
                        StartLine = i,
                        EndLine = i,
                        IsUsePass = true,
                        UsePassPath = usePassMatch.Groups[1].Value
                    });
                    continue;
                }

                if (RePassStart.IsMatch(lines[i]))
                {
                    int braceCount = 0;
                    int start = i;
                    for (int j = i; j < lines.Length; j++)
                    {
                        foreach (char c in lines[j])
                        {
                            if (c == '{') braceCount++;
                            else if (c == '}') braceCount--;
                        }
                        if (braceCount <= 0)
                        {
                            ranges.Add(new PassRange
                            {
                                StartLine = start,
                                EndLine = j,
                                IsUsePass = false,
                                UsePassPath = null
                            });
                            i = j;
                            break;
                        }
                    }
                }
            }

            return ranges;
        }

        // -------------------------------------------------------------------
        // Single pass parsing
        // -------------------------------------------------------------------

        static ShaderPass ParseSinglePass(string[] lines, PassRange range)
        {
            var pass = new ShaderPass();
            string pendingPassTagName = null;
            string pendingPassTagLightMode = null;

            for (int i = Math.Max(0, range.StartLine - 3); i <= range.StartLine; i++)
            {
                var ptm = RePassTag.Match(lines[i]);
                if (ptm.Success)
                {
                    foreach (Match kv in ReKV.Matches(ptm.Groups[1].Value))
                    {
                        string key = kv.Groups[1].Value.ToLowerInvariant();
                        string val = kv.Groups[2].Value;
                        switch (key)
                        {
                            case "name": pendingPassTagName = val; break;
                            case "lightmode": pendingPassTagLightMode = val; break;
                        }
                    }
                }
            }

            bool inHLSL = false;
            bool hasPerPassRenderState = false;
            string perPassCull = null, perPassBlend = null, perPassZWrite = null;
            string perPassZTest = null, perPassColorMask = null;
            StencilState perPassStencil = null;

            for (int i = range.StartLine; i <= range.EndLine; i++)
            {
                if (ReHLSLPROGRAM.IsMatch(lines[i])) { inHLSL = true; continue; }
                if (ReENDHLSL.IsMatch(lines[i])) { inHLSL = false; continue; }

                if (!inHLSL)
                {
                    var nameMatch = RePassName.Match(lines[i]);
                    if (nameMatch.Success && string.IsNullOrEmpty(pendingPassTagName))
                        pass.Name = nameMatch.Groups[1].Value;

                    var lmMatch = ReLightModeTag.Match(lines[i]);
                    if (lmMatch.Success && string.IsNullOrEmpty(pendingPassTagLightMode))
                        pass.LightMode = lmMatch.Groups[1].Value;

                    var cullMatch = ReCull.Match(lines[i]);
                    if (cullMatch.Success) { perPassCull = cullMatch.Groups[1].Value; hasPerPassRenderState = true; }

                    var blendMatch = ReBlend.Match(lines[i]);
                    if (blendMatch.Success) { perPassBlend = blendMatch.Groups[1].Value.Trim(); hasPerPassRenderState = true; }

                    var zwMatch = ReZWrite.Match(lines[i]);
                    if (zwMatch.Success) { perPassZWrite = zwMatch.Groups[1].Value; hasPerPassRenderState = true; }

                    var ztMatch = ReZTest.Match(lines[i]);
                    if (ztMatch.Success) { perPassZTest = ztMatch.Groups[1].Value; hasPerPassRenderState = true; }

                    var cmMatch = ReColorMask.Match(lines[i]);
                    if (cmMatch.Success) { perPassColorMask = cmMatch.Groups[1].Value; hasPerPassRenderState = true; }

                    if (ReStencilBlock.IsMatch(lines[i]))
                    {
                        perPassStencil = new StencilState();
                        hasPerPassRenderState = true;
                        for (int j = i + 1; j <= range.EndLine; j++)
                        {
                            string stLine = lines[j].Trim();
                            if (stLine.StartsWith("}")) { i = j; break; }

                            var skv = ReStencilKV.Match(lines[j]);
                            if (skv.Success)
                            {
                                string skey = skv.Groups[1].Value;
                                string sval = skv.Groups[2].Value;
                                switch (skey)
                                {
                                    case "Ref":       if (int.TryParse(sval, out int r)) perPassStencil.Ref = r; break;
                                    case "Comp":      perPassStencil.Comp = sval; break;
                                    case "Pass":      perPassStencil.Pass = sval; break;
                                    case "Fail":      perPassStencil.Fail = sval; break;
                                    case "ZFail":     perPassStencil.ZFail = sval; break;
                                    case "ReadMask":  if (int.TryParse(sval, out int rm)) perPassStencil.ReadMask = rm; break;
                                    case "WriteMask": if (int.TryParse(sval, out int wm)) perPassStencil.WriteMask = wm; break;
                                }
                            }
                        }
                    }
                }

                if (inHLSL)
                {
                    var pragmaMatch = RePragma.Match(lines[i]);
                    if (pragmaMatch.Success)
                    {
                        pass.Pragmas.Add(pragmaMatch.Groups[1].Value.Trim());
                        continue;
                    }

                    var includeMatch = ReInclude.Match(lines[i]);
                    if (includeMatch.Success)
                    {
                        pass.Includes.Add(includeMatch.Groups[1].Value);
                        continue;
                    }
                }
            }

            if (!string.IsNullOrEmpty(pendingPassTagName))
                pass.Name = pendingPassTagName;
            if (!string.IsNullOrEmpty(pendingPassTagLightMode))
                pass.LightMode = pendingPassTagLightMode;

            if (hasPerPassRenderState)
            {
                var rs = new PassRenderState(perPassCull, perPassBlend, perPassZWrite);
                rs.ZTestMode = perPassZTest;
                rs.ColorMask = perPassColorMask;
                rs.Stencil = perPassStencil;
                pass.RenderState = rs;
            }

            ParseBlocksIntoPass(lines, range.StartLine, range.EndLine, pass);
            ParseStructsIntoPass(lines, range.StartLine, range.EndLine, pass);

            return pass;
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
                        case "name":       prop.Name           = val; break;
                        case "display":    prop.DisplayName    = val; break;
                        case "type":       prop.PropertyType   = ParsePropertyType(val); break;
                        case "default":    prop.DefaultValue   = val; break;
                        case "min":        if (float.TryParse(val, out float mn)) prop.MinValue = mn; break;
                        case "max":        if (float.TryParse(val, out float mx)) prop.MaxValue = mx; break;
                        case "role":       prop.Role           = val; break;
                        case "defaulttex": prop.DefaultTexture = val; break;
                    }
                }

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
        // Blocks (within a line range)
        // -------------------------------------------------------------------

        static void ParseBlocksIntoPass(string[] lines, int rangeStart, int rangeEnd, ShaderPass pass)
        {
            ShaderSectionType currentSection = ShaderSectionType.Unknown;
            int i = rangeStart;

            while (i <= rangeEnd)
            {
                var secMatch = ReSection.Match(lines[i]);
                if (secMatch.Success)
                {
                    currentSection = ParseSectionName(secMatch.Groups[1].Value);
                    i++;
                    continue;
                }

                var sm = ReBlockStart.Match(lines[i]);
                if (!sm.Success) { i++; continue; }

                var block = new ShaderBlock
                {
                    Title = sm.Groups[1].Value,
                    StartLine = i,
                    Section = currentSection
                };

                var codeLines = new List<string>();
                bool isDisabled = false;
                i++;

                while (i <= rangeEnd)
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
                pass.Blocks.Add(block);
            }
        }

        // -------------------------------------------------------------------
        // Struct parsing -> DataFlowGraph (within a line range)
        // -------------------------------------------------------------------

        static void ParseStructsIntoPass(string[] lines, int rangeStart, int rangeEnd, ShaderPass pass)
        {
            var graph = DataFlowGraph.CreateDefault();

            var parsedAttrs = new List<StructFieldInfo>();
            var parsedVarys = new List<StructFieldInfo>();
            string currentStruct = null;

            for (int i = rangeStart; i <= rangeEnd; i++)
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

            pass.DataFlow = graph;
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

        static ShaderSectionType ParseSectionName(string sectionName)
        {
            string s = sectionName.ToLowerInvariant();
            if (s.Contains("constant")) return ShaderSectionType.Constants;
            if (s.Contains("vertex"))   return ShaderSectionType.Vertex;
            if (s.Contains("fragment")) return ShaderSectionType.Fragment;
            if (s.Contains("helper"))   return ShaderSectionType.Helper;
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
