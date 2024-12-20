using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;

public class BVHGenerator
{
    public static readonly int maxNodeTriangleCount = 5; // Just guessed. This seems fine.

    // todo
    // sometimes I call them nodes sometimes I call them entries...
    // pick a lane, me
    public static void GenerateMeshBVH(RayTracingMesh meshObject) 
    {
        

        List<int> indicies = meshObject.GetTriangles();
        List<Vector3> verticies = meshObject.GetVerticies();
        List<BVHEntry> BVHData = meshObject.GetBVH();

        uint totalTriangleCount = (uint)meshObject.GetTriangleCount(); 

        BVHData.Clear();

        // Create root bvh entry
        Bounds meshBounds = meshObject.GetMesh().bounds;
        BVHData.Add(initializeLeafEntry(meshBounds.min, meshBounds.max, 0, totalTriangleCount));

        // create dummy node for cache alignment
        //BVHData.Add(initializeLeafEntry(Vector3.zero, Vector3.zero, 0, 0));

        // Compute a centroid for each triangle, use to build bvh
        Vector3[] triangleCentroids = new Vector3[totalTriangleCount];
        for (int i = 0; i < totalTriangleCount; i++)
        {
            triangleCentroids[i] = (verticies[indicies[i * 3]] + verticies[indicies[(i * 3) + 1]] + verticies[indicies[(i * 3) + 2]]) / 3;
        }

        // Don't recursion
        List<int> entryProcessingQueue = new List<int>();
        List<int> nextEntryProcessingQueue = new List<int>();
        entryProcessingQueue.Add(0);

        int nodeSplitFailures = 0;
        int totalDepth = 0;
        int minTrisPerNode = int.MaxValue;
        int maxTrisPerNode = 0;

        // Process all nodes at each depth up until max depth
        for (int depth = 1; depth <= meshObject.MaxHierarchyDepth; depth++)
        {
            if (!(entryProcessingQueue.Count > 0)) { break; }
            
            totalDepth++;
            // Attempt splitting each leaf node at the current depth into a hierarchy node
            foreach (int entryIndex in entryProcessingQueue)
            {
                BVHEntry currentEntry = BVHData[entryIndex];

                // Every node starts here as a leaf node, meaning indicies represent triangle starting indicies and count
                uint nodeFirstTriangle = currentEntry.indexA;
                uint nodeTriangleCount = currentEntry.triangleCount;

                Vector3 boundSize = currentEntry.boundingCornerB - currentEntry.boundingCornerA;
                int splitAxis = boundSize.x > boundSize.y ? (boundSize.x > boundSize.z ? 0 : 2) : (boundSize.y > boundSize.z ? 1 : 2);
                float splitPos = currentEntry.boundingCornerA[splitAxis] + boundSize[splitAxis] / 2; // Split longest side in half

                int i = (int)nodeFirstTriangle; // Represents first triangle in this entry
                int j = i + (int)nodeTriangleCount - 1; // Equal to last triangle in this entry

                // Sort all relevant triangles to before or after split point
                while (i <= j)
                {
                    if (triangleCentroids[i][splitAxis] < splitPos) {
                        i++; // this triangle is fine, move on
                    }
                    else {
                        swapEntries(i, j, indicies, triangleCentroids); // triangle is past swap point, swap with back of list and try again
                        j--; // triangle just swapped to end is definitely past split point, don't try to swap with it again next time
                    }

                }

                uint childACount = (uint)i - nodeFirstTriangle;
                uint childBCount = nodeTriangleCount - childACount;
                if (!(childACount > 0 && childBCount > 0)) {
                    nodeSplitFailures++;
                    // Calculate leaf node statistics (like 80% sure this is right)
                    minTrisPerNode = Mathf.Min(minTrisPerNode, (int)nodeTriangleCount);
                    maxTrisPerNode = Mathf.Max(maxTrisPerNode, (int)nodeTriangleCount);
                    continue; // split position didn't create two seperate groups, give up.
                }

                if (nodeTriangleCount <= maxNodeTriangleCount) {
                    // Calculate leaf node statistics (like 80% sure this is right)
                    minTrisPerNode = Mathf.Min(minTrisPerNode, (int)nodeTriangleCount);
                    maxTrisPerNode = Mathf.Max(maxTrisPerNode, (int)nodeTriangleCount);
                    continue; // fewer than max vertex count in this node. give up since we really don't need to split it
                }

                // Create new leaf nodes as children
                int childAIndex = BVHData.Count;
                Bounds childBoundsA = calculateBounds(nodeFirstTriangle, childACount, indicies, verticies);
                BVHData.Add(initializeLeafEntry(childBoundsA.min, childBoundsA.max, nodeFirstTriangle, childACount));
                if (childACount > 2) { // Don't bother processing a node with too few triangles to split
                    nextEntryProcessingQueue.Add(childAIndex); // queue up new node for splitting next iteration
                }



                int childBIndex = BVHData.Count;
                Bounds childBoundsB = calculateBounds((uint)i, childBCount, indicies, verticies);
                BVHData.Add(initializeLeafEntry(childBoundsB.min, childBoundsB.max, (uint)i, childBCount));
                if (childBCount > 2) {
                    nextEntryProcessingQueue.Add(childBIndex);
                }



                // Update current node to point to new children
                currentEntry.indexA = (uint)childAIndex; // Child B is always at childAIndex + 1 so no need to store it
                currentEntry.triangleCount = 0; // mark as hierarchy node
                BVHData[entryIndex] = currentEntry;
            }

            entryProcessingQueue.Clear();
            entryProcessingQueue.AddRange(nextEntryProcessingQueue);
            nextEntryProcessingQueue.Clear();
        }

        Debug.Log("Finished generating BVH for object: " + meshObject.gameObject.name + " with " + nodeSplitFailures + " unsplit nodes.\nNode Depth: " + totalDepth + ", Max Node Depth: " + meshObject.MaxHierarchyDepth + ", Min Node Triangle Count: " + minTrisPerNode + ", Max Node Triangle Count: " + maxTrisPerNode);
    }

