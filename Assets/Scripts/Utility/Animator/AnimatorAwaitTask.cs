using System;
using System.Threading.Tasks;
using UnityEngine;

public class AnimatorAwaitTask{
    public bool active = false; 
    private Animator sAnimator;
    private Action callback;
    private string testState;
    public AnimatorAwaitTask(
        Animator sAnimator,
        string state,
        Action callback = null
    ) {
        this.active = true;
        this.sAnimator = sAnimator;
        this.testState = state;
        this.callback = callback;
    }

    public void Disable() => active = false;
    public async void Invoke(){
        while(true) {
            if (!active) return;
            if(sAnimator == null) return;
            if(sAnimator.GetCurrentAnimatorStateInfo(0).IsName(testState)) break;
            await Task.Yield();
        }
        callback?.Invoke();
    }
}