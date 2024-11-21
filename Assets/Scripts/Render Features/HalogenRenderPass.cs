using UnityEngine.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using System.Linq;

//[StructLayout(LayoutKind.Sequential)]
public struct HalogenSphere
{
    public Vector3 center;
    public float radius;

    public uint materialIndex;
    
    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;
}

[System.Serializable]
public struct HalogenMeshData
{
    public uint triangleBufferOffset;
    public uint accelerationBufferOffset;

    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;

    public uint materialIndex;

    public Matrix4x4 worldToLocal;
    public Matrix4x4 localToWorld;
}

public struct PackedRayMedium
{
    public float indexOfRefraction;
    public Vector3 absorption;
    public int priority;
    public uint materialID;
}

public struct PackedHalogenMaterial
{
    public uint materialID;

    public Vector4 albedo;
    public Vector4 specularAlbedo;
    public float metallic;
    public float roughness;
    public Vector4 emissive;
    // transmission properties
    public PackedRayMedium rayMedium;
}

public struct HalogenTriangle
{
    public Vector3 pointA;
    public Vector3 pointB;
    public Vector3 pointC;

    public Vector3 normalA;
    public Vector3 normalB;
    public Vector3 normalC;
}

[System.Serializable]
public struct BVHEntry // 32 byte struct, so probably cache aligned :D
{
    public uint indexA; // triangle start if leaf node, index to first child if hierarchy node
    public uint triangleCount; // triangle count. If greater than zero this is a leaf node 

    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;
}

// Useful
// https://jacco.ompf2.com/2022/04/13/how-to-build-a-bvh-part-1-basics/
public class HalogenRenderPass : ScriptableRenderPass
{
    int TileSize = 8; // Square 8x8 tile for compute wavefront
    ComputeShader halogenShader;
    int kernelIndex;

    int SamplesPerPixel;
    int MaxBounces;

    bool Accumulate;
    bool AccumulationBufferDirty;

    int EnvironmentMipLevel;

    float NearPlaneDistance;
    float FarPlaneDistance;
    float FocalPlaneDistance;
    float ApertureAngle;

    int HalogenDebugMode;
    int TriangleDebugDisplayRange;
    int BoxDebugDisplayRange;

    Cubemap EnvironmentCubemap;
    bool UseEnvironmentCubemap;

    Material AccumulationMaterial;
    Vector3 PriorCameraPosition;
    Quaternion PriorCameraRotation;
    Vector2Int PriorResolution;

    RTHandle rtAccumulationBuffer;
    RTHandle rtPathtracingBuffer;
    RTHandle rtBackBuffer;
    RTHandle rtSecondBounceBuffer;
    ProfilingSampler _profilingSampler;

    ComputeBuffer sphereBuffer = null;
    ComputeBuffer materialBuffer = null;
    ComputeBuffer triangleBuffer = null;
    ComputeBuffer meshBuffer = null;
    ComputeBuffer TLASBuffer = null;
    ComputeBuffer BLASBuffer = null;

    List<HalogenSphere> sphereList = new List<HalogenSphere>();
    List<HalogenMeshData> meshList = new List<HalogenMeshData>();
    List<PackedHalogenMaterial> materialList = new List<PackedHalogenMaterial>();
    List<HalogenTriangle> triangleList = new List<HalogenTriangle>();
    List<BVHEntry> BLASList = new List<BVHEntry>();

    List<HalogenMaterial> unpackedHalogenMaterials = new List<HalogenMaterial>();

    private readonly int sphereStructStride;

    private readonly int triangleStructStride;

    private readonly int meshStructStride;
    
    private readonly int BVHEntryStructStride;

    private readonly int materialStructStride;


    private int FrameCount;

