using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct HalogenMaterial
{
    public Color color;
    [Range(0, 1)] public float roughness;
    [Range(0, 1)] public float metallic;
    public Color specularColor;

    [Header("Emission")]
    public Color emissionColor;
    public float emissionIntensity;

    public HalogenMaterial(Color defaultColor)
    {
        color = defaultColor;
        specularColor = defaultColor;
        roughness = 1;
        metallic = 0;

        emissionColor = Color.black;
        emissionIntensity = 0;
    }
}

public class RayTracingManager
{
    static List<RayTracingSphere> rayTracingSphereList = new List<RayTracingSphere>();
    static List<RayTracingMesh> rayTracingMeshList = new List<RayTracingMesh>();

    public static void AddToSphereList(RayTracingSphere sphere)
    {
        if (rayTracingSphereList.Contains(sphere))
        {
            return;
        }
        rayTracingSphereList.Add(sphere);
    }

    public static void AddToMeshList(RayTracingMesh mesh)
    {
        if (rayTracingMeshList.Contains(mesh))
        {
            return;
        }
        rayTracingMeshList.Add(mesh);
    }

    public static void RemoveFromSphereList(RayTracingSphere sphere)
    {
        rayTracingSphereList.Remove(sphere);
    }

    public static void RemoveFromMeshList(RayTracingMesh mesh)
    {
        rayTracingMeshList.Remove(mesh);
    }

    public static ref List<RayTracingSphere> GetSphereList()
    {
        return ref rayTracingSphereList;
    }

    public static ref List<RayTracingMesh> GetMeshList()
    {
        return ref rayTracingMeshList;
    }
}
