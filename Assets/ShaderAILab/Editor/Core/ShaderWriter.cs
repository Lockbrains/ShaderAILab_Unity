using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Writes a ShaderDocument back to a .shader file with AILab metadata tags.
    /// Supports both full regeneration and targeted block replacement.
    /// </summary>
    public static class ShaderWriter
    {
        const string Indent = "            ";

        // ----- Full generation from document model -----

        public static string Generate(ShaderDocument doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Shader \"{doc.ShaderName}\" {{");

            WriteProperties(sb, doc);
            WriteSubShader(sb, doc);

            sb.AppendLine("}");
            return sb.ToString();
        }

        public static void WriteToFile(ShaderDocument doc, string path = null)
        {
            string filePath = path ?? doc.FilePath;
            string content = Generate(doc);
            File.WriteAllText(filePath, content, Encoding.UTF8);
            doc.RawContent = content;
            doc.MarkClean();
        }

        // ----- Targeted block replacement within existing content -----

        public static string ReplaceBlock(ShaderDocument doc, string blockId, string newCode)
        {
            var block = doc.FindBlockById(blockId);
            if (block == null || string.IsNullOrEmpty(doc.RawContent))
                return doc.RawContent;

            string[] lines = doc.RawContent.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
            if (block.StartLine < 0 || block.EndLine >= lines.Length)
                return doc.RawContent;

            var result = new List<string>();

            // Lines before the block's internal code (keep the start tag and metadata lines)
            for (int i = 0; i <= block.StartLine; i++)
                result.Add(lines[i]);

            // Re-emit intent and param tags
            if (!string.IsNullOrEmpty(block.Intent))
                result.Add($"{Indent}// [AILab_Intent: \"{block.Intent}\"]");
            foreach (string p in block.ReferencedParams)
            {
                var prop = doc.FindProperty(p);
                string role = !string.IsNullOrEmpty(prop?.Role) ? prop.Role : "parameter";
                result.Add($"{Indent}// [AILab_Param: \"{p}\" role=\"{role}\"]");
            }

            // New code
            foreach (string codeLine in newCode.Split('\n'))
                result.Add(Indent + codeLine.TrimEnd('\r'));

            // Lines from block end tag onward
            for (int i = block.EndLine; i < lines.Length; i++)
                result.Add(lines[i]);

            return string.Join("\n", result);
        }

        // --------------------------------------------------------
        // Internal generation helpers
        // --------------------------------------------------------

        static void WriteProperties(StringBuilder sb, ShaderDocument doc)
        {
            sb.AppendLine("    Properties {");
            foreach (var p in doc.Properties)
            {
                sb.AppendLine($"        // [AILab_Property: name=\"{p.Name}\" display=\"{p.DisplayName}\" type=\"{PropertyTypeString(p)}\" default=\"{p.DefaultValue}\"{RangeAttrs(p)}]");
                if (!string.IsNullOrEmpty(p.RawDeclaration))
                    sb.AppendLine($"        {p.RawDeclaration}");
                else
                    sb.AppendLine($"        {GeneratePropertyDeclaration(p)}");
            }
            sb.AppendLine("    }");
        }

        static void WriteSubShader(StringBuilder sb, ShaderDocument doc)
        {
            var g = doc.GlobalSettings;
            sb.AppendLine("    SubShader {");
            sb.AppendLine($"        // [AILab_Global: cull=\"{g.CullMode}\" blend=\"{g.BlendMode}\" zwrite=\"{g.ZWriteMode}\"]");
            sb.AppendLine($"        Tags {{ \"RenderType\"=\"{g.RenderType}\" \"Queue\"=\"{g.RenderQueue}\" \"RenderPipeline\"=\"UniversalPipeline\" }}");
            sb.AppendLine($"        Cull {g.CullMode}");
            if (g.BlendMode != "Off")
                sb.AppendLine($"        Blend {g.BlendMode}");
            sb.AppendLine($"        ZWrite {g.ZWriteMode}");
            sb.AppendLine();
            sb.AppendLine("        Pass {");
            sb.AppendLine("            Name \"ForwardLit\"");
            sb.AppendLine("            Tags { \"LightMode\"=\"UniversalForward\" }");
            sb.AppendLine();
            sb.AppendLine("            HLSLPROGRAM");
            sb.AppendLine("            #pragma vertex vert");
            sb.AppendLine("            #pragma fragment frag");
            sb.AppendLine("            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE");
            sb.AppendLine("            #pragma multi_compile _ _ADDITIONAL_LIGHTS");
            sb.AppendLine("            #pragma multi_compile_fragment _ _SHADOWS_SOFT");
            sb.AppendLine("            #pragma multi_compile_fog");
            sb.AppendLine();
            sb.AppendLine("            #include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"");
            sb.AppendLine("            #include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl\"");
            sb.AppendLine();

            WriteCBufferAndSamplers(sb, doc);

            WriteStructs(sb, doc);

            WriteBlocksBySection(sb, doc, ShaderSectionType.Constants, "Constants");
            WriteBlocksBySection(sb, doc, ShaderSectionType.Helper, "Helper Functions");

            // Vertex shader
            sb.AppendLine("            // [AILab_Section: \"Vertex\"]");
            WriteBlocksBySection(sb, doc, ShaderSectionType.Vertex, null);
            WriteVertexMain(sb, doc);

            // Fragment shader
            sb.AppendLine("            // [AILab_Section: \"Fragment\"]");
            WriteBlocksBySection(sb, doc, ShaderSectionType.Fragment, null);
            WriteFragmentMain(sb, doc);

            // Unknown section blocks
            WriteBlocksBySection(sb, doc, ShaderSectionType.Unknown, null);

            sb.AppendLine("            ENDHLSL");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        UsePass \"Universal Render Pipeline/Lit/ShadowCaster\"");
            sb.AppendLine("        UsePass \"Universal Render Pipeline/Lit/DepthOnly\"");
            sb.AppendLine("    }");
            sb.AppendLine("    FallBack \"Hidden/Universal Render Pipeline/FallbackError\"");
        }

        static void WriteCBufferAndSamplers(StringBuilder sb, ShaderDocument doc)
        {
            bool hasTextures = false;
            bool hasCBuffer = false;

            foreach (var p in doc.Properties)
            {
                if (p.PropertyType == ShaderPropertyType.Texture2D ||
                    p.PropertyType == ShaderPropertyType.Texture3D ||
                    p.PropertyType == ShaderPropertyType.Cubemap)
                    hasTextures = true;
                else
                    hasCBuffer = true;
            }

            if (hasTextures)
            {
                foreach (var p in doc.Properties)
                {
                    if (p.PropertyType == ShaderPropertyType.Texture2D)
                    {
                        sb.AppendLine($"            TEXTURE2D({p.Name}); SAMPLER(sampler{p.Name});");
                    }
                    else if (p.PropertyType == ShaderPropertyType.Cubemap)
                    {
                        sb.AppendLine($"            TEXTURECUBE({p.Name}); SAMPLER(sampler{p.Name});");
                    }
                }
                sb.AppendLine();
            }

            if (hasCBuffer)
            {
                sb.AppendLine("            CBUFFER_START(UnityPerMaterial)");
                foreach (var p in doc.Properties)
                {
                    switch (p.PropertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            sb.AppendLine($"                float {p.Name};");
                            break;
                        case ShaderPropertyType.Int:
                            sb.AppendLine($"                int {p.Name};");
                            break;
                        case ShaderPropertyType.Color:
                        case ShaderPropertyType.Vector:
                            sb.AppendLine($"                float4 {p.Name};");
                            break;
                        case ShaderPropertyType.Texture2D:
                            sb.AppendLine($"                float4 {p.Name}_ST;");
                            break;
                    }
                }
                sb.AppendLine("            CBUFFER_END");
                sb.AppendLine();
            }
        }

        static void WriteBlocksBySection(StringBuilder sb, ShaderDocument doc, ShaderSectionType section, string sectionLabel)
        {
            var blocks = doc.GetBlocksBySection(section);
            if (blocks.Count == 0) return;

            if (!string.IsNullOrEmpty(sectionLabel))
                sb.AppendLine($"            // [AILab_Section: \"{sectionLabel}\"]");

            foreach (var b in blocks)
            {
                // Skip blocks that only contain variable declarations
                // (the CBUFFER is auto-generated from doc.Properties)
                if (IsDeclarationOnlyBlock(b.Code))
                {
                    sb.AppendLine($"            // [AILab_Block_Start: \"{b.Title}\"]");
                    sb.AppendLine($"            // [AILab_Intent: \"(auto-skipped: declarations handled by CBUFFER)\"]");
                    sb.AppendLine("            // [AILab_Block_End]");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"            // [AILab_Block_Start: \"{b.Title}\"]");
                if (!string.IsNullOrEmpty(b.Intent))
                    sb.AppendLine($"            // [AILab_Intent: \"{b.Intent}\"]");
                foreach (string p in b.ReferencedParams)
                {
                    var prop = doc.FindProperty(p);
                    string role = !string.IsNullOrEmpty(prop?.Role) ? prop.Role : "parameter";
                    sb.AppendLine($"            // [AILab_Param: \"{p}\" role=\"{role}\"]");
                }

                if (!b.IsEnabled)
                {
                    sb.AppendLine($"            // [AILab_Disabled]");
                    string dedented = DedentCode(b.Code);
                    foreach (string codeLine in dedented.Split('\n'))
                        sb.AppendLine(Indent + "// " + codeLine.TrimEnd('\r'));
                }
                else
                {
                    string dedented = DedentCode(b.Code);
                    foreach (string codeLine in dedented.Split('\n'))
                        sb.AppendLine(Indent + codeLine.TrimEnd('\r'));
                }

                sb.AppendLine("            // [AILab_Block_End]");
                sb.AppendLine();
            }
        }

        static void WriteStructs(StringBuilder sb, ShaderDocument doc)
        {
            var df = doc.DataFlow;

            sb.AppendLine("            struct Attributes {");
            foreach (var f in df.AttributeFields)
            {
                if (!f.IsActive) continue;
                string padded = (f.HLSLType + " " + f.Name).PadRight(18);
                sb.AppendLine($"                {padded} : {f.Semantic};");
            }
            sb.AppendLine("            };");
            sb.AppendLine();

            int texcoordIndex = 0;
            sb.AppendLine("            struct Varyings {");
            foreach (var f in df.VaryingFields)
            {
                if (!f.IsActive) continue;
                string semantic;
                if (!string.IsNullOrEmpty(f.Semantic))
                {
                    semantic = f.Semantic;
                }
                else
                {
                    semantic = $"TEXCOORD{texcoordIndex}";
                    texcoordIndex++;
                }
                string padded = (f.HLSLType + " " + f.Name).PadRight(18);
                sb.AppendLine($"                {padded} : {semantic};");
            }
            sb.AppendLine("            };");
            sb.AppendLine();
        }

        static void WriteVertexMain(StringBuilder sb, ShaderDocument doc)
        {
            var df = doc.DataFlow;
            sb.AppendLine("            Varyings vert(Attributes input) {");
            sb.AppendLine("                Varyings output = (Varyings)0;");

            // Call vertex blocks first (may modify posOS)
            bool hasPositionOS = df.FindField("positionOS", DataFlowStage.Attributes)?.IsActive ?? false;
            if (hasPositionOS)
                sb.AppendLine("                float3 posOS = input.positionOS.xyz;");

            foreach (var b in doc.GetBlocksBySection(ShaderSectionType.Vertex))
            {
                if (!b.IsEnabled) continue;
                if (IsDeclarationOnlyBlock(b.Code)) continue;

                var sig = ExtractFunctionSignature(b.Code);
                if (sig == null) continue;

                // Only auto-call if it's a void function with simple parameters
                string paramStr = sig.Value.parameters.Trim();
                if (sig.Value.returnType == "void" && paramStr.Contains("float3"))
                    sb.AppendLine($"                {sig.Value.name}(posOS);");
            }

            // Emit transform code for each active dependency using a deduplication set
            // so shared helpers (like vpi) are only declared once.
            var emittedDeps = new HashSet<string>();
            bool needsVPI = false;
            bool needsVNI = false;

            foreach (var vf in df.VaryingFields)
            {
                if (!vf.IsActive) continue;
                foreach (var dep in DataFlowRegistry.GetDependencies(vf.Name))
                {
                    var src = df.FindField(dep.SourceFieldName, DataFlowStage.Attributes);
                    if (src == null || !src.IsActive) continue;

                    if (dep.TransformCode.Contains("GetVertexPositionInputs")) needsVPI = true;
                    if (dep.TransformCode.Contains("GetVertexNormalInputs")) needsVNI = true;
                }
            }

            if (needsVPI && hasPositionOS)
                sb.AppendLine("                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);");

            if (needsVNI)
            {
                bool hasTangent = df.FindField("tangentOS", DataFlowStage.Attributes)?.IsActive ?? false;
                if (hasTangent)
                    sb.AppendLine("                VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS, input.tangentOS);");
                else
                    sb.AppendLine("                VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS);");
            }

            // Emit per-field assignments (skip lines that declare vpi/vni since we handled them above)
            foreach (var vf in df.VaryingFields)
            {
                if (!vf.IsActive) continue;
                foreach (var dep in DataFlowRegistry.GetDependencies(vf.Name))
                {
                    string key = $"{dep.SourceFieldName}->{dep.TargetFieldName}";
                    if (emittedDeps.Contains(key)) continue;
                    emittedDeps.Add(key);

                    var src = df.FindField(dep.SourceFieldName, DataFlowStage.Attributes);
                    if (src == null || !src.IsActive) continue;

                    foreach (string line in dep.TransformCode.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("VertexPositionInputs") || trimmed.StartsWith("VertexNormalInputs"))
                            continue;
                        sb.AppendLine("                " + trimmed);
                    }
                }
            }

            sb.AppendLine("                return output;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        static void WriteFragmentMain(StringBuilder sb, ShaderDocument doc)
        {
            sb.AppendLine("            half4 frag(Varyings input) : SV_Target {");
            sb.AppendLine("                half4 finalColor = half4(1,1,1,1);");

            foreach (var b in doc.GetBlocksBySection(ShaderSectionType.Fragment))
            {
                if (!b.IsEnabled) continue;
                if (IsDeclarationOnlyBlock(b.Code)) continue;

                var sig = ExtractFunctionSignature(b.Code);
                if (sig == null) continue;

                // Only call functions that accept (Varyings ...) as their sole parameter
                string paramStr = sig.Value.parameters.Trim();
                bool takesVaryingsOnly = paramStr.StartsWith("Varyings");
                int commaCount = 0;
                foreach (char c in paramStr) if (c == ',') commaCount++;

                if (takesVaryingsOnly && commaCount == 0)
                {
                    string returnType = sig.Value.returnType;
                    string funcName = sig.Value.name;

                    if (returnType == "half4" || returnType == "float4")
                        sb.AppendLine($"                finalColor = {funcName}(input);");
                    else if (returnType == "half3" || returnType == "float3")
                        sb.AppendLine($"                finalColor = half4({funcName}(input), 1.0);");
                    else if (returnType == "void")
                        sb.AppendLine($"                {funcName}(input);");
                }
                // Functions with complex signatures are helper-like — not auto-called
            }

            sb.AppendLine("                return finalColor;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        // --------------------------------------------------------
        // Utility
        // --------------------------------------------------------

        /// <summary>
        /// Remove the common leading whitespace from all lines so code stored
        /// with its original file indentation gets written back cleanly.
        /// </summary>
        static string DedentCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;

            string[] lines = code.Split('\n');
            int minIndent = int.MaxValue;
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                int spaces = 0;
                foreach (char c in trimmed)
                {
                    if (c == ' ') spaces++;
                    else if (c == '\t') spaces += 4;
                    else break;
                }
                if (spaces < minIndent) minIndent = spaces;
            }

            if (minIndent <= 0 || minIndent == int.MaxValue) return code;

            var result = new List<string>();
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add("");
                    continue;
                }
                int removed = 0;
                int idx = 0;
                while (removed < minIndent && idx < trimmed.Length)
                {
                    if (trimmed[idx] == ' ') { removed++; idx++; }
                    else if (trimmed[idx] == '\t') { removed += 4; idx++; }
                    else break;
                }
                result.Add(trimmed.Substring(idx));
            }
            return string.Join("\n", result);
        }

        /// <summary>
        /// Check if a block only contains variable/constant declarations (no function body).
        /// Such blocks are redundant because the CBUFFER is auto-generated from properties.
        /// </summary>
        static bool IsDeclarationOnlyBlock(string code)
        {
            if (string.IsNullOrEmpty(code)) return true;

            // If the code contains a brace-delimited body, it has a function
            if (code.Contains("{")) return false;

            // Check if all non-empty, non-comment lines look like declarations
            foreach (string raw in code.Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//")) continue;

                // Typical declaration: "half _Foo;" or "static const float PI = 3.14;"
                bool looksLikeDecl = line.EndsWith(";") && !line.Contains("(");
                if (!looksLikeDecl) return false;
            }

            return true;
        }

        static string ExtractFunctionName(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            // Match return-type + function-name pattern
            var match = System.Text.RegularExpressions.Regex.Match(code,
                @"\b(void|half4|half3|half2|half|float4|float3|float2|float|int)\s+(\w+)\s*\(");
            if (match.Success)
                return match.Groups[2].Value;

            // Fallback: first identifier before parentheses
            match = System.Text.RegularExpressions.Regex.Match(code, @"(\w+)\s*\(");
            if (!match.Success) return null;

            string candidate = match.Groups[1].Value;
            string[] typeKeywords = { "void", "float", "float2", "float3", "float4",
                "half", "half2", "half3", "half4", "int", "uint",
                "CBUFFER_START", "CBUFFER_END", "TEXTURE2D", "SAMPLER", "if", "for", "while" };
            foreach (string kw in typeKeywords)
            {
                if (candidate == kw) return null;
            }
            return candidate;
        }

        /// <summary>
        /// Extract the full function signature to determine how to call it.
        /// Returns (returnType, funcName, parameterList) or null if not a function.
        /// </summary>
        static (string returnType, string name, string parameters)? ExtractFunctionSignature(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            var match = System.Text.RegularExpressions.Regex.Match(code,
                @"\b(void|half4|half3|half2|half|float4|float3|float2|float|int)\s+(\w+)\s*\(([^)]*)\)");
            if (!match.Success) return null;

            return (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value.Trim());
        }

        static string PropertyTypeString(ShaderProperty p)
        {
            switch (p.PropertyType)
            {
                case ShaderPropertyType.Float:     return "Float";
                case ShaderPropertyType.Range:     return "Range";
                case ShaderPropertyType.Color:     return "Color";
                case ShaderPropertyType.Vector:    return "Vector";
                case ShaderPropertyType.Texture2D: return "Texture2D";
                case ShaderPropertyType.Texture3D: return "Texture3D";
                case ShaderPropertyType.Cubemap:   return "Cubemap";
                case ShaderPropertyType.Int:       return "Int";
                default:                           return "Float";
            }
        }

        static string RangeAttrs(ShaderProperty p)
        {
            if (p.PropertyType == ShaderPropertyType.Range)
                return $" min=\"{p.MinValue}\" max=\"{p.MaxValue}\"";
            return "";
        }

        static string GeneratePropertyDeclaration(ShaderProperty p)
        {
            switch (p.PropertyType)
            {
                case ShaderPropertyType.Float:
                    return $"{p.Name}(\"{p.DisplayName}\", Float) = {p.DefaultValue}";
                case ShaderPropertyType.Range:
                    return $"{p.Name}(\"{p.DisplayName}\", Range({p.MinValue},{p.MaxValue})) = {p.DefaultValue}";
                case ShaderPropertyType.Color:
                    return $"{p.Name}(\"{p.DisplayName}\", Color) = {p.DefaultValue}";
                case ShaderPropertyType.Vector:
                    return $"{p.Name}(\"{p.DisplayName}\", Vector) = {p.DefaultValue}";
                case ShaderPropertyType.Texture2D:
                    return $"{p.Name}(\"{p.DisplayName}\", 2D) = \"white\" {{}}";
                case ShaderPropertyType.Cubemap:
                    return $"{p.Name}(\"{p.DisplayName}\", Cube) = \"\" {{}}";
                case ShaderPropertyType.Int:
                    return $"{p.Name}(\"{p.DisplayName}\", Int) = {p.DefaultValue}";
                default:
                    return $"{p.Name}(\"{p.DisplayName}\", Float) = {p.DefaultValue}";
            }
        }
    }
}