    public HalogenRenderPass(ref HalogenSettings _settings)
    {
        // Avoids memory leak on code reload 
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => { Dispose(); }; // Mmm.. Tasty Lambdas

        halogenShader = _settings.HalogenShader; 
        kernelIndex = halogenShader.FindKernel("HalogenCompute");

        profilingSampler = new ProfilingSampler("Halogen");
        sphereStructStride = Marshal.SizeOf(typeof(HalogenSphere));
        materialStructStride = Marshal.SizeOf(typeof(PackedHalogenMaterial));
        triangleStructStride = Marshal.SizeOf(typeof(HalogenTriangle));
        meshStructStride = Marshal.SizeOf(typeof(HalogenMeshData));
        BVHEntryStructStride = Marshal.SizeOf(typeof(BVHEntry));

        SamplesPerPixel = Mathf.Max(1, _settings.SamplesPerPixel);

        MaxBounces = Mathf.Max(0, _settings.MaxBounces);

        FocalPlaneDistance = Mathf.Max(Mathf.Epsilon, _settings.FocalPlaneDistance);

        NearPlaneDistance = Mathf.Max(Mathf.Epsilon, _settings.NearPlaneDistance);
        FarPlaneDistance = Mathf.Max(NearPlaneDistance + Mathf.Epsilon, _settings.FarPlaneDistance);

        ApertureAngle = Mathf.Clamp(_settings.ApertureAngle, 0, 89.9f);
        EnvironmentMipLevel = math.clamp(_settings.EnvironmentMipLevel, 0, 2);

        FrameCount = 1;
        AccumulationBufferDirty = true;
        AccumulationMaterial = CoreUtils.CreateEngineMaterial(_settings.AccumulationShader);

        Accumulate = _settings.Accumulate;

        UseEnvironmentCubemap = _settings.useHDRISky;
        if (UseEnvironmentCubemap)  {
            if (_settings.environmentCubemap != null) {
                EnvironmentCubemap = _settings.environmentCubemap;
            }
            else  {
                UseEnvironmentCubemap = false;
            }
        }

        switch (_settings.DebugMode)
        {
            default:
                HalogenDebugMode = 0;
                break;
            case global::HalogenDebugMode.Albedo:
                HalogenDebugMode = 1;
                break;
            case global::HalogenDebugMode.Normal:
                HalogenDebugMode = 2;
                break;
            case global::HalogenDebugMode.RayTriangleTests:
                HalogenDebugMode = 3;
                break;
            case global::HalogenDebugMode.RayBoxTests:
                HalogenDebugMode = 4;
                break;
            case global::HalogenDebugMode.Combined:
                HalogenDebugMode = 5;
                break;
        }

        if (HalogenDebugMode != 0) {
            MaxBounces = _settings.FirstInteractionOnly ? 0 : MaxBounces;
        }

        TriangleDebugDisplayRange = Mathf.Max(_settings.TriangleDebugDisplayRange, 1);
        BoxDebugDisplayRange = Mathf.Max(_settings.BoxDebugDisplayRange, 1);
        //settings = _settings;
    }

    ~HalogenRenderPass() { Dispose(); } // Why??

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var colorDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        
        //RenderTextureDescriptor descriptor = new RenderTextureDescriptor(colorDescriptor.width, colorDescriptor.height, colorDescriptor.colorFormat, 0, 0);
        colorDescriptor.enableRandomWrite = true;
        colorDescriptor.bindMS = false;
        colorDescriptor.depthBufferBits = 0;
        colorDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;

        //colorDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;
        RenderingUtils.ReAllocateIfNeeded(ref rtPathtracingBuffer, colorDescriptor, name: "_HalogenPathtracingBuffer");
        RenderingUtils.ReAllocateIfNeeded(ref rtAccumulationBuffer, colorDescriptor, name: "_HalogenAccumulationBuffer");
        RenderingUtils.ReAllocateIfNeeded(ref rtBackBuffer, colorDescriptor, name: "_HalogenBackBuffer");
        RenderingUtils.ReAllocateIfNeeded(ref rtSecondBounceBuffer, colorDescriptor, name: "_HalogenSecondBounceDiffuse");

        Vector2Int currentResolution = new Vector2Int(colorDescriptor.width, colorDescriptor.height);
        if (currentResolution != PriorResolution)
        {
            ClearAccumulation();
        }

