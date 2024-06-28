using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using static UnityEngine.Mesh;

[ExecuteInEditMode]
public class RayTracingMesh : MonoBehaviour
{
    private readonly float AABBEpsilon = 0.00001f;

    public HalogenMaterial material = new HalogenMaterial(Color.white); // Silly C# 9
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    // Cached mesh information
    private HalogenTriangle[] triangleList;
    private HalogenMeshData meshData = new HalogenMeshData();

    // Unique ID
    private uint id = 0;

    void OnEnable()
    {
        id = RayTracingManager.AddToMeshList(this);
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        CacheTriangleList();
    }

    void OnDisable()
    {
        RayTracingManager.RemoveFromMeshList(this);
    }

    private void CacheTriangleList()
    {
        Mesh objectMesh = meshFilter.sharedMesh;
        triangleList = new HalogenTriangle[objectMesh.triangles.Length / 3];
        
        for (int i = 0; i < triangleList.Length; i++) {
            
            HalogenTriangle tri = new HalogenTriangle();
            tri.pointA = objectMesh.vertices[objectMesh.triangles[i * 3]];
            tri.pointB = objectMesh.vertices[objectMesh.triangles[(i * 3) + 1]];
            tri.pointC = objectMesh.vertices[objectMesh.triangles[(i * 3) + 2]];

            tri.normalA = objectMesh.normals[objectMesh.triangles[i * 3]];
            tri.normalB = objectMesh.normals[objectMesh.triangles[(i * 3) + 1]];
            tri.normalC = objectMesh.normals[objectMesh.triangles[(i * 3) + 2]];

            triangleList[i] = tri;
        }
    }

    public HalogenMeshData GetRefreshedMeshData(uint materialIndex, uint startingTriangleIndex)
    {
        Bounds meshBounds = GetBounds();

        meshData.boundingCornerA = meshBounds.min;
        meshData.boundingCornerB = meshBounds.max;

        meshData.triangleCount = (uint)GetTriangleCount();
        meshData.startingIndex = startingTriangleIndex;
        meshData.materialIndex = materialIndex;

        meshData.localToWorld = meshRenderer.localToWorldMatrix;
        meshData.worldToLocal = meshRenderer.worldToLocalMatrix;

        return meshData;
    }

    public void InsertToTriangleBuffer(ref List<HalogenTriangle> triangleBuffer, uint startingIndex)
    {
        triangleBuffer.InsertRange((int)startingIndex, triangleList);
    }

    public Bounds GetBounds()
    {
        var bounds = meshRenderer.bounds;


        // Ensure AABB is at least of a certain size
        if (bounds.size.x < AABBEpsilon || bounds.size.y < AABBEpsilon || bounds.size.z < AABBEpsilon)
        {
            bounds.max += Vector3.one * AABBEpsilon;
        }
        //bs = bounds.size;
        return bounds;
    }

    public uint GetTriangleCount()
    {
        return (uint)triangleList.Length;
    }

    public uint GetID()
    {
        return id;
    }
}
