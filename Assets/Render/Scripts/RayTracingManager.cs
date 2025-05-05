using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    // Limit the number of triangles allowed per mesh
    public const int TriangleLimit = 1500;
    
    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField, Range(0, 64)] int numRaysPerPixel = 2;
    [SerializeField] EnvironmentSettings environmentSettings;
    
    [Header("View Settings")]
    [SerializeField] bool useShader;
    [SerializeField] Shader rayTracingShader;
    [SerializeField] Shader accumulateShader;
    
    [Header("Info")]
    [SerializeField] int numRenderedFrames;
    [SerializeField] int numMeshChunks;
    [SerializeField] int numTriangles;
    
    //Materials
    Material rayTracingMaterial;
    Material accumulateMaterial;
    RenderTexture resultTexture;
    
    // Buffers
    ComputeBuffer sphereBuffer;
    ComputeBuffer triangleBuffer;
    ComputeBuffer meshInfoBuffer;
    
    List<Triangle> allTriangles;
    List<MeshInfo> allMeshInfo;
    
    void OnEnable () {
        if (environmentSettings.sunLight == null) {
            // 1) Use the sun specified in Lighting settings
            if (RenderSettings.sun != null)
                environmentSettings.sunLight = RenderSettings.sun;
            else {
                // 2) Otherwise grab the first active Directional Light
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.enabled) { environmentSettings.sunLight = l; break; }
            }
        }
    }
    void Start()
    {
        numRenderedFrames = 0;
    }
    void OnRenderImage(RenderTexture src, RenderTexture target)
    {
        if (useShader)
        {
            InitFrame();
            // Run the ray‑tracing shader and draw the result to the screen
            Graphics.Blit(null, target, rayTracingMaterial);
            
            
            // Create copy of prev frame
            RenderTexture prevFrameCopy = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(resultTexture, prevFrameCopy);

            // Run the ray tracing shader and draw the result to a temp texture
            rayTracingMaterial.SetInt("Frame", numRenderedFrames);
            RenderTexture currentFrame = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(null, currentFrame, rayTracingMaterial);

            // Accumulate
            accumulateMaterial.SetInt("_Frame", numRenderedFrames);
            accumulateMaterial.SetTexture("_PrevFrame", prevFrameCopy);
            Graphics.Blit(currentFrame, resultTexture, accumulateMaterial);

            // Draw result to screen
            Graphics.Blit(resultTexture, target);

            // Release temps
            RenderTexture.ReleaseTemporary(currentFrame);
            RenderTexture.ReleaseTemporary(prevFrameCopy);
            RenderTexture.ReleaseTemporary(currentFrame);

            numRenderedFrames += Application.isPlaying ? 1 : 0;
        }
        else
        {
            // Draw the camera’s unaltered render to the screen
            Graphics.Blit(src, target);
        }
    }

    void UpdateCameraParams(Camera cam)
    {
        float planeHeight = cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        rayTracingMaterial.SetVector("ViewParams" , new Vector3(planeWidth, planeHeight, cam.nearClipPlane)); 
        rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }
    
    void InitFrame()
    {
        // Create materials used in blits
        ShaderHelper.InitMaterial(rayTracingShader, ref rayTracingMaterial);
        ShaderHelper.InitMaterial(accumulateShader, ref accumulateMaterial);
        
        // Create result render texture
        ShaderHelper.CreateRenderTexture(ref resultTexture, Screen.width, Screen.height, FilterMode.Bilinear, ShaderHelper.RGBA_SFloat, "Result");

        // Update data
        UpdateCameraParams(Camera.current);
        CreateSpheres();
        CreateMeshes();
        SetShaderParams();
    }
    void SetShaderParams()
    {
        rayTracingMaterial.SetInt("MaxBounceCount", maxBounceCount);
        rayTracingMaterial.SetInt("NumRaysPerPixel", numRaysPerPixel);
        // rayTracingMaterial.SetFloat("DefocusStrength", defocusStrength);
        // rayTracingMaterial.SetFloat("DivergeStrength", divergeStrength);

        rayTracingMaterial.SetInteger("EnvironmentEnabled", environmentSettings.enabled ? 1 : 0);
        rayTracingMaterial.SetColor("GroundColour", environmentSettings.groundColour);
        rayTracingMaterial.SetColor("SkyColourHorizon", environmentSettings.skyColourHorizon);
        rayTracingMaterial.SetColor("SkyColourZenith", environmentSettings.skyColourZenith);
        if (environmentSettings.sunLight != null) {
            rayTracingMaterial.SetVector("SunLightDirection", -environmentSettings.sunLight.transform.forward);
        }
        rayTracingMaterial.SetFloat("SunFocus", environmentSettings.sunFocus);
        rayTracingMaterial.SetFloat("SunIntensity", environmentSettings.sunIntensity);
    }
    void CreateSpheres()
    {
        // Create sphere data from the sphere objects in the scene
        RayTracedSphere[] sphereObjects = FindObjectsByType<RayTracedSphere>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Sphere[] spheres = new Sphere[sphereObjects.Length];

        for (int i = 0; i < sphereObjects.Length; i++)
        {
            spheres[i] = new Sphere()
            {
                position = sphereObjects[i].transform.position,
                radius = sphereObjects[i].transform.localScale.x * 0.5f,
                material = sphereObjects[i].material
            };
        }

        // Create buffer containing all sphere data, and send it to the shader
        ShaderHelper.CreateStructuredBuffer(ref sphereBuffer, spheres);
        rayTracingMaterial.SetBuffer("Spheres", sphereBuffer);
        rayTracingMaterial.SetInt("NumSpheres", sphereObjects.Length);
    }
    
    void CreateMeshes()
    {
        RayTracedMesh[] meshObjects = FindObjectsByType<RayTracedMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        allTriangles ??= new List<Triangle>();
        allMeshInfo ??= new List<MeshInfo>();
        allTriangles.Clear();
        allMeshInfo.Clear();

        for (int i = 0; i < meshObjects.Length; i++)
        {
            MeshChunk[] chunks = meshObjects[i].GetSubMeshes();
            foreach (MeshChunk chunk in chunks)
            {
                RayTracingMaterial material = meshObjects[i].GetMaterial(chunk.subMeshIndex);
                allMeshInfo.Add(new MeshInfo(allTriangles.Count, chunk.triangles.Length, material, chunk.bounds));
                allTriangles.AddRange(chunk.triangles);

            }
        }

        numMeshChunks = allMeshInfo.Count;
        numTriangles = allTriangles.Count;

        ShaderHelper.CreateStructuredBuffer(ref triangleBuffer, allTriangles);
        ShaderHelper.CreateStructuredBuffer(ref meshInfoBuffer, allMeshInfo);
        rayTracingMaterial.SetBuffer("Triangles", triangleBuffer);
        rayTracingMaterial.SetBuffer("AllMeshInfo", meshInfoBuffer);
        rayTracingMaterial.SetInt("NumMeshes", allMeshInfo.Count);
    }
    
    void OnDisable()
    {
        ShaderHelper.Release(triangleBuffer, sphereBuffer, meshInfoBuffer);
        ShaderHelper.Release(resultTexture);
    }
    
}
