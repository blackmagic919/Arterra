using UnityEngine;
using UnityEngine.UI;

public class AlignedScrollRect : ScrollRect
{
    // 0 = bottom, 0.5 = center, 1 = top
    [SerializeField, Range(0f, 1f)]
    private float verticalAlignment = 0f;

    protected override void LateUpdate()
    {
        base.LateUpdate();

        if (!content || !viewport) return;

        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;

        if (contentHeight >= viewportHeight)
            return;

        float freeSpace = viewportHeight - contentHeight;

        // Convert alignment to anchoredPosition offset
        // anchoredPosition.y is negative downward
        float offset = -freeSpace * verticalAlignment;

        var pos = content.anchoredPosition;
        pos.y = offset;
        content.anchoredPosition = pos;
    }
}