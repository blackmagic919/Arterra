// Made with Amplify Shader Editor v1.9.1.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Custom/My Transparent Shader"
{
	Properties
	{
		[HideInInspector] _AlphaCutoff("Alpha Cutoff ", Range(0, 1)) = 0.5
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		[ASEBegin][StyledCategory(Fog Settings, false, _HeightFogStandalone, 10, 10)]_FogCat("[ Fog Cat]", Float) = 1
		[StyledCategory(Skybox Settings, false, _HeightFogStandalone, 10, 10)]_SkyboxCat("[ Skybox Cat ]", Float) = 1
		[StyledCategory(Directional Settings, false, _HeightFogStandalone, 10, 10)]_DirectionalCat("[ Directional Cat ]", Float) = 1
		[StyledCategory(Noise Settings, false, _HeightFogStandalone, 10, 10)]_NoiseCat("[ Noise Cat ]", Float) = 1
		[StyledCategory(Advanced Settings, false, _HeightFogStandalone, 10, 10)]_AdvancedCat("[ Advanced Cat ]", Float) = 1
		[HDR]_Color("Color", Color) = (1,0,0,0)
		[Space(10)]_NoiseIntensity("Noise Intensity", Range( 0 , 0.2)) = 0
		_NoiseScale("Noise Scale", Float) = 6
		_NoiseSpeed("Noise Speed", Vector) = (0.5,0.5,0,0)
		[ASEEnd]_VertexIntensity("Vertex Intensity", Range( 0 , 0.2)) = 0


		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
	}

	SubShader
	{
		LOD 0

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Unlit" }

		Cull Back
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 3.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
						  (( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForwardOnly" }

			Blend One Zero, One Zero
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_OPAQUE_TEXTURE 1


			#pragma multi_compile _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ DEBUG_DISPLAY

			#pragma vertex vert
			#pragma fragment frag

			#define SHADERPASS SHADERPASS_UNLIT

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 worldPos : TEXCOORD0;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord : TEXCOORD1;
				#endif
				#ifdef ASE_FOG
					float fogFactor : TEXCOORD2;
				#endif
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			
			inline float4 ASE_ComputeGrabScreenPos( float4 pos )
			{
				#if UNITY_UV_STARTS_AT_TOP
				float scale = -1.0;
				#else
				float scale = 1.0;
				#endif
				float4 o = pos;
				o.y = pos.w * 0.5f;
				o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
				return o;
			}
			

			VertexOutput VertexFunction ( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				
				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord3 = screenPos;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float4 positionCS = TransformWorldToHClip( positionWS );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					o.worldPos = positionWS;
				#endif

				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					VertexPositionInputs vertexInput = (VertexPositionInputs)0;
					vertexInput.positionWS = positionWS;
					vertexInput.positionCS = positionCS;
					o.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				#ifdef ASE_FOG
					o.fogFactor = ComputeFogFactor( positionCS.z );
				#endif

				o.clipPos = positionCS;

				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
			   return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag ( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 WorldPosition = IN.worldPos;
				#endif

				float4 ShadowCoords = float4( 0, 0, 0, 0 );

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = IN.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				float simplePerlin3D27 = snoise( ( ( WorldPosition * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				float4 screenPos = IN.ase_texcoord3;
				float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
				float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
				float4 fetchOpaqueVal22 = float4( SHADERGRAPH_SAMPLE_SCENE_COLOR( ( ( _NoiseIntensity * simplePerlin3D27 ) + ase_grabScreenPosNorm ).xy ), 1.0 );
				float3 WorldPosition2_g1022 = WorldPosition;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float3 temp_output_96_86_g1019 = (staticSwitch456_g1022).xyz;
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				float3 lerpResult82_g1019 = lerp( saturate( ( _Color * fetchOpaqueVal22 ) ).rgb , temp_output_96_86_g1019 , temp_output_96_87_g1019);
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = lerpResult82_g1019;
				float Alpha = temp_output_96_87_g1019;
				float AlphaClipThreshold = 0.5;
				float AlphaClipThresholdShadow = 0.5;

				#ifdef _ALPHATEST_ON
					clip( Alpha - AlphaClipThreshold );
				#endif

				#if defined(_DBUFFER)
					ApplyDecalToBaseColor(IN.clipPos, Color);
				#endif

				#if defined(_ALPHAPREMULTIPLY_ON)
				Color *= Alpha;
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODDitheringTransition( IN.clipPos.xyz, unity_LODFade.x );
				#endif

				#ifdef ASE_FOG
					Color = MixFog( Color, IN.fogFactor );
				#endif

				return half4( Color, Alpha );
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask 0
			AlphaToMask Off

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106


			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				float3 worldPos : TEXCOORD0;
				#endif
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
				float4 shadowCoord : TEXCOORD1;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			

			VertexOutput VertexFunction( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					o.worldPos = positionWS;
				#endif

				o.clipPos = TransformWorldToHClip( positionWS );
				#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					VertexPositionInputs vertexInput = (VertexPositionInputs)0;
					vertexInput.positionWS = positionWS;
					vertexInput.positionCS = o.clipPos;
					o.shadowCoord = GetShadowCoord( vertexInput );
				#endif

				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
			   return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag(VertexOutput IN  ) : SV_TARGET
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
					float3 WorldPosition = IN.worldPos;
				#endif

				float4 ShadowCoords = float4( 0, 0, 0, 0 );

				#if defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
						ShadowCoords = IN.shadowCoord;
					#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
						ShadowCoords = TransformWorldToShadowCoord( WorldPosition );
					#endif
				#endif

				float3 WorldPosition2_g1022 = WorldPosition;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				

				float Alpha = temp_output_96_87_g1019;
				float AlphaClipThreshold = 0.5;

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODDitheringTransition( IN.clipPos.xyz, unity_LODFade.x );
				#endif
				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
            Name "SceneSelectionPass"
            Tags { "LightMode"="SceneSelectionPass" }

			Cull Off

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			

			int _ObjectId;
			int _PassValue;

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			VertexOutput VertexFunction(VertexInput v  )
			{
				VertexOutput o;
				ZERO_INITIALIZE(VertexOutput, o);

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				
				o.ase_texcoord.xyz = ase_worldPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.w = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				o.clipPos = TransformWorldToHClip(positionWS);

				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag(VertexOutput IN ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_worldPos = IN.ase_texcoord.xyz;
				float3 WorldPosition2_g1022 = ase_worldPos;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				

				surfaceDescription.Alpha = temp_output_96_87_g1019;
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
				return outColor;
			}
			ENDHLSL
		}

		
		Pass
		{
			
            Name "ScenePickingPass"
            Tags { "LightMode"="Picking" }

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			

			float4 _SelectionID;


			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			VertexOutput VertexFunction(VertexInput v  )
			{
				VertexOutput o;
				ZERO_INITIALIZE(VertexOutput, o);

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				
				o.ase_texcoord.xyz = ase_worldPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif
				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				o.clipPos = TransformWorldToHClip(positionWS);
				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag(VertexOutput IN ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_worldPos = IN.ase_texcoord.xyz;
				float3 WorldPosition2_g1022 = ase_worldPos;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				

				surfaceDescription.Alpha = temp_output_96_87_g1019;
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					float alphaClipThreshold = 0.01f;
					#if ALPHA_CLIP_THRESHOLD
						alphaClipThreshold = surfaceDescription.AlphaClipThreshold;
					#endif
					clip(surfaceDescription.Alpha - alphaClipThreshold);
				#endif

				half4 outColor = 0;
				outColor = _SelectionID;

				return outColor;
			}

			ENDHLSL
		}

		
		Pass
		{
			
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }

			ZTest LEqual
			ZWrite On


			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106


			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define VARYINGS_NEED_NORMAL_WS

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			VertexOutput VertexFunction(VertexInput v  )
			{
				VertexOutput o;
				ZERO_INITIALIZE(VertexOutput, o);

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				
				o.ase_texcoord1.xyz = ase_worldPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord1.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float3 normalWS = TransformObjectToWorldNormal(v.ase_normal);

				o.clipPos = TransformWorldToHClip(positionWS);
				o.normalWS.xyz =  normalWS;

				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag(VertexOutput IN ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_worldPos = IN.ase_texcoord1.xyz;
				float3 WorldPosition2_g1022 = ase_worldPos;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				

				surfaceDescription.Alpha = temp_output_96_87_g1019;
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODDitheringTransition( IN.clipPos.xyz, unity_LODFade.x );
				#endif

				float3 normalWS = IN.normalWS;

				return half4(NormalizeNormalPerPixel(normalWS), 0.0);
			}

			ENDHLSL
		}

		
		Pass
		{
			
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }

			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106


			#pragma exclude_renderers glcore gles gles3 
			#pragma vertex vert
			#pragma fragment frag

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define ATTRIBUTES_NEED_TEXCOORD1
			#define VARYINGS_NEED_NORMAL_WS
			#define VARYINGS_NEED_TANGENT_WS

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_VERT_NORMAL
			//Atmospheric Height Fog Defines
			//#define AHF_DISABLE_NOISE3D
			//#define AHF_DISABLE_DIRECTIONAL
			//#define AHF_DISABLE_SKYBOXFOG
			//#define AHF_DISABLE_FALLOFF
			//#define AHF_DEBUG_WORLDPOS


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half3 _NoiseSpeed;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _NoiseScale;
			float _VertexIntensity;
			float _NoiseIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END
			half4 AHF_FogColorStart;
			half4 AHF_FogColorEnd;
			half AHF_FogDistanceStart;
			half AHF_FogDistanceEnd;
			half AHF_FogDistanceFalloff;
			half AHF_FogColorDuo;
			half4 AHF_DirectionalColor;
			half3 AHF_DirectionalDir;
			half AHF_DirectionalIntensity;
			half AHF_DirectionalFalloff;
			half3 AHF_FogAxisOption;
			half AHF_FogHeightEnd;
			half AHF_FarDistanceHeight;
			float AHF_FarDistanceOffset;
			half AHF_FogHeightStart;
			half AHF_FogHeightFalloff;
			half AHF_FogLayersMode;
			half AHF_NoiseScale;
			half3 AHF_NoiseSpeed;
			half AHF_NoiseMin;
			half AHF_NoiseMax;
			half AHF_NoiseDistanceEnd;
			half AHF_NoiseIntensity;
			half AHF_FogIntensity;


			float4 mod289( float4 x )
			{
				return x - floor(x * (1.0 / 289.0)) * 289.0;
			}
			
			float4 perm( float4 x )
			{
				return mod289(((x * 34.0) + 1.0) * x);
			}
			
			float SimpleNoise3D( float3 p )
			{
				    float3 a = floor(p);
				    float3 d = p - a;
				    d = d * d * (3.0 - 2.0 * d);
				    float4 b = a.xxyy + float4(0.0, 1.0, 0.0, 1.0);
				    float4 k1 = perm(b.xyxy);
				    float4 k2 = perm(k1.xyxy + b.zzww);
				    float4 c = k2 + a.zzzz;
				    float4 k3 = perm(c);
				    float4 k4 = perm(c + 1.0);
				    float4 o1 = frac(k3 * (1.0 / 41.0));
				    float4 o2 = frac(k4 * (1.0 / 41.0));
				    float4 o3 = o2 * d.z + o1 * (1.0 - d.z);
				    float2 o4 = o3.yw * d.x + o3.xz * (1.0 - d.x);
				    return o4.y * d.y + o4.x * (1.0 - d.y);
			}
			
			float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			VertexOutput VertexFunction(VertexInput v  )
			{
				VertexOutput o;
				ZERO_INITIALIZE(VertexOutput, o);

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				float simplePerlin3D27 = snoise( ( ( ase_worldPos * _NoiseScale ) + ( -_NoiseSpeed * _TimeParameters.x ) ) );
				
				float3 ase_worldViewDir = ( _WorldSpaceCameraPos.xyz - ase_worldPos );
				ase_worldViewDir = normalize(ase_worldViewDir);
				float3 ase_worldNormal = TransformObjectToWorldNormal(v.ase_normal);
				float fresnelNdotV79 = dot( ase_worldNormal, ase_worldViewDir );
				float fresnelNode79 = ( 0.0 + 1.0 * pow( 1.0 - fresnelNdotV79, 5.0 ) );
				float3 temp_cast_0 = (saturate( ( 1.0 - fresnelNode79 ) )).xxx;
				
				o.ase_texcoord1.xyz = ase_worldPos;
				
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord1.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = ( ( simplePerlin3D27 * _VertexIntensity ) * v.ase_normal );

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = temp_cast_0;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float3 normalWS = TransformObjectToWorldNormal(v.ase_normal);

				o.clipPos = TransformWorldToHClip(positionWS);
				o.normalWS.xyz =  normalWS;

				return o;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 vertex : INTERNALTESSPOS;
				float3 ase_normal : NORMAL;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( VertexInput v )
			{
				VertexControl o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.vertex = v.vertex;
				o.ase_normal = v.ase_normal;
				
				return o;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> v)
			{
				TessellationFactors o;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(v[0].vertex, v[1].vertex, v[2].vertex, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				o.edge[0] = tf.x; o.edge[1] = tf.y; o.edge[2] = tf.z; o.inside = tf.w;
				return o;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			VertexOutput DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				VertexInput o = (VertexInput) 0;
				o.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
				o.ase_normal = patch[0].ase_normal * bary.x + patch[1].ase_normal * bary.y + patch[2].ase_normal * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = o.vertex.xyz - patch[i].ase_normal * (dot(o.vertex.xyz, patch[i].ase_normal) - dot(patch[i].vertex.xyz, patch[i].ase_normal));
				float phongStrength = _TessPhongStrength;
				o.vertex.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * o.vertex.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], o);
				return VertexFunction(o);
			}
			#else
			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}
			#endif

			half4 frag(VertexOutput IN ) : SV_TARGET
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 ase_worldPos = IN.ase_texcoord1.xyz;
				float3 WorldPosition2_g1022 = ase_worldPos;
				float temp_output_7_0_g1025 = AHF_FogDistanceStart;
				float temp_output_155_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1025 ) / ( AHF_FogDistanceEnd - temp_output_7_0_g1025 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1022 = temp_output_155_0_g1022;
				#else
				float staticSwitch467_g1022 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1022 ) ) , AHF_FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1022 = staticSwitch467_g1022;
				float3 lerpResult258_g1022 = lerp( (AHF_FogColorStart).rgb , (AHF_FogColorEnd).rgb , ( ( FogDistanceMask12_g1022 * FogDistanceMask12_g1022 * FogDistanceMask12_g1022 ) * AHF_FogColorDuo ));
				float3 normalizeResult318_g1022 = normalize( ( WorldPosition2_g1022 - _WorldSpaceCameraPos ) );
				float dotResult145_g1022 = dot( normalizeResult318_g1022 , AHF_DirectionalDir );
				half Jitter502_g1022 = 0.0;
				float temp_output_140_0_g1022 = ( saturate( (( dotResult145_g1022 + Jitter502_g1022 )*0.5 + 0.5) ) * AHF_DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1022 = temp_output_140_0_g1022;
				#else
				float staticSwitch470_g1022 = pow( abs( temp_output_140_0_g1022 ) , AHF_DirectionalFalloff );
				#endif
				float DirectionalMask30_g1022 = staticSwitch470_g1022;
				float3 lerpResult40_g1022 = lerp( lerpResult258_g1022 , (AHF_DirectionalColor).rgb , DirectionalMask30_g1022);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1022 = lerpResult258_g1022;
				#else
				float3 staticSwitch442_g1022 = lerpResult40_g1022;
				#endif
				half3 Input_Color6_g1023 = staticSwitch442_g1022;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1023 = Input_Color6_g1023;
				#else
				float3 staticSwitch1_g1023 = ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * ( ( Input_Color6_g1023 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1022 = staticSwitch1_g1023;
				half3 AHF_FogAxisOption181_g1022 = AHF_FogAxisOption;
				float3 break159_g1022 = ( WorldPosition2_g1022 * AHF_FogAxisOption181_g1022 );
				float temp_output_7_0_g1026 = AHF_FogDistanceEnd;
				float temp_output_643_0_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1026 ) / ( ( AHF_FogDistanceEnd + AHF_FarDistanceOffset ) - temp_output_7_0_g1026 ) ) );
				half FogDistanceMaskFar645_g1022 = ( temp_output_643_0_g1022 * temp_output_643_0_g1022 );
				float lerpResult690_g1022 = lerp( AHF_FogHeightEnd , ( AHF_FogHeightEnd + AHF_FarDistanceHeight ) , FogDistanceMaskFar645_g1022);
				float temp_output_7_0_g1027 = lerpResult690_g1022;
				float temp_output_167_0_g1022 = saturate( ( ( ( break159_g1022.x + break159_g1022.y + break159_g1022.z ) - temp_output_7_0_g1027 ) / ( AHF_FogHeightStart - temp_output_7_0_g1027 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1022 = temp_output_167_0_g1022;
				#else
				float staticSwitch468_g1022 = pow( abs( temp_output_167_0_g1022 ) , AHF_FogHeightFalloff );
				#endif
				half FogHeightMask16_g1022 = staticSwitch468_g1022;
				float lerpResult328_g1022 = lerp( ( FogDistanceMask12_g1022 * FogHeightMask16_g1022 ) , saturate( ( FogDistanceMask12_g1022 + FogHeightMask16_g1022 ) ) , AHF_FogLayersMode);
				float mulTime204_g1022 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1022 = ( ( WorldPosition2_g1022 * ( 1.0 / AHF_NoiseScale ) ) + ( -AHF_NoiseSpeed * mulTime204_g1022 ) );
				float3 p1_g1031 = temp_output_197_0_g1022;
				float localSimpleNoise3D1_g1031 = SimpleNoise3D( p1_g1031 );
				float temp_output_7_0_g1030 = AHF_NoiseMin;
				float temp_output_7_0_g1029 = AHF_NoiseDistanceEnd;
				half NoiseDistanceMask7_g1022 = saturate( ( ( distance( WorldPosition2_g1022 , _WorldSpaceCameraPos ) - temp_output_7_0_g1029 ) / ( 0.0 - temp_output_7_0_g1029 ) ) );
				float lerpResult198_g1022 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1031 - temp_output_7_0_g1030 ) / ( AHF_NoiseMax - temp_output_7_0_g1030 ) ) ) , ( NoiseDistanceMask7_g1022 * AHF_NoiseIntensity ));
				half NoiseSimplex3D24_g1022 = lerpResult198_g1022;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1022 = lerpResult328_g1022;
				#else
				float staticSwitch42_g1022 = ( lerpResult328_g1022 * NoiseSimplex3D24_g1022 );
				#endif
				float temp_output_454_0_g1022 = ( staticSwitch42_g1022 * AHF_FogIntensity );
				half Final_Alpha463_g1022 = temp_output_454_0_g1022;
				float4 appendResult114_g1022 = (float4(Final_Color462_g1022 , Final_Alpha463_g1022));
				float4 appendResult457_g1022 = (float4(WorldPosition2_g1022 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1022 = appendResult457_g1022;
				#else
				float4 staticSwitch456_g1022 = appendResult114_g1022;
				#endif
				float temp_output_96_87_g1019 = (staticSwitch456_g1022).w;
				

				surfaceDescription.Alpha = temp_output_96_87_g1019;
				surfaceDescription.AlphaClipThreshold = 0.5;

				#if _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODDitheringTransition( IN.clipPos.xyz, unity_LODFade.x );
				#endif

				float3 normalWS = IN.normalWS;

				return half4(NormalizeNormalPerPixel(normalWS), 0.0);
			}

			ENDHLSL
		}
		
	}
	
	CustomEditor "UnityEditor.ShaderGraphUnlitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback Off
}
/*ASEBEGIN
Version=19108
Node;AmplifyShaderEditor.Vector3Node;30;-1280,1664;Half;False;Property;_NoiseSpeed;Noise Speed;46;0;Create;True;0;0;0;False;0;False;0.5,0.5,0;0.5,0.5,0;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleTimeNode;31;-1280,1824;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.NegateNode;36;-1088,1664;Inherit;False;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.WorldPosInputsNode;39;-1280,1280;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;32;-1280,1440;Half;False;Property;_NoiseScale;Noise Scale;45;0;Create;True;0;0;0;False;0;False;6;1.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;37;-960,1664;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;35;-960,1344;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleAddOpNode;33;-768,1536;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;27;-640,1536;Inherit;False;Simplex3D;False;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;41;0,1024;Inherit;False;Property;_NoiseIntensity;Noise Intensity;44;0;Create;True;0;0;0;False;1;Space(10);False;0;0.103;0;0.2;0;1;FLOAT;0
Node;AmplifyShaderEditor.GrabScreenPosition;23;256,1152;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;40;288,1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;24;448,1024;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RangedFloatNode;50;0,1728;Inherit;False;Property;_VertexIntensity;Vertex Intensity;47;0;Create;True;0;0;0;False;0;False;0;0.103;0;0.2;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;5;640,896;Inherit;False;Property;_Color;Color;43;1;[HDR];Create;True;0;0;0;False;0;False;1,0,0,0;1,0,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.FresnelNode;79;1408,1664;Inherit;False;Standard;WorldNormal;ViewDir;False;False;5;0;FLOAT3;0,0,1;False;4;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;5;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenColorNode;22;640,1152;Inherit;False;Global;_GrabScreen0;Grab Screen 0;1;0;Create;True;0;0;0;False;0;False;Object;-1;False;False;False;False;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.NormalVertexDataNode;49;384,1792;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.OneMinusNode;80;1664,1664;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;44;896,896;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;45;384,1664;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;82;1056,896;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;81;1824,1664;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;48;704,1664;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.CommentaryNode;76;0,768;Inherit;False;1182;100;Grab Screen Color;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;83;1408,1536;Inherit;False;588.5403;100;Edge Opacity;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;51;-1280,1152;Inherit;False;832.0697;100;Noise;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;54;0,1536;Inherit;False;826.2407;100;Vertex Animaton;0;;1,1,1,1;0;0
Node;AmplifyShaderEditor.FunctionNode;98;1280,896;Inherit;False;Apply Height Fog Unlit;0;;1019;950890317d4f36a48a68d150cdab0168;0;1;81;FLOAT3;0,0,0;False;3;FLOAT3;85;FLOAT3;86;FLOAT;87
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;99;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;5;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;100;2048,896;Float;False;True;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;13;Custom/My Transparent Shader;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;8;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;1;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=UniversalForwardOnly;False;False;0;;0;0;Standard;23;Surface;0;0;  Blend;0;0;Two Sided;1;0;Forward Only;0;0;Cast Shadows;0;638186283833608412;  Use Shadow Threshold;0;0;Receive Shadows;0;638186283864800509;GPU Instancing;0;638186283872324931;LOD CrossFade;0;0;Built-in Fog;0;0;DOTS Instancing;0;0;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Vertex Position,InvertActionOnDeselection;1;0;0;10;False;True;False;True;False;False;True;True;True;True;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;101;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=ShadowCaster;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;102;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;True;1;LightMode=DepthOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;103;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;104;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;105;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;106;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;107;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;108;2048,896;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;True;9;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;0;;0;0;Standard;0;False;0
WireConnection;36;0;30;0
WireConnection;37;0;36;0
WireConnection;37;1;31;0
WireConnection;35;0;39;0
WireConnection;35;1;32;0
WireConnection;33;0;35;0
WireConnection;33;1;37;0
WireConnection;27;0;33;0
WireConnection;40;0;41;0
WireConnection;40;1;27;0
WireConnection;24;0;40;0
WireConnection;24;1;23;0
WireConnection;22;0;24;0
WireConnection;80;0;79;0
WireConnection;44;0;5;0
WireConnection;44;1;22;0
WireConnection;45;0;27;0
WireConnection;45;1;50;0
WireConnection;82;0;44;0
WireConnection;81;0;80;0
WireConnection;48;0;45;0
WireConnection;48;1;49;0
WireConnection;98;81;82;0
WireConnection;100;2;98;85
WireConnection;100;3;98;87
WireConnection;100;5;48;0
WireConnection;100;6;81;0
ASEEND*/
//CHKSM=BE8301E8C7B1A2588CB4DCCDFAF2CEEA856D0BB5