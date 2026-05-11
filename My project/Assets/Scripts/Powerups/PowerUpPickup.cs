using UnityEngine;

public enum PowerUpType
{
    Invincible,
    Speed,
    ExtraLife
}

public class PowerUpPickup : MonoBehaviour
{
    public PowerUpType powerUpType;
    public float duration = 5f;
    public SpriteRenderer spriteRenderer;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        if (powerUpType == PowerUpType.Invincible) spriteRenderer.color = Color.yellow;
        else if (powerUpType == PowerUpType.Speed) spriteRenderer.color = Color.blue;
        else spriteRenderer.color = Color.green;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        RobotCombat robot = other.GetComponent<RobotCombat>();
        if (robot == null || !robot.IsAlive) return;

        robot.ApplyPowerUp(powerUpType, duration);
        Destroy(gameObject);
    }
}
