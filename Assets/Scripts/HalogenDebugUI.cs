using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class HalogenDebugUI : MonoBehaviour
{
    [SerializeField] TMPro.TMP_Text mRaysText;

    private static Dictionary<float, int> rayCounts = new Dictionary<float, int>();
    private static List<float> framesToRemove = new List<float>(10);

    private Camera camera;

    // Start is called before the first frame update
    void Start()
    {
        UpdateMRaysText(0);
        camera = Camera.main;
    }

    public static void AddFrameToAverage(int mRays) {
        //rayCounts.Add(Time.time, mRays); 
    }

    // Update is called once per frame
    void Update()
    {
        // This is a terrible system. But I can't think of anything better :/
        //framesToRemove.Clear();
        //foreach (float t in rayCounts.Keys)
        //{
        //    if (t < Time.time - 1)
        //    {
        //        framesToRemove.Add(t);
        //    }
        //}

        //foreach (float key in framesToRemove)
        //{
        //    rayCounts.Remove(key);
        //}

        //int sum = 0; 
        //foreach (int v in rayCounts.Values)
        //{
        //    sum += v;
        //}
        //Debug.Log(sum / 1000000.0f + " Mrays / sec");

        UpdateMRaysText(1f / Time.smoothDeltaTime * camera.pixelWidth * camera.pixelHeight);
    }

    void UpdateMRaysText(float value) {
        mRaysText.text = (value / 1000000.0f) + " MRays/sec";
    }
}
