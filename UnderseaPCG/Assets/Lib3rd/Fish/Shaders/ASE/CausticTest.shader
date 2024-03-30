Shader "Unlit/CausticTest"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _UVScale("UVScale",float) = 1.0
        _TimeScale("TimeScale",Range(0,3)) = 1.0
       // _FractalParam("Fractal",Vector) = (0.5,0.4,0.3,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UVScale;
          //  float4 _FractalParam;
            float _TimeScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

           

            float2 randomVec(float2 uv)
            {
	            float vec = dot(uv, float2(127.1, 311.7));
	            return -1.0 + 2.0 * frac(sin(vec) * 43758.5453123);
            }

   
            fixed4 frag (v2f i) : SV_Target
            {
                 #define F length(.5-frac(k.xyz =mul(float3x3(-2,-1,2, 3,-2,1, 1,2,2),k.xyz)*
                // sample the texture
                float4 k = float4(1,0,0,0);
                 k.z = _Time.x*_TimeScale;
                float2 p = (i.uv)*_UVScale*float2(900,800);
                //fixed4 col = tex2D(_MainTex, i.uv);
                k.xy = p*(sin(0.3))/2e2;
                 
                k = pow(min(min(F .5)),F .4))),F .3))), 6)*25+float4(0,0,0,1);
                float4 col = k;
         
                return col;
            }
            ENDCG
        }
    }
}
