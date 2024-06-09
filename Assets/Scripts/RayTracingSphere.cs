using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class RayTracingSphere : MonoBehaviour
{
    [SerializeField] public HalogenMaterial material = new HalogenMaterial(Color.white); // Silly C# 9
    [SerializeField] float Radius;

    private void OnValidate()
    {
        transform.localScale = new Vector3(Radius * 2, Radius * 2, Radius * 2);
    }

    void OnEnable()
    {
        RayTracingManager.AddToSphereList(this);
    }

    public float GetRadius()
    {
        return Radius;
    }

    void OnDisable()
    {
        RayTracingManager.RemoveFromSphereList(this);
    }
}
