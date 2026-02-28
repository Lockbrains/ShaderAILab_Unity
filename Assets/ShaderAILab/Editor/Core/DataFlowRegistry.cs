using System.Collections.Generic;

namespace ShaderAILab.Editor.Core
{
    /// <summary>
    /// Static registry of all known URP Attributes/Varyings fields, global uniforms,
    /// and their inter-stage dependency rules.
    /// </summary>
    public static class DataFlowRegistry
    {
        // ---- Attributes (a2v) prototypes ----

        public static readonly DataFlowField[] AllAttributes =
        {
            new DataFlowField("positionOS", "float4", "POSITION",  "Object Position",  DataFlowStage.Attributes, true),
            new DataFlowField("normalOS",   "float3", "NORMAL",    "Object Normal",    DataFlowStage.Attributes, false),
            new DataFlowField("tangentOS",  "float4", "TANGENT",   "Object Tangent",   DataFlowStage.Attributes, false),
            new DataFlowField("uv",         "float2", "TEXCOORD0", "UV Channel 0",     DataFlowStage.Attributes, false),
            new DataFlowField("uv2",        "float2", "TEXCOORD1", "UV Channel 1",     DataFlowStage.Attributes, false),
            new DataFlowField("color",      "float4", "COLOR",     "Vertex Color",     DataFlowStage.Attributes, false),
        };

        // ---- Varyings (v2f) prototypes ----

        public static readonly DataFlowField[] AllVaryings =
        {
            new DataFlowField("positionCS",   "float4", "SV_POSITION", "Clip Position",       DataFlowStage.Varyings, true),
            new DataFlowField("normalWS",     "float3", "",            "World Normal",        DataFlowStage.Varyings, false),
            new DataFlowField("tangentWS",    "float4", "",            "World Tangent",       DataFlowStage.Varyings, false),
            new DataFlowField("bitangentWS",  "float3", "",            "World Bitangent",     DataFlowStage.Varyings, false),
            new DataFlowField("uv",           "float2", "",            "UV",                  DataFlowStage.Varyings, false),
            new DataFlowField("positionWS",   "float3", "",            "World Position",      DataFlowStage.Varyings, false),
            new DataFlowField("viewDirWS",    "float3", "",            "View Direction",      DataFlowStage.Varyings, false),
            new DataFlowField("fogFactor",    "float",  "",            "Fog Factor",          DataFlowStage.Varyings, false),
            new DataFlowField("shadowCoord",  "float4", "",            "Shadow Coord",        DataFlowStage.Varyings, false),
            new DataFlowField("vertexColor",  "float4", "",            "Vertex Color",        DataFlowStage.Varyings, false),
            new DataFlowField("screenPos",    "float4", "",            "Screen Position",     DataFlowStage.Varyings, false),
        };

        // ---- Global uniforms: always available in fragment, no struct entry needed ----

        public static readonly DataFlowField[] AllGlobals =
        {
            new DataFlowField("_Time",            "float4", "", "Time (t/20, t, t*2, t*3)",       DataFlowStage.Global, false),
            new DataFlowField("_SinTime",         "float4", "", "Sine of Time",                   DataFlowStage.Global, false),
            new DataFlowField("_CosTime",         "float4", "", "Cosine of Time",                 DataFlowStage.Global, false),
            new DataFlowField("unity_DeltaTime",  "float4", "", "Delta Time (dt, 1/dt, ...)",     DataFlowStage.Global, false),
            new DataFlowField("_WorldSpaceCameraPos", "float3", "", "Camera Position (World)",    DataFlowStage.Global, false),
            new DataFlowField("_ScreenParams",    "float4", "", "Screen Size (w, h, 1+1/w, 1+1/h)", DataFlowStage.Global, false),
            new DataFlowField("_ProjectionParams","float4", "", "Projection Params (near/far)",   DataFlowStage.Global, false),
            new DataFlowField("unity_OrthoParams","float4", "", "Ortho Params",                   DataFlowStage.Global, false),
            new DataFlowField("unity_ObjectToWorld","float4x4","","Object → World Matrix",        DataFlowStage.Global, false),
            new DataFlowField("unity_WorldToObject","float4x4","","World → Object Matrix",        DataFlowStage.Global, false),
        };

