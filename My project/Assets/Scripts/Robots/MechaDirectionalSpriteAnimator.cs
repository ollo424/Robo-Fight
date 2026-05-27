using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class MechaDirectionalSpriteAnimator : MonoBehaviour
{
    [Header("Frame Lists")]
    [Tooltip("Bu liste doluysa tum yonlerde bunu kullanir.")]
    public List<Sprite> allDirectionFrames = new List<Sprite>();

    [Tooltip("Asagi yone giderken (sag asagi temel) kullanilacak frame listesi.")]
    public List<Sprite> lowerDirectionFrames = new List<Sprite>();

    [Tooltip("Yukari yone giderken (sag ust veya sol ust temel) kullanilacak frame listesi.")]
    public List<Sprite> upperDirectionFrames = new List<Sprite>();

    [Header("Playback")]
    [Min(1f)] public float framesPerSecond = 10f;
    [Min(0f)] public float movementThreshold = 0.05f;
    public bool loopWhenStopped;

    [Header("Flip Rules")]
    [Tooltip("Alt liste temel yonu saga bakiyorsa true birak. Sola bakiyorsa false yap.")]
    public bool lowerBaseFacesRight = true;

    [Tooltip("Ust liste temel yonu saga bakiyorsa true birak. Sola bakiyorsa false yap.")]
    public bool upperBaseFacesRight = true;

    [Header("Compatibility")]
    [Tooltip("SteeringMovementController scale flip'ini otomatik kapatir.")]
    public bool disableMovementScaleFlip = true;

    private SpriteRenderer spriteRenderer;
    private SteeringMovementController movement;
    private MechaCombatController combat;
    private Rigidbody2D rb;
    private float frameTimer;
    private int currentFrame;
    private List<Sprite> activeFrames;
    private bool isFacingLeft;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        movement = GetComponent<SteeringMovementController>();
        combat = GetComponent<MechaCombatController>();
        rb = GetComponent<Rigidbody2D>();

        if (disableMovementScaleFlip && movement != null)
        {
            movement.flipByDirection = false;
        }

        activeFrames = ResolveFallbackFrames(allDirectionFrames);
        ApplyCurrentSprite();
    }

    private void Update()
    {
        Vector2 velocity = GetVelocity();
        bool isMoving = velocity.sqrMagnitude > movementThreshold * movementThreshold;

        if (isMoving)
        {
            bool movingUp = velocity.y >= 0f;
            isFacingLeft = velocity.x < 0f;
            SetDirection(movingUp);
            Animate(Time.deltaTime);
            return;
        }

        if (loopWhenStopped)
        {
            ApplyFacingFromCombat();
            Animate(Time.deltaTime);
        }
        else
        {
            ApplyFacingFromCombat();
            frameTimer = 0f;
            currentFrame = 0;
            ApplyCurrentSprite();
        }
    }

    private Vector2 GetVelocity()
    {
        if (movement != null)
        {
            return movement.Velocity;
        }

        if (rb != null)
        {
            return rb.linearVelocity;
        }

        return Vector2.zero;
    }

    private void SetDirection(bool movingUp)
    {
        if (allDirectionFrames != null && allDirectionFrames.Count > 0)
        {
            if (!ReferenceEquals(activeFrames, allDirectionFrames))
            {
                activeFrames = allDirectionFrames;
                currentFrame = 0;
                frameTimer = 0f;
            }

            bool shouldFlipXForSingleSet = lowerBaseFacesRight ? isFacingLeft : !isFacingLeft;
            spriteRenderer.flipX = shouldFlipXForSingleSet;
            return;
        }

        List<Sprite> targetFrames = movingUp ? upperDirectionFrames : lowerDirectionFrames;
        targetFrames = ResolveFallbackFrames(targetFrames);

        if (!ReferenceEquals(activeFrames, targetFrames))
        {
            activeFrames = targetFrames;
            currentFrame = 0;
            frameTimer = 0f;
        }

        bool baseFacesRight = movingUp ? upperBaseFacesRight : lowerBaseFacesRight;
        bool shouldFlipX = baseFacesRight ? isFacingLeft : !isFacingLeft;
        spriteRenderer.flipX = shouldFlipX;
    }

    private void Animate(float deltaTime)
    {
        if (activeFrames == null || activeFrames.Count == 0 || framesPerSecond <= 0f)
        {
            return;
        }

        frameTimer += deltaTime;
        float frameDuration = 1f / framesPerSecond;

        while (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            currentFrame = (currentFrame + 1) % activeFrames.Count;
        }

        ApplyCurrentSprite();
    }

    private void ApplyCurrentSprite()
    {
        if (activeFrames == null || activeFrames.Count == 0)
        {
            return;
        }

        currentFrame = Mathf.Clamp(currentFrame, 0, activeFrames.Count - 1);
        spriteRenderer.sprite = activeFrames[currentFrame];
    }

    private List<Sprite> ResolveFallbackFrames(List<Sprite> preferred)
    {
        if (preferred != null && preferred.Count > 0)
        {
            return preferred;
        }

        if (lowerDirectionFrames != null && lowerDirectionFrames.Count > 0)
        {
            return lowerDirectionFrames;
        }

        if (upperDirectionFrames != null && upperDirectionFrames.Count > 0)
        {
            return upperDirectionFrames;
        }

        return preferred;
    }

    private void ApplyFacingFromCombat()
    {
        if (combat == null) return;

        Vector2 facing = combat.FacingDirection;
        if (facing.sqrMagnitude < 0.0001f) return;

        bool movingUp = facing.y >= 0f;
        isFacingLeft = facing.x < 0f;
        SetDirection(movingUp);
    }
}
