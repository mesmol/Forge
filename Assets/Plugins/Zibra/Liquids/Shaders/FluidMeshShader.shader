Shader "ZibraLiquids/FluidMeshShader"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest Always

            HLSLPROGRAM

            // Physically based Standard lighting model
            #pragma multi_compile_local __ HDRP
            #pragma multi_compile_local __ CUSTOM_REFLECTION_PROBE
            #pragma multi_compile_local __ VISUALIZE_SDF
            #pragma multi_compile_local __ FLIP_BACKGROUND
            #pragma multi_compile_local __ UNDERWATER_RENDER
            #pragma instancing_options procedural:setup
            #pragma vertex VSMain
            #pragma fragment PSMain
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityStandardBRDF.cginc"
            #include "UnityImageBasedLighting.cginc"

            struct VSIn
            {
                uint vertexID : SV_VertexID;
            };

            struct VSOut
            {
                float4 position : POSITION;
                float3 raydir : TEXCOORD1;
                float2 uv : TEXCOORD0;
            };
            
            struct PSOut
            {
                float4 color : COLOR;
            };

            // Fluid material parameters, see SetMaterialParams()
            float4x4 ProjectionInverse;
            float4x4 ViewProjectionInverse;
            float4x4 EyeRayCameraCoeficients;
            float Roughness;
            float AbsorptionAmount;
            float ScatteringAmount;
            float RefractionDistortion;
            float4 RefractionColor;
            float4 ReflectionColor;
            float4 EmissiveColor;
            float Metalness;
            float FoamIntensity;
            float FoamAmount;
            float3 GridSize;
            float3 ContainerScale;
            float3 ContainerPosition;
            float LiquidIOR;

#ifdef HDRP
            float3 LightColor; 
            float3 LightDirection;
#endif

            // Light and reflection params
            UNITY_DECLARE_TEXCUBE(ReflectionProbe);
            float4 ReflectionProbe_BoxMax;
            float4 ReflectionProbe_BoxMin;
            float4 ReflectionProbe_ProbePosition;
            float4 ReflectionProbe_HDR;
            float4 WorldSpaceLightPos;
            
            // Camera params
            float2 TextureScale;

            // Input resources
            sampler2D Background;
            float4 Background_TexelSize;
            StructuredBuffer<int> Quads;
            StructuredBuffer<int> VertexIDGrid;
            StructuredBuffer<float4> Vertices;

            // built-in Unity sampler name - do not change
            sampler2D _CameraDepthTexture;

            float2 GetFlippedUV(float2 uv)
            {
                if (_ProjectionParams.x > 0)
                    return float2(uv.x, 1 - uv.y);
                return uv;
            }

            float2 GetFlippedUVBackground(float2 uv)
            {
                uv = GetFlippedUV(uv);
#ifdef FLIP_BACKGROUND
                // Temporary fix for flipped reflection on iOS
                uv.y = 1 - uv.y;
#else
                if (Background_TexelSize.y < 0)
                {
                    uv.y = 1 - uv.y;
                }
#endif
                return uv;
            }

            float4 ComputeClipSpacePosition(float2 positionNDC, float deviceDepth)
            {
                float4 positionCS = float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);

            #if UNITY_UV_STARTS_AT_TOP
                positionCS.y = -positionCS.y;
            #endif

                return positionCS;
            }

            float3 ComputeWorldSpacePosition(float2 positionNDC, float deviceDepth, float4x4 invViewProjMatrix)
            {
                float4 positionCS  = ComputeClipSpacePosition(positionNDC, deviceDepth);
                float4 hpositionWS = mul(invViewProjMatrix, positionCS);
                return hpositionWS.xyz / hpositionWS.w;
            }

            float3 DepthToWorld(float2 uv, float depth)
            {
                return ComputeWorldSpacePosition(uv, depth, ViewProjectionInverse);
            }

            float4 GetDepthAndPos(float2 uv)
            {
                float depth = tex2D(_CameraDepthTexture, uv).x;
                float3 pos = DepthToWorld(uv, depth);
                return float4(pos, depth);
            }

            float PositionToDepth(float3 pos)
            {
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(pos, 1));
                return (1.0 / clipPos.w - _ZBufferParams.w) / _ZBufferParams.z; //inverse of linearEyeDepth
            }

            float3 PositionToScreen(float3 pos)
            {
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(pos, 1));
                clipPos = ComputeScreenPos(clipPos); 

                return float3(clipPos.xy/clipPos.w, (1.0 / clipPos.w - _ZBufferParams.w) / _ZBufferParams.z); 
            }

            float3 BoxProjection(float3 rayOrigin, float3 rayDir, float3 cubemapPosition, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return normalize(rayOrigin + rayDir*tFar - cubemapPosition);
            };

            float3 SampleCubemap(float3 pos, float3 ray, float roughness)
            {
                Unity_GlossyEnvironmentData g;
                g.roughness = roughness;

#if defined(CUSTOM_REFLECTION_PROBE) || defined(HDRP)
                g.reflUVW = BoxProjection(pos, ray,
                    ReflectionProbe_ProbePosition,
                    ReflectionProbe_BoxMin, ReflectionProbe_BoxMax
                );
                float3 reflection = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(ReflectionProbe), ReflectionProbe_HDR, g);
