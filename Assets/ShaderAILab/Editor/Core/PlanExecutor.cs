using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ShaderAILab.Editor.LLM;

namespace ShaderAILab.Editor.Core
{
    public class PlanExecutor
    {
        const int MaxRetries = 2;
        const int RetryDelayMs = 3000;
        const int InterPhaseDelayMs = 2500;

        public event Action<PlanPhaseType, string> OnPhaseExecuting;
        public event Action<PlanPhaseType, bool, string> OnPhaseCompleted;
        public event Action OnPlanCompleted;

        static readonly PlanPhaseType[] ExecutionOrder =
        {
            PlanPhaseType.DataFlow,
            PlanPhaseType.ShaderOptions,
            PlanPhaseType.Textures,
            PlanPhaseType.MultiPass,
            PlanPhaseType.VertexShader
        };

        public void ClearExistingBlocks(ShaderDocument doc)
        {
            var passIdsToRemove = new List<string>();
            for (int i = doc.Passes.Count - 1; i >= 0; i--)
            {
                var pass = doc.Passes[i];
                if (pass.IsUsePass) continue;

                if (i == doc.ActivePassIndex)
                {
                    var blockIds = new List<string>();
                    foreach (var block in pass.Blocks)
                        blockIds.Add(block.Id);
                    foreach (string id in blockIds)
                        pass.RemoveBlock(id);
                }
                else
                {
                    passIdsToRemove.Add(pass.Id);
                }
            }

            foreach (string passId in passIdsToRemove)
                doc.RemovePass(passId);

            doc.IsDirty = true;
        }

        public async Task ExecuteAsync(ShaderDocument doc, ShaderPlan plan)
        {
            ClearExistingBlocks(doc);

            bool hadLLMCall = false;

            foreach (var phaseType in ExecutionOrder)
            {
                var phase = plan.FindPhaseByType(phaseType);
                if (phase == null || phase.Status == PhaseStatus.Skipped)
                    continue;

                bool requiresLLM = phaseType == PlanPhaseType.MultiPass
                                || phaseType == PlanPhaseType.VertexShader;

                if (requiresLLM && hadLLMCall)
                    await Task.Delay(InterPhaseDelayMs);

                phase.Status = PhaseStatus.Executing;
                string label = ShaderPlan.GetPhaseTypeLabel(phaseType);
                OnPhaseExecuting?.Invoke(phaseType, $"Executing: {label}...");

                try
                {
                    bool success = false;

                    switch (phaseType)
                    {
                        case PlanPhaseType.DataFlow:
                            success = ExecuteDataFlowPhase(doc, phase);
                            break;
                        case PlanPhaseType.ShaderOptions:
                            success = ExecuteShaderOptionsPhase(doc, phase);
                            break;
                        case PlanPhaseType.Textures:
                            success = ExecuteTexturesPhase(doc, phase);
                            break;
                        case PlanPhaseType.MultiPass:
                            success = await ExecuteWithRetry(() => ExecuteMultiPassPhase(doc, plan, phase), label);
                            hadLLMCall = true;
                            break;
                        case PlanPhaseType.VertexShader:
                            success = await ExecuteWithRetry(() => ExecuteMainPassCode(doc, plan), "Main Pass Code");
                            hadLLMCall = true;
                            var fragPhase = plan.FindPhaseByType(PlanPhaseType.FragmentShader);
                            if (fragPhase != null)
                                fragPhase.Status = success ? PhaseStatus.Done : PhaseStatus.Confirmed;
                            break;
                    }

                    phase.Status = success ? PhaseStatus.Done : PhaseStatus.Confirmed;
                    string msg = success
                        ? $"{label} completed successfully."
                        : $"{label} completed with warnings.";
                    OnPhaseCompleted?.Invoke(phaseType, success, msg);
                }
                catch (Exception ex)
                {
                    phase.Status = PhaseStatus.Confirmed;
                    OnPhaseCompleted?.Invoke(phaseType, false, $"{label} failed: {ex.Message}");
                    Debug.LogError($"[ShaderAILab] PlanExecutor {label} error: {ex}");
                    if (requiresLLM) hadLLMCall = true;
                }

                plan.NotifyChanged();
            }

            var fragPhaseCheck = plan.FindPhaseByType(PlanPhaseType.FragmentShader);
            bool fragNeedsExecution = fragPhaseCheck != null
                && fragPhaseCheck.Status != PhaseStatus.Skipped
                && fragPhaseCheck.Status != PhaseStatus.Done;
            var vertPhaseCheck = plan.FindPhaseByType(PlanPhaseType.VertexShader);
            bool vertWasSkipped = vertPhaseCheck == null || vertPhaseCheck.Status == PhaseStatus.Skipped;

            if (fragNeedsExecution && vertWasSkipped)
            {
                if (hadLLMCall) await Task.Delay(InterPhaseDelayMs);
                fragPhaseCheck.Status = PhaseStatus.Executing;
                OnPhaseExecuting?.Invoke(PlanPhaseType.FragmentShader, "Executing: Main Pass Code...");
                try
                {
                    bool success = await ExecuteWithRetry(() => ExecuteMainPassCode(doc, plan), "Main Pass Code");
                    fragPhaseCheck.Status = success ? PhaseStatus.Done : PhaseStatus.Confirmed;
                    OnPhaseCompleted?.Invoke(PlanPhaseType.FragmentShader, success,
                        success ? "Main pass code completed." : "Main pass code completed with warnings.");
                }
                catch (Exception ex)
                {
                    fragPhaseCheck.Status = PhaseStatus.Confirmed;
                    OnPhaseCompleted?.Invoke(PlanPhaseType.FragmentShader, false, $"Main pass code failed: {ex.Message}");
                    Debug.LogError($"[ShaderAILab] PlanExecutor Main Pass Code error: {ex}");
                }
                plan.NotifyChanged();
            }

            OnPlanCompleted?.Invoke();
        }

