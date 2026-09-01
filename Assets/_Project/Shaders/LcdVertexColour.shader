// ---------------------------------------------------------------------------------
// LcdVertexColour — tint x vertex colour, opaque, nothing else.
//
// WHY THIS EXISTS. LcdText packs BOTH the glyph colour and the outline colour into one
// mesh, in mesh.colors — that is the whole reason it can draw a run of text and its
// border in a single draw call. Stock "Universal Render Pipeline/Unlit" ignores vertex
// colours completely, so under it every quad comes out the same _BaseColor and the
// outline is drawn perfectly, in exactly the same colour as the glyph, and is therefore
// invisible. That failure looks like the outline code never ran.
// ---------------------------------------------------------------------------------
Shader "SnakeReturns/LcdVertexColour"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (0.92, 1, 0.85, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On

        // Cull Off: the text mesh is a flat sheet of quads with no thickness, and winding is
        // not worth policing for something that can only ever be seen from one side anyway.
        Cull Off

        Pass
        {
            Name "LcdUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 colour     : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 colour      : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.colour = input.colour;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 rgb = _BaseColor.rgb * input.colour.rgb;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
