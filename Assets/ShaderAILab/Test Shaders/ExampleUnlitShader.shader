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
        // [AILab_Property: name="_ScreenHatchParams" display="屏幕空间浅阴影排线参数 (Screen Space Hatching)" type="Texture2D" default="" defaultTex="white"]
        _ScreenHatchParams("屏幕空间浅阴影排线参数 (Screen Space Hatching)", 2D) = "white" {}
        // [AILab_Property: name="_ScreenCrossHatchParams" display="屏幕空间深阴影交叉排线 (Screen Space Cross-Hatching)" type="Texture2D" default="" defaultTex="white"]
        _ScreenCrossHatchParams("屏幕空间深阴影交叉排线 (Screen Space Cross-Hatching)", 2D) = "white" {}
        // [AILab_Property: name="_LightingThresholds" display="光照阶梯化阈值 (Procedural Ramp Thresholds)" type="Texture2D" default="" defaultTex="white"]
        _LightingThresholds("光照阶梯化阈值 (Procedural Ramp Thresholds)", 2D) = "white" {}
        // [AILab_Property: name="_ShadowSmoothing" display="Shadow Smoothing" type="Range" default="0.05" min="0.001" max="0.5"]
        _ShadowSmoothing("Shadow Smoothing", Range(0.001, 0.5)) = 0.05
        // [AILab_Property: name="_DeepShadowThreshold" display="Deep Shadow Threshold" type="Range" default="0.1" min="0" max="1"]
        _DeepShadowThreshold("Deep Shadow Threshold", Range(0, 1)) = 0.1
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
            TEXTURE2D(_ScreenHatchParams); SAMPLER(sampler_ScreenHatchParams);
            TEXTURE2D(_ScreenCrossHatchParams); SAMPLER(sampler_ScreenCrossHatchParams);
            TEXTURE2D(_LightingThresholds); SAMPLER(sampler_LightingThresholds);

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
                float4 _ScreenHatchParams_ST;
                float4 _ScreenCrossHatchParams_ST;
                float4 _LightingThresholds_ST;
                float _ShadowSmoothing;
                float _DeepShadowThreshold;
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
                float3 positionWS  : TEXCOORD2;
                float fogFactor    : TEXCOORD3;
                float4 screenPos   : TEXCOORD4;
            };

            // [AILab_Section: "Helper Functions"]
            // [AILab_Block_Start: "Moebius Stylized Shading"]
            // [AILab_Intent: "Evaluate stepped lighting and apply screen-space procedural hatching based on shadow intensity"]
            // [AILab_Param: "_ShadowThreshold" role="parameter"]
            // [AILab_Param: "_ShadowSmoothing" role="parameter"]
            // [AILab_Param: "_DeepShadowThreshold" role="parameter"]
            // [AILab_Param: "_HatchScale" role="parameter"]
            // [AILab_Param: "_Hatching_Layer1" role="parameter"]
            // [AILab_Param: "_Hatching_Layer2" role="parameter"]
            half3 ApplyMoebiusShading(Varyings input, half3 albedo) {
                // 1. Get URP Main Light & Real-time Shadow
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                // 2. Calculate Diffuse Intensity
                half NdotL = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half attenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half diffuseIntensity = NdotL * attenuation;
                
                // 3. Stepped Lighting Masks (Binary/Tertiary Masking)
                half lightMask = smoothstep(_ShadowThreshold - _ShadowSmoothing, _ShadowThreshold + _ShadowSmoothing, diffuseIntensity);
                half darkMask = smoothstep(_DeepShadowThreshold - _ShadowSmoothing, _DeepShadowThreshold + _ShadowSmoothing, diffuseIntensity);
                
                // 4. Screen-Space UV Calculation (Aspect Ratio Corrected)
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 0.001);
                screenUV.x *= _ScreenParams.x / _ScreenParams.y;
                screenUV *= _HatchScale * 10.0; // Scaled to reasonable stroke density
                
                // 5. Sample Hatching Textures
                half hatch1 = SAMPLE_TEXTURE2D(_Hatching_Layer1, sampler_Hatching_Layer1, screenUV).r;
                half hatch2 = SAMPLE_TEXTURE2D(_Hatching_Layer2, sampler_Hatching_Layer2, screenUV).r;
                
                // 6. Combine Cross-Hatching based on Shadow Depth
                // Darkest regions get cross-hatching (Layer1 * Layer2)
                // Mid-shadows get single hatching (Layer1)
                // Light regions get no hatching (1.0)
                half midHatch = hatch1;
                half deepHatch = hatch1 * hatch2;
                
                half hatchIntensity = lerp(deepHatch, midHatch, darkMask);
                hatchIntensity = lerp(hatchIntensity, 1.0, lightMask);
                
                // 7. Base Color Cel Shading
                half3 lightColor = albedo * mainLight.color;
                half3 shadowColor = albedo * mainLight.color * 0.5; // Tint albedo for unlit regions
                half3 celShadedColor = lerp(shadowColor, lightColor, lightMask);
                
                // 8. Multiply Inked Hatching to the Shaded Color
                return celShadedColor * hatchIntensity;
            }
            // [AILab_Block_End]

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Main Vertex"]
            // [AILab_Intent: "Pass-through for vertex position, standard transform handled by framework"]
            void MainVertex(inout float3 posOS) {
                // No specific vertex displacement needed for the main stylized pass
            }
            // [AILab_Block_End]

            Varyings vert(Attributes input) {
                Varyings output = (Varyings)0;
                float3 posOS = input.positionOS.xyz;
                MainVertex(posOS);
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS);
                output.positionCS = vpi.positionCS;
                output.normalWS = vni.normalWS;
                output.uv = input.uv;
                output.positionWS = vpi.positionWS;
                output.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                output.screenPos = ComputeScreenPos(vpi.positionCS);
                return output;
            }

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Moebius Fragment"]
            // [AILab_Intent: "Sample base color and execute Moebius screen-space hatching"]
            // [AILab_Param: "_BaseMap" role="parameter"]
            // [AILab_Param: "_BaseColor" role="parameter"]
            half4 MoebiusFragment(Varyings input) {
                float2 uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half4 baseColor = texColor * _BaseColor;
                
                half3 finalRGB = ApplyMoebiusShading(input, baseColor.rgb);
                
                return half4(finalRGB, baseColor.a);
            }
            // [AILab_Block_End]

            half4 frag(Varyings input) : SV_Target {
                half4 finalColor = half4(1,1,1,1);
                finalColor = MoebiusFragment(input);
                return finalColor;
            }

            ENDHLSL
        }

        // [AILab_Pass: name="DepthOnly" lightmode="DepthOnly"]
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Hatching_Layer1); SAMPLER(sampler_Hatching_Layer1);
            TEXTURE2D(_Hatching_Layer2); SAMPLER(sampler_Hatching_Layer2);
            TEXTURE2D(_ShadowDepthThresholds); SAMPLER(sampler_ShadowDepthThresholds);
            TEXTURE2D(_HatchTex); SAMPLER(sampler_HatchTex);
            TEXTURE2D(_ScreenHatchParams); SAMPLER(sampler_ScreenHatchParams);
            TEXTURE2D(_ScreenCrossHatchParams); SAMPLER(sampler_ScreenCrossHatchParams);
            TEXTURE2D(_LightingThresholds); SAMPLER(sampler_LightingThresholds);

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
                float4 _ScreenHatchParams_ST;
                float4 _ScreenCrossHatchParams_ST;
                float4 _LightingThresholds_ST;
                float _ShadowSmoothing;
                float _DeepShadowThreshold;
            CBUFFER_END

            struct Attributes {
                float4 positionOS  : POSITION;
            };

            struct Varyings {
                float4 positionCS  : SV_POSITION;
            };

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Depth Only Vertex"]
            // [AILab_Intent: "Pass-through for depth pre-pass"]
            void DepthOnlyVertex(inout float3 positionOS) {
            }
            // [AILab_Block_End]

            Varyings vert(Attributes input) {
                Varyings output = (Varyings)0;
                float3 posOS = input.positionOS.xyz;
                DepthOnlyVertex(posOS);
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                output.positionCS = vpi.positionCS;
                return output;
            }

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Depth Only Fragment"]
            // [AILab_Intent: "Output zero — ColorMask 0 means only depth buffer is written"]
            half4 DepthOnlyFragment(Varyings input) {
                return 0;
            }
            // [AILab_Block_End]

            half4 frag(Varyings input) : SV_Target {
                half4 finalColor = half4(1,1,1,1);
                finalColor = DepthOnlyFragment(input);
                return finalColor;
            }

            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "ShaderAILab.Editor.ShaderAILabGUI"
}