        // ---- Dependency rules: Varyings field -> required Attributes field(s) ----

        static readonly Dictionary<string, DataFlowDependency[]> _dependencies
            = new Dictionary<string, DataFlowDependency[]>
        {
            ["positionCS"] = new[]
            {
                new DataFlowDependency("positionOS", "positionCS",
                    "VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);\n                output.positionCS = vpi.positionCS;",
                    "GetVertexPositionInputs")
            },
            ["positionWS"] = new[]
            {
                new DataFlowDependency("positionOS", "positionWS",
                    "output.positionWS = vpi.positionWS;",
                    "GetVertexPositionInputs \u2192 positionWS")
            },
            ["normalWS"] = new[]
            {
                new DataFlowDependency("normalOS", "normalWS",
                    "VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS);\n                output.normalWS = vni.normalWS;",
                    "TransformObjectToWorldNormal")
            },
            ["tangentWS"] = new[]
            {
                new DataFlowDependency("tangentOS", "tangentWS",
                    "VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS, input.tangentOS);\n                output.tangentWS = float4(vni.tangentWS, input.tangentOS.w);",
                    "GetVertexNormalInputs \u2192 tangentWS")
            },
            ["bitangentWS"] = new[]
            {
                new DataFlowDependency("normalOS", "bitangentWS",
                    "output.bitangentWS = vni.bitangentWS;",
                    "GetVertexNormalInputs \u2192 bitangentWS"),
                new DataFlowDependency("tangentOS", "bitangentWS",
                    "",
                    "requires tangentOS for bitangent")
            },
            ["uv"] = new[]
            {
                new DataFlowDependency("uv", "uv",
                    "output.uv = input.uv;",
                    "Pass-through UV")
            },
            ["fogFactor"] = new[]
            {
                new DataFlowDependency("positionOS", "fogFactor",
                    "output.fogFactor = ComputeFogFactor(vpi.positionCS.z);",
                    "ComputeFogFactor")
            },
            ["shadowCoord"] = new[]
            {
                new DataFlowDependency("positionOS", "shadowCoord",
                    "output.shadowCoord = GetShadowCoord(vpi);",
                    "GetShadowCoord")
            },
            ["viewDirWS"] = new[]
            {
                new DataFlowDependency("positionOS", "viewDirWS",
                    "output.viewDirWS = GetWorldSpaceNormalizeViewDir(vpi.positionWS);",
                    "GetWorldSpaceNormalizeViewDir")
            },
            ["vertexColor"] = new[]
            {
                new DataFlowDependency("color", "vertexColor",
                    "output.vertexColor = input.color;",
                    "Pass-through vertex color")
            },
            ["screenPos"] = new[]
            {
                new DataFlowDependency("positionOS", "screenPos",
                    "output.screenPos = ComputeScreenPos(vpi.positionCS);",
                    "ComputeScreenPos")
            },
        };

        public static DataFlowDependency[] GetDependencies(string varyingFieldName)
        {
            if (_dependencies.TryGetValue(varyingFieldName, out var deps))
                return deps;
            return new DataFlowDependency[0];
        }

        public static DataFlowField FindAttributePrototype(string nameOrSemantic)
        {
            foreach (var f in AllAttributes)
            {
                if (f.Name == nameOrSemantic || f.Semantic == nameOrSemantic)
                    return f;
            }
            return null;
        }

        public static DataFlowField FindVaryingPrototype(string name)
        {
            foreach (var f in AllVaryings)
            {
                if (f.Name == name)
                    return f;
            }
            return null;
        }
    }
}