    // todo: this is kinda cursed, do something about it?
    private static void swapEntries(int i, int j, List<int> trianglesArray, Vector3[] centroidArray)
    {
        Vector3Int oldTri = new Vector3Int(trianglesArray[i * 3], trianglesArray[i * 3 + 1], trianglesArray[i * 3 + 2]); // store all three indicies of old triangle

        trianglesArray[i * 3] = trianglesArray[j * 3];
        trianglesArray[i * 3 + 1] = trianglesArray[j * 3 + 1];
        trianglesArray[i * 3 + 2] = trianglesArray[j * 3 + 2];

        trianglesArray[j * 3] = oldTri[0];
        trianglesArray[j * 3 + 1] = oldTri[1];
        trianglesArray[j * 3 + 2] = oldTri[2];

        Vector3 oldCentroid = centroidArray[i];
        centroidArray[i] = centroidArray[j];
        centroidArray[j] = oldCentroid;
    }

    private static Bounds calculateBounds(uint startIndex, uint count, List<int> triangleArray, List<Vector3> vertexArray)
    {
        Vector3 boundsMin = new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity);
        Vector3 boundsMax = -boundsMin; 

        for (int i = (int)startIndex; i < startIndex + count; i++)
        {
            int triangleStartIndex = i * 3;
            boundsMin = Vector3.Min(boundsMin, vertexArray[triangleArray[triangleStartIndex]]);
            boundsMin = Vector3.Min(boundsMin, vertexArray[triangleArray[triangleStartIndex + 1]]);
            boundsMin = Vector3.Min(boundsMin, vertexArray[triangleArray[triangleStartIndex + 2]]);

            boundsMax = Vector3.Max(boundsMax, vertexArray[triangleArray[triangleStartIndex]]);
            boundsMax = Vector3.Max(boundsMax, vertexArray[triangleArray[triangleStartIndex + 1]]);
            boundsMax = Vector3.Max(boundsMax, vertexArray[triangleArray[triangleStartIndex + 2]]);
        }

        Bounds bounds = new Bounds();
        bounds.SetMinMax(boundsMin, boundsMax);

        if (!float.IsNormal(boundsMin.x) && boundsMin.x != 0)
        {
            Debug.Log("Error: bounding box contains NaN, start idx: " + startIndex + ", count: " + count);
        }

        // Ensure AABB is at least of a certain size, to make sure thin meshes still render
        if (bounds.size.x < RayTracingMesh.AABBEpsilon || bounds.size.y < RayTracingMesh.AABBEpsilon || bounds.size.z < RayTracingMesh.AABBEpsilon)
        {
            bounds.max += Vector3.one * RayTracingMesh.AABBEpsilon;
        } 

        return bounds;
    }

    private static BVHEntry initializeLeafEntry(Vector3 boundsMin, Vector3 boundsMax, uint indexA, uint triangleCount)
    {
        BVHEntry entry = new BVHEntry();
        //entry.isLeafNode = true;

        entry.boundingCornerA = boundsMin;
        entry.boundingCornerB = boundsMax;

        entry.indexA = indexA;
        entry.triangleCount = triangleCount;

        return entry;
    }
}
