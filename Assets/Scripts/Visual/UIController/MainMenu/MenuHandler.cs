using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arterra.GamePlay.UI {
    public class MenuHandler : MonoBehaviour {
        private static RectTransform sTransform;
        private static Animator sAnimator;
        private static bool active = false;
        private void OnEnable() {
            sTransform = this.gameObject.GetComponent<RectTransform>();
            sAnimator = this.gameObject.GetComponent<Animator>();
            active = true; sAnimator.SetTrigger("Unmask");
        }

        public static void Activate(Action callback = null) {
            if (active) return;
            active = true;

            new AnimatorAwaitTask(sAnimator, "MaskedAnimation", () => {
                sAnimator.SetTrigger("Unmask");
                new AnimatorAwaitTask(sAnimator, "UnmaskedAnimation", callback).Invoke();
            }).Invoke();
        }
        public static void Deactivate(Action callback = null) {
            if (!active) return;
            active = false;

            new AnimatorAwaitTask(sAnimator, "UnmaskedAnimation", () => {
                sAnimator.SetTrigger("Mask");
                new AnimatorAwaitTask(sAnimator, "MaskedAnimation", callback).Invoke();
            }).Invoke();
        }


        public void Quit() {
            if (!active) return;
            Application.Quit();
        }
        public void Play() {
            if (!active) return;
            _ = Arterra.Core.Storage.World.SaveOptions();
            SceneManager.LoadScene("GameScene");
        }
        public void Select() {
            if (!active) return;
            OptionsHandler.Deactivate();
            Deactivate(() => SelectionHandler.Activate());
        }

        public void Options() {
            if (!active) return;
            OptionsHandler.TogglePanel();
        }

    }
}
