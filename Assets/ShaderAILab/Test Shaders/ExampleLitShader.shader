Shader "AILab/ExampleLitShader" {
    Properties {
        // [AILab_Property: name="_BaseColor" display="Base Color" type="Color" default="(1,1,1,1)"]
        _BaseColor("Base Color", Color) = (1,1,1,1)
        // [AILab_Property: name="_Smoothness" display="Smoothness" type="Range" default="0.5" min="0" max="1"]
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        // [AILab_Property: name="_Metallic" display="Metallic" type="Range" default="0" min="0" max="1"]
        _Metallic("Metallic", Range(0,1)) = 0
        // [AILab_Property: name="_MainTex" display="Base Map" type="Texture2D" default="white"]
        _MainTex("Base Map", 2D) = "white" {}
        // [AILab_Property: name="_ToonThreshold" display="Toon Threshold" type="Range" default="0.5" min="0" max="1"]
        _ToonThreshold("Toon Threshold", Range(0.0, 1.0)) = 0.5
        // [AILab_Property: name="_ToonSmoothness" display="Toon Smoothness" type="Range" default="0.05" min="0" max="1"]
        _ToonSmoothness("Toon Smoothness", Range(0.0, 1.0)) = 0.05
        // [AILab_Property: name="_ShadowColor" display="Shadow Color" type="Color" default="(0.3, 0.3, 0.4, 1.0)"]
        _ShadowColor("Shadow Color", Color) = (0.3, 0.3, 0.4, 1.0)
        // [AILab_Property: name="_SpecularThreshold" display="Specular Threshold" type="Range" default="0.9" min="0" max="1"]
        _SpecularThreshold("Specular Threshold", Range(0.0, 1.0)) = 0.9
        // [AILab_Property: name="_SpecularSmoothness" display="Specular Smoothness" type="Range" default="0.01" min="0" max="1"]
        _SpecularSmoothness("Specular Smoothness", Range(0.0, 1.0)) = 0.01
    }
    SubShader {
        // [AILab_Global: cull="Back" blend="Off" zwrite="On"]
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Smoothness;
                float _Metallic;
                float4 _MainTex_ST;
                float _ToonThreshold;
                float _ToonSmoothness;
                float4 _ShadowColor;
                float _SpecularThreshold;
                float _SpecularSmoothness;
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
                float4 shadowCoord : TEXCOORD4;
            };

            // [AILab_Section: "Vertex"]
            // [AILab_Block_Start: "Standard Vertex Transform"]
            // [AILab_Intent: "(auto-skipped: declarations handled by CBUFFER)"]
            // [AILab_Block_End]

            Varyings vert(Attributes input) {
                Varyings output = (Varyings)0;
                float3 posOS = input.positionOS.xyz;
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                VertexNormalInputs vni = GetVertexNormalInputs(input.normalOS);
                output.positionCS = vpi.positionCS;
                output.normalWS = vni.normalWS;
                output.uv = input.uv;
                output.positionWS = vpi.positionWS;
                output.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                output.shadowCoord = GetShadowCoord(vpi);
                return output;
            }

            // [AILab_Section: "Fragment"]
            // [AILab_Block_Start: "Toon Shading"]
            // [AILab_Intent: "Calculate basic toon (cel) shading with smooth-stepped diffuse banding, tinted shadows, and sharp specular highlights."]
            // [AILab_Param: "_BaseColor" role="parameter"]
            // [AILab_Param: "_ShadowColor" role="parameter"]
            // [AILab_Param: "_ToonThreshold" role="parameter"]
            // [AILab_Param: "_ToonSmoothness" role="parameter"]
            // [AILab_Param: "_SpecularThreshold" role="parameter"]
            // [AILab_Param: "_SpecularSmoothness" role="parameter"]
            //Compute Toon Shading
            half4 ComputeToonShading(Varyings input) {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 baseColor = texColor.rgb * _BaseColor.rgb;
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
            
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 lightDir = mainLight.direction;
                half3 lightColor = mainLight.color;
                half lightAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
            
                half NdotL = dot(normalWS, lightDir);
                half halfLambert = NdotL * 0.5 + 0.5;
                half litIntensity = halfLambert * lightAttenuation;
                half toonBand = smoothstep(_ToonThreshold - _ToonSmoothness, _ToonThreshold + _ToonSmoothness, litIntensity);
            
                half3 diffuseToon = lerp(baseColor * _ShadowColor.rgb, baseColor, toonBand);
                diffuseToon *= lightColor;
            
                half3 halfVector = normalize(lightDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfVector));
                half specPower = pow(NdotH, 150.0);
                half specIntensity = smoothstep(_SpecularThreshold - _SpecularSmoothness, _SpecularThreshold + _SpecularSmoothness, specPower * lightAttenuation);
                half3 specularToon = specIntensity * lightColor;
            
                half3 finalRGB = diffuseToon + specularToon;
                finalRGB = MixFog(finalRGB, input.fogFactor);
                return half4(finalRGB, texColor.a * _BaseColor.a);
            }
            // [AILab_Block_End]

            half4 frag(Varyings input) : SV_Target {
                half4 finalColor = half4(1,1,1,1);
                finalColor = ComputeToonShading(input);
                return finalColor;
            }

            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
