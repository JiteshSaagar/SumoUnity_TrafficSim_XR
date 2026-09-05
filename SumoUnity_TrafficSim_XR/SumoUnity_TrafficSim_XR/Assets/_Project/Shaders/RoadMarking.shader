// ==============================
// RoadMarking.shader
// ==============================
// Lane paint drawn inside an oversized carrier ribbon.
//
// The mesh handed to this shader is NOT the paint. It is a wide carrier strip
// (see LaneMarkingMesh.cs). Two stages turn it into a line:
//
//  1. Vertex: the carrier is widened about its centre line until it covers at
//     least _MinPixelWidth pixels, so the rasteriser can never miss it. Without
//     this the quad itself goes sub-pixel past ~10 m and drops out, which is
//     what made solid lines read as crawling dashes.
//
//  2. Fragment: the real paint is drawn analytically from the interpolated
//     world distance to the centre line. Coverage is the exact overlap between
//     the painted band and this pixel's footprint, so it is crisp up close and
//     decays to the true area average far away rather than flickering. This is
//     what a mipmapped texture does; fwidth() just lets us do it on geometry.
//
// The paint stays coplanar with the asphalt and wins the depth test through
// ShaderLab Offset rather than by being lifted, so it cannot float at the
// grazing angles a seated VR driver looks from.
Shader "Sumo2Unity/Road Marking"
{
    Properties
    {
        [MainColor] _BaseColor ("Paint Color", Color)       = (1, 1, 1, 1)
                    _Smoothness("Smoothness", Range(0, 1))  = 0.25

        // Painted width of the line. Must stay within the carrier the mesh was
        // built with (marking width + 2 * 0.15 m) or the paint gets clipped.
        _MarkingWidth ("Marking Width (m)", Range(0.02, 0.4)) = 0.12

        _DashLength ("Dash Length (m)", Range(0.1, 10)) = 1.5
        _GapLength  ("Gap Length (m)",  Range(0.1, 10)) = 1.5

        // Carrier is widened until it covers at least this many pixels, so it
        // always rasterises. Raise it if distant lines still break up.
        _MinPixelWidth ("Min Carrier Width (px)", Range(1, 8))  = 2
        _MaxWidthScale ("Max Widening Factor",    Range(1, 64)) = 16

        // 1.0 is the mathematically correct filter width.
        _EdgeSoftness ("Edge Softness", Range(0.5, 3)) = 1.0

        _OffsetFactor ("Depth Offset Factor", Range(-10, 10)) = -1
        _OffsetUnits  ("Depth Offset Units",  Range(-10, 10)) = -1
    }

    SubShader
    {
        Tags
        {
            // Paint fades with distance instead of dropping out, so it has to
            // blend with the road. Just after opaque geometry, well before real
            // transparents such as glass.
            "RenderType"            = "Transparent"
            "RenderPipeline"        = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue"                 = "Transparent-100"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor;
            half   _Smoothness;
            float  _MarkingWidth;
            float  _DashLength;
            float  _GapLength;
            float  _MinPixelWidth;
            float  _MaxWidthScale;
            float  _EdgeSoftness;
            float  _OffsetFactor;
            float  _OffsetUnits;
        CBUFFER_END

        // Exact overlap between the painted band [-hw, hw] and this pixel's
        // footprint centred at signed distance d. An analytic box filter, not a
        // smoothstep: a smoothstep saturates near 0.5 at the band centre however
        // thin the line gets, which would make distant paint glow instead of
        // fading. This decays to the true area average at every scale.
        float BandCoverage(float d, float hw, float footprint)
        {
            float halfFootprint = max(footprint * 0.5 * _EdgeSoftness, 1e-6);
            float lo = max(d - halfFootprint, -hw);
            float hi = min(d + halfFootprint,  hw);
            return saturate((hi - lo) / (2.0 * halfFootprint));
        }

        // 1 for a solid line; otherwise the dash pattern along the lane,
        // filtered the same way so distant dashes average out rather than strobe.
        float DashCoverage(float along, float footprint, float broken)
        {
            if (broken < 0.5) return 1.0;

            float period = max(_DashLength + _GapLength, 1e-3);
            float t      = frac(along / period) * period;

            // Footprint comes from `along`, never from the wrapped `t`: fwidth()
            // across the frac() seam spikes and punches a hole in every dash.
            float halfFootprint = max(footprint * 0.5 * _EdgeSoftness, 1e-6);

            float lo  = max(t - halfFootprint, 0.0);
            float hi  = min(t + halfFootprint, _DashLength);
            float cov = saturate((hi - lo) / (2.0 * halfFootprint));

            // Once a pixel spans a whole period its phase is meaningless, so
            // settle on the duty cycle instead of whichever dash it landed in.
            float blend = saturate(2.0 * halfFootprint / period);
            return lerp(cov, _DashLength / period, blend);
        }
        ENDHLSL

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Offset still applies to the depth TEST with ZWrite off, which is
            // what keeps coplanar paint from Z-fighting the asphalt.
            Offset [_OffsetFactor], [_OffsetUnits]
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   MarkingVertex
            #pragma fragment MarkingFragment
            #pragma target 3.0   // ddx/ddy

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;   // xyz = lateral dir, w = signed carrier half
                float2 uv         : TEXCOORD0; // y = metres along the lane
                float4 color      : COLOR;     // r = broken
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                float  across     : TEXCOORD4;  // true metres from the centre line
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MarkingVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = input.positionOS.xyz;
                float3 lateralOS  = input.tangentOS.xyz;
                float  signedHalf = input.tangentOS.w;
                float  halfMag    = abs(signedHalf);

                // Widen the carrier about its centre line until it is at least
                // _MinPixelWidth pixels across, so it always rasterises. `across`
                // tracks the widening, so the fragment stage still sees this
                // vertex's TRUE world distance from the centre and the painted
                // line does not grow with the carrier.
                float across = signedHalf;

                if (halfMag > 1e-6 && _MinPixelWidth > 0.0)
                {
                    float3 centerOS = positionOS - lateralOS * signedHalf;

                    float4 centerCS = TransformObjectToHClip(centerOS);
                    float4 edgeCS   = TransformObjectToHClip(centerOS + lateralOS * halfMag);

                    float wC = abs(centerCS.w);
                    float wE = abs(edgeCS.w);

                    // Behind the camera the projection is meaningless; leave it be.
                    if (wC > 1e-4 && wE > 1e-4)
                    {
                        float2 centerSS = (centerCS.xy / wC) * 0.5 * _ScreenParams.xy;
                        float2 edgeSS   = (edgeCS.xy   / wE) * 0.5 * _ScreenParams.xy;
                        float  pixHalf  = length(edgeSS - centerSS);

                        if (pixHalf > 1e-6)
                        {
                            float scale = max(1.0, (_MinPixelWidth * 0.5) / pixHalf);
                            scale = min(scale, _MaxWidthScale);

                            positionOS = centerOS + lateralOS * signedHalf * scale;
                            across     = signedHalf * scale;
                        }
                    }
                }

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                VertexNormalInputs   normalInputs   = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS   = normalInputs.normalWS;
                output.uv         = input.uv;
                output.across     = across;
                output.color      = input.color;
                output.fogCoord   = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 MarkingFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float d     = input.across;
                float along  = input.uv.y;

                // Taken before any branching so the derivatives stay valid
                // across the whole quad.
                float acrossFootprint = fwidth(d);
                float alongFootprint  = fwidth(along);

                float coverage = BandCoverage(d, _MarkingWidth * 0.5, acrossFootprint);
                coverage *= DashCoverage(along, alongFootprint, input.color.r);

                half alpha = (half)saturate(coverage) * _BaseColor.a;
                clip(alpha - 0.002h);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = _BaseColor.rgb;
                surfaceData.alpha      = alpha;
                surfaceData.metallic   = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS              = input.positionWS;
                inputData.normalWS                = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord             = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord                = input.fogCoord;
                inputData.bakedGI                 = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb   = MixFog(color.rgb, inputData.fogCoord);
                color.a     = alpha;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