#else
                g.reflUVW = ray;
                g.reflUVW.y = g.reflUVW.y; //don't render the bottom part of the cubemap
                g.roughness = roughness;
                float3 reflection = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, g);
#endif

                return reflection;
            }

            float3 ComputeMaterial(float3 cameraPos, float3 cameraRay, float3 normal, float3 lightDirection, float3 lightColor)
            {
                float3 worldView = -cameraRay;
                float4 reflColor = ReflectionColor;
                float3 H = normalize(lightDirection + worldView);
                float NH = BlinnTerm(normal, H);
                float NL = DotClamped(normal, lightDirection);
                float NV  = abs(dot(normal, worldView)); 
                half V = SmithBeckmannVisibilityTerm(NL, NV, Roughness);
                half D = NDFBlinnPhongNormalizedTerm(NH, RoughnessToSpecPower(Roughness));
                float3 spec = (V * D) * (UNITY_PI / 4);
                return lightColor * max(0, spec * NL);
            }

            float Average(float3 x)
            {
                return (x.x + x.y + x.z) / 3.0;
            }

            float RefractionMinimumDepth;
            float RefractionDepthBias;

            float3 RefractSample(float3 pos, float3 ray)
            {
                float3 uvz = PositionToScreen(pos);
                float scene_depth = tex2D(_CameraDepthTexture, uvz.xy).x;
                float3 CubeMapSample = Average(ReflectionColor.xyz) * SampleCubemap(pos, ray, 0.05);
                float3 BackgroundSample = tex2D(Background, GetFlippedUVBackground(uvz.xy)).xyz;
                float Interpolate = smoothstep(0.0, 0.1, min(min(uvz.x, uvz.y), min(1.0 - uvz.x, 1.0 - uvz.y)));

                return lerp(CubeMapSample, BackgroundSample, Interpolate);
            }

            float3 ReflectSample(float3 pos, float3 ray)
            {
                return Average(ReflectionColor.xyz) * SampleCubemap(pos, ray, 0.05);
            }

            #define SHADING

            #include <RenderingUtils.cginc>

            // See Raytracing Gems 1 20.3.2.1 EYE RAY SETUP
            float3 GetCameraRay(float2 uv)
            {
                float2 c = float2(2.0f * uv.x - 1.0f, -2.0f * uv.y + 1.0f);

                float3 r = EyeRayCameraCoeficients[0].xyz;
                float3 u = EyeRayCameraCoeficients[1].xyz;
                float3 v = EyeRayCameraCoeficients[2].xyz;

                float3 direction = c.x * r + c.y * u + v;
                return normalize(direction);
            }

            VSOut VSMain(VSIn input)
            {
                VSOut output;

                float2 vertexBuffer[4] = {
                    float2(0.0f, 0.0f),
                    float2(0.0f, 1.0f),
                    float2(1.0f, 0.0f),
                    float2(1.0f, 1.0f),
                };
                uint indexBuffer[6] = { 0, 1, 2, 2, 1, 3 };
                uint indexID = indexBuffer[input.vertexID];

                float2 uv = vertexBuffer[indexID];
                float2 flippedUV = GetFlippedUV(uv);

                output.position = float4(2 * flippedUV.x - 1, 1 - 2 * flippedUV.y, 0.5, 1.0);
                output.uv = uv;
                output.raydir = GetCameraRay(uv);
                
                return output;
            }

            Texture2D<float4> MeshRenderData;
            Texture2D<float> MeshDepth;
            Texture2D<float4> RayMarchData;

            float RayMarchResolutionDownscale;
            float4 MeshRenderData_TexelSize;

            //required to work with custom depth formats
            float3 GetLiquidDepth(float2 pixelCoord)
            {
                float2 rayMarchPixel = pixelCoord * RayMarchResolutionDownscale;

                //custom bilinear interpolation
                float2 intPixel = floor(rayMarchPixel);
                float2 fracPixel = frac(rayMarchPixel);

                float3 data00 = RayMarchData.Load(int3(intPixel, 0) + int3(0,0,0)).xyz;
                float3 data10 = RayMarchData.Load(int3(intPixel, 0) + int3(1,0,0)).xyz;
                data00 = lerp(data00, data10, fracPixel.x);

                float3 data01 = RayMarchData.Load(int3(intPixel, 0) + int3(0,1,0)).xyz;
                float3 data11 = RayMarchData.Load(int3(intPixel, 0) + int3(1,1,0)).xyz;
                data01 = lerp(data01, data11, fracPixel.x);

                return lerp(data00, data01, fracPixel.y);
            }

            PSOut PSMain(VSOut input)
            {
                PSOut output;

                float3 cameraPos = _WorldSpaceCameraPos;
                float3 cameraRay = normalize(input.raydir);
                int3 pixelCoord = int3(input.position.xy, 0);
#if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0)
                {
                    pixelCoord.y = MeshRenderData_TexelSize.w - pixelCoord.y;
                }
#endif

                float4 data = MeshRenderData.Load(pixelCoord);
                uint encodedNormal = asuint(data.w);
                float liquidDepth = MeshDepth.Load(pixelCoord);
                float sceneDepth = tex2D(_CameraDepthTexture, input.uv).x;
#if !defined(UNITY_REVERSED_Z)
                liquidDepth = 1.0 - liquidDepth;
                sceneDepth = 1.0 - sceneDepth;
#endif



                if (!any(data.xyz) && !encodedNormal)
                {
                 	discard;
                }
#ifndef UNDERWATER_RENDER
				if (liquidDepth < sceneDepth)
                {
                    discard;
                }
#endif
                float3 normal = DecodeDirection(asuint(encodedNormal));
                float3 incomingLight = 0;
                float ndotv = dot(normal, -cameraRay);
                float NV = abs(ndotv); 
                float fresnel = FresnelTerm(Metalness, NV);
#ifdef HDRP
                float3 lightColor = LightColor;
                float3 lightDirWorld = LightDirection;
#else
                float3 lightColor = _LightColor0;
                float3 lightDirWorld = normalize(_WorldSpaceLightPos0.xyz);
#endif
                
                RayDepths.xyz = GetLiquidDepth(pixelCoord.xy);
               
                float3 worldPos = DepthToWorld(input.uv, liquidDepth);
#ifdef UNDERWATER_RENDER
                float CameraDensity = 0.0f;
                if(insideGrid(cameraPos))
                CameraDensity = SampleDensity(cameraPos);
                bool isUnderwater = (step(ndotv, 0.0)) && (CameraDensity > 0.0);
				if (!isUnderwater && liquidDepth < sceneDepth)
                {
                    discard;
                }

                if(isUnderwater)
                {
                    float3 background_color = 0.0f;
                    float opticalDensity = 0.0f;
                    if (liquidDepth < sceneDepth)
                    {
                        background_color = tex2D(Background, GetFlippedUVBackground(input.uv)).xyz;
						liquidDepth = sceneDepth;
                        worldPos = DepthToWorld(input.uv, liquidDepth);
                    }
                    else
                    {
                        background_color = RefractionRay(worldPos, cameraRay, -normal, true);
                    }

                    float liquidWorldSpaceDepth = length(cameraPos - worldPos);
                    opticalDensity += liquidWorldSpaceDepth;

#ifdef HDRP
                    float3 lightColor = LightColor;
#else
                    float3 lightColor = _LightColor0;
#endif

                    incomingLight = IntegrateAbsorptionScattering(opticalDensity, background_color, lightColor);
                }
                else
#endif
                {
                    ////
                    ////compute reflected color
                    ////

                    float3 ReflectRay = reflect(cameraRay, normal);
                    float3 ReflectedColor = ReflectSample(worldPos, ReflectRay);
                    incomingLight += ReflectionColor.xyz * fresnel * ReflectedColor / Average(ReflectionColor.xyz);

                    ////
                    ////compute light from light sources
                    ////

                    //TODO loop over all lights
                    incomingLight += fresnel*ComputeMaterial(cameraPos, cameraRay, normal, lightDirWorld, lightColor);

					incomingLight += EmissiveColor.rgb;

                    ////
                    ////compute refracted color
                    ////

                    incomingLight += (1.0 - fresnel) * RefractionRay(worldPos, cameraRay, normal, false);
                }

                output.color = float4(clamp(incomingLight , 0., 10000.0), 1.0);
                return output;
            }
            ENDHLSL
        }
    }
}
