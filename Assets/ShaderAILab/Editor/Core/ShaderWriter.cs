using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaderAILab.Editor.Core
{
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

            for (int i = 0; i <= block.StartLine; i++)
                result.Add(lines[i]);

            if (!string.IsNullOrEmpty(block.Intent))
                result.Add($"{Indent}// [AILab_Intent: \"{block.Intent}\"]");
            foreach (string p in block.ReferencedParams)
            {
                var prop = doc.FindProperty(p);
                string role = !string.IsNullOrEmpty(prop?.Role) ? prop.Role : "parameter";
                result.Add($"{Indent}// [AILab_Param: \"{p}\" role=\"{role}\"]");
            }

            foreach (string codeLine in newCode.Split('\n'))
                result.Add(Indent + codeLine.TrimEnd('\r'));

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
                sb.AppendLine($"        // [AILab_Property: name=\"{p.Name}\" display=\"{p.DisplayName}\" type=\"{PropertyTypeString(p)}\" default=\"{p.DefaultValue}\"{RangeAttrs(p)}{TextureAttrs(p)}]");
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

            foreach (var pass in doc.Passes)
            {
                if (pass.IsUsePass)
                {
                    sb.AppendLine($"        UsePass \"{pass.UsePassPath}\"");
                }
                else
                {
                    WritePass(sb, doc, pass);
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("    FallBack \"Hidden/Universal Render Pipeline/FallbackError\"");
        }

        static void WritePass(StringBuilder sb, ShaderDocument doc, ShaderPass pass)
        {
            sb.AppendLine($"        // [AILab_Pass: name=\"{pass.Name}\" lightmode=\"{pass.LightMode}\"]");
            sb.AppendLine("        Pass {");
            sb.AppendLine($"            Name \"{pass.Name}\"");
            if (!string.IsNullOrEmpty(pass.LightMode))
                sb.AppendLine($"            Tags {{ \"LightMode\"=\"{pass.LightMode}\" }}");

            if (pass.RenderState != null && pass.RenderState.HasOverrides)
            {
                sb.AppendLine();
                if (!string.IsNullOrEmpty(pass.RenderState.CullMode))
                    sb.AppendLine($"            Cull {pass.RenderState.CullMode}");
                if (!string.IsNullOrEmpty(pass.RenderState.BlendMode) && pass.RenderState.BlendMode != "Off")
                    sb.AppendLine($"            Blend {pass.RenderState.BlendMode}");
                if (!string.IsNullOrEmpty(pass.RenderState.ZWriteMode))
                    sb.AppendLine($"            ZWrite {pass.RenderState.ZWriteMode}");
            }

            sb.AppendLine();
            sb.AppendLine("            HLSLPROGRAM");

            foreach (string pragma in pass.Pragmas)
                sb.AppendLine($"            {pragma}");
            sb.AppendLine();

            foreach (string include in pass.Includes)
                sb.AppendLine($"            #include \"{include}\"");
            sb.AppendLine();

            WriteCBufferAndSamplers(sb, doc);
            WriteStructs(sb, pass);

            WriteBlocksBySection(sb, doc, pass, ShaderSectionType.Constants, "Constants");
            WriteBlocksBySection(sb, doc, pass, ShaderSectionType.Helper, "Helper Functions");

            sb.AppendLine("            // [AILab_Section: \"Vertex\"]");
            WriteBlocksBySection(sb, doc, pass, ShaderSectionType.Vertex, null);
            WriteVertexMain(sb, pass);

            sb.AppendLine("            // [AILab_Section: \"Fragment\"]");
            WriteBlocksBySection(sb, doc, pass, ShaderSectionType.Fragment, null);
            WriteFragmentMain(sb, pass);

            WriteBlocksBySection(sb, doc, pass, ShaderSectionType.Unknown, null);

            sb.AppendLine("            ENDHLSL");
            sb.AppendLine("        }");
            sb.AppendLine();
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
                        sb.AppendLine($"            TEXTURE2D({p.Name}); SAMPLER(sampler{p.Name});");
                    else if (p.PropertyType == ShaderPropertyType.Texture3D)
                        sb.AppendLine($"            TEXTURE3D({p.Name}); SAMPLER(sampler{p.Name});");
                    else if (p.PropertyType == ShaderPropertyType.Cubemap)
                        sb.AppendLine($"            TEXTURECUBE({p.Name}); SAMPLER(sampler{p.Name});");
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
                        case ShaderPropertyType.Texture3D:
                        case ShaderPropertyType.Cubemap:
                            sb.AppendLine($"                float4 {p.Name}_ST;");
                            break;
                    }
                }
                sb.AppendLine("            CBUFFER_END");
                sb.AppendLine();
            }
        }

        static void WriteBlocksBySection(StringBuilder sb, ShaderDocument doc, ShaderPass pass,
            ShaderSectionType section, string sectionLabel)
        {
            var blocks = pass.GetBlocksBySection(section);
            if (blocks.Count == 0) return;

            if (!string.IsNullOrEmpty(sectionLabel))
                sb.AppendLine($"            // [AILab_Section: \"{sectionLabel}\"]");

            foreach (var b in blocks)
            {
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

        static void WriteStructs(StringBuilder sb, ShaderPass pass)
        {
            var df = pass.DataFlow;
            if (df == null) return;

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

        static void WriteVertexMain(StringBuilder sb, ShaderPass pass)
        {
            var df = pass.DataFlow;
            if (df == null) return;

            sb.AppendLine("            Varyings vert(Attributes input) {");
            sb.AppendLine("                Varyings output = (Varyings)0;");

            bool hasPositionOS = df.FindField("positionOS", DataFlowStage.Attributes)?.IsActive ?? false;
            if (hasPositionOS)
                sb.AppendLine("                float3 posOS = input.positionOS.xyz;");

            foreach (var b in pass.GetBlocksBySection(ShaderSectionType.Vertex))
            {
                if (!b.IsEnabled) continue;
                if (IsDeclarationOnlyBlock(b.Code)) continue;

                var sig = ExtractFunctionSignature(b.Code);
                if (sig == null) continue;

                string paramStr = sig.Value.parameters.Trim();
                if (sig.Value.returnType == "void" && paramStr.Contains("float3"))
                    sb.AppendLine($"                {sig.Value.name}(posOS);");
            }

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

        static void WriteFragmentMain(StringBuilder sb, ShaderPass pass)
        {
            sb.AppendLine("            half4 frag(Varyings input) : SV_Target {");
            sb.AppendLine("                half4 finalColor = half4(1,1,1,1);");

            foreach (var b in pass.GetBlocksBySection(ShaderSectionType.Fragment))
            {
                if (!b.IsEnabled) continue;
                if (IsDeclarationOnlyBlock(b.Code)) continue;

                var sig = ExtractFunctionSignature(b.Code);
                if (sig == null) continue;

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
            }

            sb.AppendLine("                return finalColor;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        // --------------------------------------------------------
        // Utility
        // --------------------------------------------------------

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

        static bool IsDeclarationOnlyBlock(string code)
        {
            if (string.IsNullOrEmpty(code)) return true;
            if (code.Contains("{")) return false;

            foreach (string raw in code.Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//")) continue;

                bool looksLikeDecl = line.EndsWith(";") && !line.Contains("(");
                if (!looksLikeDecl) return false;
            }

            return true;
        }

        static (string returnType, string name, string parameters)? ExtractFunctionSignature(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            var match = Regex.Match(code,
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

        static string TextureAttrs(ShaderProperty p)
        {
            if (p.IsTexture && !string.IsNullOrEmpty(p.DefaultTexture))
                return $" defaultTex=\"{p.DefaultTexture}\"";
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
                {
                    string tex = string.IsNullOrEmpty(p.DefaultTexture) ? "white" : p.DefaultTexture;
                    return $"{p.Name}(\"{p.DisplayName}\", 2D) = \"{tex}\" {{}}";
                }
                case ShaderPropertyType.Texture3D:
                {
                    string tex = string.IsNullOrEmpty(p.DefaultTexture) ? "white" : p.DefaultTexture;
                    return $"{p.Name}(\"{p.DisplayName}\", 3D) = \"{tex}\" {{}}";
                }
                case ShaderPropertyType.Cubemap:
                {
                    string tex = string.IsNullOrEmpty(p.DefaultTexture) ? "" : p.DefaultTexture;
                    return $"{p.Name}(\"{p.DisplayName}\", Cube) = \"{tex}\" {{}}";
                }
                case ShaderPropertyType.Int:
                    return $"{p.Name}(\"{p.DisplayName}\", Int) = {p.DefaultValue}";
                default:
                    return $"{p.Name}(\"{p.DisplayName}\", Float) = {p.DefaultValue}";
            }
        }
    }
}
