using System;
using System.Threading.Tasks;
using UnityEngine;

public interface IUpdateSubscriber
{
    public bool Active{ get; set; }
    public void Update(MonoBehaviour mono = null);
}

public class IndirectUpdate : IUpdateSubscriber {
    private bool active = false;
    public bool Active {
        get => active;
        set => active = value;
    }
    Action<MonoBehaviour> callback;
    public IndirectUpdate(Action<MonoBehaviour> callback) {
        this.active = true;
        this.callback = callback;
    }
    public void Update(MonoBehaviour mono = null) {
        callback.Invoke(mono);
    }
}

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
