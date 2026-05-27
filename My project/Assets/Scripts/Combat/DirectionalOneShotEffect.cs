using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class DirectionalOneShotEffect : MonoBehaviour
{
    [Header("Frame Lists")]
    [Tooltip("Bu liste doluysa tum yonlerde bunu kullanir.")]
    public List<Sprite> allDirectionFrames = new List<Sprite>();

    public List<Sprite> lowerDirectionFrames = new List<Sprite>();
    public List<Sprite> upperDirectionFrames = new List<Sprite>();

    [Header("Playback")]
    [Min(1f)] public float framesPerSecond = 12f;
    public bool destroyOnFinish = true;

    [Header("Flip Rules")]
    public bool lowerBaseFacesRight = true;
    public bool upperBaseFacesRight = true;

    private SpriteRenderer spriteRenderer;
    private List<Sprite> activeFrames;
    private float timer;
    private int frameIndex;
    private bool started;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Play(Vector2 direction, SpriteRenderer ownerRenderer = null)
    {
        float rawAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Repeat(Mathf.Round(rawAngle / 45f) * 45f, 360f);
        if (allDirectionFrames != null && allDirectionFrames.Count > 0)
        {
            activeFrames = allDirectionFrames;
        }
        else
        {
        bool movingUp = direction.y >= 0f;
        activeFrames = movingUp ? upperDirectionFrames : lowerDirectionFrames;

        if (activeFrames == null || activeFrames.Count == 0)
        {
            activeFrames = movingUp ? lowerDirectionFrames : upperDirectionFrames;
        }
        }

        bool facingLeft = direction.x < 0f;
        bool movingUpForFlip = direction.y >= 0f;
        bool baseFacesRight = movingUpForFlip ? upperBaseFacesRight : lowerBaseFacesRight;
        spriteRenderer.flipX = baseFacesRight ? facingLeft : !facingLeft;
        #region agent log
        AgentDebugLogger.Log(
            "H29",
            "DirectionalOneShotEffect.cs:Play",
            "Effect orientation evaluated",
            "{\"effect\":\"" + gameObject.name + "\",\"dirX\":" + direction.x.ToString("0.###") + ",\"dirY\":" + direction.y.ToString("0.###") + ",\"rawAngle\":" + rawAngle.ToString("0.###") + ",\"snapped45\":" + snappedAngle.ToString("0.###") + ",\"flipX\":" + (spriteRenderer.flipX ? "true" : "false") + ",\"rotZ\":" + transform.eulerAngles.z.ToString("0.###") + "}"
        );
        #endregion

        if (ownerRenderer != null)
        {
            spriteRenderer.sortingLayerID = ownerRenderer.sortingLayerID;
            spriteRenderer.sortingOrder = ownerRenderer.sortingOrder + 1;
        }

        frameIndex = 0;
        timer = 0f;
        started = true;
        ApplyFrame();
    }

    private void Update()
    {
        if (!started || activeFrames == null || activeFrames.Count == 0) return;

        float frameDuration = 1f / framesPerSecond;
        timer += Time.deltaTime;

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            frameIndex++;
            if (frameIndex >= activeFrames.Count)
            {
                if (destroyOnFinish)
                {
                    Destroy(gameObject);
                }
                else
                {
                    frameIndex = activeFrames.Count - 1;
                }
                return;
            }
        }

        ApplyFrame();
    }

    private void ApplyFrame()
    {
        if (activeFrames == null || activeFrames.Count == 0) return;
        frameIndex = Mathf.Clamp(frameIndex, 0, activeFrames.Count - 1);
        spriteRenderer.sprite = activeFrames[frameIndex];
    }
}
