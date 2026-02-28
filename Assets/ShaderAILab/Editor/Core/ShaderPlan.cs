using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShaderAILab.Editor.Core
{
    public enum PlanStatus
    {
        Empty,
        Decomposing,
        Refining,
        Ready,
        Executing,
        Completed,
        Failed
    }

    public enum PhaseStatus
    {
        Pending,
        WaitingForUser,
        Confirmed,
        Executing,
        Done,
        Skipped
    }

    public enum PlanPhaseType
    {
        VisualAnalysis,
        DataFlow,
        Textures,
        VertexShader,
        FragmentShader,
        ShaderOptions,
        MultiPass
    }

    [Serializable]
    public class PlanItem
    {
        public string Key;
        public string Description;
        public string Detail;
        public bool IsConfirmed;
        public string Category;

        public PlanItem() { }

        public PlanItem(string key, string description, string detail, string category = "")
        {
            Key = key;
            Description = description;
            Detail = detail;
            Category = category;
            IsConfirmed = false;
        }
    }

    [Serializable]
    public class PlanPhase
    {
        public string Id;
        public string Title;
        public PlanPhaseType Type;
        public string LLMProposal;
        public string LLMQuestion;
        public string UserResponse;
        public PhaseStatus Status;
        public List<PlanItem> Items;
        public int RefinementCount;

        public PlanPhase()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Items = new List<PlanItem>();
            Status = PhaseStatus.Pending;
        }

        public PlanPhase(string title, PlanPhaseType type) : this()
        {
            Title = title;
            Type = type;
        }

        public bool IsTerminal => Status == PhaseStatus.Confirmed
                               || Status == PhaseStatus.Done
                               || Status == PhaseStatus.Skipped;
    }

    [Serializable]
    public class ShaderPlan
    {
        public string Id;
        public string UserRequest;
        public PlanStatus Status;
        public List<PlanPhase> Phases;
        public DateTime CreatedAt;
        public DateTime LastModified;

        [NonSerialized]
        public Action OnPlanChanged;

        public ShaderPlan()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Phases = new List<PlanPhase>();
            Status = PlanStatus.Empty;
            CreatedAt = DateTime.Now;
            LastModified = DateTime.Now;
        }

        public ShaderPlan(string userRequest) : this()
        {
            UserRequest = userRequest;
        }

        public bool AllPhasesHandled
        {
            get
            {
                if (Phases.Count == 0) return false;
                foreach (var phase in Phases)
                {
                    if (!phase.IsTerminal) return false;
                }
                return true;
            }
        }

        public int ConfirmedCount
        {
            get
            {
                int count = 0;
                foreach (var phase in Phases)
                {
                    if (phase.IsTerminal) count++;
                }
                return count;
            }
        }

        public PlanPhase FindPhaseById(string phaseId)
        {
            return Phases.Find(p => p.Id == phaseId);
        }

        public PlanPhase FindPhaseByType(PlanPhaseType type)
        {
            return Phases.Find(p => p.Type == type);
        }

        public void NotifyChanged()
        {
            LastModified = DateTime.Now;
            OnPlanChanged?.Invoke();
        }

        public static string GetPhaseTypeLabel(PlanPhaseType type)
        {
            switch (type)
            {
                case PlanPhaseType.VisualAnalysis: return "Visual Analysis";
                case PlanPhaseType.DataFlow:       return "Data Flow";
                case PlanPhaseType.Textures:       return "Textures & Resources";
                case PlanPhaseType.VertexShader:    return "Vertex Shader";
                case PlanPhaseType.FragmentShader:  return "Fragment Shader";
                case PlanPhaseType.ShaderOptions:   return "Shader Options";
                case PlanPhaseType.MultiPass:       return "Multi-Pass";
                default:                            return type.ToString();
            }
        }

        public static string GetPhaseTypeIcon(PlanPhaseType type)
        {
            switch (type)
            {
                case PlanPhaseType.VisualAnalysis: return "\u25c9";
                case PlanPhaseType.DataFlow:       return "\u21c4";
                case PlanPhaseType.Textures:       return "\u25a3";
                case PlanPhaseType.VertexShader:    return "\u25b2";
                case PlanPhaseType.FragmentShader:  return "\u25cf";
                case PlanPhaseType.ShaderOptions:   return "\u2699";
                case PlanPhaseType.MultiPass:       return "\u229a";
                default:                            return "\u25cb";
            }
        }

        // ---- Persistence ----

        const string PlanFolderName = ".ailab_history";

        static string GetPlanFilePath(string shaderPath)
        {
            if (string.IsNullOrEmpty(shaderPath)) return null;
            string dir = Path.GetDirectoryName(shaderPath);
            string planDir = Path.Combine(dir, PlanFolderName);
            string fileName = Path.GetFileNameWithoutExtension(shaderPath) + "_plan.json";
            return Path.Combine(planDir, fileName);
        }

        public void SaveToFile(string shaderPath)
        {
            string path = GetPlanFilePath(shaderPath);
            if (path == null) return;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ShaderAILab] Failed to save plan: {ex.Message}");
            }
        }

        public static ShaderPlan LoadFromFile(string shaderPath)
        {
            string path = GetPlanFilePath(shaderPath);
            if (path == null || !File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                var plan = JsonUtility.FromJson<ShaderPlan>(json);
                if (plan != null && plan.Status != PlanStatus.Empty && plan.Phases != null && plan.Phases.Count > 0)
                {
                    if (plan.Status == PlanStatus.Decomposing || plan.Status == PlanStatus.Executing)
                        plan.Status = PlanStatus.Refining;

                    foreach (var phase in plan.Phases)
                    {
                        if (phase.Status == PhaseStatus.Executing)
                            phase.Status = PhaseStatus.Confirmed;
                    }

                    return plan;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ShaderAILab] Failed to load plan: {ex.Message}");
            }

            return null;
        }

        public static void DeleteFile(string shaderPath)
        {
            string path = GetPlanFilePath(shaderPath);
            if (path != null && File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex) { Debug.LogWarning($"[ShaderAILab] Failed to delete plan file: {ex.Message}"); }
            }
        }
    }
}
