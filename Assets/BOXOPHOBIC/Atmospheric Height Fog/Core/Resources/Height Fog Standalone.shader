// Made with Amplify Shader Editor v1.9.1.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "BOXOPHOBIC/Atmospherics/Height Fog Standalone"
{
	Properties
	{
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		[HideInInspector] _AlphaCutoff("Alpha Cutoff ", Range(0, 1)) = 0.5
		[StyledBanner(Height Fog Standalone)]_Banner("Banner", Float) = 0
		[StyledCategory(Fog Settings, false, _HeightFogStandalone, 10, 10)]_FogCat("[ Fog Cat]", Float) = 1
		_FogIntensity("Fog Intensity", Range( 0 , 1)) = 1
		[Enum(X Axis,0,Y Axis,1,Z Axis,2)][Space(10)]_FogAxisMode("Fog Axis Mode", Float) = 1
		[Enum(Multiply Distance and Height,0,Additive Distance and Height,1)]_FogLayersMode("Fog Layers Mode", Float) = 0
		[Enum(Perspective,0,Orthographic,1,Both,2)]_FogCameraMode("Fog Camera Mode", Float) = 0
		[HDR][Space(10)]_FogColorStart("Fog Color Start", Color) = (0.4411765,0.722515,1,0)
		[HDR]_FogColorEnd("Fog Color End", Color) = (0.4411765,0.722515,1,0)
		_FogColorDuo("Fog Color Duo", Range( 0 , 1)) = 1
		[Space(10)]_FogDistanceStart("Fog Distance Start", Float) = 0
		_FogDistanceEnd("Fog Distance End", Float) = 100
		_FogDistanceFalloff("Fog Distance Falloff", Range( 1 , 8)) = 2
		[Space(10)]_FogHeightStart("Fog Height Start", Float) = 0
		_FogHeightEnd("Fog Height End", Float) = 100
		_FogHeightFalloff("Fog Height Falloff", Range( 1 , 8)) = 2
		[Space(10)]_FarDistanceHeight("Far Distance Height", Float) = 0
		_FarDistanceOffset("Far Distance Offset", Float) = 0
		[StyledCategory(Skybox Settings, false, _HeightFogStandalone, 10, 10)]_SkyboxCat("[ Skybox Cat ]", Float) = 1
		_SkyboxFogIntensity("Skybox Fog Intensity", Range( 0 , 1)) = 0
		_SkyboxFogHeight("Skybox Fog Height", Range( 0 , 8)) = 1
		_SkyboxFogFalloff("Skybox Fog Falloff", Range( 1 , 8)) = 2
		_SkyboxFogOffset("Skybox Fog Offset", Range( -1 , 1)) = 0
		_SkyboxFogBottom("Skybox Fog Bottom", Range( 0 , 1)) = 0
		_SkyboxFogFill("Skybox Fog Fill", Range( 0 , 1)) = 0
		[StyledCategory(Directional Settings, false, _HeightFogStandalone, 10, 10)]_DirectionalCat("[ Directional Cat ]", Float) = 1
		[HDR]_DirectionalColor("Directional Color", Color) = (1,0.8280286,0.6084906,0)
		_DirectionalIntensity("Directional Intensity", Range( 0 , 1)) = 1
		_DirectionalFalloff("Directional Falloff", Range( 1 , 8)) = 2
		[StyledVector(18)]_DirectionalDir("Directional Dir", Vector) = (1,1,1,0)
		[StyledCategory(Noise Settings, false, _HeightFogStandalone, 10, 10)]_NoiseCat("[ Noise Cat ]", Float) = 1
		_NoiseIntensity("Noise Intensity", Range( 0 , 1)) = 1
		_NoiseMin("Noise Min", Range( 0 , 1)) = 0
		_NoiseMax("Noise Max", Range( 0 , 1)) = 1
		_NoiseScale("Noise Scale", Float) = 30
		[StyledVector(18)]_NoiseSpeed("Noise Speed", Vector) = (0.5,0.5,0,0)
		[Space(10)]_NoiseDistanceEnd("Noise Distance End", Float) = 200
		[StyledCategory(Advanced Settings, false, _HeightFogStandalone, 10, 10)]_AdvancedCat("[ Advanced Cat ]", Float) = 1
		[ASEEnd]_JitterIntensity("Jitter Intensity", Float) = 0
		[HideInInspector]_FogAxisOption("_FogAxisOption", Vector) = (0,0,0,0)
		[HideInInspector]_HeightFogStandalone("_HeightFogStandalone", Float) = 1
		[HideInInspector]_IsHeightFogShader("_IsHeightFogShader", Float) = 1


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

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

		Cull Front
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 3.0
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
			Tags { "LightMode"="UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest Always
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_DEPTH_TEXTURE 1


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

			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_FRAG_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
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
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _FogAxisOption;
			half3 _DirectionalDir;
			half3 _NoiseSpeed;
			float _FarDistanceOffset;
			half _FogHeightStart;
			half _FogHeightFalloff;
			half _FogLayersMode;
			half _NoiseScale;
			half _NoiseMax;
			half _FarDistanceHeight;
			half _NoiseDistanceEnd;
			half _NoiseIntensity;
			half _FogIntensity;
			half _SkyboxFogOffset;
			half _SkyboxFogHeight;
			half _SkyboxFogFalloff;
			half _SkyboxFogBottom;
			half _NoiseMin;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			uniform float4 _CameraDepthTexture_TexelSize;


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
			

			VertexOutput VertexFunction ( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord3 = screenPos;
				
				o.ase_texcoord4 = v.vertex;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = v.ase_normal;

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

				float4 screenPos = IN.ase_texcoord3;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float eyeDepth218_g1027 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float4 unityObjectToClipPos224_g1027 = TransformWorldToHClip(TransformObjectToWorld(IN.ase_texcoord4.xyz));
				float4 computeScreenPos225_g1027 = ComputeScreenPos( unityObjectToClipPos224_g1027 );
				half3 WorldPosFromDepth_SRP567_g1027 = ( _WorldSpaceCameraPos - ( eyeDepth218_g1027 * ( ( _WorldSpaceCameraPos - WorldPosition ) / computeScreenPos225_g1027.w ) ) );
				float3 objToView587_g1027 = mul( UNITY_MATRIX_MV, float4( IN.ase_texcoord4.xyz, 1 ) ).xyz;
				float clampDepth572_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				float lerpResult577_g1027 = lerp( ( 1.0 - clampDepth572_g1027 ) , clampDepth572_g1027 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1027 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1027);
				float3 appendResult582_g1027 = (float3(objToView587_g1027.x , objToView587_g1027.y , -lerpResult579_g1027));
				float3 viewToWorld583_g1027 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1027, 1 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1027 = viewToWorld583_g1027;
				float3 lerpResult593_g1027 = lerp( WorldPosFromDepth_SRP567_g1027 , WorldPosFromDepth_SRP_Ortho584_g1027 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined(AHF_CAMERAMODE_PERSPECTIVE)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#elif defined(AHF_CAMERAMODE_ORTHOGRAPHIC)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP_Ortho584_g1027;
				#elif defined(AHF_CAMERAMODE_BOTH)
				float3 staticSwitch598_g1027 = lerpResult593_g1027;
				#else
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#endif
				half3 WorldPosFromDepth253_g1027 = staticSwitch598_g1027;
				float3 WorldPosition2_g1027 = WorldPosFromDepth253_g1027;
				float temp_output_7_0_g1030 = _FogDistanceStart;
				float temp_output_155_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1030 ) / ( _FogDistanceEnd - temp_output_7_0_g1030 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1027 = temp_output_155_0_g1027;
				#else
				float staticSwitch467_g1027 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1027 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1027 = staticSwitch467_g1027;
				float3 lerpResult258_g1027 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1027 * FogDistanceMask12_g1027 * FogDistanceMask12_g1027 ) * _FogColorDuo ));
				float3 normalizeResult318_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float dotResult145_g1027 = dot( normalizeResult318_g1027 , _DirectionalDir );
				float4 ScreenPos3_g1029 = screenPos;
				float2 UV13_g1029 = ( ( (ScreenPos3_g1029).xy / (ScreenPos3_g1029).z ) * (_ScreenParams).xy );
				float3 Magic14_g1029 = float3(0.06711056,0.00583715,52.98292);
				float dotResult16_g1029 = dot( UV13_g1029 , (Magic14_g1029).xy );
				float lerpResult494_g1027 = lerp( 0.0 , frac( ( frac( dotResult16_g1029 ) * (Magic14_g1029).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1027 = lerpResult494_g1027;
				float temp_output_140_0_g1027 = ( saturate( (( dotResult145_g1027 + Jitter502_g1027 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1027 = temp_output_140_0_g1027;
				#else
				float staticSwitch470_g1027 = pow( abs( temp_output_140_0_g1027 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1027 = staticSwitch470_g1027;
				float3 lerpResult40_g1027 = lerp( lerpResult258_g1027 , (_DirectionalColor).rgb , DirectionalMask30_g1027);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1027 = lerpResult258_g1027;
				#else
				float3 staticSwitch442_g1027 = lerpResult40_g1027;
				#endif
				half3 Input_Color6_g1028 = staticSwitch442_g1027;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1028 = Input_Color6_g1028;
				#else
				float3 staticSwitch1_g1028 = ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1027 = staticSwitch1_g1028;
				half3 AHF_FogAxisOption181_g1027 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1027 = ( WorldPosition2_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_7_0_g1031 = _FogDistanceEnd;
				float temp_output_643_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1031 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1031 ) ) );
				half FogDistanceMaskFar645_g1027 = ( temp_output_643_0_g1027 * temp_output_643_0_g1027 );
				float lerpResult614_g1027 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1027);
				float temp_output_7_0_g1032 = lerpResult614_g1027;
				float temp_output_167_0_g1027 = saturate( ( ( ( break159_g1027.x + break159_g1027.y + break159_g1027.z ) - temp_output_7_0_g1032 ) / ( _FogHeightStart - temp_output_7_0_g1032 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1027 = temp_output_167_0_g1027;
				#else
				float staticSwitch468_g1027 = pow( abs( temp_output_167_0_g1027 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1027 = staticSwitch468_g1027;
				float lerpResult328_g1027 = lerp( ( FogDistanceMask12_g1027 * FogHeightMask16_g1027 ) , saturate( ( FogDistanceMask12_g1027 + FogHeightMask16_g1027 ) ) , _FogLayersMode);
				float mulTime204_g1027 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1027 = ( ( WorldPosition2_g1027 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1027 ) );
				float3 p1_g1036 = temp_output_197_0_g1027;
				float localSimpleNoise3D1_g1036 = SimpleNoise3D( p1_g1036 );
				float temp_output_7_0_g1035 = _NoiseMin;
				float temp_output_7_0_g1034 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1034 ) / ( 0.0 - temp_output_7_0_g1034 ) ) );
				float lerpResult198_g1027 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1036 - temp_output_7_0_g1035 ) / ( _NoiseMax - temp_output_7_0_g1035 ) ) ) , ( NoiseDistanceMask7_g1027 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1027 = lerpResult198_g1027;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1027 = lerpResult328_g1027;
				#else
				float staticSwitch42_g1027 = ( lerpResult328_g1027 * NoiseSimplex3D24_g1027 );
				#endif
				float temp_output_454_0_g1027 = ( staticSwitch42_g1027 * _FogIntensity );
				float3 normalizeResult169_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float3 break170_g1027 = ( normalizeResult169_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_449_0_g1027 = ( ( break170_g1027.x + break170_g1027.y + break170_g1027.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1033 = _SkyboxFogHeight;
				float temp_output_176_0_g1027 = saturate( ( ( abs( temp_output_449_0_g1027 ) - temp_output_7_0_g1033 ) / ( 0.0 - temp_output_7_0_g1033 ) ) );
				float saferPower309_g1027 = abs( temp_output_176_0_g1027 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1027 = temp_output_176_0_g1027;
				#else
				float staticSwitch469_g1027 = pow( saferPower309_g1027 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1027 = lerp( saturate( ( staticSwitch469_g1027 + ( _SkyboxFogBottom * step( temp_output_449_0_g1027 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1027 = ( lerpResult179_g1027 * _SkyboxFogIntensity );
				float clampDepth118_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1027 = clampDepth118_g1027;
				#else
				float staticSwitch123_g1027 = ( 1.0 - clampDepth118_g1027 );
				#endif
				half SkyboxFogMask95_g1027 = ( 1.0 - ceil( staticSwitch123_g1027 ) );
				float lerpResult112_g1027 = lerp( temp_output_454_0_g1027 , SkyboxFogHeightMask108_g1027 , SkyboxFogMask95_g1027);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1027 = temp_output_454_0_g1027;
				#else
				float staticSwitch455_g1027 = lerpResult112_g1027;
				#endif
				half Final_Alpha463_g1027 = staticSwitch455_g1027;
				float4 appendResult114_g1027 = (float4(Final_Color462_g1027 , Final_Alpha463_g1027));
				float4 appendResult457_g1027 = (float4(WorldPosition2_g1027 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1027 = appendResult457_g1027;
				#else
				float4 staticSwitch456_g1027 = appendResult114_g1027;
				#endif
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = (staticSwitch456_g1027).xyz;
				float Alpha = (staticSwitch456_g1027).w;
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
			
            Name "SceneSelectionPass"
            Tags { "LightMode"="SceneSelectionPass" }

			Cull Off

			HLSLPROGRAM

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_DEPTH_TEXTURE 1


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

			#define ASE_NEEDS_FRAG_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
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
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _FogAxisOption;
			half3 _DirectionalDir;
			half3 _NoiseSpeed;
			float _FarDistanceOffset;
			half _FogHeightStart;
			half _FogHeightFalloff;
			half _FogLayersMode;
			half _NoiseScale;
			half _NoiseMax;
			half _FarDistanceHeight;
			half _NoiseDistanceEnd;
			half _NoiseIntensity;
			half _FogIntensity;
			half _SkyboxFogOffset;
			half _SkyboxFogHeight;
			half _SkyboxFogFalloff;
			half _SkyboxFogBottom;
			half _NoiseMin;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			uniform float4 _CameraDepthTexture_TexelSize;


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

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord = screenPos;
				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				o.ase_texcoord1.xyz = ase_worldPos;
				
				o.ase_texcoord2 = v.vertex;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord1.w = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = v.ase_normal;

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

				float4 screenPos = IN.ase_texcoord;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float eyeDepth218_g1027 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float3 ase_worldPos = IN.ase_texcoord1.xyz;
				float4 unityObjectToClipPos224_g1027 = TransformWorldToHClip(TransformObjectToWorld(IN.ase_texcoord2.xyz));
				float4 computeScreenPos225_g1027 = ComputeScreenPos( unityObjectToClipPos224_g1027 );
				half3 WorldPosFromDepth_SRP567_g1027 = ( _WorldSpaceCameraPos - ( eyeDepth218_g1027 * ( ( _WorldSpaceCameraPos - ase_worldPos ) / computeScreenPos225_g1027.w ) ) );
				float3 objToView587_g1027 = mul( UNITY_MATRIX_MV, float4( IN.ase_texcoord2.xyz, 1 ) ).xyz;
				float clampDepth572_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				float lerpResult577_g1027 = lerp( ( 1.0 - clampDepth572_g1027 ) , clampDepth572_g1027 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1027 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1027);
				float3 appendResult582_g1027 = (float3(objToView587_g1027.x , objToView587_g1027.y , -lerpResult579_g1027));
				float3 viewToWorld583_g1027 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1027, 1 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1027 = viewToWorld583_g1027;
				float3 lerpResult593_g1027 = lerp( WorldPosFromDepth_SRP567_g1027 , WorldPosFromDepth_SRP_Ortho584_g1027 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined(AHF_CAMERAMODE_PERSPECTIVE)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#elif defined(AHF_CAMERAMODE_ORTHOGRAPHIC)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP_Ortho584_g1027;
				#elif defined(AHF_CAMERAMODE_BOTH)
				float3 staticSwitch598_g1027 = lerpResult593_g1027;
				#else
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#endif
				half3 WorldPosFromDepth253_g1027 = staticSwitch598_g1027;
				float3 WorldPosition2_g1027 = WorldPosFromDepth253_g1027;
				float temp_output_7_0_g1030 = _FogDistanceStart;
				float temp_output_155_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1030 ) / ( _FogDistanceEnd - temp_output_7_0_g1030 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1027 = temp_output_155_0_g1027;
				#else
				float staticSwitch467_g1027 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1027 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1027 = staticSwitch467_g1027;
				float3 lerpResult258_g1027 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1027 * FogDistanceMask12_g1027 * FogDistanceMask12_g1027 ) * _FogColorDuo ));
				float3 normalizeResult318_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float dotResult145_g1027 = dot( normalizeResult318_g1027 , _DirectionalDir );
				float4 ScreenPos3_g1029 = screenPos;
				float2 UV13_g1029 = ( ( (ScreenPos3_g1029).xy / (ScreenPos3_g1029).z ) * (_ScreenParams).xy );
				float3 Magic14_g1029 = float3(0.06711056,0.00583715,52.98292);
				float dotResult16_g1029 = dot( UV13_g1029 , (Magic14_g1029).xy );
				float lerpResult494_g1027 = lerp( 0.0 , frac( ( frac( dotResult16_g1029 ) * (Magic14_g1029).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1027 = lerpResult494_g1027;
				float temp_output_140_0_g1027 = ( saturate( (( dotResult145_g1027 + Jitter502_g1027 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1027 = temp_output_140_0_g1027;
				#else
				float staticSwitch470_g1027 = pow( abs( temp_output_140_0_g1027 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1027 = staticSwitch470_g1027;
				float3 lerpResult40_g1027 = lerp( lerpResult258_g1027 , (_DirectionalColor).rgb , DirectionalMask30_g1027);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1027 = lerpResult258_g1027;
				#else
				float3 staticSwitch442_g1027 = lerpResult40_g1027;
				#endif
				half3 Input_Color6_g1028 = staticSwitch442_g1027;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1028 = Input_Color6_g1028;
				#else
				float3 staticSwitch1_g1028 = ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1027 = staticSwitch1_g1028;
				half3 AHF_FogAxisOption181_g1027 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1027 = ( WorldPosition2_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_7_0_g1031 = _FogDistanceEnd;
				float temp_output_643_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1031 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1031 ) ) );
				half FogDistanceMaskFar645_g1027 = ( temp_output_643_0_g1027 * temp_output_643_0_g1027 );
				float lerpResult614_g1027 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1027);
				float temp_output_7_0_g1032 = lerpResult614_g1027;
				float temp_output_167_0_g1027 = saturate( ( ( ( break159_g1027.x + break159_g1027.y + break159_g1027.z ) - temp_output_7_0_g1032 ) / ( _FogHeightStart - temp_output_7_0_g1032 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1027 = temp_output_167_0_g1027;
				#else
				float staticSwitch468_g1027 = pow( abs( temp_output_167_0_g1027 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1027 = staticSwitch468_g1027;
				float lerpResult328_g1027 = lerp( ( FogDistanceMask12_g1027 * FogHeightMask16_g1027 ) , saturate( ( FogDistanceMask12_g1027 + FogHeightMask16_g1027 ) ) , _FogLayersMode);
				float mulTime204_g1027 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1027 = ( ( WorldPosition2_g1027 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1027 ) );
				float3 p1_g1036 = temp_output_197_0_g1027;
				float localSimpleNoise3D1_g1036 = SimpleNoise3D( p1_g1036 );
				float temp_output_7_0_g1035 = _NoiseMin;
				float temp_output_7_0_g1034 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1034 ) / ( 0.0 - temp_output_7_0_g1034 ) ) );
				float lerpResult198_g1027 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1036 - temp_output_7_0_g1035 ) / ( _NoiseMax - temp_output_7_0_g1035 ) ) ) , ( NoiseDistanceMask7_g1027 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1027 = lerpResult198_g1027;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1027 = lerpResult328_g1027;
				#else
				float staticSwitch42_g1027 = ( lerpResult328_g1027 * NoiseSimplex3D24_g1027 );
				#endif
				float temp_output_454_0_g1027 = ( staticSwitch42_g1027 * _FogIntensity );
				float3 normalizeResult169_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float3 break170_g1027 = ( normalizeResult169_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_449_0_g1027 = ( ( break170_g1027.x + break170_g1027.y + break170_g1027.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1033 = _SkyboxFogHeight;
				float temp_output_176_0_g1027 = saturate( ( ( abs( temp_output_449_0_g1027 ) - temp_output_7_0_g1033 ) / ( 0.0 - temp_output_7_0_g1033 ) ) );
				float saferPower309_g1027 = abs( temp_output_176_0_g1027 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1027 = temp_output_176_0_g1027;
				#else
				float staticSwitch469_g1027 = pow( saferPower309_g1027 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1027 = lerp( saturate( ( staticSwitch469_g1027 + ( _SkyboxFogBottom * step( temp_output_449_0_g1027 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1027 = ( lerpResult179_g1027 * _SkyboxFogIntensity );
				float clampDepth118_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1027 = clampDepth118_g1027;
				#else
				float staticSwitch123_g1027 = ( 1.0 - clampDepth118_g1027 );
				#endif
				half SkyboxFogMask95_g1027 = ( 1.0 - ceil( staticSwitch123_g1027 ) );
				float lerpResult112_g1027 = lerp( temp_output_454_0_g1027 , SkyboxFogHeightMask108_g1027 , SkyboxFogMask95_g1027);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1027 = temp_output_454_0_g1027;
				#else
				float staticSwitch455_g1027 = lerpResult112_g1027;
				#endif
				half Final_Alpha463_g1027 = staticSwitch455_g1027;
				float4 appendResult114_g1027 = (float4(Final_Color462_g1027 , Final_Alpha463_g1027));
				float4 appendResult457_g1027 = (float4(WorldPosition2_g1027 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1027 = appendResult457_g1027;
				#else
				float4 staticSwitch456_g1027 = appendResult114_g1027;
				#endif
				

				surfaceDescription.Alpha = (staticSwitch456_g1027).w;
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_DEPTH_TEXTURE 1


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

			#define ASE_NEEDS_FRAG_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
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
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _FogAxisOption;
			half3 _DirectionalDir;
			half3 _NoiseSpeed;
			float _FarDistanceOffset;
			half _FogHeightStart;
			half _FogHeightFalloff;
			half _FogLayersMode;
			half _NoiseScale;
			half _NoiseMax;
			half _FarDistanceHeight;
			half _NoiseDistanceEnd;
			half _NoiseIntensity;
			half _FogIntensity;
			half _SkyboxFogOffset;
			half _SkyboxFogHeight;
			half _SkyboxFogFalloff;
			half _SkyboxFogBottom;
			half _NoiseMin;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			uniform float4 _CameraDepthTexture_TexelSize;


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

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord = screenPos;
				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				o.ase_texcoord1.xyz = ase_worldPos;
				
				o.ase_texcoord2 = v.vertex;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord1.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif
				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif
				v.ase_normal = v.ase_normal;

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

				float4 screenPos = IN.ase_texcoord;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float eyeDepth218_g1027 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float3 ase_worldPos = IN.ase_texcoord1.xyz;
				float4 unityObjectToClipPos224_g1027 = TransformWorldToHClip(TransformObjectToWorld(IN.ase_texcoord2.xyz));
				float4 computeScreenPos225_g1027 = ComputeScreenPos( unityObjectToClipPos224_g1027 );
				half3 WorldPosFromDepth_SRP567_g1027 = ( _WorldSpaceCameraPos - ( eyeDepth218_g1027 * ( ( _WorldSpaceCameraPos - ase_worldPos ) / computeScreenPos225_g1027.w ) ) );
				float3 objToView587_g1027 = mul( UNITY_MATRIX_MV, float4( IN.ase_texcoord2.xyz, 1 ) ).xyz;
				float clampDepth572_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				float lerpResult577_g1027 = lerp( ( 1.0 - clampDepth572_g1027 ) , clampDepth572_g1027 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1027 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1027);
				float3 appendResult582_g1027 = (float3(objToView587_g1027.x , objToView587_g1027.y , -lerpResult579_g1027));
				float3 viewToWorld583_g1027 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1027, 1 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1027 = viewToWorld583_g1027;
				float3 lerpResult593_g1027 = lerp( WorldPosFromDepth_SRP567_g1027 , WorldPosFromDepth_SRP_Ortho584_g1027 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined(AHF_CAMERAMODE_PERSPECTIVE)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#elif defined(AHF_CAMERAMODE_ORTHOGRAPHIC)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP_Ortho584_g1027;
				#elif defined(AHF_CAMERAMODE_BOTH)
				float3 staticSwitch598_g1027 = lerpResult593_g1027;
				#else
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#endif
				half3 WorldPosFromDepth253_g1027 = staticSwitch598_g1027;
				float3 WorldPosition2_g1027 = WorldPosFromDepth253_g1027;
				float temp_output_7_0_g1030 = _FogDistanceStart;
				float temp_output_155_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1030 ) / ( _FogDistanceEnd - temp_output_7_0_g1030 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1027 = temp_output_155_0_g1027;
				#else
				float staticSwitch467_g1027 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1027 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1027 = staticSwitch467_g1027;
				float3 lerpResult258_g1027 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1027 * FogDistanceMask12_g1027 * FogDistanceMask12_g1027 ) * _FogColorDuo ));
				float3 normalizeResult318_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float dotResult145_g1027 = dot( normalizeResult318_g1027 , _DirectionalDir );
				float4 ScreenPos3_g1029 = screenPos;
				float2 UV13_g1029 = ( ( (ScreenPos3_g1029).xy / (ScreenPos3_g1029).z ) * (_ScreenParams).xy );
				float3 Magic14_g1029 = float3(0.06711056,0.00583715,52.98292);
				float dotResult16_g1029 = dot( UV13_g1029 , (Magic14_g1029).xy );
				float lerpResult494_g1027 = lerp( 0.0 , frac( ( frac( dotResult16_g1029 ) * (Magic14_g1029).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1027 = lerpResult494_g1027;
				float temp_output_140_0_g1027 = ( saturate( (( dotResult145_g1027 + Jitter502_g1027 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1027 = temp_output_140_0_g1027;
				#else
				float staticSwitch470_g1027 = pow( abs( temp_output_140_0_g1027 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1027 = staticSwitch470_g1027;
				float3 lerpResult40_g1027 = lerp( lerpResult258_g1027 , (_DirectionalColor).rgb , DirectionalMask30_g1027);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1027 = lerpResult258_g1027;
				#else
				float3 staticSwitch442_g1027 = lerpResult40_g1027;
				#endif
				half3 Input_Color6_g1028 = staticSwitch442_g1027;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1028 = Input_Color6_g1028;
				#else
				float3 staticSwitch1_g1028 = ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1027 = staticSwitch1_g1028;
				half3 AHF_FogAxisOption181_g1027 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1027 = ( WorldPosition2_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_7_0_g1031 = _FogDistanceEnd;
				float temp_output_643_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1031 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1031 ) ) );
				half FogDistanceMaskFar645_g1027 = ( temp_output_643_0_g1027 * temp_output_643_0_g1027 );
				float lerpResult614_g1027 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1027);
				float temp_output_7_0_g1032 = lerpResult614_g1027;
				float temp_output_167_0_g1027 = saturate( ( ( ( break159_g1027.x + break159_g1027.y + break159_g1027.z ) - temp_output_7_0_g1032 ) / ( _FogHeightStart - temp_output_7_0_g1032 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1027 = temp_output_167_0_g1027;
				#else
				float staticSwitch468_g1027 = pow( abs( temp_output_167_0_g1027 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1027 = staticSwitch468_g1027;
				float lerpResult328_g1027 = lerp( ( FogDistanceMask12_g1027 * FogHeightMask16_g1027 ) , saturate( ( FogDistanceMask12_g1027 + FogHeightMask16_g1027 ) ) , _FogLayersMode);
				float mulTime204_g1027 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1027 = ( ( WorldPosition2_g1027 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1027 ) );
				float3 p1_g1036 = temp_output_197_0_g1027;
				float localSimpleNoise3D1_g1036 = SimpleNoise3D( p1_g1036 );
				float temp_output_7_0_g1035 = _NoiseMin;
				float temp_output_7_0_g1034 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1034 ) / ( 0.0 - temp_output_7_0_g1034 ) ) );
				float lerpResult198_g1027 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1036 - temp_output_7_0_g1035 ) / ( _NoiseMax - temp_output_7_0_g1035 ) ) ) , ( NoiseDistanceMask7_g1027 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1027 = lerpResult198_g1027;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1027 = lerpResult328_g1027;
				#else
				float staticSwitch42_g1027 = ( lerpResult328_g1027 * NoiseSimplex3D24_g1027 );
				#endif
				float temp_output_454_0_g1027 = ( staticSwitch42_g1027 * _FogIntensity );
				float3 normalizeResult169_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float3 break170_g1027 = ( normalizeResult169_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_449_0_g1027 = ( ( break170_g1027.x + break170_g1027.y + break170_g1027.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1033 = _SkyboxFogHeight;
				float temp_output_176_0_g1027 = saturate( ( ( abs( temp_output_449_0_g1027 ) - temp_output_7_0_g1033 ) / ( 0.0 - temp_output_7_0_g1033 ) ) );
				float saferPower309_g1027 = abs( temp_output_176_0_g1027 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1027 = temp_output_176_0_g1027;
				#else
				float staticSwitch469_g1027 = pow( saferPower309_g1027 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1027 = lerp( saturate( ( staticSwitch469_g1027 + ( _SkyboxFogBottom * step( temp_output_449_0_g1027 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1027 = ( lerpResult179_g1027 * _SkyboxFogIntensity );
				float clampDepth118_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1027 = clampDepth118_g1027;
				#else
				float staticSwitch123_g1027 = ( 1.0 - clampDepth118_g1027 );
				#endif
				half SkyboxFogMask95_g1027 = ( 1.0 - ceil( staticSwitch123_g1027 ) );
				float lerpResult112_g1027 = lerp( temp_output_454_0_g1027 , SkyboxFogHeightMask108_g1027 , SkyboxFogMask95_g1027);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1027 = temp_output_454_0_g1027;
				#else
				float staticSwitch455_g1027 = lerpResult112_g1027;
				#endif
				half Final_Alpha463_g1027 = staticSwitch455_g1027;
				float4 appendResult114_g1027 = (float4(Final_Color462_g1027 , Final_Alpha463_g1027));
				float4 appendResult457_g1027 = (float4(WorldPosition2_g1027 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1027 = appendResult457_g1027;
				#else
				float4 staticSwitch456_g1027 = appendResult114_g1027;
				#endif
				

				surfaceDescription.Alpha = (staticSwitch456_g1027).w;
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_DEPTH_TEXTURE 1


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

			#define ASE_NEEDS_FRAG_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
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
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _FogAxisOption;
			half3 _DirectionalDir;
			half3 _NoiseSpeed;
			float _FarDistanceOffset;
			half _FogHeightStart;
			half _FogHeightFalloff;
			half _FogLayersMode;
			half _NoiseScale;
			half _NoiseMax;
			half _FarDistanceHeight;
			half _NoiseDistanceEnd;
			half _NoiseIntensity;
			half _FogIntensity;
			half _SkyboxFogOffset;
			half _SkyboxFogHeight;
			half _SkyboxFogFalloff;
			half _SkyboxFogBottom;
			half _NoiseMin;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			uniform float4 _CameraDepthTexture_TexelSize;


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

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord1 = screenPos;
				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				o.ase_texcoord2.xyz = ase_worldPos;
				
				o.ase_texcoord3 = v.vertex;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord2.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = v.ase_normal;

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

				float4 screenPos = IN.ase_texcoord1;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float eyeDepth218_g1027 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float3 ase_worldPos = IN.ase_texcoord2.xyz;
				float4 unityObjectToClipPos224_g1027 = TransformWorldToHClip(TransformObjectToWorld(IN.ase_texcoord3.xyz));
				float4 computeScreenPos225_g1027 = ComputeScreenPos( unityObjectToClipPos224_g1027 );
				half3 WorldPosFromDepth_SRP567_g1027 = ( _WorldSpaceCameraPos - ( eyeDepth218_g1027 * ( ( _WorldSpaceCameraPos - ase_worldPos ) / computeScreenPos225_g1027.w ) ) );
				float3 objToView587_g1027 = mul( UNITY_MATRIX_MV, float4( IN.ase_texcoord3.xyz, 1 ) ).xyz;
				float clampDepth572_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				float lerpResult577_g1027 = lerp( ( 1.0 - clampDepth572_g1027 ) , clampDepth572_g1027 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1027 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1027);
				float3 appendResult582_g1027 = (float3(objToView587_g1027.x , objToView587_g1027.y , -lerpResult579_g1027));
				float3 viewToWorld583_g1027 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1027, 1 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1027 = viewToWorld583_g1027;
				float3 lerpResult593_g1027 = lerp( WorldPosFromDepth_SRP567_g1027 , WorldPosFromDepth_SRP_Ortho584_g1027 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined(AHF_CAMERAMODE_PERSPECTIVE)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#elif defined(AHF_CAMERAMODE_ORTHOGRAPHIC)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP_Ortho584_g1027;
				#elif defined(AHF_CAMERAMODE_BOTH)
				float3 staticSwitch598_g1027 = lerpResult593_g1027;
				#else
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#endif
				half3 WorldPosFromDepth253_g1027 = staticSwitch598_g1027;
				float3 WorldPosition2_g1027 = WorldPosFromDepth253_g1027;
				float temp_output_7_0_g1030 = _FogDistanceStart;
				float temp_output_155_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1030 ) / ( _FogDistanceEnd - temp_output_7_0_g1030 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1027 = temp_output_155_0_g1027;
				#else
				float staticSwitch467_g1027 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1027 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1027 = staticSwitch467_g1027;
				float3 lerpResult258_g1027 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1027 * FogDistanceMask12_g1027 * FogDistanceMask12_g1027 ) * _FogColorDuo ));
				float3 normalizeResult318_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float dotResult145_g1027 = dot( normalizeResult318_g1027 , _DirectionalDir );
				float4 ScreenPos3_g1029 = screenPos;
				float2 UV13_g1029 = ( ( (ScreenPos3_g1029).xy / (ScreenPos3_g1029).z ) * (_ScreenParams).xy );
				float3 Magic14_g1029 = float3(0.06711056,0.00583715,52.98292);
				float dotResult16_g1029 = dot( UV13_g1029 , (Magic14_g1029).xy );
				float lerpResult494_g1027 = lerp( 0.0 , frac( ( frac( dotResult16_g1029 ) * (Magic14_g1029).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1027 = lerpResult494_g1027;
				float temp_output_140_0_g1027 = ( saturate( (( dotResult145_g1027 + Jitter502_g1027 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1027 = temp_output_140_0_g1027;
				#else
				float staticSwitch470_g1027 = pow( abs( temp_output_140_0_g1027 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1027 = staticSwitch470_g1027;
				float3 lerpResult40_g1027 = lerp( lerpResult258_g1027 , (_DirectionalColor).rgb , DirectionalMask30_g1027);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1027 = lerpResult258_g1027;
				#else
				float3 staticSwitch442_g1027 = lerpResult40_g1027;
				#endif
				half3 Input_Color6_g1028 = staticSwitch442_g1027;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1028 = Input_Color6_g1028;
				#else
				float3 staticSwitch1_g1028 = ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1027 = staticSwitch1_g1028;
				half3 AHF_FogAxisOption181_g1027 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1027 = ( WorldPosition2_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_7_0_g1031 = _FogDistanceEnd;
				float temp_output_643_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1031 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1031 ) ) );
				half FogDistanceMaskFar645_g1027 = ( temp_output_643_0_g1027 * temp_output_643_0_g1027 );
				float lerpResult614_g1027 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1027);
				float temp_output_7_0_g1032 = lerpResult614_g1027;
				float temp_output_167_0_g1027 = saturate( ( ( ( break159_g1027.x + break159_g1027.y + break159_g1027.z ) - temp_output_7_0_g1032 ) / ( _FogHeightStart - temp_output_7_0_g1032 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1027 = temp_output_167_0_g1027;
				#else
				float staticSwitch468_g1027 = pow( abs( temp_output_167_0_g1027 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1027 = staticSwitch468_g1027;
				float lerpResult328_g1027 = lerp( ( FogDistanceMask12_g1027 * FogHeightMask16_g1027 ) , saturate( ( FogDistanceMask12_g1027 + FogHeightMask16_g1027 ) ) , _FogLayersMode);
				float mulTime204_g1027 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1027 = ( ( WorldPosition2_g1027 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1027 ) );
				float3 p1_g1036 = temp_output_197_0_g1027;
				float localSimpleNoise3D1_g1036 = SimpleNoise3D( p1_g1036 );
				float temp_output_7_0_g1035 = _NoiseMin;
				float temp_output_7_0_g1034 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1034 ) / ( 0.0 - temp_output_7_0_g1034 ) ) );
				float lerpResult198_g1027 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1036 - temp_output_7_0_g1035 ) / ( _NoiseMax - temp_output_7_0_g1035 ) ) ) , ( NoiseDistanceMask7_g1027 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1027 = lerpResult198_g1027;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1027 = lerpResult328_g1027;
				#else
				float staticSwitch42_g1027 = ( lerpResult328_g1027 * NoiseSimplex3D24_g1027 );
				#endif
				float temp_output_454_0_g1027 = ( staticSwitch42_g1027 * _FogIntensity );
				float3 normalizeResult169_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float3 break170_g1027 = ( normalizeResult169_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_449_0_g1027 = ( ( break170_g1027.x + break170_g1027.y + break170_g1027.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1033 = _SkyboxFogHeight;
				float temp_output_176_0_g1027 = saturate( ( ( abs( temp_output_449_0_g1027 ) - temp_output_7_0_g1033 ) / ( 0.0 - temp_output_7_0_g1033 ) ) );
				float saferPower309_g1027 = abs( temp_output_176_0_g1027 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1027 = temp_output_176_0_g1027;
				#else
				float staticSwitch469_g1027 = pow( saferPower309_g1027 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1027 = lerp( saturate( ( staticSwitch469_g1027 + ( _SkyboxFogBottom * step( temp_output_449_0_g1027 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1027 = ( lerpResult179_g1027 * _SkyboxFogIntensity );
				float clampDepth118_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1027 = clampDepth118_g1027;
				#else
				float staticSwitch123_g1027 = ( 1.0 - clampDepth118_g1027 );
				#endif
				half SkyboxFogMask95_g1027 = ( 1.0 - ceil( staticSwitch123_g1027 ) );
				float lerpResult112_g1027 = lerp( temp_output_454_0_g1027 , SkyboxFogHeightMask108_g1027 , SkyboxFogMask95_g1027);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1027 = temp_output_454_0_g1027;
				#else
				float staticSwitch455_g1027 = lerpResult112_g1027;
				#endif
				half Final_Alpha463_g1027 = staticSwitch455_g1027;
				float4 appendResult114_g1027 = (float4(Final_Color462_g1027 , Final_Alpha463_g1027));
				float4 appendResult457_g1027 = (float4(WorldPosition2_g1027 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1027 = appendResult457_g1027;
				#else
				float4 staticSwitch456_g1027 = appendResult114_g1027;
				#endif
				

				surfaceDescription.Alpha = (staticSwitch456_g1027).w;
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

			#define _SURFACE_TYPE_TRANSPARENT 1
			#define _RECEIVE_SHADOWS_OFF 1
			#define ASE_SRP_VERSION 120106
			#define REQUIRE_DEPTH_TEXTURE 1


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

			#define ASE_NEEDS_FRAG_POSITION
			#pragma multi_compile_local AHF_CAMERAMODE_PERSPECTIVE AHF_CAMERAMODE_ORTHOGRAPHIC AHF_CAMERAMODE_BOTH
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
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			half4 _DirectionalColor;
			half4 _FogColorStart;
			half4 _FogColorEnd;
			half3 _FogAxisOption;
			half3 _DirectionalDir;
			half3 _NoiseSpeed;
			float _FarDistanceOffset;
			half _FogHeightStart;
			half _FogHeightFalloff;
			half _FogLayersMode;
			half _NoiseScale;
			half _NoiseMax;
			half _FarDistanceHeight;
			half _NoiseDistanceEnd;
			half _NoiseIntensity;
			half _FogIntensity;
			half _SkyboxFogOffset;
			half _SkyboxFogHeight;
			half _SkyboxFogFalloff;
			half _SkyboxFogBottom;
			half _NoiseMin;
			half _FogHeightEnd;
			float _Banner;
			half _SkyboxFogFill;
			half _HeightFogStandalone;
			half _IsHeightFogShader;
			half _FogCat;
			half _SkyboxCat;
			half _AdvancedCat;
			half _NoiseCat;
			half _DirectionalCat;
			half _FogCameraMode;
			half _FogDistanceStart;
			half _FogDistanceEnd;
			half _FogDistanceFalloff;
			half _FogColorDuo;
			half _JitterIntensity;
			half _DirectionalIntensity;
			half _DirectionalFalloff;
			half _FogAxisMode;
			half _SkyboxFogIntensity;
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END
			uniform float4 _CameraDepthTexture_TexelSize;


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

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord1 = screenPos;
				float3 ase_worldPos = TransformObjectToWorld( (v.vertex).xyz );
				o.ase_texcoord2.xyz = ase_worldPos;
				
				o.ase_texcoord3 = v.vertex;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord2.w = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = v.ase_normal;

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

				float4 screenPos = IN.ase_texcoord1;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float eyeDepth218_g1027 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float3 ase_worldPos = IN.ase_texcoord2.xyz;
				float4 unityObjectToClipPos224_g1027 = TransformWorldToHClip(TransformObjectToWorld(IN.ase_texcoord3.xyz));
				float4 computeScreenPos225_g1027 = ComputeScreenPos( unityObjectToClipPos224_g1027 );
				half3 WorldPosFromDepth_SRP567_g1027 = ( _WorldSpaceCameraPos - ( eyeDepth218_g1027 * ( ( _WorldSpaceCameraPos - ase_worldPos ) / computeScreenPos225_g1027.w ) ) );
				float3 objToView587_g1027 = mul( UNITY_MATRIX_MV, float4( IN.ase_texcoord3.xyz, 1 ) ).xyz;
				float clampDepth572_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				float lerpResult577_g1027 = lerp( ( 1.0 - clampDepth572_g1027 ) , clampDepth572_g1027 , saturate( _ProjectionParams.x ));
				float lerpResult579_g1027 = lerp( _ProjectionParams.y , _ProjectionParams.z , lerpResult577_g1027);
				float3 appendResult582_g1027 = (float3(objToView587_g1027.x , objToView587_g1027.y , -lerpResult579_g1027));
				float3 viewToWorld583_g1027 = mul( UNITY_MATRIX_I_V, float4( appendResult582_g1027, 1 ) ).xyz;
				half3 WorldPosFromDepth_SRP_Ortho584_g1027 = viewToWorld583_g1027;
				float3 lerpResult593_g1027 = lerp( WorldPosFromDepth_SRP567_g1027 , WorldPosFromDepth_SRP_Ortho584_g1027 , ( unity_OrthoParams.w + ( _FogCameraMode * 0.0 ) ));
				#if defined(AHF_CAMERAMODE_PERSPECTIVE)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#elif defined(AHF_CAMERAMODE_ORTHOGRAPHIC)
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP_Ortho584_g1027;
				#elif defined(AHF_CAMERAMODE_BOTH)
				float3 staticSwitch598_g1027 = lerpResult593_g1027;
				#else
				float3 staticSwitch598_g1027 = WorldPosFromDepth_SRP567_g1027;
				#endif
				half3 WorldPosFromDepth253_g1027 = staticSwitch598_g1027;
				float3 WorldPosition2_g1027 = WorldPosFromDepth253_g1027;
				float temp_output_7_0_g1030 = _FogDistanceStart;
				float temp_output_155_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1030 ) / ( _FogDistanceEnd - temp_output_7_0_g1030 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch467_g1027 = temp_output_155_0_g1027;
				#else
				float staticSwitch467_g1027 = ( 1.0 - pow( ( 1.0 - abs( temp_output_155_0_g1027 ) ) , _FogDistanceFalloff ) );
				#endif
				half FogDistanceMask12_g1027 = staticSwitch467_g1027;
				float3 lerpResult258_g1027 = lerp( (_FogColorStart).rgb , (_FogColorEnd).rgb , ( ( FogDistanceMask12_g1027 * FogDistanceMask12_g1027 * FogDistanceMask12_g1027 ) * _FogColorDuo ));
				float3 normalizeResult318_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float dotResult145_g1027 = dot( normalizeResult318_g1027 , _DirectionalDir );
				float4 ScreenPos3_g1029 = screenPos;
				float2 UV13_g1029 = ( ( (ScreenPos3_g1029).xy / (ScreenPos3_g1029).z ) * (_ScreenParams).xy );
				float3 Magic14_g1029 = float3(0.06711056,0.00583715,52.98292);
				float dotResult16_g1029 = dot( UV13_g1029 , (Magic14_g1029).xy );
				float lerpResult494_g1027 = lerp( 0.0 , frac( ( frac( dotResult16_g1029 ) * (Magic14_g1029).z ) ) , ( _JitterIntensity * 0.1 ));
				half Jitter502_g1027 = lerpResult494_g1027;
				float temp_output_140_0_g1027 = ( saturate( (( dotResult145_g1027 + Jitter502_g1027 )*0.5 + 0.5) ) * _DirectionalIntensity );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch470_g1027 = temp_output_140_0_g1027;
				#else
				float staticSwitch470_g1027 = pow( abs( temp_output_140_0_g1027 ) , _DirectionalFalloff );
				#endif
				float DirectionalMask30_g1027 = staticSwitch470_g1027;
				float3 lerpResult40_g1027 = lerp( lerpResult258_g1027 , (_DirectionalColor).rgb , DirectionalMask30_g1027);
				#ifdef AHF_DISABLE_DIRECTIONAL
				float3 staticSwitch442_g1027 = lerpResult258_g1027;
				#else
				float3 staticSwitch442_g1027 = lerpResult40_g1027;
				#endif
				half3 Input_Color6_g1028 = staticSwitch442_g1027;
				#ifdef UNITY_COLORSPACE_GAMMA
				float3 staticSwitch1_g1028 = Input_Color6_g1028;
				#else
				float3 staticSwitch1_g1028 = ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * ( ( Input_Color6_g1028 * 0.305306 ) + 0.6821711 ) ) + 0.01252288 ) );
				#endif
				half3 Final_Color462_g1027 = staticSwitch1_g1028;
				half3 AHF_FogAxisOption181_g1027 = ( _FogAxisOption + ( _FogAxisMode * 0.0 ) );
				float3 break159_g1027 = ( WorldPosition2_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_7_0_g1031 = _FogDistanceEnd;
				float temp_output_643_0_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1031 ) / ( ( _FogDistanceEnd + _FarDistanceOffset ) - temp_output_7_0_g1031 ) ) );
				half FogDistanceMaskFar645_g1027 = ( temp_output_643_0_g1027 * temp_output_643_0_g1027 );
				float lerpResult614_g1027 = lerp( _FogHeightEnd , ( _FogHeightEnd + _FarDistanceHeight ) , FogDistanceMaskFar645_g1027);
				float temp_output_7_0_g1032 = lerpResult614_g1027;
				float temp_output_167_0_g1027 = saturate( ( ( ( break159_g1027.x + break159_g1027.y + break159_g1027.z ) - temp_output_7_0_g1032 ) / ( _FogHeightStart - temp_output_7_0_g1032 ) ) );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch468_g1027 = temp_output_167_0_g1027;
				#else
				float staticSwitch468_g1027 = pow( abs( temp_output_167_0_g1027 ) , _FogHeightFalloff );
				#endif
				half FogHeightMask16_g1027 = staticSwitch468_g1027;
				float lerpResult328_g1027 = lerp( ( FogDistanceMask12_g1027 * FogHeightMask16_g1027 ) , saturate( ( FogDistanceMask12_g1027 + FogHeightMask16_g1027 ) ) , _FogLayersMode);
				float mulTime204_g1027 = _TimeParameters.x * 2.0;
				float3 temp_output_197_0_g1027 = ( ( WorldPosition2_g1027 * ( 1.0 / _NoiseScale ) ) + ( -_NoiseSpeed * mulTime204_g1027 ) );
				float3 p1_g1036 = temp_output_197_0_g1027;
				float localSimpleNoise3D1_g1036 = SimpleNoise3D( p1_g1036 );
				float temp_output_7_0_g1035 = _NoiseMin;
				float temp_output_7_0_g1034 = _NoiseDistanceEnd;
				half NoiseDistanceMask7_g1027 = saturate( ( ( distance( WorldPosition2_g1027 , _WorldSpaceCameraPos ) - temp_output_7_0_g1034 ) / ( 0.0 - temp_output_7_0_g1034 ) ) );
				float lerpResult198_g1027 = lerp( 1.0 , saturate( ( ( localSimpleNoise3D1_g1036 - temp_output_7_0_g1035 ) / ( _NoiseMax - temp_output_7_0_g1035 ) ) ) , ( NoiseDistanceMask7_g1027 * _NoiseIntensity ));
				half NoiseSimplex3D24_g1027 = lerpResult198_g1027;
				#ifdef AHF_DISABLE_NOISE3D
				float staticSwitch42_g1027 = lerpResult328_g1027;
				#else
				float staticSwitch42_g1027 = ( lerpResult328_g1027 * NoiseSimplex3D24_g1027 );
				#endif
				float temp_output_454_0_g1027 = ( staticSwitch42_g1027 * _FogIntensity );
				float3 normalizeResult169_g1027 = normalize( ( WorldPosition2_g1027 - _WorldSpaceCameraPos ) );
				float3 break170_g1027 = ( normalizeResult169_g1027 * AHF_FogAxisOption181_g1027 );
				float temp_output_449_0_g1027 = ( ( break170_g1027.x + break170_g1027.y + break170_g1027.z ) + -_SkyboxFogOffset );
				float temp_output_7_0_g1033 = _SkyboxFogHeight;
				float temp_output_176_0_g1027 = saturate( ( ( abs( temp_output_449_0_g1027 ) - temp_output_7_0_g1033 ) / ( 0.0 - temp_output_7_0_g1033 ) ) );
				float saferPower309_g1027 = abs( temp_output_176_0_g1027 );
				#ifdef AHF_DISABLE_FALLOFF
				float staticSwitch469_g1027 = temp_output_176_0_g1027;
				#else
				float staticSwitch469_g1027 = pow( saferPower309_g1027 , _SkyboxFogFalloff );
				#endif
				float lerpResult179_g1027 = lerp( saturate( ( staticSwitch469_g1027 + ( _SkyboxFogBottom * step( temp_output_449_0_g1027 , 0.0 ) ) ) ) , 1.0 , _SkyboxFogFill);
				half SkyboxFogHeightMask108_g1027 = ( lerpResult179_g1027 * _SkyboxFogIntensity );
				float clampDepth118_g1027 = SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy );
				#ifdef UNITY_REVERSED_Z
				float staticSwitch123_g1027 = clampDepth118_g1027;
				#else
				float staticSwitch123_g1027 = ( 1.0 - clampDepth118_g1027 );
				#endif
				half SkyboxFogMask95_g1027 = ( 1.0 - ceil( staticSwitch123_g1027 ) );
				float lerpResult112_g1027 = lerp( temp_output_454_0_g1027 , SkyboxFogHeightMask108_g1027 , SkyboxFogMask95_g1027);
				#ifdef AHF_DISABLE_SKYBOXFOG
				float staticSwitch455_g1027 = temp_output_454_0_g1027;
				#else
				float staticSwitch455_g1027 = lerpResult112_g1027;
				#endif
				half Final_Alpha463_g1027 = staticSwitch455_g1027;
				float4 appendResult114_g1027 = (float4(Final_Color462_g1027 , Final_Alpha463_g1027));
				float4 appendResult457_g1027 = (float4(WorldPosition2_g1027 , 1.0));
				#ifdef AHF_DEBUG_WORLDPOS
				float4 staticSwitch456_g1027 = appendResult457_g1027;
				#else
				float4 staticSwitch456_g1027 = appendResult114_g1027;
				#endif
				

				surfaceDescription.Alpha = (staticSwitch456_g1027).w;
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
	
	CustomEditor "HeightFogShaderGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Fallback "Hidden/InternalErrorShader"
}
/*ASEBEGIN
Version=19108
Node;AmplifyShaderEditor.RangedFloatNode;1093;-3328,-4736;Inherit;False;Property;_Banner;Banner;0;0;Create;True;0;0;0;True;1;StyledBanner(Height Fog Standalone);False;0;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;1107;-3136,-4736;Half;False;Property;_HeightFogStandalone;_HeightFogStandalone;43;1;[HideInInspector];Create;False;0;0;0;True;0;False;1;1;1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;1106;-2880,-4736;Half;False;Property;_IsHeightFogShader;_IsHeightFogShader;44;1;[HideInInspector];Create;False;0;0;0;True;0;False;1;1;1;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1120;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;5;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1121;-3072,-4608;Float;False;True;-1;2;HeightFogShaderGUI;0;13;BOXOPHOBIC/Atmospherics/Height Fog Standalone;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;8;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;1;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=0;UniversalMaterialType=Unlit;True;2;True;12;all;0;False;True;1;5;False;;10;False;;1;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;True;2;False;;True;7;False;;True;True;0;False;;0;False;;True;1;LightMode=UniversalForward;False;False;0;Hidden/InternalErrorShader;0;0;Standard;23;Surface;1;0;  Blend;0;0;Two Sided;2;0;Forward Only;0;0;Cast Shadows;0;0;  Use Shadow Threshold;0;0;Receive Shadows;0;0;GPU Instancing;0;0;LOD CrossFade;0;0;Built-in Fog;0;0;DOTS Instancing;0;0;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,;0;  Type;0;0;  Tess;16,False,;0;  Min;10,False,;0;  Max;25,False,;0;  Edge Length;16,False,;0;  Max Displacement;25,False,;0;Vertex Position,InvertActionOnDeselection;1;0;0;10;False;True;False;False;False;False;True;True;True;True;False;;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1122;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=ShadowCaster;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1123;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;False;False;True;False;False;False;False;0;False;;False;False;False;False;False;False;False;False;False;True;1;False;;False;False;True;1;LightMode=DepthOnly;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1124;-3072,-4608;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;255;False;;255;False;;255;False;;7;False;;1;False;;1;False;;1;False;;7;False;;1;False;;1;False;;1;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;0;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.CommentaryNode;1105;-3328,-4864;Inherit;False;919.8825;100;Drawers;0;;1,0.475862,0,1;0;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1128;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;True;1;1;False;;0;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;1;LightMode=Universal2D;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1129;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1130;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1131;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;False;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;1132;-3072,-4558;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;False;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;False;False;False;True;4;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;UniversalMaterialType=Unlit;True;3;True;12;all;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;;True;3;False;;False;True;1;LightMode=DepthNormalsOnly;False;True;9;d3d11;metal;vulkan;xboxone;xboxseries;playstation;ps4;ps5;switch;0;;0;0;Standard;0;False;0
Node;AmplifyShaderEditor.FunctionNode;1134;-3328,-4608;Inherit;False;Base;1;;1027;13c50910e5b86de4097e1181ba121e0e;36,360,1,376,1,380,1,372,1,384,1,476,1,450,1,382,1,370,1,378,1,386,1,555,1,557,1,388,1,550,1,374,1,347,1,351,1,685,1,339,1,392,1,355,1,116,1,364,1,361,1,366,1,597,1,343,1,354,1,99,1,500,1,603,1,681,1,345,1,368,1,349,1;0;3;FLOAT4;113;FLOAT3;86;FLOAT;87
WireConnection;1121;2;1134;86
WireConnection;1121;3;1134;87
ASEEND*/
//CHKSM=C30D73E4421A5FF50A6751C50E13056E632C9D97