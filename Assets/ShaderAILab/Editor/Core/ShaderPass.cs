using System;
using System.Collections.Generic;

namespace ShaderAILab.Editor.Core
{
    [Serializable]
    public class PassRenderState
    {
        public string CullMode;
        public string BlendMode;
        public string ZWriteMode;

        public bool HasOverrides =>
            !string.IsNullOrEmpty(CullMode) ||
            !string.IsNullOrEmpty(BlendMode) ||
            !string.IsNullOrEmpty(ZWriteMode);

        public PassRenderState() { }

        public PassRenderState(string cull, string blend, string zwrite)
        {
            CullMode = cull;
            BlendMode = blend;
            ZWriteMode = zwrite;
        }
    }

    [Serializable]
    public class ShaderPass
    {
        public string Id;
        public string Name;
        public string LightMode;
        public List<ShaderBlock> Blocks;
        public DataFlowGraph DataFlow;
        public List<string> Pragmas;
        public List<string> Includes;
        public PassRenderState RenderState;
        public bool IsEnabled;

        public bool IsUsePass;
        public string UsePassPath;

        public ShaderPass()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "NewPass";
            LightMode = "";
            Blocks = new List<ShaderBlock>();
            DataFlow = DataFlowGraph.CreateDefault();
            Pragmas = new List<string>();
            Includes = new List<string>();
            RenderState = null;
            IsEnabled = true;
            IsUsePass = false;
            UsePassPath = "";
        }

        public ShaderPass(string name, string lightMode) : this()
        {
            Name = name;
            LightMode = lightMode;
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

        public void AddBlock(ShaderBlock block)
        {
            Blocks.Add(block);
        }

        public bool RemoveBlock(string id)
        {
            return Blocks.RemoveAll(b => b.Id == id) > 0;
        }

        public static ShaderPass CreateForwardLit()
        {
            var pass = new ShaderPass("ForwardLit", "UniversalForward");
            pass.Pragmas.AddRange(new[]
            {
                "#pragma vertex vert",
                "#pragma fragment frag",
                "#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE",
                "#pragma multi_compile _ _ADDITIONAL_LIGHTS",
                "#pragma multi_compile_fragment _ _SHADOWS_SOFT",
                "#pragma multi_compile_fog"
            });
            pass.Includes.AddRange(new[]
            {
                "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl",
                "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            });
            return pass;
        }

        public static ShaderPass CreateUnlit()
        {
            var pass = new ShaderPass("ForwardUnlit", "UniversalForward");
            pass.Pragmas.AddRange(new[]
            {
                "#pragma vertex vert",
                "#pragma fragment frag",
                "#pragma multi_compile_fog"
            });
            pass.Includes.Add("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl");
            return pass;
        }

        public static ShaderPass CreateUsePass(string path)
        {
            string name = path;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
                name = path.Substring(lastSlash + 1);

            var pass = new ShaderPass(name, "");
            pass.IsUsePass = true;
            pass.UsePassPath = path;
            return pass;
        }

        public static ShaderPass CreateOutline()
        {
            var pass = new ShaderPass("Outline", "SRPDefaultUnlit");
            pass.RenderState = new PassRenderState("Front", "Off", "On");
            pass.Pragmas.AddRange(new[]
            {
                "#pragma vertex vert",
                "#pragma fragment frag"
            });
            pass.Includes.Add("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl");
            return pass;
        }

        public static ShaderPass CreateShadowCaster()
        {
            return CreateUsePass("Universal Render Pipeline/Lit/ShadowCaster");
        }

        public static ShaderPass CreateDepthOnly()
        {
            return CreateUsePass("Universal Render Pipeline/Lit/DepthOnly");
        }
    }
}
