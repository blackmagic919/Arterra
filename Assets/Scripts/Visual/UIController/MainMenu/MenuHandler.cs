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
        private async void OnEnable() {
            sTransform = this.gameObject.GetComponent<RectTransform>();
            sAnimator = this.gameObject.GetComponent<Animator>();
            active = true; sAnimator.SetTrigger("Unmask");
            await EnsureWorldReadyAsync();
        }

        private void OnDisable() { active = false; }

        // Disable menu input during loading. After an error, the next action retries initialization.
        private Task<bool> readiness;
        private Task<bool> EnsureWorldReadyAsync() {
            if (readiness == null || readiness.IsCompleted)
                readiness = WaitForWorldAsync();
            return readiness;
            
            async Task<bool> WaitForWorldAsync() {
                var controls = GetComponentsInChildren<UnityEngine.UI.Selectable>(true);
                var interactable = new bool[controls.Length];
                for (int i = 0; i < controls.Length; i++) {
                    interactable[i] = controls[i].interactable;
                    controls[i].interactable = false;
                }
                try {
                    await Core.Storage.World.EnsureInitializedAsync();
                    return this && isActiveAndEnabled;
                } catch (Exception exception) {
                    Debug.LogException(exception);
                    return false;
                } finally {
                    for (int i = 0; i < controls.Length; i++)
                        if (controls[i]) controls[i].interactable = interactable[i];
                }
            }
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
        public async void Play() {
            if (!active) return;
            if (!await EnsureWorldReadyAsync() || !active) return;
            _ = Core.Storage.World.SaveOptions();
            SceneManager.LoadScene("GameScene");
        }
        public async void Select() {
            if (!active) return;
            if (!await EnsureWorldReadyAsync() || !active) return;
            OptionsHandler.Deactivate();
            Deactivate(() => SelectionHandler.Activate());
        }

        public async void Options() {
            if (!active) return;
            if (!await EnsureWorldReadyAsync() || !active) return;
            OptionsHandler.TogglePanel();
        }

    }
}
