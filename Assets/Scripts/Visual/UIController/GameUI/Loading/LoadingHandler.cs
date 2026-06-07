using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Configuration;
using Arterra.Engine.Audio;
using static Arterra.Core.ArterraRuntime;
using Arterra.Core;
using System;

namespace Arterra.GamePlay.UI {
    public static class LoadingHandler {
        public static GameObject LoadingScreen;
        public static Image Background;
        public static Slider slider;
        public static TextMeshProUGUI taskText;
        private static IUpdateSubscriber eventTask;
        private static float finishedLoad;
        private static float progress;
        public static Action OnStartupTasksFinished;
        public static string[] taskDescriptions = {
            "Generating Surface Data",
            "Planning Structures",
            "Combining Map Infomation",
            "Filling Hills and Clouds",
            "Generating Terrain",
            "Facilitating Generation",
            "Plating Grass and Growing Leaves"
        };

        //Please improve loading screen one day
        public static void Initialize() {
            LoadingScreen = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Loading"), GameUIManager.UIHandle.transform);
            Background = LoadingScreen.transform.GetComponent<Image>();
            slider = LoadingScreen.transform.GetChild(0).GetComponent<Slider>();
            taskText = LoadingScreen.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            Background.sprite = Resources.Load<Sprite>($"Textures/BackgroundImages/Background_{UnityEngine.Random.Range(1, 10)}");
            Activate();
        }

        public static void Activate() {
            if (eventTask != null && eventTask.Active)
                eventTask.Active = false;
            
            Register(new () {
                id = (int)Utils.priorities.propogation,
                task = () => RegisterFence(new () {
                    OnReached = OnStartupTasksFinished,
                    IsBlocking = false
                })
            });

            OnStartupTasksFinished += OnComplete;
            eventTask = new IndirectUpdate(Update);
            MainLoopUpdateTasks.Enqueue(eventTask);
            LoadingScreen.SetActive(true);
            finishedLoad = 0;
            progress = 0;
        }

        private static void OnComplete() {
            LoadingScreen.SetActive(false);
            eventTask.Active = false;
        }

        public static void Update(MonoBehaviour mono) {
            float totalRemainingLoad = 0;
            int firstTaskId = -1;
            foreach (RuntimeTaskEl el in EventTaskQueue) {
                //Assume first fence is 
                if (el.type != RuntimeTaskEl.ElType.Task)
                    break;

                totalRemainingLoad += taskLoadTable[el.task.id];
                if (firstTaskId < 0) firstTaskId = el.task.id;
            }

            if (firstTaskId >= 0) taskText.text = taskDescriptions[firstTaskId];
            progress = finishedLoad / (totalRemainingLoad + finishedLoad);
            AudioManager.Instance.UpdateAmbience(progress);

            slider.value = progress;
            finishedLoad += Config.CURRENT.Quality.Terrain.value.maxFrameLoad;
        }
    }
}
