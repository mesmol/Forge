Shader "Fluxy/Simulation/Splat"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Velocity ("Texture", 2D) = "black" {}
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _BlendOp ("__op", Float) = 1.0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "../FluidUtils.hlsl"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 tileuv : TEXCOORD1;
                float4 vertex : SV_POSITION;
            }; 

            v2f vert (appdata_lean v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.vertex.xy = VertexToSplat(o.vertex.xy,_TileIndex);
                o.uv = v.uv;
                
                float2 vertex = o.vertex.xy;

                #if UNITY_UV_STARTS_AT_TOP
                    vertex.y *= -1;
                #endif 

                o.tileuv = vertex * 0.5f + 0.5f;
                return o;
            }
        ENDHLSL

        Pass
        {
            Name "Splat state"
            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend]
            
            HLSLPROGRAM
            float4 frag (v2f i) : SV_Target
            {
                int tile = GetTileID(i.tileuv);
                if (tile != _TileIndex) discard;
            
                return tex2D(_MainTex, i.uv) * _SplatWeights;
            }
            ENDHLSL
        }

        Pass
        {

            Name "Splat Velocity"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RG

            HLSLPROGRAM

            float _AngularVelocity;

            float4 frag (v2f i) : SV_Target
            {            
                int tile = GetTileID(i.tileuv);
                if (tile != _TileIndex) discard;

                float2 offset = _MainTex_TexelSize.xy;
                
                float4 splatVel = _SplatWeights * tex2D(_MainTex, i.uv).a;
                float4 textureVel = tex2D(_Velocity, i.uv);

                float pL =  tex2D(_MainTex, i.uv + float2(-offset.x,0)).a;
                float pR =  tex2D(_MainTex, i.uv + float2(offset.x, 0)).a;
                float pB =  tex2D(_MainTex, i.uv + float2(0,-offset.y)).a;
                float pT =  tex2D(_MainTex, i.uv + float2(0, offset.y)).a;
                float2 pressureVel = -float2(pR - pL, pT - pB) * 0.5;
                
                // due to depth velocity:
                splatVel.xy += pressureVel.xy * abs(splatVel.z);

                // due to linear velocity:
                splatVel.xy += RotateVector(UnpackVelocity(textureVel.rg)) * textureVel.b;

                // due to angular velocity:
                float2 r = RotateVector((i.uv - 0.5f)* _SplatTransform.zw);
                splatVel.xy += float2(-_AngularVelocity*r.y,_AngularVelocity*r.x);
                
                return float4(splatVel.xy,0,splatVel.w);
                             
            }
            ENDHLSL
        }
    }
}
