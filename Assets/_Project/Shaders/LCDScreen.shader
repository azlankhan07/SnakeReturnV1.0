// ---------------------------------------------------------------------------------
// LCDScreen — the phone screen surface.
//
// THIS DELIBERATELY DOES NOT QUANTISE TO TWO TONES, AND THAT IS NOT AN OVERSIGHT.
// A faithful 84x48 monochrome Nokia LCD would throw away everything this project's art
// was made for: the modelled snake segments, the baked normal maps, the watermelon, the
// textures. The result would be a worse-looking game that is merely more accurate.
//
// What this shader does instead is put a HINT of LCD character over a modern render —
// a green cast, faint cell lines, a little vignette and a glass sheen. _TintStrength
// defaults to 0.22 for exactly that reason: it is seasoning, not a costume. Cranking it
// to 1 does not make the game more authentic, it makes the art invisible. If you find
// yourself reaching for that slider, the thing you actually want is a different game.
// ---------------------------------------------------------------------------------
Shader "SnakeReturns/LCDScreen"
{
    Properties
    {
        _BaseMap       ("Screen texture", 2D)              = "black" {}
        _Tint          ("LCD tint", Color)                 = (0.60, 0.72, 0.45, 1)
        _TintStrength  ("Tint strength", Range(0,1))       = 0.22
        _Brightness    ("Brightness", Range(0,2))          = 1.02
        _Contrast      ("Contrast", Range(0,2))            = 1.06
        _PixelGrid     ("Pixel grid strength", Range(0,1)) = 0.12
        _GridScale     ("Grid cells (x,y)", Vector)        = (21, 15, 0, 0)
        _GridSharp     ("Grid line width", Range(0.5,8))   = 1.5
        _Vignette      ("Vignette", Range(0,1))            = 0.25
        _VignettePower ("Vignette falloff", Range(0.5,6))  = 2.5
        _Glass         ("Glass sheen", Range(0,1))         = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On
        Cull Back

        Pass
        {
            Name "LCDUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // One CBUFFER named UnityPerMaterial, holding every non-texture property, is what
            // makes this shader SRP-batcher compatible. Miss one property out and the batcher
            // silently drops the whole shader.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float4 _GridScale;
                float  _TintStrength;
                float  _Brightness;
                float  _Contrast;
                float  _PixelGrid;
                float  _GridSharp;
                float  _Vignette;
                float  _VignettePower;
                float  _Glass;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // a) The board, as rendered by Game_Cam into the RenderTexture.
                half3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                // b) Contrast about mid grey, then brightness.
                col = (col - 0.5) * _Contrast + 0.5;
                col *= _Brightness;

                // c) Tint toward the LCD green by luminance, so bright areas take the most
                //    colour and the dark background stays dark rather than turning green.
                half lum = Luminance(col);
                half3 tinted = _Tint.rgb * lum * 1.6;
                col = lerp(col, tinted, _TintStrength);

                // d) Cell grid. Screen-space derivatives keep the lines one pixel wide however
                //    close the camera gets — a fixed-width line in UV space would fatten into
                //    a smear up close and alias into sparkle at a distance.
                float2 cell  = uv * _GridScale.xy;
                float2 wid   = fwidth(cell) * _GridSharp;
                float2 edge  = min(frac(cell), 1.0 - frac(cell));
                float2 lines = 1.0 - smoothstep(0.0, wid, edge);
                float  grid  = max(lines.x, lines.y);
                col *= 1.0 - grid * _PixelGrid;

                // e) Vignette. The 0.70710678 is 1/sqrt(2), so the corners land at exactly 1
                //    and the falloff curve covers its full range instead of clipping early.
                float2 v = uv * 2.0 - 1.0;
                float  r = length(v) * 0.70710678;
                col *= 1.0 - _Vignette * pow(saturate(r), _VignettePower);

                // f) Glass sheen: a soft diagonal band, cubed to keep it tight and subtle.
                float band  = saturate(1.0 - abs((uv.x + uv.y) - 0.62) * 9.0);
                float sheen = band * band * band;
                col += sheen * _Glass;

                // g) The screen is opaque. It is a lit panel, not a window.
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
