// Built-in RP equirectangular (latitude/longitude) skybox for the photographic skies in
// Resources/Sky.
//
// Why not Unity's own Skybox/Panoramic: a shader only survives a player build if something
// included in the build references it, and the only skybox the scene's RenderSettings names
// is the built-in procedural one. Shader.Find("Skybox/Panoramic") therefore returns null in
// a shipped build and the sky goes flat. A .shader sitting in Resources is always included,
// which is the same route Resources/Shaders/HairCard already takes.
//
// The mapping here is the exact inverse of the one in Tools/skyprep.py, which is what lets
// that script measure each sky's sun position and hand back a directional-light angle that
// agrees with the image.
Shader "Trickshot/SkyPanoramic"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama (lat/long)", 2D) = "grey" {}
        _Tint ("Grade", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        _GroundColor ("Below Horizon", Color) = (0.42, 0.44, 0.42, 1)
        _GroundBlend ("Below Horizon Blend", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half4 _GroundColor;
            half _GroundBlend;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Unity draws the skybox mesh with only the camera's rotation applied, so the
                // object-space position of each corner IS the world view direction.
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // Yaw the sky. Rotating the sampling direction (rather than the image) means a
                // venue can keep its authored shadow direction and still get a sky whose sun
                // sits in the right place: skyprep.py solves for the angle that lines the two up.
                float a = _Rotation * UNITY_PI / 180.0;
                float sa, ca;
                sincos(a, sa, ca);
                d = float3(ca * d.x - sa * d.z, d.y, sa * d.x + ca * d.z);

                float lat = acos(clamp(d.y, -1.0, 1.0));
                float lon = atan2(d.z, -d.x);
                float2 uv = float2(0.5 - lon / (2.0 * UNITY_PI), 1.0 - lat / UNITY_PI);
                // Half a texel off each pole, so the bilinear tap at the exact zenith does not
                // wrap around and fetch a row from the opposite end of the image.
                uv.y = clamp(uv.y, _MainTex_TexelSize.y * 0.5, 1.0 - _MainTex_TexelSize.y * 0.5);

                // The longitude wrap makes u jump by a whole texture width between two adjacent
                // pixels, which the hardware reads as an enormous rate of change and answers with
                // the smallest mip: a blurred seam straight up the sky. Unwrap the derivatives
                // before handing them over.
                float2 dx = float2(ddx(uv.x), ddx(uv.y));
                float2 dy = float2(ddy(uv.x), ddy(uv.y));
                if (abs(dx.x) > 0.5) dx.x -= sign(dx.x);
                if (abs(dy.x) > 0.5) dy.x -= sign(dy.x);

                half3 c = tex2Dgrad(_MainTex, uv, dx, dy).rgb * _Tint.rgb * _Exposure;

                // These panoramas are sky only, so below the horizon is a mirrored gradient
                // rather than ground. Anything the pitch and the stands do not cover should read
                // as far-off haze, not as an upside-down sky.
                half below = saturate(-d.y * 3.0) * _GroundBlend;
                c = lerp(c, _GroundColor.rgb * _Exposure, below);

                return half4(c, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
