Shader "LD/FloatingTextRenderFeature/TextInstanced"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Columns ("Atlas Columns", Float) = 0
        _Rows    ("Atlas Rows",    Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Overlay"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "FloatingTextUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Columns;
                float _Rows;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half   alpha       : TEXCOORD1;
                float  charIndex   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.alpha       = (half)UNITY_MATRIX_M._m02;
                output.charIndex   = UNITY_MATRIX_M._m12;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv          = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uv = input.uv;

                // Atlas mode: _Columns > 0
                if (_Columns > 0)
                {
                    float2 cellSize = float2(1.0 / _Columns, 1.0 / _Rows);
                    float col = fmod(input.charIndex, _Columns);
                    float row = floor(input.charIndex / _Columns);
                    uv = input.uv * cellSize + float2(col * cellSize.x, (_Rows - 1 - row) * cellSize.y);
                }

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return texColor * half4(1, 1, 1, input.alpha);
            }

            ENDHLSL
        }
    }
}
