// Built-in Render Pipeline anisotropic hair shader for the line-mesh strands built by HairSim.
//
// The strands are drawn as GL line primitives, so there is no surface normal to light. Instead
// this uses the KAJIYA-KAY hair model, which lights a strand from its TANGENT (the along-strand
// direction) rather than a normal - the physically-right model for hair and exactly the data a
// line mesh can provide. HairSim bakes the per-vertex world-ready tangent into mesh.normals and a
// root->tip factor (0 at the scalp, 1 at the tip) into mesh.uv.x.
//
// Look: a soft wrapped diffuse so hair never goes flat-black on the shadow side, plus a DUAL
// anisotropic specular streak (a primary highlight tinted toward the light + a secondary,
// shifted, tinted toward the hair colour) - the shifted double streak is what reads as "hair"
// far more than the geometry does. Roots are darkened slightly for depth. Unlit-cheap: one
// directional light + flat ambient, no shadows, Cull Off (a line has no facing).
Shader "Trickshot/HairStrand"
{
    Properties
    {
        _Color      ("Hair Color", Color) = (0.3, 0.2, 0.1, 1)
        _SpecColor1 ("Primary Spec Tint", Color) = (1, 1, 1, 1)
        _SpecColor2 ("Secondary Spec Tint", Color) = (0.5, 0.4, 0.3, 1)
        _SpecExp1   ("Primary Sharpness", Range(1, 200)) = 60
        _SpecExp2   ("Secondary Sharpness", Range(1, 200)) = 18
        _SpecShift  ("Highlight Shift", Range(-1, 1)) = 0.12
        _Spec1Str   ("Primary Strength", Range(0, 4)) = 1.1
        _Spec2Str   ("Secondary Strength", Range(0, 4)) = 0.5
        _DiffWrap   ("Diffuse Wrap", Range(0, 1)) = 0.5
        _RootDark   ("Root Darkening", Range(0, 1)) = 0.45
        _Ambient    ("Ambient Boost", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off        // a line primitive has no facing; light it from both sides
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"       // _LightColor0, _WorldSpaceLightPos0
            #include "AutoLight.cginc"

            fixed4 _Color;
            fixed4 _SpecColor1;
            fixed4 _SpecColor2;
            float  _SpecExp1;
            float  _SpecExp2;
            float  _SpecShift;
            float  _Spec1Str;
            float  _Spec2Str;
            float  _DiffWrap;
            float  _RootDark;
            float  _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;    // HairSim packs the STRAND TANGENT (local) here
                float2 uv     : TEXCOORD0; // uv.x = root(0)->tip(1) factor
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 tangentW : TEXCOORD0;   // world-space strand tangent
                float3 viewW    : TEXCOORD1;   // world-space view direction
                float  rootTip  : TEXCOORD2;   // 0 at scalp, 1 at tip
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Object->world for the tangent (direction, so use the rotation part).
                o.tangentW = normalize(UnityObjectToWorldDir(v.normal));
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewW = normalize(_WorldSpaceCameraPos - worldPos);
                o.rootTip = v.uv.x;
                return o;
            }

            // One shifted anisotropic specular lobe (Kajiya-Kay / Scheuermann).
            float StrandSpec(float3 t, float3 v, float3 l, float shift, float exponent)
            {
                float3 tShift = normalize(t + shift * float3(0,1,0)); // tilt the highlight along the strand
                float3 h = normalize(l + v);
                float dotTH = dot(tShift, h);
                float sinTH = sqrt(max(1.0 - dotTH * dotTH, 0.0));    // sin(tangent, half)
                return pow(sinTH, exponent);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 t = normalize(i.tangentW);
                float3 v = normalize(i.viewW);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);        // directional light

                // Hair diffuse from the tangent: sin(t,l), wrapped so the shadow side stays lit.
                float dotTL = dot(t, l);
                float sinTL = sqrt(max(1.0 - dotTL * dotTL, 0.0));
                float diff  = saturate((sinTL + _DiffWrap) / (1.0 + _DiffWrap));

                // Dual shifted highlights: primary tinted to the light, secondary to the hair.
                float s1 = StrandSpec(t, v, l, _SpecShift,        _SpecExp1) * _Spec1Str;
                float s2 = StrandSpec(t, v, l, _SpecShift * -1.5, _SpecExp2) * _Spec2Str;

                fixed3 baseCol = _Color.rgb;
                // Roots sit deeper in the head -> a touch darker for depth (root=0 darkest).
                baseCol *= lerp(1.0 - _RootDark, 1.0, saturate(i.rootTip));

                fixed3 ambient = baseCol * ShadeSH9(float4(0,1,0,1)) * _Ambient; // flat SH ambient
                fixed3 lit = baseCol * diff * _LightColor0.rgb;
                fixed3 spec = (_SpecColor1.rgb * s1 + _SpecColor2.rgb * s2) * _LightColor0.rgb;
                // Secondary lobe is modulated by the hair colour so it reads as a sheen, not white paint.
                spec = lerp(spec, spec * baseCol, 0.4);

                return fixed4(ambient + lit + spec, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
