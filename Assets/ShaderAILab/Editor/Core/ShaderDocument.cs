using System;
using System.Collections.Generic;

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
        public List<ShaderBlock> Blocks;
        public DataFlowGraph DataFlow;
        public string RawContent;
        public DateTime LastModified;
        public bool IsDirty;

        public ShaderDocument()
        {
            FilePath = string.Empty;
            ShaderName = "AILab/NewShader";
            GlobalSettings = new ShaderGlobalSettings();
            Properties = new List<ShaderProperty>();
            Blocks = new List<ShaderBlock>();
            DataFlow = DataFlowGraph.CreateDefault();
            RawContent = string.Empty;
        }

        public ShaderBlock FindBlockByTitle(string title)
        {
            return Blocks.Find(b =>
                string.Equals(b.Title, title, StringComparison.OrdinalIgnoreCase));
        }

        public ShaderBlock FindBlockById(string id)
        {
            return Blocks.Find(b => b.Id == id);
        }

        public List<ShaderBlock> GetBlocksBySection(ShaderSectionType section)
        {
            return Blocks.FindAll(b => b.Section == section);
        }

        public ShaderProperty FindProperty(string name)
        {
            return Properties.Find(p => p.Name == name);
        }

        public void AddBlock(ShaderBlock block)
        {
            Blocks.Add(block);
            IsDirty = true;
        }

        public bool RemoveBlock(string id)
        {
            int removed = Blocks.RemoveAll(b => b.Id == id);
            if (removed > 0) IsDirty = true;
            return removed > 0;
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

        public void MarkClean()
        {
            IsDirty = false;
        }
    }
}
