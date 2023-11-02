using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PresetFogCurve : MonoBehaviour
{
    [SerializeField]
    private Material fogMaterial;
    [SerializeField]
    private List<fogSnapshot> snapshots;
    int nextInd = 0;
    int prevInd = 0;

    // Update is called once per frame
    void Update()
    {
        float progress = TimeOfDay.GetProgress();
        for (; nextInd < snapshots.Count && progress > snapshots[nextInd].progress; nextInd++)
        {
            prevInd = nextInd;
        };
        nextInd %= snapshots.Count;

        fogSnapshot prevSnapshot = snapshots[prevInd];
        fogSnapshot nextSnapshot = snapshots[nextInd];
        float t = Mathf.InverseLerp(prevSnapshot.progress, nextSnapshot.progress, progress);
        fogMaterial.Lerp(prevSnapshot.fog, nextSnapshot.fog, t);
    }
}

[System.Serializable]
public class fogSnapshot
{
    public Material fog;
    public float progress;
}
