// ==============================
// Grass.shader
// ==============================
// Lit grass blades for the procedural verges.
//
// Blades carry no texture. Colour comes from vertex colour (dark at the base,
// lighter at the tip) which keeps the memory cost to nothing and avoids the
// alpha-cutout overdraw that textured grass cards bring in VR.
//
// Mesh contract, written by GrassField.cs:
//   COLOR     : blade tint, already shaded base -> tip
//   TEXCOORD0 : y = 0 at the base, 1 at the tip (drives bend and wind)
Shader "Sumo2Unity/Grass"
{
    Properties
    {
        _BaseColor  ("Tint", Color)                 = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1))     = 0.15
        _Translucency ("Backlight", Range(0, 1))    = 0.35

        _WindStrength ("Wind Strength (m)", Range(0, 0.5)) = 0.08
        _WindSpeed    ("Wind Speed", Range(0, 5))          = 1.2
        _WindScale    ("Wind Scale", Range(0.01, 1))       = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"            = "Opaque"
            "RenderPipeline"        = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue"                 = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Blades are single-sided quads; without this they vanish from behind.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   GrassVertex
            #pragma fragment GrassFragment
            #pragma target 2.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half  _Smoothness;
                half  _Translucency;
                float _WindStrength;
                float _WindSpeed;
                float _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GrassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Sway scaled by height^2 so the base stays planted while the tip
                // moves. Phase comes from world position, so neighbouring blades
                // move together in gusts rather than all in lockstep.
                float height = saturate(input.uv.y);
                float phase  = (positionWS.x + positionWS.z) * _WindScale + _Time.y * _WindSpeed;
                float sway   = sin(phase) + 0.5 * sin(phase * 2.3 + 1.7);
                positionWS.xz += sway * _WindStrength * height * height;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.color      = input.color;
                output.fogCoord   = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Two-sided lighting: flip the normal for back faces so blades
                // seen from behind are not black.
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float3 viewWS   = GetWorldSpaceNormalizeViewDir(input.positionWS);
                if (dot(normalWS, viewWS) < 0.0) normalWS = -normalWS;

                half3 albedo = input.color.rgb * _BaseColor.rgb;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.alpha      = 1.0h;
                surfaceData.metallic   = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS              = input.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = viewWS;
                inputData.shadowCoord             = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord                = input.fogCoord;
                inputData.bakedGI                 = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Cheap stand-in for light passing through a thin blade, so grass
                // lit from behind glows instead of reading as a dark mass.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half back = saturate(dot(-mainLight.direction, viewWS));
                color.rgb += albedo * mainLight.color * back * _Translucency * mainLight.shadowAttenuation;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a   = 1.0h;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
