# Shader AILab Project Guideline

## 1. Project Vision
Shader AILab is a semantic-driven shader development environment for Unity. It abstracts complex HLSL code into "Natural Language Blocks" while maintaining high-performance output. It allows Technical Artists (TAs) to build shaders by describing intent, while retaining the ability to fine-tune raw code in VSCode/Cursor.

## 2. Core Mechanism: Bidirectional Semantic Sync
To bridge the gap between LLM and HLSL, we use a "Tagging Contract". Every shader generated must use metadata tags to allow the Unity Editor to reconstruct the UI.

### 2.1 The Metadata Contract (Tagging Standard)
Every `.shader` file must be parsed/written using these specific comment tags:

- `// [AILab_Global: cull="Back" blend="Off" zwrite="On"]` : Global shader settings.
- `// [AILab_Property: name="_Name" display="Display" type="Range" min="0" max="1" default="0.5"]` : Marks a property for the Houdini-style UI.
- `// [AILab_Section: "Section Name"]` : Marks a logical section (Constants, Vertex, Fragment, Helper Functions).
- `// [AILab_Block_Start: "Semantic Title"]` : Wraps a logical HLSL function or snippet.
- `// [AILab_Intent: "Natural language description"]` : Describes what the block does.
- `// [AILab_Param: "_ParamName" role="role_description"]` : References a property used by this block.
- `// [AILab_Block_End]` : Closes the block.

### 2.2 Example File Structure
```hlsl
Shader "AILab/MyShader" {
    Properties {
        // [AILab_Property: name="_BaseColor" display="Base Color" type="Color" default="(1,1,1,1)"]
        _BaseColor("Base Color", Color) = (1,1,1,1)
        // [AILab_Property: name="_WaveSpeed" display="Wave Speed" type="Range" min="0" max="10" default="1"]
        _WaveSpeed("Wave Speed", Range(0,10)) = 1
    }
    SubShader {
        // [AILab_Global: cull="Back" blend="Off" zwrite="On"]
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Vertex Wave Animation"]
            // [AILab_Intent: "Make vertices wave up and down based on time and position"]
            // [AILab_Param: "_WaveSpeed" role="frequency_multiplier"]
            void ApplyWave(inout float3 vertex, float time) {
                vertex.y += sin(time * _WaveSpeed + vertex.x) * 0.5;
            }
            // [AILab_Block_End]

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Fragment Color Output"]
            // [AILab_Intent: "Output base color with simple lighting"]
            // [AILab_Param: "_BaseColor" role="albedo_tint"]
            half4 ComputeColor(float3 normal) {
                half NdotL = saturate(dot(normal, float3(0,1,0)));
                return _BaseColor * NdotL;
            }
            // [AILab_Block_End]

            ENDHLSL
        }
    }
}
```

## 3. Architecture Overview

### 3.1 Directory Structure
```
Assets/ShaderAILab/
├── Editor/
│   ├── Core/         - Data models, parser, writer, file watcher, compile checker, version history
│   ├── UI/           - EditorWindow, block list, code editor, parameter panel, preview, draggable fields
│   ├── LLM/          - Provider interface, OpenAI/Anthropic/Ollama providers, service, settings, prompt templates
│   ├── Integration/  - Asset handler (double-click intercept), menu items
│   └── Resources/    - UXML layout, USS stylesheet, LLMSettings asset
└── Templates/        - URP Lit/Unlit shader templates with AILab tags
```

### 3.2 Key Components
- **ShaderDocument**: In-memory representation of a tagged shader file
- **ShaderParser/Writer**: Bidirectional tag-based serialization
- **ShaderAILabWindow**: Main UI Toolkit + IMGUI hybrid editor window
- **LLMService**: Multi-provider dispatcher (OpenAI, Anthropic, Ollama) with streaming
- **PromptTemplates**: Shader-specific prompt engineering for reliable HLSL generation
- **ShaderFileWatcher**: External edit detection for VSCode interop
- **ShaderVersionHistory**: Snapshot-based undo/revision system
- **ShaderCompileChecker**: Maps Unity compile errors back to AILab blocks