Shader "Custom/DOD/Unlit_SpineArray_Instancing"
{
    Properties
    {
        _MainTexArray ("Spine Texture Array", 2DArray) = "" {}
    }

    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent" 
            "IgnoreProjector" = "True" 
            "RenderType" = "Transparent" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 声明 2D 贴图数组及其采样器
            UNITY_DECLARE_TEX2DARRAY(_MainTexArray);

            // 声明每个 Instance 独立的属性 Buffer
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _FrameIndex)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // 获取当前 Instance 对应的帧索引
                float frameIdx = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameIndex);

                // 采样 Texture2DArray (UV坐标需要包含 x, y 和 Slice深度 index)
                fixed4 col = UNITY_SAMPLE_TEX2DARRAY(_MainTexArray, float3(i.uv, frameIdx));
                
                return col;
            }
            ENDCG
        }
    }
}