        async Task<bool> ExecuteWithRetry(Func<Task<bool>> action, string label)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    bool isCanceled = ex.Message.Contains("canceled") || ex.Message.Contains("cancelled");
                    if (!isCanceled || attempt >= MaxRetries)
                        throw;

                    int delay = RetryDelayMs * (attempt + 1);
                    Debug.LogWarning($"[ShaderAILab] {label} attempt {attempt + 1} canceled, retrying in {delay}ms...");
                    await Task.Delay(delay);
                }
            }
            return false;
        }

        bool ExecuteDataFlowPhase(ShaderDocument doc, PlanPhase phase)
        {
            if (doc.ActivePass == null) return false;
            var dataFlow = doc.ActivePass.DataFlow;
            if (dataFlow == null) return false;

            int activated = 0;
            foreach (var item in phase.Items)
            {
                if (!item.IsConfirmed && phase.Status != PhaseStatus.Confirmed)
                    continue;

                string fieldName = item.Key;
                if (string.IsNullOrEmpty(fieldName)) continue;

                string category = (item.Category ?? "").ToLowerInvariant();
                if (category.Contains("varying") || category == "")
                {
                    var field = dataFlow.FindField(fieldName, DataFlowStage.Varyings);
                    if (field != null && !field.IsActive)
                    {
                        dataFlow.ActivateVaryingWithDependencies(fieldName);
                        if (!string.IsNullOrEmpty(item.Description))
                            field.Annotation = item.Description;
                        activated++;
                    }
                }
                else if (category.Contains("attribute"))
                {
                    var field = dataFlow.FindField(fieldName, DataFlowStage.Attributes);
                    if (field != null && !field.IsActive)
                    {
                        field.IsActive = true;
                        activated++;
                    }
                }
            }

            doc.IsDirty = true;
            Debug.Log($"[ShaderAILab] DataFlow phase: activated {activated} field(s).");
            return activated > 0;
        }

        bool ExecuteShaderOptionsPhase(ShaderDocument doc, PlanPhase phase)
        {
            if (doc.ActivePass == null) return false;

            bool changed = false;

            foreach (var item in phase.Items)
            {
                if (!item.IsConfirmed && phase.Status != PhaseStatus.Confirmed)
                    continue;

                string key = (item.Key ?? "").ToLowerInvariant();
                string value = SanitizeOptionValue(item.Detail ?? item.Description ?? "");

                if (string.IsNullOrEmpty(value)) continue;

                if (doc.ActivePass.RenderState == null)
                    doc.ActivePass.RenderState = new PassRenderState();

                switch (key)
                {
                    case "cull":
                    case "cullmode":
                        doc.ActivePass.RenderState.CullMode = value;
                        changed = true;
                        break;
                    case "blend":
                    case "blendmode":
                        doc.ActivePass.RenderState.BlendMode = value;
                        changed = true;
                        break;
                    case "zwrite":
                    case "zwritemode":
                        doc.ActivePass.RenderState.ZWriteMode = value;
                        changed = true;
                        break;
                    case "ztest":
                    case "ztestmode":
                        doc.ActivePass.RenderState.ZTestMode = value;
                        changed = true;
                        break;
                    case "colormask":
                        doc.ActivePass.RenderState.ColorMask = value;
                        changed = true;
                        break;
                    case "renderqueue":
                    case "queue":
                        doc.GlobalSettings.RenderQueue = SanitizeTagValue(value);
                        changed = true;
                        break;
                    case "rendertype":
                        doc.GlobalSettings.RenderType = SanitizeTagValue(value);
                        changed = true;
                        break;
                }
            }

            if (changed) doc.IsDirty = true;
            return changed;
        }

        static readonly System.Text.RegularExpressions.Regex ReNonAscii =
            new System.Text.RegularExpressions.Regex(@"[^\x00-\x7F]", System.Text.RegularExpressions.RegexOptions.Compiled);

        static readonly HashSet<string> ValidRenderStateTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "On", "Off", "Back", "Front",
            "One", "Zero", "SrcAlpha", "OneMinusSrcAlpha", "DstAlpha", "OneMinusDstAlpha",
            "SrcColor", "OneMinusSrcColor", "DstColor", "OneMinusDstColor",
            "Always", "LEqual", "Less", "GEqual", "Greater", "Equal", "NotEqual", "Never",
            "Keep", "Replace", "IncrSat", "DecrSat", "Invert", "IncrWrap", "DecrWrap",
            "Opaque", "Transparent", "TransparentCutout", "Background", "Geometry",
            "AlphaTest", "Overlay",
            "0", "R", "G", "B", "A", "RG", "RB", "GB", "RGB", "RGBA"
        };

        static string SanitizeOptionValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim().Trim('"');
            if (ReNonAscii.IsMatch(value))
                value = ReNonAscii.Replace(value, "").Trim();
            int braceIdx = value.IndexOf('{');
            if (braceIdx >= 0) value = value.Substring(0, braceIdx).Trim();
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            string first = parts[0];
            if (ValidRenderStateTokens.Contains(first))
                return parts.Length <= 2 ? value.Trim() : first;
            foreach (string token in parts)
                if (ValidRenderStateTokens.Contains(token)) return token;
            return null;
        }

        static string SanitizeTagValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Opaque";
            value = value.Trim().Trim('"');
            if (ReNonAscii.IsMatch(value))
                value = ReNonAscii.Replace(value, "").Trim();
            if (value.Contains("Tags") || value.Contains("{") || value.Contains("="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(value, @"""([^""]+)""");
                if (match.Success) return match.Groups[1].Value;
            }
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Opaque";
            string first = parts[0];
            if (ValidRenderStateTokens.Contains(first)) return first;
            return first;
        }

        bool ExecuteTexturesPhase(ShaderDocument doc, PlanPhase phase)
        {
            int added = 0;

            foreach (var item in phase.Items)
            {
                if (!item.IsConfirmed && phase.Status != PhaseStatus.Confirmed)
                    continue;

                string propName = item.Key;
                if (string.IsNullOrEmpty(propName)) continue;
                if (!propName.StartsWith("_"))
                    propName = "_" + propName;

                if (doc.FindProperty(propName) != null)
                    continue;

                string displayName = item.Description ?? propName.Substring(1);
                string defaultTex = "white";

                string detail = (item.Detail ?? "").ToLowerInvariant();
                if (detail.Contains("normal")) defaultTex = "bump";
                else if (detail.Contains("black") || detail.Contains("mask")) defaultTex = "black";

                var prop = new ShaderProperty
                {
                    Name = propName,
                    DisplayName = displayName,
                    PropertyType = ShaderPropertyType.Texture2D,
                    DefaultTexture = defaultTex,
                    RawDeclaration = $"{propName}(\"{displayName}\", 2D) = \"{defaultTex}\" {{}}"
                };

                doc.AddProperty(prop);
                added++;
            }

            if (added > 0)
                Debug.Log($"[ShaderAILab] Textures phase: added {added} texture property(ies).");

            return added > 0;
        }

        async Task<bool> ExecuteMultiPassPhase(ShaderDocument doc, ShaderPlan plan, PlanPhase phase)
        {
            bool hasContent = false;
            foreach (var item in phase.Items)
            {
                if (!item.IsConfirmed && phase.Status != PhaseStatus.Confirmed)
                    continue;
                if (!string.IsNullOrEmpty(item.Key))
                    hasContent = true;
            }

            if (!hasContent) return false;

            string systemPrompt = PromptTemplates.BuildPlanExecutionSystemPrompt(plan, PlanPhaseType.MultiPass, doc);
            string userPrompt = PromptTemplates.BuildPlanExecutionUserPrompt(plan, PlanPhaseType.MultiPass);

            var llmService = LLMService.Instance;
            string response = await llmService.GenerateAsync(systemPrompt, userPrompt);
            string code = PromptTemplates.ExtractCodeFromResponse(response);

            if (string.IsNullOrEmpty(code)) return false;

            var parsed = PromptTemplates.ParseFullResponse(code, ShaderSectionType.Fragment);
            MergeNewProperties(doc, parsed.Properties);

            if (parsed.PassInfo != null && parsed.Blocks.Count > 0)
            {
                var passInfo = parsed.PassInfo;
                string passName = !string.IsNullOrEmpty(passInfo.Name) ? passInfo.Name : "NewPass";
                string lightMode = !string.IsNullOrEmpty(passInfo.LightMode) ? passInfo.LightMode : "SRPDefaultUnlit";

                var newPass = new ShaderPass(passName, lightMode);
                newPass.Pragmas.AddRange(new[] { "#pragma vertex vert", "#pragma fragment frag" });
                newPass.Includes.Add("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl");

                if (passInfo.CullOverride != null || passInfo.BlendOverride != null || passInfo.ZWriteOverride != null)
                {
                    newPass.RenderState = new PassRenderState(
                        passInfo.CullOverride, passInfo.BlendOverride, passInfo.ZWriteOverride);
                    if (!string.IsNullOrEmpty(passInfo.ZTestOverride))
                        newPass.RenderState.ZTestMode = passInfo.ZTestOverride;
                    if (!string.IsNullOrEmpty(passInfo.ColorMaskOverride))
                        newPass.RenderState.ColorMask = passInfo.ColorMaskOverride;
                    if (passInfo.StencilOverride != null)
                        newPass.RenderState.Stencil = passInfo.StencilOverride;
                }

                foreach (var pb in parsed.Blocks)
                {
                    var block = new ShaderBlock(pb.Title, pb.Intent ?? "", pb.Section);
                    block.Code = pb.Code;
                    foreach (var p in pb.ReferencedParams)
                        block.ReferencedParams.Add(p);
                    newPass.AddBlock(block);
                }

                doc.AddPass(newPass);
                Debug.Log($"[ShaderAILab] MultiPass phase: created pass \"{passName}\" with {parsed.Blocks.Count} block(s).");
                return true;
            }

            return false;
        }

        async Task<bool> ExecuteMainPassCode(ShaderDocument doc, ShaderPlan plan)
        {
            string systemPrompt = PromptTemplates.BuildMainPassExecutionSystemPrompt(plan, doc);
            string userPrompt = PromptTemplates.BuildMainPassExecutionUserPrompt(plan);

            var llmService = LLMService.Instance;
            string response = await llmService.GenerateAsync(systemPrompt, userPrompt);
            string code = PromptTemplates.ExtractCodeFromResponse(response);

            if (string.IsNullOrEmpty(code)) return false;

            var parsed = PromptTemplates.ParseFullResponse(code, ShaderSectionType.Fragment);
            MergeNewProperties(doc, parsed.Properties);

            int blockCount = 0;

            foreach (var pb in parsed.Blocks)
            {
                var block = new ShaderBlock(pb.Title, pb.Intent ?? "", pb.Section);
                block.Code = pb.Code;
                foreach (var p in pb.ReferencedParams)
                    block.ReferencedParams.Add(p);
                doc.AddBlock(block);
                blockCount++;
            }

            if (blockCount == 0 && !string.IsNullOrEmpty(parsed.LeftoverCode))
            {
                var block = new ShaderBlock("Main Fragment", "", ShaderSectionType.Fragment);
                block.Code = parsed.LeftoverCode;
                doc.AddBlock(block);
                blockCount = 1;
            }

            if (blockCount > 0)
            {
                doc.IsDirty = true;
                Debug.Log($"[ShaderAILab] Main pass code: added {blockCount} block(s).");
                return true;
            }

            return false;
        }

        static void MergeNewProperties(ShaderDocument doc, List<ShaderProperty> newProps)
        {
            if (newProps == null) return;
            foreach (var prop in newProps)
            {
                if (string.IsNullOrEmpty(prop.Name)) continue;
                if (doc.FindProperty(prop.Name) != null) continue;
                doc.AddProperty(prop);
            }
        }
    }
}
