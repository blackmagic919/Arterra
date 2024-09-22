using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static EndlessTerrain;

public class LoadingHandler : MonoBehaviour
{
    public Image Background;
    public Slider slider;
    public TextMeshProUGUI taskText;
    private float finishedLoad;
    public static string[] taskDescriptions = {
        "Generating Surface Data",
        "Planning Structures",
        "Combining Map Infomation",
        "Generating Terrain",
        "Facilitating Generation"
    };

    //Please improve loading screen one day
    void Start(){
        //Random background
        Background.sprite = Resources.Load<Sprite>($"Textures/BackgroundImages/Background_{Random.Range(1, 9)}");
        finishedLoad = 0;
    }
    void Update()
    {
        if(RequestQueue.IsEmpty){
            this.gameObject.SetActive(false);
            return;
        }

        float totalRemainingLoad = 0; 
        foreach(GenTask task in RequestQueue){
            totalRemainingLoad += taskLoadTable[task.id];
        }

        RequestQueue.TryPeek(out GenTask fTask);
        taskText.text = taskDescriptions[fTask.id];
        slider.value = finishedLoad / (totalRemainingLoad + finishedLoad);
        finishedLoad += maxFrameLoad;
    }
}
