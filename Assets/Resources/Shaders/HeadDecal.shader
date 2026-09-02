// Built-in RP alpha-BLENDED decal for regions DRAWN ON a body surface: the buzz-cut scalp tint,
// stubble, a mohawk's shaved band, painted mask faces, an eye patch. Sits a few mm proud of the
// sphere with a depth Offset so it never z-fights the skin, writes no depth (a decal must not
// occlude the hair cards built over it), and lights with a wrapped Lambert on the GEOMETRIC
// normal plus SH ambient so it shades exactly like the skin under it.
//
// alpha = _Color.a * tex.r * vertexColor.a. The texture is a grayscale mask (r), tiled by
// _MainTex_ST, so one small tileable stipple serves every scalp; vertex alpha carries the edge
// fade and any latitude ramp, which the mesh builder sets per vertex.
//
// Lives in Resources so it ships in a player build: the Standard shader's Fade variant is stripped
// (no material asset in the project uses it), the same trap Make.cs documents for Standard itself.
Shader "Trickshot/HeadDecal"
{
    Properties
    {
        _MainTex  ("Mask (grayscale, tiled)", 2D) = "white" {}
        _Color    ("Tint (alpha = opacity)", Color) = (0.2, 0.15, 0.1, 0.6)
        _DiffWrap ("Diffuse Wrap", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset -1, -1
        Cull Off        // a decal is a skin; never lose one to a winding mistake

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _DiffWrap;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float3 nrmW  : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.nrmW = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed mask = tex2D(_MainTex, i.uv).r;
                float3 n = normalize(i.nrmW);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float ndl = dot(n, l);
                float diff = saturate((ndl + _DiffWrap) / (1.0 + _DiffWrap));
                fixed3 col = _Color.rgb * (ShadeSH9(float4(n, 1)) + diff * _LightColor0.rgb);
                fixed a = _Color.a * mask * i.color.a;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
