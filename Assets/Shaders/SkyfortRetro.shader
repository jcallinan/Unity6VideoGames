// Retro post-process for Skyfort Strike (Built-in Render Pipeline image effect, driven by SkyfortRetroFilter).
// Gives the image a soft, low-resolution look: a coarse pixel grid whose pixels blur into each other like an old
// composite video signal, a gently reduced color palette, a wide neon glow, and optional CRT traits
// (soft scanlines, slight screen curvature, a faint RGB mask, color fringing and vignette).
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
            float2 _Resolution;   // virtual pixel grid (e.g. 480 x 270)
            float _Softness;      // 0 = hard pixels, 1 = pixels fully blended with neighbors
            float _Levels;        // color levels per channel minus one (31 = 15-bit color)
            float _Dither;        // ordered-dither strength, 0..1
            float _Scanlines;     // 0..1
            float _Curvature;     // barrel distortion, 0..0.2
            float _Mask;          // aperture-grille strength, 0..1
            float _Vignette;      // 0..1
            float _Glow;          // neon bloom strength
            float _Fringe;        // horizontal color fringing, in virtual pixels
            float _Saturation;    // 1 = unchanged
            float _Contrast;      // 1 = unchanged; higher deepens darks and brightens lights

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float2 Curve(float2 uv)
            {
                uv = uv * 2.0 - 1.0;
                float2 offset = abs(uv.yx) * _Curvature;
                uv += uv * offset * offset;
                return uv * 0.5 + 0.5;
            }

            // Soft sample: a small horizontal-weighted blur over about one virtual pixel, like a composite signal.
            float3 Soft(float2 uv, float2 px)
            {
                float3 c = tex2D(_MainTex, uv).rgb * 0.36;
                c += (tex2D(_MainTex, uv + float2(px.x, 0)).rgb + tex2D(_MainTex, uv - float2(px.x, 0)).rgb) * 0.2;
                c += (tex2D(_MainTex, uv + float2(0, px.y * 0.6)).rgb + tex2D(_MainTex, uv - float2(0, px.y * 0.6)).rgb) * 0.12;
                return c;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = Curve(i.uv);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                {
                    return fixed4(0, 0, 0, 1);
                }

                float2 px = 1.0 / _Resolution;
                float2 cell = floor(uv * _Resolution);
                float2 snapped = (cell + 0.5) * px;

                // Blend the hard pixel with a softened sample so edges read clearly but are not jagged.
                float3 hard = tex2D(_MainTex, snapped).rgb;
                float3 soft = Soft(uv, px);
                if (_Fringe > 0.0)
                {
                    // Red and blue drift slightly apart, as on an analog tube.
                    soft.r = Soft(uv + float2(px.x * _Fringe, 0), px).r;
                    soft.b = Soft(uv - float2(px.x * _Fringe, 0), px).b;
                }

                float3 color = lerp(hard, soft, _Softness);

                // Wide neon bloom: bright pixels spill light over a few virtual pixels around them.
                if (_Glow > 0.0)
                {
                    float3 bloom = 0;
                    float2 r1 = px * 2.0;
                    float2 r2 = px * 4.5;
                    bloom += tex2D(_MainTex, uv + float2(r1.x, 0)).rgb + tex2D(_MainTex, uv - float2(r1.x, 0)).rgb;
                    bloom += tex2D(_MainTex, uv + float2(0, r1.y)).rgb + tex2D(_MainTex, uv - float2(0, r1.y)).rgb;
                    bloom += tex2D(_MainTex, uv + r2).rgb + tex2D(_MainTex, uv - r2).rgb;
                    bloom += tex2D(_MainTex, uv + float2(r2.x, -r2.y)).rgb + tex2D(_MainTex, uv + float2(-r2.x, r2.y)).rgb;
                    bloom /= 8.0;
                    // Only neon-bright light blooms; ordinary lit surfaces stay clean.
                    float bloomLuma = dot(bloom, float3(0.299, 0.587, 0.114));
                    color += bloom * saturate((bloomLuma - 0.7) / 0.3) * _Glow * 0.5;
                }

                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(luma.xxx, color, _Saturation);
                color = saturate((color - 0.5) * _Contrast + 0.5);

                // Light ordered dither, then quantize each channel to the palette's levels.
                int index = (int)(fmod(cell.x, 4.0) + fmod(cell.y, 4.0) * 4.0);
                float threshold = (Bayer[index] + 0.5) / 16.0 - 0.5;
                color = saturate(color + threshold * _Dither / _Levels);
                color = floor(color * _Levels + 0.5) / _Levels;

                // Soft scanlines follow the virtual pixel rows.
                float row = frac(uv.y * _Resolution.y);
                color *= lerp(1.0, 0.72 + 0.28 * sin(row * 3.14159), _Scanlines);

                // Faint RGB aperture grille on real screen columns.
                float column = fmod(floor(i.uv.x * _ScreenParams.x), 3.0);
                float3 mask = column < 1.0 ? float3(1.0, 0.82, 0.82) : column < 2.0 ? float3(0.82, 1.0, 0.82) : float3(0.82, 0.82, 1.0);
                color *= lerp(float3(1, 1, 1), mask * 1.12, _Mask);

                float2 edge = uv * (1.0 - uv.yx);
                float vignette = pow(saturate(edge.x * edge.y * 18.0), 0.2);
                color *= lerp(1.0, vignette, _Vignette);

                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
