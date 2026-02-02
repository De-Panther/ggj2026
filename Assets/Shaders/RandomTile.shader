Shader "Custom/URP/RandomTile"
{
  Properties
  {
    _BaseMap ("Base Map", 2D) = "white" {}
    _BaseColor ("Base Color", Color) = (1,1,1,1)
    _BumpMap ("Normal Map", 2D) = "bump" {}
    _Smoothness ("Smoothness", Range(0,1)) = 0.5
    _Metallic ("Metallic", Range(0,1)) = 0.0
    _TileScale ("Tile Scale", Float) = 4
  }

  SubShader
  {
    Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

    Pass
    {
      Name "ForwardLit"
      Tags { "LightMode"="UniversalForward" }

      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag

      // URP lighting
      #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
      #pragma multi_compile _ _ADDITIONAL_LIGHTS
      #pragma multi_compile _ _SHADOWS_SOFT

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

      struct Attributes
      {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
      };

      struct Varyings
      {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        float3 normalWS   : TEXCOORD1;
        float4 tangentWS  : TEXCOORD2;
        float3 positionWS : TEXCOORD3;
      };

      CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Smoothness;
        float _Metallic;
        float _TileScale;
      CBUFFER_END

      // --- Hash function (stable per tile) ---
      float Hash21(float2 p)
      {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
      }

      // --- Random UV per tile ---
      float2 RandomizeTileUV(float2 uv)
      {
        uv *= _TileScale;

        float2 tile = floor(uv);
        float2 local = frac(uv);

        float r = Hash21(tile);

        if (r < 0.25)
        {
          return local;
        }
        if (r < 0.5)
        {
          local.x = 1 - local.x;
          return local;
        }
        if (r < 0.75)
        {
          local.y = 1 - local.y;
          return local;
        }
        return local.yx;
      }

      Varyings vert (Attributes v)
      {
        Varyings o;

        o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
        o.positionCS = TransformWorldToHClip(o.positionWS);
        o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
        o.tangentWS  = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w);
        o.uv         = v.uv;

        return o;
      }

      half4 frag (Varyings i) : SV_Target
      {
        float2 uv = RandomizeTileUV(i.uv);

        // Sample textures
        half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
        half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv));

        // Build surface data
        SurfaceData surfaceData;
        surfaceData.albedo = albedo.rgb;
        surfaceData.metallic = _Metallic;
        surfaceData.specular = half3(0.0, 0.0, 0.0); // REQUIRED
        surfaceData.smoothness = _Smoothness;
        surfaceData.normalTS = normalTS;
        surfaceData.emission = half3(0, 0, 0);
        surfaceData.occlusion = 1.0;
        surfaceData.alpha = albedo.a;
        surfaceData.clearCoatMask = 0.0;            // REQUIRED (URP 12+)
        surfaceData.clearCoatSmoothness = 0.0;      // REQUIRED (URP 12+)

        // Build input data
        InputData inputData;
        inputData.positionWS = i.positionWS;
        inputData.normalWS = NormalizeNormalPerPixel(i.normalWS);
        inputData.viewDirectionWS = GetWorldSpaceViewDir(i.positionWS);
        inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
        inputData.fogCoord = ComputeFogFactor(i.positionCS.z);
        inputData.vertexLighting = 0;
        inputData.bakedGI = SampleSH(i.normalWS);

        // URP lighting
        half4 color = UniversalFragmentPBR(inputData, surfaceData);
        color.rgb = MixFog(color.rgb, inputData.fogCoord);

        return color;
      }
      ENDHLSL
    }
  }
}
