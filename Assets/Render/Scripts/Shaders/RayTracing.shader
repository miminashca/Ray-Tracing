Shader "Custom/RayTracing"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			// --- Constants ---
			static const float PI = 3.1415;

			// Raytracing Settings
			int MaxBounceCount;
			int NumRaysPerPixel;
			int Frame;

			// Camera Parameters
			float DefocusStrength;
			float DivergeStrength;
			float3 ViewParams;
			float4x4 CamLocalToWorldMatrix;

			// Environment Settings
			int EnvironmentEnabled;
			float4 GroundColour;
			float4 SkyColourHorizon;
			float4 SkyColourZenith;
			float3 SunLightDirection;
			float SunFocus;
			float SunIntensity;
			
			// --- Structures ---
			struct Ray
			{
				float3 origin;
				float3 dir;
			};
			
			struct RayTracingMaterial
			{
				float4 colour;
				float4 emissionColour;
				float4 specularColour;
				float emissionStrength;
				float smoothness;
				float specularProbability;
				int flag;
			};

			struct Sphere
			{
				float3 position;
				float radius;
				RayTracingMaterial material;
			};
			
			struct Triangle
			{
				float3 posA, posB, posC;
				float3 normalA, normalB, normalC;
			};

			struct MeshInfo
			{
				uint firstTriangleIndex;
				uint numTriangles;
				RayTracingMaterial material;
				float3 boundsMin;
				float3 boundsMax;
			};
			
			struct HitInfo
			{
				bool didHit;
				float distance;
				float3 hitPoint;
				float3 normal;
				RayTracingMaterial material;
			};

			// --- Buffers ---
			StructuredBuffer<Sphere> Spheres;
			int NumSpheres;

			StructuredBuffer<Triangle> Triangles;
			StructuredBuffer<MeshInfo> AllMeshInfo;
			int NumMeshes;

			// --- Ray Triangle + Mesh ---
			
			// Calculate the intersection of a ray with a triangle using Möller–Trumbore algorithm
			// Thanks to https://stackoverflow.com/a/42752998
			HitInfo RayTriangle(Ray ray, Triangle tri)
			{
				float3 edgeAB = tri.posB - tri.posA;
				float3 edgeAC = tri.posC - tri.posA;
				float3 normalVector = cross(edgeAB, edgeAC);
				float3 ao = ray.origin - tri.posA;
				float3 dao = cross(ao, ray.dir);

				float determinant = -dot(ray.dir, normalVector);
				float invDet = 1 / determinant;
				
				// Calculate dst to triangle & barycentric coordinates of intersection point
				float dst = dot(ao, normalVector) * invDet;
				float u = dot(edgeAC, dao) * invDet;
				float v = -dot(edgeAB, dao) * invDet;
				float w = 1 - u - v;
				
				// Initialize hit info
				HitInfo hitInfo;
				hitInfo.didHit = determinant >= 1E-6 && dst >= 0 && u >= 0 && v >= 0 && w >= 0;
				hitInfo.hitPoint = ray.origin + ray.dir * dst;
				hitInfo.normal = normalize(tri.normalA * w + tri.normalB * u + tri.normalC * v);
				hitInfo.distance = dst;
				return hitInfo;
			}

			// Thanks to https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
			bool RayBoundingBox(Ray ray, float3 boxMin, float3 boxMax)
			{
				float3 invDir = 1 / ray.dir;
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;
				float3 t1 = min(tMin, tMax);
				float3 t2 = max(tMin, tMax);
				float tNear = max(max(t1.x, t1.y), t1.z);
				float tFar = min(min(t2.x, t2.y), t2.z);
				return tNear <= tFar;
			};
			
			// --- Ray Sphere ---
			HitInfo RaycastSphere(Ray ray, float3 sphereCentre, float sphereRadius)
			{
				HitInfo hitInfo = (HitInfo)0;
				float3 offsetRayOrigin = ray.origin - sphereCentre;
				// From the equation: sqrLength(rayOrigin + rayDir * dst) = radius^2
				// Solving for dst results in a quadratic equation with coefficients:
				float a = dot(ray.dir, ray.dir); // a = 1 (assuming unit vector)
				float b = 2 * dot(offsetRayOrigin, ray.dir);
				float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;
				// Quadratic discriminant
				float discriminant = b * b - 4 * a * c; 

				// No solution when d < 0 (ray misses sphere)
				if (discriminant >= 0) {
					// Distance to nearest intersection point (from quadratic formula)
					float dst = (-b - sqrt(discriminant)) / (2 * a);

					// Ignore intersections that occur behind the ray
					if (dst >= 0) {
						hitInfo.didHit = true;
						hitInfo.distance = dst;
						hitInfo.hitPoint = ray.origin + ray.dir * dst;
						hitInfo.normal = normalize(hitInfo.hitPoint - sphereCentre);
					}
				}
				return hitInfo;
			}

			HitInfo CalculateRayCollision(Ray ray)
			{
				HitInfo closestHit = (HitInfo)0;
				closestHit.distance = 1.#INF; //because before we hit the hit distance is infinite

				// Raycast all spheres and keep hit info of closest hit
				for (int i = 0; i < NumSpheres; i++)
				{
					Sphere sphere = Spheres[i];
					HitInfo hitInfo = RaycastSphere(ray, sphere.position, sphere.radius);

					if(hitInfo.didHit && hitInfo.distance < closestHit.distance)
					{
						closestHit = hitInfo;
						closestHit.material = sphere.material;
					}
				}

				// Raycast all triangles in all meshes and keep the closest hit
				for (int meshIndex = 0; meshIndex < NumMeshes; meshIndex++)
				{
					MeshInfo meshInfo = AllMeshInfo[meshIndex];
					if(!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax))
					{
						continue;
					}

					for(int i = 0; i < meshInfo.numTriangles; i++)
					{
						int triangleIndex = meshInfo.firstTriangleIndex + i;
						Triangle tri = Triangles[triangleIndex];
						HitInfo hitInfo = RayTriangle(ray, tri);

						if(hitInfo.didHit && hitInfo.distance < closestHit.distance)
						{
							closestHit = hitInfo;
							closestHit.material = meshInfo.material;
						}
					}
				}
				
				return closestHit;
			}

			
			// --- Random ---
			// PCG (permuted congruential generator). Thanks to:
			// www.pcg-random.org and www.shadertoy.com/view/XlGcRh
			uint NextRandom(inout uint state)
			{
				state = state * 747796405 + 2891336453;
				uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
				result = (result >> 22) ^ result;
				return result;
			}
			float RandomValue(inout uint state)
			{
				return NextRandom(state) / 4294967295.0; // 2^32 - 1
			}

			// Random value in normal distribution (with mean=0 and sd=1)
			float RandomValueNormalDistribution(inout uint state)
			{
				// Thanks to https://stackoverflow.com/a/6178290
				float theta = 2 * 3.1415926 * RandomValue(state);
				float rho = sqrt(-2 * log(RandomValue(state)));
				return rho * cos(theta);
			}

			// Calculate a random direction
			float3 RandomDirection(inout uint state)
			{
				// Thanks to https://math.stackexchange.com/a/1585996
				float x = RandomValueNormalDistribution(state);
				float y = RandomValueNormalDistribution(state);
				float z = RandomValueNormalDistribution(state);
				return normalize(float3(x, y, z));
			}

			float3 RandomHemisphereDirection(float3 normal, inout uint rngState)
			{
				float3 dir = RandomDirection(rngState);
				return dir * sign(dot(normal, dir));
			}

			float2 RandomPointInCircle(inout uint rngState)
			{
				float angle = RandomValue(rngState) * 2 * PI;
				float2 pointOnCircle = float2(cos(angle), sin(angle));
				return pointOnCircle * sqrt(RandomValue(rngState));
			}
			

			// --- Enviroment ---
			float3 GetEnviromentLight(Ray ray)
			{
				if (!EnvironmentEnabled) {
					return 0;
				}
				
				float skyGradientT = pow(smoothstep(0, 0.4, ray.dir.y), 0.35);
				float3 skyGradient = lerp(SkyColourHorizon, SkyColourZenith, skyGradientT);
				float sun = pow(max(0, dot(ray.dir, -SunLightDirection)), SunFocus) * SunIntensity;

				// combine
				float groundToSkyT = smoothstep(-0.01, 0, ray.dir.y);
				float sunMask = groundToSkyT >= 1;
				
				return lerp(GroundColour, skyGradient, groundToSkyT) + sun * sunMask;
			}

			// --- Trace ---
			//Tracing the reverced ray of light, which travels from the camera,
			//bounces off objects in the scene, and hopefully ends up at a light source.
			float3 Trace(Ray ray, inout uint rngState)
			{
				float3 incomingLight = 0;
				float3 rayColor = 1;
				
				for(int i = 0; i <= MaxBounceCount; i++)
				{
					HitInfo hitinfo = CalculateRayCollision(ray);
					RayTracingMaterial material = hitinfo.material;
					
					if(hitinfo.didHit)
					{
						ray.origin = hitinfo.hitPoint;
						float3 diffuseDir =  normalize(hitinfo.normal + RandomDirection(rngState));
						float3 specularDir =  reflect(ray.dir, hitinfo.normal);
						
						bool isSpecularBounce = material.specularProbability >= RandomValue(rngState);
						
						ray.dir = lerp(diffuseDir, specularDir, material.smoothness * isSpecularBounce);

						float3 emittedLight = material.emissionColour * material.emissionStrength;
						incomingLight += emittedLight * rayColor;
						rayColor *= lerp(material.colour, material.specularColour, isSpecularBounce);
					} else
					{
						incomingLight += GetEnviromentLight(ray) * rayColor;
						break;
					}
				}

				return incomingLight;
			}
			
			
			
			// --- Main ---
			// Run for every pixel in the display
			float4 frag (v2f i) : SV_Target
			{
				// seed for random number generation
				uint2 numPixels = _ScreenParams.xy;
				uint2 pixelCoord = i.uv * numPixels;
				uint pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
				uint rngState = pixelIndex + Frame * 719393;

				// ray
				float3 viewPointLocal = float3(i.uv - 0.5, 1) * ViewParams;
				float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal,1));
				float3 camRight = CamLocalToWorldMatrix._m00_m10_m20;
				float3 camUp = CamLocalToWorldMatrix._m01_m11_m21;

				//color per pixel
				float3 totalIncomingLight = 0;
				for (int rayIndex = 0; rayIndex < NumRaysPerPixel; rayIndex ++)
				{
					Ray ray;
					ray.origin = _WorldSpaceCameraPos;

					float2 jitter = RandomPointInCircle(rngState) * DivergeStrength / numPixels.x;
					float3 jitteredViewPoint = viewPoint + camRight * jitter.x + camUp * jitter.y;
					
					ray.dir = normalize(jitteredViewPoint - ray.origin);
					
					totalIncomingLight += Trace(ray, rngState);
				}
				
				float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
				return float4(pixelCol, 1);
			}

			ENDCG
		}
	}
}