using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public class LoadingHandler : MonoBehaviour
{
    public Slider slider;
    private float finishedLoad;

    //Please improve loading screen one day
    void Start(){
        finishedLoad = 0;
    }
    void Update()
    {
        if(EndlessTerrain.RequestQueue.IsEmpty){
            this.gameObject.SetActive(false);
            return;
        }

        float totalRemainingLoad = 0; 
        foreach(EndlessTerrain.GenTask task in EndlessTerrain.RequestQueue){
            totalRemainingLoad += task.load;
        }

        slider.value = finishedLoad / (totalRemainingLoad + finishedLoad);
        finishedLoad += EndlessTerrain.maxFrameLoad;
    }
}
