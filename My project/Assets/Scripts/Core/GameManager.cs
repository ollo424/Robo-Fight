using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    private static readonly Dictionary<string, int> winCounts = new Dictionary<string, int>();
    private static float savedSimulationSpeed = 1f;

    [System.Serializable]
    public class MechaSpawnEntry
    {
        public string displayName;
        public GameObject prefab;
    }

    [Header("Arena")]
    public IsometricArenaController arenaController;
    public List<MechaSpawnEntry> mechaEntries = new List<MechaSpawnEntry>();

    [Header("UI")]
    public TMP_Text winText;
    public GameObject winBackground;
    public TMP_Text winListText;
    public TMP_InputField speedInputField;

    [Header("Loop")]
    public float restartDelay = 3f;

    [Header("Simulation Speed")]
    public float minSimulationSpeed = 0.5f;
    public float maxSimulationSpeed = 10f;

    private readonly List<MechaCombatController> aliveMechas = new List<MechaCombatController>();
    private bool gameEnded;
    private float aliveSyncTimer;

    private void Awake()
    {
        Instance = this;
        if (winText != null) winText.gameObject.SetActive(false);
        if (winBackground != null) winBackground.SetActive(false);
        EnsureAllNamesInWinCounts();
        ApplySimulationSpeed(savedSimulationSpeed);
        SetupSpeedInput();
        UpdateWinListText();
    }

    private IEnumerator Start()
    {
        if (arenaController != null)
        {
            arenaController.BuildArena();
        }

        yield return null;
        CleanupSceneMechasBeforeSpawn();
        SpawnAllMechas();
    }

    private void Update()
    {
        if (gameEnded) return;

        aliveSyncTimer -= Time.deltaTime;
        if (aliveSyncTimer > 0f) return;
        aliveSyncTimer = 0.5f;

        int before = aliveMechas.Count;
        aliveMechas.RemoveAll(m => m == null || !m.IsAlive);
        int after = aliveMechas.Count;
        #region agent log
        AgentDebugLogger.Log(
            "H7",
            "GameManager.cs:Update",
            "Alive list synchronized",
            "{\"before\":" + before + ",\"after\":" + after + ",\"gameEnded\":" + (gameEnded ? "true" : "false") + "}"
        );
        #endregion

        if (after <= 1)
        {
            CheckWinCondition();
        }
    }

    public void RegisterMecha(MechaCombatController mecha)
    {
        if (mecha == null) return;
        if (aliveMechas.Contains(mecha)) return;
        for (int i = 0; i < aliveMechas.Count; i++)
        {
            MechaCombatController existing = aliveMechas[i];
            if (existing == null) continue;
            if (existing.gameObject == mecha.gameObject) return;
        }
        aliveMechas.Add(mecha);
        #region agent log
        AgentDebugLogger.Log(
            "H16",
            "GameManager.cs:RegisterMecha",
            "Mecha registered",
            "{\"name\":\"" + mecha.mechaName + "\",\"aliveCount\":" + aliveMechas.Count + "}"
        );
        #endregion
    }

    public void NotifyMechaDestroyed(MechaCombatController mecha)
    {
        if (mecha != null) aliveMechas.Remove(mecha);
        #region agent log
        AgentDebugLogger.Log(
            "H7",
            "GameManager.cs:NotifyMechaDestroyed",
            "Mecha removed from alive list",
            "{\"removed\":\"" + (mecha != null ? mecha.mechaName : "null") + "\",\"aliveCount\":" + aliveMechas.Count + ",\"gameEnded\":" + (gameEnded ? "true" : "false") + "}"
        );
        #endregion
        if (!gameEnded) CheckWinCondition();
    }

    private void SpawnAllMechas()
    {
        if (arenaController == null) return;

        List<Vector3> spawnPoints = arenaController.GetSpawnPositions(mechaEntries.Count);
        List<MechaSpawnEntry> shuffled = new List<MechaSpawnEntry>(mechaEntries);
        ShuffleEntries(shuffled);
        int count = Mathf.Min(shuffled.Count, spawnPoints.Count);
        #region agent log
        AgentDebugLogger.Log(
            "H1",
            "GameManager.cs:SpawnAllMechas",
            "Spawn phase started",
            "{\"entriesCount\":" + mechaEntries.Count + ",\"spawnPointsCount\":" + spawnPoints.Count + ",\"spawnCount\":" + count + "}"
        );
        #endregion

        for (int i = 0; i < count; i++)
        {
            MechaSpawnEntry entry = shuffled[i];
            if (entry == null || entry.prefab == null) continue;

            GameObject mechaObj = Instantiate(entry.prefab, spawnPoints[i], Quaternion.identity);
            mechaObj.name = string.IsNullOrWhiteSpace(entry.displayName) ? "Mecha_" + i : entry.displayName;
            Collider2D ensuredCollider = EnsureMechaCollider(mechaObj);

            MechaCombatController combat = mechaObj.GetComponent<MechaCombatController>();
            if (combat != null)
            {
                combat.mechaName = mechaObj.name;
                combat.mechaClass = ResolveClassFromName(entry.displayName, combat.mechaClass);
                #region agent log
                AgentDebugLogger.Log(
                    "H9",
                    "GameManager.cs:SpawnAllMechas",
                    "Mecha spawned",
                    "{\"index\":" + i + ",\"name\":\"" + mechaObj.name + "\",\"class\":\"" + combat.mechaClass + "\",\"x\":" + spawnPoints[i].x.ToString("0.###") + ",\"y\":" + spawnPoints[i].y.ToString("0.###") + ",\"hasCollider\":" + (ensuredCollider != null ? "true" : "false") + ",\"colliderIsTrigger\":" + (ensuredCollider != null && ensuredCollider.isTrigger ? "true" : "false") + "}"
                );
                #endregion
            }
        }
    }

    private Collider2D EnsureMechaCollider(GameObject mechaObj)
    {
        if (mechaObj == null) return null;

        Collider2D col = mechaObj.GetComponent<Collider2D>();
        if (col == null)
        {
            CircleCollider2D circle = mechaObj.AddComponent<CircleCollider2D>();
            SpriteRenderer sr = mechaObj.GetComponent<SpriteRenderer>();
            float baseRadius = 0.35f;
            if (sr != null && sr.sprite != null)
            {
                baseRadius = Mathf.Max(0.2f, Mathf.Min(sr.bounds.extents.x, sr.bounds.extents.y) * 0.35f);
            }
            circle.radius = baseRadius;
            col = circle;
            #region agent log
            AgentDebugLogger.Log(
                "H6",
                "GameManager.cs:EnsureMechaCollider",
                "Auto collider added to mecha",
                "{\"name\":\"" + mechaObj.name + "\",\"radius\":" + baseRadius.ToString("0.###") + "}"
            );
            #endregion
        }

        col.isTrigger = false;
        return col;
    }

    private void CleanupSceneMechasBeforeSpawn()
    {
        MechaCombatController[] existing = FindObjectsByType<MechaCombatController>(FindObjectsSortMode.None);
        int removed = 0;
        for (int i = 0; i < existing.Length; i++)
        {
            MechaCombatController c = existing[i];
            if (c == null) continue;
            Destroy(c.gameObject);
            removed++;
        }
        aliveMechas.Clear();
        #region agent log
        AgentDebugLogger.Log(
            "H7",
            "GameManager.cs:CleanupSceneMechasBeforeSpawn",
            "Pre-existing mechas cleared",
            "{\"removed\":" + removed + "}"
        );
        #endregion
    }

    private MechaClass ResolveClassFromName(string displayName, MechaClass fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return fallback;

        string nameLower = displayName.ToLowerInvariant();
        if (nameLower.Contains("gun") || nameLower.Contains("tabanca"))
        {
            return MechaClass.Gunner;
        }
        if (nameLower.Contains("bomb") || nameLower.Contains("roket") || nameLower.Contains("rocket"))
        {
            return MechaClass.Bomber;
        }
        if (nameLower.Contains("knife") || nameLower.Contains("bicak") || nameLower.Contains("kalkan") || nameLower.Contains("shield"))
        {
            return MechaClass.ShieldKnife;
        }
        if (nameLower.Contains("tank"))
        {
            return MechaClass.ShieldKnife;
        }
        if (nameLower.Contains("sword") || nameLower.Contains("kilic"))
        {
            return MechaClass.Sword;
        }

        return fallback;
    }

    private void ShuffleEntries(List<MechaSpawnEntry> entries)
    {
        for (int i = entries.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            MechaSpawnEntry temp = entries[i];
            entries[i] = entries[j];
            entries[j] = temp;
        }
    }

    private void CheckWinCondition()
    {
        aliveMechas.RemoveAll(m => m == null);
        #region agent log
        AgentDebugLogger.Log(
            "H7",
            "GameManager.cs:CheckWinCondition",
            "Checking win state",
            "{\"aliveCount\":" + aliveMechas.Count + ",\"gameEnded\":" + (gameEnded ? "true" : "false") + "}"
        );
        #endregion
        if (aliveMechas.Count != 1) return;

        gameEnded = true;
        MechaCombatController winner = aliveMechas[0];
        RegisterWin(winner.mechaName);

        if (winText != null)
        {
            winText.gameObject.SetActive(true);
            winText.text = winner.mechaName + " KAZANDI";
        }
        if (winBackground != null) winBackground.SetActive(true);
        #region agent log
        AgentDebugLogger.Log(
            "H8",
            "GameManager.cs:CheckWinCondition",
            "Winner decided and restart scheduled",
            "{\"winner\":\"" + winner.mechaName + "\",\"restartDelay\":" + restartDelay.ToString("0.###") + "}"
        );
        #endregion
        StartCoroutine(RestartSceneRoutine());
    }

    private IEnumerator RestartSceneRoutine()
    {
        yield return new WaitForSeconds(restartDelay);
        #region agent log
        AgentDebugLogger.Log(
            "H8",
            "GameManager.cs:RestartSceneRoutine",
            "Restarting scene",
            "{\"sceneIndex\":" + SceneManager.GetActiveScene().buildIndex + "}"
        );
        #endregion
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SetupSpeedInput()
    {
        if (speedInputField == null) return;
        speedInputField.onEndEdit.RemoveListener(OnSpeedInputChanged);
        speedInputField.onEndEdit.AddListener(OnSpeedInputChanged);
        speedInputField.text = savedSimulationSpeed.ToString("0.##");
    }

    private void OnSpeedInputChanged(string textValue)
    {
        if (!float.TryParse(textValue, out float parsed))
        {
            speedInputField.text = savedSimulationSpeed.ToString("0.##");
            return;
        }
        ApplySimulationSpeed(parsed);
        speedInputField.text = savedSimulationSpeed.ToString("0.##");
    }

    private void ApplySimulationSpeed(float value)
    {
        savedSimulationSpeed = Mathf.Clamp(value, minSimulationSpeed, maxSimulationSpeed);
        Time.timeScale = savedSimulationSpeed;
        Time.fixedDeltaTime = 0.02f * savedSimulationSpeed;
        #region agent log
        AgentDebugLogger.Log(
            "H28",
            "GameManager.cs:ApplySimulationSpeed",
            "Simulation speed applied",
            "{\"input\":" + value.ToString("0.###") + ",\"clamped\":" + savedSimulationSpeed.ToString("0.###") + ",\"timeScale\":" + Time.timeScale.ToString("0.###") + ",\"fixedDeltaTime\":" + Time.fixedDeltaTime.ToString("0.#####") + "}"
        );
        #endregion
    }

    private void RegisterWin(string winnerName)
    {
        if (string.IsNullOrWhiteSpace(winnerName)) return;
        if (!winCounts.ContainsKey(winnerName)) winCounts[winnerName] = 0;
        winCounts[winnerName]++;
        #region agent log
        AgentDebugLogger.Log(
            "H7",
            "GameManager.cs:RegisterWin",
            "Win count incremented",
            "{\"winner\":\"" + winnerName + "\",\"wins\":" + winCounts[winnerName] + "}"
        );
        #endregion
        UpdateWinListText();
    }

    private void EnsureAllNamesInWinCounts()
    {
        for (int i = 0; i < mechaEntries.Count; i++)
        {
            MechaSpawnEntry entry = mechaEntries[i];
            if (entry == null) continue;

            string name = entry.displayName;
            if (string.IsNullOrWhiteSpace(name) && entry.prefab != null) name = entry.prefab.name;
            if (string.IsNullOrWhiteSpace(name)) name = "Mecha_" + i;

            if (!winCounts.ContainsKey(name)) winCounts[name] = 0;
        }
    }

    private void UpdateWinListText()
    {
        if (winListText == null) return;
        EnsureAllNamesInWinCounts();
        if (winCounts.Count == 0)
        {
            winListText.text = "Win Count:\n-";
            return;
        }

        List<string> names = new List<string>(winCounts.Keys);
        names.Sort();
        string text = "Win Count:";
        for (int i = 0; i < names.Count; i++)
        {
            text += "\n" + names[i] + ": " + winCounts[names[i]];
        }
        winListText.text = text;
    }
}
