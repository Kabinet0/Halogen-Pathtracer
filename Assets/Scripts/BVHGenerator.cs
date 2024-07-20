using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;

public class BVHGenerator
{
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
        var meshBounds = meshObject.GetBounds();
        BVHData.Add(initializeLeafEntry(meshBounds.min, meshBounds.max, 0, totalTriangleCount));

        // create dummy node for cache alignment
        BVHData.Add(initializeLeafEntry(Vector3.zero, Vector3.zero, 0, 0));

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
        // Process all nodes at each depth up until max depth
        for (int depth = 0; depth < meshObject.HierarchyDepth; depth++)
        {
            // Attempt splitting each leaf node at the current depth into a hierarchy node
            foreach (int entryIndex in entryProcessingQueue)
            {
                // Every node starts here as a leaf node, meaning indicies represent triangle starting indicies and count
                uint nodeFirstTriangle = BVHData[entryIndex].indexA;
                uint nodeTriangleCount = BVHData[entryIndex].triangleCount;

                
                Vector3 boundSize = BVHData[entryIndex].boundingCornerB - BVHData[entryIndex].boundingCornerA;
                int splitAxis = boundSize.x > boundSize.y ? (boundSize.x > boundSize.z ? 0 : 2) : (boundSize.y > boundSize.z ? 1 : 2);
                float splitPos = boundSize[splitAxis] / 2; // Split longest side in half

                uint i = nodeFirstTriangle; // Represents first triangle in this entry
                uint j = i + nodeTriangleCount - 1; // Equal to last triangle in this entry

                // Sort all relevant triangles to before or after split point
                while (i <= j)
                {
                    if (triangleCentroids[i][splitAxis] < splitPos) {
                        i++; // this triangle is fine, move on
                    }
                    else {
                        swapEntries((int)i, (int)j, indicies, triangleCentroids); // triangle is past swap point, swap with back of list and try again
                        j--; // triangle just swapped to end is definitely past split point, don't try to swap with it again next time
                    }

                }

                uint childACount = i - nodeFirstTriangle;
                uint childBCount = nodeTriangleCount - childACount;
                if (!(childACount > 0)) {
                    nodeSplitFailures++;
                    continue; // split position didn't create two seperate groups, give up.
                }

                // Create new leaf nodes as children
                int childAIndex = BVHData.Count;
                Bounds childBoundsA = calculateBounds(nodeFirstTriangle, childACount, indicies, verticies);
                BVHData.Add(initializeLeafEntry(childBoundsA.min, childBoundsA.max, nodeFirstTriangle, childACount));
                nextEntryProcessingQueue.Add(childAIndex); // queue up new node for splitting next iteration

                int childBIndex = BVHData.Count;
                Bounds childBoundsB = calculateBounds(i, childBCount, indicies, verticies);
                BVHData.Add(initializeLeafEntry(childBoundsB.min, childBoundsB.max, i, childBCount));
                nextEntryProcessingQueue.Add(childBIndex); // queue up new node for splitting next iteration

                // Update current node to point to new children
                BVHEntry NewEntry = BVHData[entryIndex];
                
                NewEntry.indexA = (uint)childAIndex; // Child B is always at childAIndex + 1 so no need to store it
                NewEntry.triangleCount = 0; // mark as hierarchy node

                BVHData[entryIndex] = NewEntry;
            }

            entryProcessingQueue = nextEntryProcessingQueue;
            nextEntryProcessingQueue.Clear();
        }

        Debug.Log("Finished generating bvh with " + nodeSplitFailures + " node splitting failures");
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

        return new Bounds(boundsMin, boundsMax);
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