        PriorResolution = currentResolution;
    }

    void ClearAccumulation()
    {
        FrameCount = 1;
        AccumulationBufferDirty = true;
        //Debug.Log("Clearing buffer");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get(name: "HalogenPass");
        Transform cameraTransform = renderingData.cameraData.camera.transform;

        // Clear at the begining for reasons?
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        if (PriorCameraPosition != null && PriorCameraRotation != null) { 
            if (!PriorCameraPosition.Equals(cameraTransform.position) || !PriorCameraRotation.Equals(cameraTransform.rotation))
            {
                // Clear accumulation
                ClearAccumulation();
            }
        }

        // Handle Accumulate being disabled
        if (FrameCount > 1 && !Accumulate)
        {
            ClearAccumulation();
        }

        PriorCameraPosition = cameraTransform.position;
        PriorCameraRotation = cameraTransform.rotation;
        

        UpdateObjectBuffers(cmd);

        using (new ProfilingScope(cmd, _profilingSampler))
        {
            RTHandle rtCameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Camera camera = renderingData.cameraData.camera;

            if (Accumulate)
            {
                // Copy previous result to accumulation buffer
                Blitter.BlitCameraTexture(cmd, rtBackBuffer, rtAccumulationBuffer);
            }
            

            // Calculate w/2 and h/2 for clipping plane
            float nClip = NearPlaneDistance;
            float h = Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f) * nClip;
            float w = camera.aspect * h;


            cmd.SetComputeMatrixParam(halogenShader, "CamLocalToWorldMatrix", camera.transform.localToWorldMatrix);
            cmd.SetComputeVectorParam(halogenShader, "ScreenParameters", new Vector3(camera.pixelWidth, camera.pixelHeight, 0));
            cmd.SetComputeVectorParam(halogenShader, "ViewParameters", new Vector4(w, h, nClip, FarPlaneDistance));
            cmd.SetComputeVectorParam(halogenShader, "CameraParameters", camera.transform.position);

            
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "MeshList", meshBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "SphereList", sphereBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "MaterialList", materialBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "TriangleBuffer", triangleBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "BLASBuffer", BLASBuffer);

            cmd.SetComputeIntParam(halogenShader, "RandomSeed", Accumulate ? FrameCount : 1);
            cmd.SetComputeIntParam(halogenShader, "default_hdri_mipmap", EnvironmentMipLevel);

            cmd.SetComputeVectorParam(halogenShader, "BufferCounts", new Vector4(sphereList.Count, meshList.Count, 0, 0));
            cmd.SetComputeIntParam(halogenShader, "SamplesPerPixel", SamplesPerPixel);
            cmd.SetComputeIntParam(halogenShader, "MaxBounces", MaxBounces);

            // Debugging parameters
            cmd.SetComputeIntParam(halogenShader, "HalogenDebugMode", HalogenDebugMode);
            cmd.SetComputeIntParam(halogenShader, "TriangleDebugDisplayRange", TriangleDebugDisplayRange);
            cmd.SetComputeIntParam(halogenShader, "BoxDebugDisplayRange", BoxDebugDisplayRange);

            cmd.SetComputeFloatParam(halogenShader, "focalPlaneDistance", FocalPlaneDistance);
            cmd.SetComputeFloatParam(halogenShader, "focalConeAngle", ApertureAngle);

            cmd.SetComputeTextureParam(halogenShader, kernelIndex, "EnvironmentCubemap", EnvironmentCubemap);
            cmd.SetComputeIntParam(halogenShader, "UseEnvironmentCubemap", UseEnvironmentCubemap ? 1 : 0);

            cmd.SetComputeTextureParam(halogenShader, kernelIndex, "Output", rtPathtracingBuffer);
            cmd.SetComputeTextureParam(halogenShader, kernelIndex, "OutputSecondBounce", rtSecondBounceBuffer);

            //Debug.Log("Dispatching " + Mathf.CeilToInt((float)camera.pixelWidth / TileSize) + " X Tiles for an X resolution of " + camera.pixelWidth);

            // Perform path tracing
            cmd.DispatchCompute(halogenShader, kernelIndex, Mathf.CeilToInt(camera.pixelWidth / (float)TileSize), Mathf.CeilToInt(camera.pixelHeight / (float)TileSize), 1);


            AccumulationMaterial.SetTexture("_AccumulationBuffer", rtAccumulationBuffer);
            AccumulationMaterial.SetInteger("_FrameCount", FrameCount);

            if (AccumulationBufferDirty)
            {
                CoreUtils.SetRenderTarget(cmd, rtAccumulationBuffer);
                CoreUtils.ClearRenderTarget(cmd, ClearFlag.Color, Color.black);
                AccumulationBufferDirty = false;
            }

            if (Accumulate) {
                
                Blitter.BlitCameraTexture(cmd, rtPathtracingBuffer, rtBackBuffer, AccumulationMaterial, 0);

                Blitter.BlitCameraTexture(cmd, rtBackBuffer, rtCameraColor);

                FrameCount++;
            }
            else
            {
                Blitter.BlitCameraTexture(cmd, rtPathtracingBuffer, rtCameraColor);
            }
        }
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Dispose()
    {
        rtPathtracingBuffer?.Release();
        rtAccumulationBuffer?.Release();
        rtSecondBounceBuffer?.Release();
        rtBackBuffer?.Release();
        // Release the tons of compute buffers
        sphereBuffer?.Release();
        materialBuffer?.Release();
        triangleBuffer?.Release();
        meshBuffer?.Release();
        TLASBuffer?.Release();
        BLASBuffer?.Release();
    }

    private PackedHalogenMaterial PackHalogenMaterial(HalogenMaterial material, int materialID)
    {
        PackedHalogenMaterial packedMaterial = new PackedHalogenMaterial();
        PackedRayMedium packedMedium = new PackedRayMedium();
        packedMaterial.albedo = (Vector4)material.color;
        packedMaterial.specularAlbedo = (Vector4)material.specularColor;
        packedMaterial.metallic = material.metallic;
        packedMaterial.roughness = material.roughness;
        packedMaterial.emissive = new Vector4(material.emissionColor.r, material.emissionColor.g, material.emissionColor.b, material.emissionIntensity);

        Vector3 vec = ((Vector3)(Vector4)material.subsurfaceColor);
        packedMedium.absorption = new Vector3(1 / vec.x, 1 / vec.y, 1 / vec.z) * Mathf.Max(material.absorption, 0);
        packedMedium.indexOfRefraction = material.indexOfRefraction;
        packedMedium.priority = material.dielectricPriority;
        packedMedium.materialID = (uint)materialID;
        //packedMedium.mediumHash = (uint)(packedMedium.indexOfRefraction * packedMedium.priority + packedMedium.absorption.x * packedMedium.absorption.y * packedMedium.absorption.z); // Todo: this is pretty bad

        packedMaterial.rayMedium = packedMedium;

        packedMaterial.materialID = (uint)materialID;
        return packedMaterial;
    }

    private void UpdateObjectBuffers(CommandBuffer cmd)
    {
        // Empty all lists. Does not free memory. 
        sphereList.Clear();
        meshList.Clear();
        materialList.Clear();
        triangleList.Clear();
        BLASList.Clear();

        unpackedHalogenMaterials.Clear();

        // Fill sphere list
        foreach (RayTracingSphere sphere in RayTracingManager.GetSphereList().Values){
            // Pack struct with sphere data
            HalogenSphere sphereStruct = new HalogenSphere();
            sphereStruct.center = sphere.transform.position;
            sphereStruct.radius = sphere.GetRadius();

            sphereStruct.materialIndex = PackMaterialToList(sphere.material);

            sphereStruct.boundingCornerA = sphereStruct.center - (Vector3.one * sphereStruct.radius); // Lower corner
            sphereStruct.boundingCornerB = sphereStruct.center + (Vector3.one * sphereStruct.radius); // Upper corner

            sphereList.Add(sphereStruct);
        }

        // Fill mesh related lists
        int numTrianglesAdded = 0;
        int numBVHEntriesAdded = 0;
        foreach (RayTracingMesh mesh in RayTracingManager.GetMeshList().Values)
        {
            // Add to material list & get index
            uint materialIndex = PackMaterialToList(mesh.material);

            // Copy triangles from mesh into list
            triangleList.InsertRange(numTrianglesAdded, mesh.GetPackedTriangles());

            // Copy mesh data into list
            meshList.Add(mesh.GetRefreshedMeshData(materialIndex, (uint)numTrianglesAdded, (uint)numBVHEntriesAdded));

            // Copy BVH data into list
            BLASList.InsertRange(numBVHEntriesAdded, mesh.GetBVH());

            // Increment number of triangles added
            numTrianglesAdded += mesh.GetTriangleCount();
            numBVHEntriesAdded += mesh.GetBVH().Count;
        }

        //Debug.Log("Number of meshes loaded for raytracing: " + meshList.Count);

        ReallocateComputeBufferIfNeeded(ref sphereBuffer, sphereList.Count, sphereStructStride);
        ReallocateComputeBufferIfNeeded(ref meshBuffer, meshList.Count, meshStructStride);
        ReallocateComputeBufferIfNeeded(ref materialBuffer, materialList.Count, materialStructStride);
        ReallocateComputeBufferIfNeeded(ref triangleBuffer, triangleList.Count, triangleStructStride);
        ReallocateComputeBufferIfNeeded(ref BLASBuffer, BLASList.Count, BVHEntryStructStride);

        cmd.SetBufferData(sphereBuffer, sphereList);
        cmd.SetBufferData(meshBuffer, meshList);
        cmd.SetBufferData(materialBuffer, materialList);
        cmd.SetBufferData(triangleBuffer, triangleList);
        cmd.SetBufferData(BLASBuffer, BLASList);
    }

    //uint AddMaterialToList(PackedHalogenMaterial material)
    //{
    //    int materialIndex = materialList.Count;
    //    if (materialList.Contains(material)) {
    //        materialIndex = materialList.IndexOf(material);
    //    }
    //    else {
    //        materialList.Add(material);
    //    }

    //    return (uint)materialIndex;
    //}

    uint PackMaterialToList(HalogenMaterial material)
    {
        int materialIndex = materialList.Count;
        if (unpackedHalogenMaterials.Contains(material)) {
            materialIndex = unpackedHalogenMaterials.IndexOf(material);
        }
        else
        {
            unpackedHalogenMaterials.Add(material);
            materialList.Add(PackHalogenMaterial(material, materialIndex));
        }

        return (uint)materialIndex;
    }

    void ReallocateComputeBufferIfNeeded(ref ComputeBuffer buffer, int count, int stride)
    {
        if (buffer == null || buffer.count != count)  
        {
            buffer?.Release();
            buffer = new ComputeBuffer(Mathf.Max(count, 1), stride);
        }
    }
}
