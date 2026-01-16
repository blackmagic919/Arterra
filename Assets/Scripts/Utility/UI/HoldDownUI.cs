using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Graphic))]
public class HoldDownUI : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private bool holding = false;
    private bool hovering = false;

    private Action holdPointer;
    private Action OnDownPointer;
    private Action OnUpPointer;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color highlightedColor = new Color(0.9f, 0.9f, 0.9f);
    public Color pressedColor = new Color(0.8f, 0.8f, 0.8f);

    private Graphic graphic;

    void Awake() {
        graphic = GetComponent<Graphic>();
        SetColor(normalColor);
    }

    public void AddHeldListener(Action action)
    {
        holdPointer += action;
    }

    public void RemoveHeldListener(Action action)
    {
        holdPointer -= action;
    }

    public void AddDownListener(Action action)
    {
        OnDownPointer += action;
    }

    public void RemoveDownListener(Action action)
    {
        OnDownPointer -= action;
    }

    public void AddUpListener(Action action)
    {
        OnUpPointer += action;
    }

    public void RemoveUpListener(Action action)
    {
        OnUpPointer -= action;
    }

    public void ClearAllListeners()
    {
        holdPointer = null;
        OnDownPointer = null;
        OnUpPointer = null;
    }

    void Update()
    {
        if (!holding) return;
        holdPointer?.Invoke();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        holding = true;
        SetColor(pressedColor);
        OnDownPointer?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        holding = false;
        SetColor(hovering ? highlightedColor : normalColor);
        OnUpPointer?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        if (!holding)
            SetColor(highlightedColor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        holding = false; // cancel hold if leaving
        SetColor(normalColor);
    }

    private void SetColor(Color color)
    {
        graphic.color = color;
    }
}
