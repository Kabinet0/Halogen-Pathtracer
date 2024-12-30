using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

public class HalogenDebugger : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private RenderTexture outputTexture;
    [SerializeField] private ComputeShader shader;

    [Header("Settings")]
    [SerializeField] private bool run;

    private GraphicsFence completionFence;
    private CommandBuffer cmd;

    private bool running = false;
    
    void Start()
    {
        if (!run) { 
            return;
        }

        if (outputTexture == null || shader == null) {
            Debug.Log("Cannot run debug kernel. One or more inputs are null.");
            return;
        }

        if (outputTexture.width != outputTexture.height) {
            Debug.Log("Debug texture isn't square. Stopping debugger.");
            return;
        }

        int kernelID = shader.FindKernel("DebugKernel");

        shader.SetTexture(kernelID, "_DebugOutput", outputTexture);
        shader.SetInt("_TextureSize", outputTexture.width);

        shader.Dispatch(kernelID, 1, 1, 1);

        Debug.Log("Dispatched Debug Kernel.");

    }

    void Update()
    {

    }

    private void OnDisable()
    {
    }
}
