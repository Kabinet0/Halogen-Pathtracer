using UnityEngine.Rendering.Universal;
using UnityEngine;


public enum HalogenDebugMode {
    None,
    Albedo,
    Normal,
    RayTriangleTests,
    RayBoxTests,
    Combined
}

public enum HalogenMeshBoundsDebugMode
{
    None,
    MeshBounds,
    BLAS,
    TLAS
}


[System.Serializable]
public struct HalogenSettings
{
    [Header("General")]
    public ComputeShader HalogenShader;
    public Shader AccumulationShader;
    public bool ShowInSceneView;
    public bool Accumulate;

    [Header("Ray Tracing")]

    public int SamplesPerPixel;
    public int MaxBounces;
    
    [Header("Camera")]
    public float NearPlaneDistance;
    public float FarPlaneDistance;
    public float FocalPlaneDistance;
    [Range(0, 90)] public float ApertureAngle;

    [Header("Debug")]
    
    public HalogenDebugMode DebugMode;
    public HalogenMeshBoundsDebugMode BoundsDebugMode;
    public int TriangleDebugDisplayRange;
    public int BoxDebugDisplayRange;
} 

[DisallowMultipleRendererFeature]
public class HalogenRenderFeature : ScriptableRendererFeature
{
    [SerializeField] HalogenSettings settings;

    private HalogenRenderPass halogenPass = null;


    public override void Create()
    {
        TryCreatePass();
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (halogenPass == null) { TryCreatePass(); return; }

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview) return; // Ignore feature for editor/inspector previews & asset thumbnails
        if (!settings.ShowInSceneView && cameraType == CameraType.SceneView) return;

        renderer.EnqueuePass(halogenPass);
    }

    private void TryCreatePass()
    {
        if (settings.HalogenShader != null && settings.AccumulationShader != null)
        {
            halogenPass = new HalogenRenderPass(ref settings);
            halogenPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }
    }

    protected override void Dispose(bool disposing)
    {
        halogenPass?.Dispose();
        base.Dispose(disposing);
    }

}
