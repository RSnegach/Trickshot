// Built-in RP alpha-cutout hair-CARD shader for the textured quad ribbons built by HairSim.
//
// Each simulated strand is now a flat ribbon (quad strip) UV-mapped to one vertical strip of a
// hair atlas. The atlas is a GRAYSCALE OPACITY MASK (white strands on black, no alpha channel),
// so opacity comes from the sampled COLOR (r), not a texture alpha. Pixels below _Cutoff are
// clipped, giving the wispy see-through strand edges a line mesh can't. The strand is tinted by
// the player's hair colour (_Color) and lit with the same Kajiya-Kay tangent model as the strand
// shader (a line/ribbon has no useful surface normal; HairSim bakes the strand tangent into
// mesh.normals and the root->tip factor into uv... uv.xy is the atlas UV, uv2.x is root->tip).
Shader "Trickshot/HairCard"
{
    Properties
    {
        _MainTex    ("Hair Atlas (grayscale opacity)", 2D) = "white" {}
        _Color      ("Hair Color", Color) = (0.3, 0.2, 0.1, 1)
        _Cutoff     ("Alpha Cutoff", Range(0,1)) = 0.4
        _SpecColor1 ("Spec Tint", Color) = (1, 1, 1, 1)
        _SpecExp    ("Spec Sharpness", Range(1,200)) = 50
        _SpecStr    ("Spec Strength", Range(0,4)) = 0.8
        _SpecShift  ("Highlight Shift", Range(-1,1)) = 0.15
        _DiffWrap   ("Diffuse Wrap", Range(0,1)) = 0.5
        _RootDark   ("Root Darkening", Range(0,1)) = 0.35
        _Ambient    ("Ambient Boost", Range(0,2)) = 1.0
    }

    SubShader
    {
        // AlphaTest queue + cutout so the see-through ribbons sort and write depth cleanly (no
        // blend-order artifacts across the many overlapping cards).
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Cull Off        // a thin ribbon is seen from both sides
        ZWrite On

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
            fixed   _Cutoff;
            fixed4 _SpecColor1;
            float  _SpecExp;
            float  _SpecStr;
            float  _SpecShift;
            float  _DiffWrap;
            float  _RootDark;
            float  _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;     // strand TANGENT (local), baked by HairSim
                float2 uv     : TEXCOORD0;  // atlas UV (into a strip of _MainTex)
                float2 uv2    : TEXCOORD1;  // uv2.x = root(0)->tip(1)
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 tangentW : TEXCOORD1;
                float3 viewW    : TEXCOORD2;
                float  rootTip  : TEXCOORD3;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.tangentW = normalize(UnityObjectToWorldDir(v.normal));
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewW = normalize(_WorldSpaceCameraPos - worldPos);
                o.rootTip = v.uv2.x;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Grayscale atlas: use the red channel as opacity, clip the gaps between strands.
                fixed opacity = tex2D(_MainTex, i.uv).r;
                clip(opacity - _Cutoff);

                float3 t = normalize(i.tangentW);
                float3 v = normalize(i.viewW);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);

                // Kajiya-Kay tangent diffuse (wrapped so the shadow side stays lit).
                float dotTL = dot(t, l);
                float sinTL = sqrt(max(1.0 - dotTL*dotTL, 0.0));
                float diff  = saturate((sinTL + _DiffWrap) / (1.0 + _DiffWrap));

                // One shifted anisotropic highlight.
                float3 tS = normalize(t + _SpecShift * float3(0,1,0));
                float3 h  = normalize(l + v);
                float dTH = dot(tS, h);
                float sTH = sqrt(max(1.0 - dTH*dTH, 0.0));
                float spec = pow(sTH, _SpecExp) * _SpecStr;

                fixed3 baseCol = _Color.rgb;
                baseCol *= lerp(1.0 - _RootDark, 1.0, saturate(i.rootTip));  // roots a touch darker

                fixed3 ambient = baseCol * ShadeSH9(float4(0,1,0,1)) * _Ambient;
                fixed3 lit = baseCol * diff * _LightColor0.rgb;
                fixed3 sp  = _SpecColor1.rgb * spec * _LightColor0.rgb;

                return fixed4(ambient + lit + sp, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
