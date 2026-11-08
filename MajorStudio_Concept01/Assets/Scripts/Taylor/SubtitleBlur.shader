Shader "CarryOn/SubtitleBlur"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Direction;
            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 result = tex2D(_MainTex, i.uv) * .227027;
                result += tex2D(_MainTex, i.uv + _Direction.xy * 1.384615) * .316216;
                result += tex2D(_MainTex, i.uv - _Direction.xy * 1.384615) * .316216;
                result += tex2D(_MainTex, i.uv + _Direction.xy * 3.230769) * .070270;
                result += tex2D(_MainTex, i.uv - _Direction.xy * 3.230769) * .070270;
                return result;
            }
            ENDCG
        }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragCapture
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4x4 _CaptureMVP;
            fixed4 _Color;
            float _IsSDF;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            Output vert(Input v)
            {
                Output o;
                o.vertex = mul(_CaptureMVP, v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 fragCapture(Output i) : SV_Target
            {
                fixed4 sample = tex2D(_MainTex, i.uv);
                if (_IsSDF > .5)
                    sample = fixed4(1, 1, 1, smoothstep(.45, .55, sample.a));
                return sample * i.color;
            }
            ENDCG
        }
    }
}
