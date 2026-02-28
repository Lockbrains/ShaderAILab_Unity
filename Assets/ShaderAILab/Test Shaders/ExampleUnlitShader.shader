Shader "AILab/ExampleUnlitShader" {
    Properties {
        // [AILab_Property: name="_BaseColor" display="Base Color" type="Color" default="(1,1,1,1)"]
        _BaseColor("Base Color", Color) = (1,1,1,1)
        // [AILab_Property: name="_Brightness" display="Brightness" type="Range" default="1" min="0" max="2"]
        _Brightness("Brightness", Range(0,2)) = 1
        // [AILab_Property: name="_BaseMap" display="基础颜色贴图" type="Texture2D" default="" defaultTex="white"]
        _BaseMap("基础颜色贴图", 2D) = "white" {}
        // [AILab_Property: name="_Hatching_Layer1" display="第一层排线属性 (浅阴影)" type="Texture2D" default="" defaultTex="white"]
        _Hatching_Layer1("第一层排线属性 (浅阴影)", 2D) = "white" {}
        // [AILab_Property: name="_Hatching_Layer2" display="第二层排线属性 (深阴影交叉)" type="Texture2D" default="" defaultTex="white"]
        _Hatching_Layer2("第二层排线属性 (深阴影交叉)", 2D) = "white" {}
        // [AILab_Property: name="_ShadowDepthThresholds" display="阴影深度分级阈值" type="Texture2D" default="" defaultTex="white"]
        _ShadowDepthThresholds("阴影深度分级阈值", 2D) = "white" {}
        // [AILab_Property: name="_OutlineWidth" display="Outline Width" type="Range" default="0.015" min="0" max="0.1"]
        _OutlineWidth("Outline Width", Range(0, 0.1)) = 0.015
        // [AILab_Property: name="_OutlineColor" display="Outline Color" type="Color" default="(0,0,0,1)"]
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        // [AILab_Property: name="_ShadowThreshold" display="Shadow Threshold" type="Range" default="0.0" min="-1" max="1"]
        _ShadowThreshold("Shadow Threshold", Range(-1, 1)) = 0.0
        // [AILab_Property: name="_HatchTex" display="Hatching Texture" type="Texture2D" default="" defaultTex="white"]
        _HatchTex("Hatching Texture", 2D) = "white" {}
        // [AILab_Property: name="_HatchScale" display="Hatching Scale" type="Range" default="10.0" min="1" max="50"]
        _HatchScale("Hatching Scale", Range(1, 50)) = 10.0
    }
    SubShader {
        // [AILab_Global: cull="Back" blend="Off" zwrite="On"]
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On

        // [AILab_Pass: name="ForwardUnlit" lightmode="UniversalForward"]
        Pass {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Hatching_Layer1); SAMPLER(sampler_Hatching_Layer1);
            TEXTURE2D(_Hatching_Layer2); SAMPLER(sampler_Hatching_Layer2);
            TEXTURE2D(_ShadowDepthThresholds); SAMPLER(sampler_ShadowDepthThresholds);
            TEXTURE2D(_HatchTex); SAMPLER(sampler_HatchTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Brightness;
                float4 _BaseMap_ST;
                float4 _Hatching_Layer1_ST;
                float4 _Hatching_Layer2_ST;
                float4 _ShadowDepthThresholds_ST;
                float _OutlineWidth;
                float4 _OutlineColor;
                float _ShadowThreshold;
                float4 _HatchTex_ST;
                float _HatchScale;
            CBUFFER_END

            struct Attributes {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float2 uv          : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float fogFactor    : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
            };

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Base Transform"]
            // [AILab_Intent: "Pass-through for base geometry; standard MVP, normalWS, and screenPos are auto-handled by the framework"]
            void BaseTransform(inout float3 posOS) {
                // No vertex displacement needed for the base pass.
                // The framework will automatically transform posOS to positionCS
                // and compute normalWS and screenPos as required by the fragment shader.
            }
            // [AILab_Block_End]

            // [AILab_Block_Start: "Outline Displacement"]
            // [AILab_Intent: "Extrude vertices outwards along their normal to create an inverted hull outline"]
            // [AILab_Param: "_OutlineWidth" role="parameter"]
            void ExtrudeOutline(inout float3 posOS) {
                // Normalize position as a fast approximation for normalOS to expand the mesh outwards.
                // This creates the constant-width outline shell when front faces are culled.
                float3 normalOS = normalize(posOS);
                posOS += normalOS * _OutlineWidth;
            }
            // [AILab_Block_End]

            Varyings vert(Attributes input) {
                Varyings output = (Varyings)0;
                float3 posOS = input.positionOS.xyz;
                BaseTransform(posOS);
                ExtrudeOutline(posOS);
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS);
                output.positionCS = vpi.positionCS;
                output.normalWS = vni.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                output.screenPos = ComputeScreenPos(vpi.positionCS);
                return output;
            }

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Base Color Sampling"]
            // [AILab_Intent: "Sample the base texture and multiply it by the solid base color"]
            // [AILab_Param: "_BaseMap" role="parameter"]
            // [AILab_Param: "_BaseColor" role="parameter"]
            half4 SampleBaseColor(Varyings input) {
                float2 uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            }
            // [AILab_Block_End]

            // [AILab_Block_Start: "Binary Shadow Mask"]
            // [AILab_Intent: "Calculate NdotL and apply a hard threshold to determine shadow areas"]
            // [AILab_Param: "_ShadowThreshold" role="parameter"]
            half CalcBinaryShadowMask(Varyings input) {
                Light mainLight = GetMainLight();
                float NdotL = dot(normalize(input.normalWS), mainLight.direction);
                // Returns 1.0 for illuminated areas, 0.0 for shadowed areas
                return step(_ShadowThreshold, NdotL);
            }
            // [AILab_Block_End]

            // [AILab_Block_Start: "Screen Space Hatching"]
            // [AILab_Intent: "Compute screen-space UV coordinates and sample the hatching texture"]
            // [AILab_Param: "_HatchTex" role="parameter"]
            // [AILab_Param: "_HatchScale" role="parameter"]
            half SampleHatchingPattern(Varyings input) {
                // Perform perspective divide to get normalized screen coordinates
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 0.0001);
                screenUV *= _HatchScale;
                return SAMPLE_TEXTURE2D(_HatchTex, sampler_HatchTex, screenUV).r;
            }
            // [AILab_Block_End]

            // [AILab_Block_Start: "Final Composition"]
            // [AILab_Intent: "Composite base color and hatching shadow based on the illumination mask"]
            half4 StylizedMoebiusFragment(Varyings input) {
                // 1) Base Color
                half4 baseColor = SampleBaseColor(input);
                
                // 2) Illumination Mask
                half shadowMask = CalcBinaryShadowMask(input);
                
                // 3) Hatching Application
                half hatchIntensity = SampleHatchingPattern(input);
                half4 shadowedColor = baseColor * hatchIntensity;
                
                // 4) Final Output (Lerp between shadow hatching and solid base color)
                // Pure flat shading output unaffected by actual light color/attenuation
                return lerp(shadowedColor, baseColor, shadowMask);
            }
            // [AILab_Block_End]

            half4 frag(Varyings input) : SV_Target {
                half4 finalColor = half4(1,1,1,1);
                finalColor = StylizedMoebiusFragment(input);
                return finalColor;
            }

            ENDHLSL
        }

        // [AILab_Pass: name="Outline" lightmode="SRPDefaultUnlit"]
        Pass {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Hatching_Layer1); SAMPLER(sampler_Hatching_Layer1);
            TEXTURE2D(_Hatching_Layer2); SAMPLER(sampler_Hatching_Layer2);
            TEXTURE2D(_ShadowDepthThresholds); SAMPLER(sampler_ShadowDepthThresholds);
            TEXTURE2D(_HatchTex); SAMPLER(sampler_HatchTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Brightness;
                float4 _BaseMap_ST;
                float4 _Hatching_Layer1_ST;
                float4 _Hatching_Layer2_ST;
                float4 _ShadowDepthThresholds_ST;
                float _OutlineWidth;
                float4 _OutlineColor;
                float _ShadowThreshold;
                float4 _HatchTex_ST;
                float _HatchScale;
            CBUFFER_END

            struct Attributes {
                float4 positionOS  : POSITION;
            };

            struct Varyings {
                float4 positionCS  : SV_POSITION;
            };

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Outline Extrusion"]
            // [AILab_Intent: "Extrude vertices along their object space normals to create an inverted hull outline"]
            // [AILab_Param: "_OutlineWidth" role="parameter"]
            void ExtrudeOutline(inout float3 posOS) 
            {
                float3 normalOS = normalize(posOS);
                posOS += normalOS * _OutlineWidth;
            }
            // [AILab_Block_End]

            Varyings vert(Attributes input) {
                Varyings output = (Varyings)0;
                float3 posOS = input.positionOS.xyz;
                ExtrudeOutline(posOS);
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                output.positionCS = vpi.positionCS;
                return output;
            }

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Outline Color"]
            // [AILab_Intent: "Output the solid custom color for the outline pass"]
            // [AILab_Param: "_OutlineColor" role="parameter"]
            half4 OutlineColor(Varyings input) 
            {
                return _OutlineColor;
            }
            // [AILab_Block_End]

            half4 frag(Varyings input) : SV_Target {
                half4 finalColor = half4(1,1,1,1);
                finalColor = OutlineColor(input);
                return finalColor;
            }

            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "ShaderAILab.Editor.ShaderAILabGUI"
}
