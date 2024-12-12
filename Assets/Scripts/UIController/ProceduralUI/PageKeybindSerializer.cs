using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InputPoller;
using static PaginatedUIEditor;


//List must be held by an Option which only holds it
//Therefore it already has its own page and doesn't need to create another
public class PageKeybindSerializer : IConverter{
    private readonly Dictionary<KeyBind.BindPoll, Color> ConditionColors = new (){
        {KeyBind.BindPoll.Axis, Color.blue},
        {KeyBind.BindPoll.Down, Color.white},
        {KeyBind.BindPoll.Hold, Color.green},
        {KeyBind.BindPoll.Up, Color.red},
        {KeyBind.BindPoll.Exclude, Color.black}
    };

    private static readonly Dictionary<KeyCode, string> AxisNames = new Dictionary<KeyCode, string>{
        {KeyCode.Alpha0, "Mouse X"},
        {KeyCode.Alpha1, "Mouse Y"},
        {KeyCode.Alpha2, "Scroll"},
        {KeyCode.Alpha3, "Horizontal"},
        {KeyCode.Alpha4, "Vertical"},
    };

    public bool CanConvert(Type type){ return typeof(Registry<KeyBind>.Pair).IsAssignableFrom(type); }
    public void Serialize(GameObject page, GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate){
        GameObject KeybindName = parent.transform.Find("Name").gameObject;
        Button RebindButton = KeybindName.AddComponent<Button>();
        RebindButton.onClick.AddListener(async () => {
            ClearKeybinds(parent.transform);
            Option<List<KeyBind.Binding>> nBindings = await ListenKeypress();
            nBindings.isDirty = true;
            OnUpdate((ref object option) => {
                Registry<KeyBind>.Pair nKeyBind = (Registry<KeyBind>.Pair)field.GetValue(option);
                nKeyBind._value = new KeyBind{ bindings = nBindings };
                nKeyBind._value.isDirty = true;

                field.SetValue(option, nKeyBind);
                ReflectKeybind(parent, nKeyBind);
                ForceLayoutRefresh(parent.transform);
            });
        });
        
        ReflectKeybind(parent, (Registry<KeyBind>.Pair)value);
        ForceLayoutRefresh(parent.transform);
    }

    private void ClearKeybinds(Transform content){
        foreach(Transform child in content.transform){  
            if(child.name.Contains("Keybind")) 
                GameObject.Destroy(child.gameObject); 
        }
    }

    private void UpdateConfigKeybind(ref object option){}
    
    private void ReflectKeybind(GameObject parent, Registry<KeyBind>.Pair keybind){
        ClearKeybinds(parent.transform);
        List<KeyBind.Binding> bindings = keybind.Value.Bindings;
        for(int i = 0; i < bindings.Count; i++){
            KeyBind.Binding binding = bindings[i];
            GameObject bindConjunction = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Keybind_Conjunction"), parent.transform);
            GameObject bindKey = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Keybind_Key"), parent.transform);
            TextMeshProUGUI conjText = bindConjunction.GetComponentInChildren<TextMeshProUGUI>();
            TextMeshProUGUI keyText = bindKey.GetComponentInChildren<TextMeshProUGUI>();

            conjText.text = binding.IsAlias ? "/" : "+";
            keyText.color = ConditionColors[binding.PollType];
            string keyName = binding.Key.ToString();
            if(binding.PollType == KeyBind.BindPoll.Axis && AxisNames.ContainsKey(binding.Key)) 
                keyName = AxisNames[binding.Key];
            keyText.text = keyName;

            Button pollTypeButton = bindKey.AddComponent<Button>();
            pollTypeButton.onClick.AddListener(() => {
                binding.PollType = (KeyBind.BindPoll)(((int)binding.PollType + 1) % Enum.GetValues(typeof(KeyBind.BindPoll)).Length);
                keyText.color = ConditionColors[binding.PollType];
            });

            Button conjButton = bindConjunction.AddComponent<Button>();
            conjButton.onClick.AddListener(() => {
                binding.IsAlias = !binding.IsAlias;
                conjText.text = binding.IsAlias ? "/" : "+";
            });

            if(i == 0) bindConjunction.SetActive(false);
        }
    }

    //Wait until key is pressed, then keep waiting until no keys are pressed
    private async Task<List<KeyBind.Binding>> ListenKeypress(){
        HashSet<KeyBind.Binding> pressedKeys = new();
        bool isListening = true;
        while(isListening){
            isListening = false;
            foreach(KeyCode key in Enum.GetValues(typeof(KeyCode))){
                if(Input.GetKey(key)){
                    pressedKeys.Add(new KeyBind.Binding{Key = key, PollType = KeyBind.BindPoll.Hold});
                    isListening = true;
                }
            }

            //Handle axises. If both axis keys are pressed at the same time, count axis
            if(Input.GetAxis("Mouse X") > 0.25){
                pressedKeys.Add(new KeyBind.Binding{Key = KeyCode.Alpha0, PollType = KeyBind.BindPoll.Axis});
                isListening = true;
            } if(Input.GetAxis("Mouse Y") > 0.25){
                pressedKeys.Add(new KeyBind.Binding{Key = KeyCode.Alpha1, PollType = KeyBind.BindPoll.Axis});
                isListening = true;
            } if((Input.GetKey(KeyCode.A) && Input.GetKey(KeyCode.D)) || (Input.GetKey(KeyCode.LeftArrow) && Input.GetKey(KeyCode.RightArrow))){
                pressedKeys.Add(new KeyBind.Binding{Key = KeyCode.Alpha3, PollType = KeyBind.BindPoll.Axis});
                isListening = true;
            } if((Input.GetKey(KeyCode.W) && Input.GetKey(KeyCode.S)) || (Input.GetKey(KeyCode.UpArrow) && Input.GetKey(KeyCode.DownArrow))){
                pressedKeys.Add(new KeyBind.Binding{Key = KeyCode.Alpha4, PollType = KeyBind.BindPoll.Axis});
                isListening = true;
            }
            if(pressedKeys.Count == 0) isListening = true;

            await Task.Yield();
        }
        return new List<KeyBind.Binding>(pressedKeys);
        
    }
}
