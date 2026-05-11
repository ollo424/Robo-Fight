using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RobotController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2f;
    public float wallCheckDistance = 0.55f;
    public float decisionCooldown = 0.2f;

    [Header("Awareness")]
    public float awarenessRadius = 2.5f;
    public LayerMask wallMask;
    public LayerMask robotMask;

    private float speedMultiplier = 1f;
    private float decisionTimer;
    private RobotCombat combat;

    private void Awake()
    {
        combat = GetComponent<RobotCombat>();
    }

    private void Update()
    {
        if (combat != null && !combat.IsAlive) return;

        decisionTimer -= Time.deltaTime;
        TryDecision();
        transform.position += transform.up * (moveSpeed * speedMultiplier * Time.deltaTime);
    }

    public void SetSpeedMultiplier(float value)
    {
        speedMultiplier = Mathf.Max(0.1f, value);
    }

    public void SetSpeedMultiplierFor(float value, float duration)
    {
        StartCoroutine(SpeedRoutine(value, duration));
    }

    private IEnumerator SpeedRoutine(float value, float duration)
    {
        float previous = speedMultiplier;
        speedMultiplier = Mathf.Max(0.1f, value);
        yield return new WaitForSeconds(duration);
        speedMultiplier = previous;
    }

    private void TryDecision()
    {
        if (decisionTimer > 0f) return;

        Vector2 pos = transform.position;
        Vector2 forward = transform.up;
        Vector2 left = -transform.right;
        Vector2 right = transform.right;

        bool forwardOpen = IsDirectionOpen(pos, forward);
        bool leftOpen = IsDirectionOpen(pos, left);
        bool rightOpen = IsDirectionOpen(pos, right);

        List<Vector2> options = new List<Vector2>();
        if (forwardOpen) options.Add(forward);
        if (leftOpen) options.Add(left);
        if (rightOpen) options.Add(right);

        if (!forwardOpen)
        {
            TurnTo(leftOpen && rightOpen ? (Random.value > 0.5f ? left : right) : (leftOpen ? left : right));
            return;
        }

        if (options.Count >= 2)
        {
            Vector2 targetDirection;
            if (TryGetEnemyDirection(pos, options, out targetDirection))
            {
                TurnTo(targetDirection);
            }
            else
            {
                TurnTo(options[Random.Range(0, options.Count)]);
            }
        }
    }

    private bool IsDirectionOpen(Vector2 position, Vector2 direction)
    {
        RaycastHit2D hit = Physics2D.Raycast(position, direction, wallCheckDistance, wallMask);
        return hit.collider == null;
    }

    private bool TryGetEnemyDirection(Vector2 position, List<Vector2> options, out Vector2 bestDirection)
    {
        bestDirection = transform.up;
        Collider2D[] targets = Physics2D.OverlapCircleAll(position, awarenessRadius, robotMask);
        if (targets.Length == 0) return false;

        Vector2 toEnemy = Vector2.zero;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null || targets[i].gameObject == gameObject) continue;

            RobotCombat rc = targets[i].GetComponent<RobotCombat>();
            if (rc != null && !rc.IsAlive) continue;

            float distance = Vector2.Distance(position, targets[i].transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                toEnemy = ((Vector2)targets[i].transform.position - position).normalized;
            }
        }

        if (bestDistance == float.MaxValue) return false;

        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < options.Count; i++)
        {
            float score = Vector2.Dot(options[i], toEnemy);
            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = options[i];
            }
        }
        return true;
    }

    private void TurnTo(Vector2 direction)
    {
        if (direction == Vector2.zero) return;
        transform.up = direction.normalized;
        decisionTimer = decisionCooldown;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, awarenessRadius);
    }
}
