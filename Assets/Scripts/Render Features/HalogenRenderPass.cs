using UnityEngine.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor.Rendering;
using UnityEngine.Experimental.Rendering;

//[StructLayout(LayoutKind.Sequential)]
public struct HalogenSphere
{
    public Vector3 center;
    public float radius;
    public PackedHalogenMaterial material;
    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;
}

public struct HalogenMeshData
{
    public uint startingIndex;
    public uint triangleCount;

    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;

    public uint materialIndex;

    public Matrix4x4 worldToLocal;
    public Matrix4x4 localToWorld;
}
public struct PackedHalogenMaterial
{
    public Vector4 albedo;
    public Vector4 specularAlbedo;
    public float metallic;
    public float roughness;
    public Vector4 emissive;
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

public struct BVHEntry
{
    public uint childIdxA;
    public uint childIdxB;

    public Vector3 boundingCornerA;
    public Vector3 boundingCornerB;

    public bool isLeafNode;
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

    float NearPlaneDistance;
    float FocalPlaneDistance;
    float ApertureAngle;

    Material AccumulationMaterial;
    Vector3 PriorCameraPosition;
    Quaternion PriorCameraRotation;

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

        ApertureAngle = Mathf.Clamp(_settings.ApertureAngle, 0, 89.9f);

        Accumulate = _settings.Accumulate;

        FrameCount = 1;
        AccumulationBufferDirty = true;
        AccumulationMaterial = CoreUtils.CreateEngineMaterial(_settings.AccumulationShader);

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
            cmd.SetComputeVectorParam(halogenShader, "ViewParameters", new Vector3(w, h, nClip));
            cmd.SetComputeVectorParam(halogenShader, "CameraParameters", camera.transform.position);

            
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "MeshList", meshBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "SphereList", sphereBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "MaterialList", materialBuffer);
            cmd.SetComputeBufferParam(halogenShader, kernelIndex, "TriangleBuffer", triangleBuffer);

            cmd.SetComputeIntParam(halogenShader, "RandomSeed", Accumulate ? FrameCount : 1);

            cmd.SetComputeVectorParam(halogenShader, "BufferCounts", new Vector4(sphereList.Count, meshList.Count, 0, 0));
            cmd.SetComputeIntParam(halogenShader, "SamplesPerPixel", SamplesPerPixel);
            cmd.SetComputeIntParam(halogenShader, "MaxBounces", MaxBounces);

            cmd.SetComputeFloatParam(halogenShader, "focalPlaneDistance", FocalPlaneDistance);
            cmd.SetComputeFloatParam(halogenShader, "focalConeAngle", ApertureAngle);
            //Debug.Log("AA: " + FocalPlaneDistance + " " +  ApertureAngle);

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

    private PackedHalogenMaterial PackHalogenMaterial(HalogenMaterial material)
    {
        PackedHalogenMaterial packedMaterial = new PackedHalogenMaterial();
        packedMaterial.albedo = (Vector4)material.color;
        packedMaterial.specularAlbedo = (Vector4)material.specularColor;
        packedMaterial.metallic = material.metallic;
        packedMaterial.roughness = material.roughness;
        packedMaterial.emissive = new Vector4(material.emissionColor.r, material.emissionColor.g, material.emissionColor.b, material.emissionIntensity);

        return packedMaterial;
    }

    private void UpdateObjectBuffers(CommandBuffer cmd)
    {
        // Empty all lists. Does not free memory. 
        sphereList.Clear();
        meshList.Clear();
        materialList.Clear();
        triangleList.Clear();


        // Fill sphere list
        foreach (var sphere in RayTracingManager.GetSphereList()){
            HalogenSphere sphereStruct = new HalogenSphere();
            sphereStruct.center = sphere.transform.position;
            sphereStruct.radius = sphere.GetRadius();

            sphereStruct.material = PackHalogenMaterial(sphere.material);

            sphereStruct.boundingCornerA = sphereStruct.center - (Vector3.one * sphereStruct.radius); // Lower corner
            sphereStruct.boundingCornerB = sphereStruct.center + (Vector3.one * sphereStruct.radius); // Upper corner

            sphereList.Add(sphereStruct);
        }

        // Fill mesh related lists
        uint numTrianglesAdded = 0;
        foreach (RayTracingMesh mesh in RayTracingManager.GetMeshList())
        {
            // Add to material list & get index
            uint materialIndex = AddMaterialToList(PackHalogenMaterial(mesh.material));

            // Copy triangles from mesh into list
            mesh.InsertToTriangleBuffer(ref triangleList, numTrianglesAdded);

            // Copy mesh data into array
            meshList.Add(mesh.GetRefreshedMeshData(materialIndex, numTrianglesAdded));

            // Increment number of triangles added
            numTrianglesAdded += mesh.GetTriangleCount();
        }

        ReallocateComputeBufferIfNeeded(ref sphereBuffer, sphereList.Count, sphereStructStride);
        ReallocateComputeBufferIfNeeded(ref meshBuffer, meshList.Count, meshStructStride);
        ReallocateComputeBufferIfNeeded(ref materialBuffer, materialList.Count, materialStructStride);
        ReallocateComputeBufferIfNeeded(ref triangleBuffer, triangleList.Count, triangleStructStride);

        cmd.SetBufferData(sphereBuffer, sphereList);
        cmd.SetBufferData(meshBuffer, meshList);
        cmd.SetBufferData(materialBuffer, materialList);
        cmd.SetBufferData(triangleBuffer, triangleList);
    }

    uint AddMaterialToList(PackedHalogenMaterial material)
    {
        int materialIndex = materialList.Count;
        if (materialList.Contains(material)) {
            materialIndex = materialList.IndexOf(material);
        }
        else {
            materialList.Add(material);
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
