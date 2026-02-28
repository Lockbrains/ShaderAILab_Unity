using System;
using System.Collections.Generic;
using System.Linq;

namespace ShaderAILab.Editor.Core
{
    [Serializable]
    public class ShaderGlobalSettings
    {
        public string CullMode = "Back";
        public string BlendMode = "Off";
        public string ZWriteMode = "On";
        public string RenderType = "Opaque";
        public string RenderQueue = "Geometry";
    }

    [Serializable]
    public class ShaderDocument
    {
        public string FilePath;
        public string ShaderName;
        public ShaderGlobalSettings GlobalSettings;
        public List<ShaderProperty> Properties;
        public List<ShaderPass> Passes;
        public int ActivePassIndex;
        public string RawContent;
        public DateTime LastModified;
        public bool IsDirty;

        [System.NonSerialized]
        public LLMHistory History = new LLMHistory();

        [System.NonSerialized]
        public ShaderPlan Plan;

        public ShaderDocument()
        {
            FilePath = string.Empty;
            ShaderName = "AILab/NewShader";
            GlobalSettings = new ShaderGlobalSettings();
            Properties = new List<ShaderProperty>();
            Passes = new List<ShaderPass>();
            ActivePassIndex = 0;
            RawContent = string.Empty;
        }

        public ShaderPass ActivePass
        {
            get
            {
                if (Passes.Count == 0) return null;
                if (ActivePassIndex < 0 || ActivePassIndex >= Passes.Count)
                    ActivePassIndex = 0;
                return Passes[ActivePassIndex];
            }
        }

        public DataFlowGraph ActiveDataFlow => ActivePass?.DataFlow;

        // -- Convenience: aggregate blocks across all passes --

        public List<ShaderBlock> AllBlocks => Passes.SelectMany(p => p.Blocks).ToList();

        [Obsolete("Use ActivePass.Blocks or AllBlocks instead.")]
        public List<ShaderBlock> Blocks => ActivePass?.Blocks ?? new List<ShaderBlock>();

        [Obsolete("Use ActivePass.DataFlow instead.")]
        public DataFlowGraph DataFlow
        {
            get => ActiveDataFlow;
            set
            {
                if (ActivePass != null)
                    ActivePass.DataFlow = value;
            }
        }

        // -- Block lookup (searches all passes) --

        public ShaderBlock FindBlockByTitle(string title)
        {
            foreach (var pass in Passes)
            {
                var b = pass.FindBlockByTitle(title);
                if (b != null) return b;
            }
            return null;
        }

        public ShaderBlock FindBlockById(string id)
        {
            foreach (var pass in Passes)
            {
                var b = pass.FindBlockById(id);
                if (b != null) return b;
            }
            return null;
        }

        public ShaderPass FindPassContainingBlock(string blockId)
        {
            foreach (var pass in Passes)
            {
                if (pass.FindBlockById(blockId) != null)
                    return pass;
            }
            return null;
        }

        public List<ShaderBlock> GetBlocksBySection(ShaderSectionType section)
        {
            return ActivePass?.GetBlocksBySection(section) ?? new List<ShaderBlock>();
        }

        // -- Property operations (shader-wide) --

        public ShaderProperty FindProperty(string name)
        {
            return Properties.Find(p => p.Name == name);
        }

        public void AddProperty(ShaderProperty prop)
        {
            Properties.Add(prop);
            IsDirty = true;
        }

        public bool RemoveProperty(string name)
        {
            int removed = Properties.RemoveAll(p => p.Name == name);
            if (removed > 0) IsDirty = true;
            return removed > 0;
        }

        // -- Block operations (on active pass) --

        public void AddBlock(ShaderBlock block)
        {
            if (ActivePass != null)
            {
                ActivePass.AddBlock(block);
                IsDirty = true;
            }
        }

        public bool RemoveBlock(string id)
        {
            foreach (var pass in Passes)
            {
                if (pass.RemoveBlock(id))
                {
                    IsDirty = true;
                    return true;
                }
            }
            return false;
        }

        public void UpdateBlockCode(string id, string newCode)
        {
            var block = FindBlockById(id);
            if (block != null)
            {
                block.Code = newCode;
                IsDirty = true;
            }
        }

        // -- Pass operations --

        public void AddPass(ShaderPass pass)
        {
            Passes.Add(pass);
            IsDirty = true;
        }

        public bool RemovePass(string passId)
        {
            if (Passes.Count <= 1) return false;
            int idx = Passes.FindIndex(p => p.Id == passId);
            if (idx < 0) return false;

            Passes.RemoveAt(idx);
            if (ActivePassIndex >= Passes.Count)
                ActivePassIndex = Passes.Count - 1;
            IsDirty = true;
            return true;
        }

        public void SetActivePass(int index)
        {
            if (index >= 0 && index < Passes.Count)
                ActivePassIndex = index;
        }

        public void SetActivePassById(string passId)
        {
            int idx = Passes.FindIndex(p => p.Id == passId);
            if (idx >= 0) ActivePassIndex = idx;
        }

        public void MovePass(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= Passes.Count) return;
            if (toIndex < 0 || toIndex >= Passes.Count) return;
            if (fromIndex == toIndex) return;

            var pass = Passes[fromIndex];
            Passes.RemoveAt(fromIndex);
            Passes.Insert(toIndex, pass);

            if (ActivePassIndex == fromIndex)
                ActivePassIndex = toIndex;
            else if (fromIndex < ActivePassIndex && toIndex >= ActivePassIndex)
                ActivePassIndex--;
            else if (fromIndex > ActivePassIndex && toIndex <= ActivePassIndex)
                ActivePassIndex++;

            IsDirty = true;
        }

        public void MarkClean()
        {
            IsDirty = false;
        }
    }
}
