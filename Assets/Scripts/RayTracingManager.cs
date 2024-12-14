using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[System.Serializable]
public struct HalogenMaterial
{
    public Color color;
    [Range(0, 1)] public float roughness;
    [Range(0, 1)] public float metallic;
    public Color specularColor;

    [Header("Transmission")]
    public Color subsurfaceColor;
    [Range(1, 8)] public float indexOfRefraction;
    [Range(0, 4)] public float absorption;
    public int dielectricPriority;

    [Header("Emission")]
    public Color emissionColor;
    public float emissionIntensity;

    public HalogenMaterial(Color defaultColor)
    {
        color = defaultColor;
        specularColor = defaultColor;
        subsurfaceColor = defaultColor;
        roughness = 1;
        metallic = 0;
        indexOfRefraction = 1;
        absorption = 0;
        dielectricPriority = 0;

        emissionColor = Color.black;
        emissionIntensity = 0;
    }
}

public class RayTracingManager
{
    static Dictionary<uint, RayTracingSphere> rayTracingSphereList = new Dictionary<uint, RayTracingSphere>();
    static Dictionary<uint, RayTracingMesh> rayTracingMeshList = new Dictionary<uint, RayTracingMesh>();

    private static uint sphereIDCount = 0, meshIDCount = 0;

    private static bool sphereListDirty = true, meshListDirty = true;

    public static uint AddToSphereList(RayTracingSphere sphere)
    {
        uint sphereID = sphere.GetID();
        if (sphereID == 0) // If uninitialized
        {
            sphereIDCount++;
            sphereID = sphereIDCount;
        }

        // If not already in list, add to list
        if (!rayTracingSphereList.ContainsKey(sphereID))
        {
            rayTracingSphereList.Add(sphereID, sphere);
        }
        else
        {
            Debug.Log("Trying to add duplicate ray tracing sphere with ID " + sphereID);
        }

        sphereListDirty = true;
        return sphereID;
    }

    public static uint AddToMeshList(RayTracingMesh mesh)
    {
        uint meshID = mesh.GetID();
        if (meshID == 0) // If uninitialized
        {
            meshIDCount++;
            meshID = meshIDCount;
        }

        // If not already in list, add to list
        if (!rayTracingMeshList.ContainsKey(meshID))
        {
            rayTracingMeshList.Add(meshID, mesh);
        }
        else
        {
            Debug.Log("Trying to add duplicate ray tracing mesh with ID " + meshID);
        }

        meshListDirty = true;
        return meshID;
    }

    public static void RemoveFromSphereList(RayTracingSphere sphere)
    {
        markSphereListDirty();
        rayTracingSphereList.Remove(sphere.GetID());
    }

    public static void RemoveFromMeshList(RayTracingMesh mesh)
    {
        markSphereListDirty();
        rayTracingMeshList.Remove(mesh.GetID());
    }

    public static ref Dictionary<uint, RayTracingSphere> GetSphereList()
    {
        return ref rayTracingSphereList;
    }

    public static ref Dictionary<uint, RayTracingMesh> GetMeshList()
    {
        return ref rayTracingMeshList;
    }

    public static void markSphereListDirty()
    {
        sphereListDirty = true;
    }

    public static void markMeshListDirty()
    {
        meshListDirty = true;
    }

    public static void markSphereListClean()
    {
        sphereListDirty = false;
    }

    public static void markMeshListClean()
    {
        meshListDirty = false;
    }

    public bool getMeshListDirty() { 
        return meshListDirty;
    }

    public bool getSphereListDirty()
    {
        return sphereListDirty;
    }
}
