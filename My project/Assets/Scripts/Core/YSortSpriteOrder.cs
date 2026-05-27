using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class YSortSpriteOrder : MonoBehaviour
{
    public int baseOrder = 1000;
    public int orderOffset = 0;
    public float unitsPerOrder = 100f;
    public bool updateEveryFrame = true;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        ApplySort();
    }

    private void LateUpdate()
    {
        if (updateEveryFrame)
        {
            ApplySort();
        }
    }

    public void ApplySort()
    {
        if (spriteRenderer == null) return;
        int dynamicOrder = baseOrder - Mathf.RoundToInt(transform.position.y * unitsPerOrder) + orderOffset;
        spriteRenderer.sortingOrder = dynamicOrder;
    }
}
