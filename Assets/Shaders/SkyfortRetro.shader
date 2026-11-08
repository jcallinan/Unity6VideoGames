// Retro post-process for Skyfort Strike (Built-in Render Pipeline image effect, driven by SkyfortRetroFilter).
// Snaps the image to a low-resolution pixel grid, dithers and quantizes colors to a limited palette, and can add
// CRT traits: scanlines, screen curvature, an RGB aperture-grille mask, glow and vignette.
Shader "Hidden/Skyfort/Retro"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _Resolution;   // virtual pixel grid (e.g. 398 x 224)
            float _Levels;        // color levels per channel minus one (31 = 15-bit SNES color, 7 = arcade)
            float _Dither;        // ordered-dither strength, 0..1
            float _Scanlines;     // 0..1
            float _Curvature;     // barrel distortion, 0..0.2
            float _Mask;          // aperture-grille strength, 0..1
            float _Vignette;      // 0..1
            float _Glow;          // bright-pixel bleed, 0..1
            float _Saturation;    // 1 = unchanged

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float2 Curve(float2 uv)
            {
                uv = uv * 2.0 - 1.0;
                float2 offset = abs(uv.yx) * _Curvature;
                uv += uv * offset * offset;
                return uv * 0.5 + 0.5;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = Curve(i.uv);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                {
                    return fixed4(0, 0, 0, 1);
                }

                // Sample the center of each virtual pixel: hard-edged, chunky pixels.
                float2 cell = floor(uv * _Resolution);
                float2 snapped = (cell + 0.5) / _Resolution;
                float3 color = tex2D(_MainTex, snapped).rgb;

                if (_Glow > 0.0)
                {
                    float2 o = 1.5 / _Resolution;
                    float3 around = tex2D(_MainTex, snapped + float2(o.x, 0)).rgb + tex2D(_MainTex, snapped - float2(o.x, 0)).rgb
                                  + tex2D(_MainTex, snapped + float2(0, o.y)).rgb + tex2D(_MainTex, snapped - float2(0, o.y)).rgb;
                    color += max(around * 0.25 - 0.55, 0.0) * _Glow;
                }

                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(luma.xxx, color, _Saturation);

                // 4x4 ordered (Bayer) dither, then quantize each channel to the palette's levels.
                int index = (int)(fmod(cell.x, 4.0) + fmod(cell.y, 4.0) * 4.0);
                float threshold = (Bayer[index] + 0.5) / 16.0 - 0.5;
                color = saturate(color + threshold * _Dither / _Levels);
                color = floor(color * _Levels + 0.5) / _Levels;

                // Scanlines follow the virtual pixel rows.
                float row = frac(uv.y * _Resolution.y);
                color *= lerp(1.0, 0.5 + 0.5 * sin(row * 3.14159), _Scanlines);

                // RGB aperture grille on real screen columns.
                float column = fmod(floor(i.uv.x * _ScreenParams.x), 3.0);
                float3 mask = column < 1.0 ? float3(1.0, 0.72, 0.72) : column < 2.0 ? float3(0.72, 1.0, 0.72) : float3(0.72, 0.72, 1.0);
                color *= lerp(float3(1, 1, 1), mask * 1.22, _Mask);

                float2 edge = uv * (1.0 - uv.yx);
                float vignette = pow(saturate(edge.x * edge.y * 15.0), 0.25);
                color *= lerp(1.0, vignette, _Vignette);

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
