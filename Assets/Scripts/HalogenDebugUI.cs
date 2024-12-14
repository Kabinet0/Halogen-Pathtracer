using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class HalogenDebugUI : MonoBehaviour
{
    [SerializeField] TMPro.TMP_Text mRaysText;
    [SerializeField] TMPro.TMP_Text frameCountText;

    class RayCountTimestamp {
        public RayCountTimestamp(float _time, int _rayCount) { 
            time = _time;
            rayCount = _rayCount;
        }

        public float time;
        public int rayCount;
    }

    private static List<RayCountTimestamp> rayCounts = new List<RayCountTimestamp>();
    private static List<int> invalidFrames = new List<int>(10);

    private Camera camera;
    public static int frameCount = 0;

    // Start is called before the first frame update
    void Start()
    {
        UpdateMRaysText(0);
        camera = Camera.main;

        rayCounts.Clear();
        frameCount = 0;
    }

    public static void AddFrameToAverage(int rayCount) {
        invalidFrames.Clear();
        for (int i = 0; i < rayCounts.Count; i++) {
            if (rayCounts[i].time < Time.unscaledTime - 1.0f)
            {
                invalidFrames.Add(i);
                rayCounts[i].time = -1;
            }
        }

        if (invalidFrames.Count > 0)
        {
            rayCounts[invalidFrames[0]].time = Time.unscaledTime;
            rayCounts[invalidFrames[0]].rayCount = rayCount;
        }
        else { 
            rayCounts.Add(new RayCountTimestamp(Time.unscaledTime, rayCount));
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        int sum = 0;
        foreach (var elem in rayCounts)
        {
            if (elem.time > Time.unscaledTime - 1) {
                sum += elem.rayCount;
            }
        }
        Debug.Log(sum / 1000000.0f + " Mrays / sec");

        UpdateMRaysText(sum);
        UpdateFrameCountText();
    }

    void UpdateMRaysText(float value) {
        mRaysText.text = (value / 1000000.0f) + " MRays/sec";
    }

    void UpdateFrameCountText() {
        if (HalogenRenderFeature.Instance.settings.Accumulate)
        {
            if (!HalogenRenderFeature.Instance.settings.UnlimitedSampling)
            {
                frameCountText.text = frameCount + " / " + HalogenRenderFeature.Instance.settings.MaxAccumulatedFrames + " Frames";
            }
            else
            {
                frameCountText.text = frameCount + " Frames";
            }
        }
        else 
        {
            frameCountText.text = "";
        }
    }
}
