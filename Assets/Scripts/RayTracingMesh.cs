using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using static UnityEngine.Mesh;

[ExecuteInEditMode]
public class RayTracingMesh : MonoBehaviour
{
    public static readonly float AABBEpsilon = 0.00001f;

    public HalogenMaterial material = new HalogenMaterial(Color.white); // Silly C# 9

    [Header("Acceleration Structure Parameters")]
    public int MaxHierarchyDepth = 32;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    // Cached mesh information
    private HalogenTriangle[] halogenTriangleList;
    private HalogenMeshData meshData = new HalogenMeshData();

    private List<BVHEntry> meshBVH = new List<BVHEntry>();

    // Cached raw meshdata
    private List<Vector3> verticies = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private List<Vector3> normals = new List<Vector3>();

    // Unique ID
    private uint id = 0;

    void OnEnable()
    {
        id = RayTracingManager.AddToMeshList(this);
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        CacheRaytracingData();
    }

    void OnDisable()
    {
        RayTracingManager.RemoveFromMeshList(this);
        id = 0;
    }

    private void CacheRaytracingData()
    {
        Mesh objectMesh = meshFilter.sharedMesh;

        // For performance reasons copying these over locally is VERY important
        // Unity meshes are on the native side and these properties are NOT C# arrays we can just reference
        // Each access is more like a memory copy to a newly instanciated array in managed code, just for this one purpose, and accessing these in a loop would be insane
        // Better yet use .GetVerticies() with an argument to just populate an array you manage instead of allocating a new one

        objectMesh.GetTriangles(triangles, 0);
        objectMesh.GetVertices(verticies);
        objectMesh.GetNormals(normals);

        // Must run first since it reorders mesh data
        BVHGenerator.GenerateMeshBVH(this); 

        UpdateTriangleList();
    }

    private void UpdateTriangleList()
    {
        halogenTriangleList = new HalogenTriangle[triangles.Count / 3];
        //Debug.Log("Allocating " + triangles.Length / 3 + " triangles, what should be " + (72 * triangles.Length / 3) + " bytes");

        for (int i = 0; i < halogenTriangleList.Length; i++)
        {
            halogenTriangleList[i].pointA = verticies[triangles[i * 3]];
            halogenTriangleList[i].pointB = verticies[triangles[(i * 3) + 1]];
            halogenTriangleList[i].pointC = verticies[triangles[(i * 3) + 2]];

            halogenTriangleList[i].normalA = normals[triangles[i * 3]];
            halogenTriangleList[i].normalB = normals[triangles[(i * 3) + 1]];
            halogenTriangleList[i].normalC = normals[triangles[(i * 3) + 2]];
        }

        //Debug.Log("Done");
    }

    public HalogenMeshData GetRefreshedMeshData(uint materialIndex, uint startingTriangleIndex, uint accelerationStartingIndex)
    {
        Bounds meshBounds = GetBounds();

        meshData.boundingCornerA = meshBounds.min;
        meshData.boundingCornerB = meshBounds.max;

        meshData.triangleBufferOffset = startingTriangleIndex;
        meshData.accelerationBufferOffset = accelerationStartingIndex;
        meshData.materialIndex = materialIndex;

        meshData.localToWorld = meshRenderer.localToWorldMatrix;
        meshData.worldToLocal = meshRenderer.worldToLocalMatrix;

        return meshData;
    }

    public Bounds GetBounds()
    {
        var bounds = meshRenderer.bounds;

        // Ensure AABB is at least of a certain size, to make sure planes still render
        if (bounds.size.x < AABBEpsilon || bounds.size.y < AABBEpsilon || bounds.size.z < AABBEpsilon)
        {
            bounds.max += Vector3.one * AABBEpsilon;
        }

        return bounds;
    }

    public Mesh GetMesh()
    {
        return meshFilter.sharedMesh;
    }

    public int GetTriangleCount()
    {
        return triangles.Count / 3;
    }

    public HalogenTriangle[] GetPackedTriangles()
    {
        return halogenTriangleList;
    }

    public List<Vector3> GetVerticies()
    {
        return verticies;
    }

    public List<int> GetTriangles()
    {
        return triangles;
    }

    public List<Vector3> GetNormals()
    {
        return normals;
    }

    public List<BVHEntry> GetBVH()
    {
        return meshBVH;
    }

    public uint GetID()
    {
        return id;
    }
}
