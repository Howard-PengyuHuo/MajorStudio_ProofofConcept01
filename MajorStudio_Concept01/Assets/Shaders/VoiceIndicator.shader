Shader "UI/VoiceIndicator"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _LightAmount ("Light Amount", Range(0,1)) = 0
        _LampCenter ("Lamp Center (UV)", Vector) = (0.132,0.669,0,0)
        _LampRadius ("Lamp Radius", Float) = 0.079
        _TextureAspect ("Texture Aspect", Float) = 2.092
        _GlowColor ("Glow Color", Color) = (1,0.09,0.025,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd, _GlowColor;
            float4 _ClipRect, _LampCenter;
            float _LightAmount, _LampRadius, _TextureAspect;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 col=(tex2D(_MainTex,i.uv)+_TextureSampleAdd)*i.color;
                float2 delta=(i.uv-_LampCenter.xy)*float2(_TextureAspect,1);
                float radius=max(_LampRadius,0.001);
                float d=length(delta)/radius;
                float core=1-smoothstep(0.75,1.02,d);
                float halo=exp(-d*d*1.2)*(1-smoothstep(1.5,2.7,d));
                float pulse=0.94+0.06*sin(_Time.y*5);
                // Brighten the original painted lamp while retaining its concentric details.
                col.rgb *= 1-0.3*core*(1-_LightAmount);
                col.rgb += _LightAmount*pulse*(core*_GlowColor.rgb*0.48+halo*_GlowColor.rgb*0.34);
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a-0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
