Shader "Custom/InstancedShader"
{
    Properties
    {
        _Color("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            float4 _Color;

            StructuredBuffer<float4x4> _Matrices;
            float4x4 _VP; // View-Projection matrix passed from C#

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : NORMAL;
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;

                float4x4 model = _Matrices[instanceID];

                // Object → World
                float4 worldPos = mul(model, float4(IN.positionOS, 1.0));

                // World → Clip
                OUT.positionCS = mul(_VP, worldPos);

                // Normal to world space
                OUT.normalWS = normalize(mul((float3x3)model, IN.normalOS));

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 lightDir = normalize(float3(0.3, 1.0, 0.5));
                float diff = max(0, dot(IN.normalWS, lightDir));
                return float4(_Color.rgb * diff, 1.0);
            }
            ENDHLSL
        }
    }
}
