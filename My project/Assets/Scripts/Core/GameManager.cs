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
    public class RobotSpawnEntry
    {
        public string displayName;
        public GameObject prefab;
    }

    [Header("References")]
    public MazeGenerator mazeGenerator;
    public List<RobotSpawnEntry> robotEntries = new List<RobotSpawnEntry>();
    public TMP_Text winText;
    public GameObject winBackground;
    public TMP_Text winListText;
    public TMP_InputField speedInputField;

    [Header("Loop")]
    public float restartDelay = 3f;

    [Header("Simulation Speed")]
    public float minSimulationSpeed = 0.5f;
    public float maxSimulationSpeed = 10f;

    private readonly List<RobotCombat> aliveRobots = new List<RobotCombat>();
    private bool gameEnded;

    private void Awake()
    {
        Instance = this;
        if (winText != null) winText.gameObject.SetActive(false);
        if (winBackground != null) winBackground.SetActive(false);
        EnsureAllRobotNamesInWinCounts();
        ApplySimulationSpeed(savedSimulationSpeed);
        SetupSpeedInput();
        UpdateWinListText();
    }

    private IEnumerator Start()
    {
        if (mazeGenerator != null)
        {
            mazeGenerator.GenerateMaze();
            FitCameraToGrid();
            mazeGenerator.FillOutsideCameraWithWalls(Camera.main);
        }

        yield return null;
        SpawnAllRobots();
    }

    public void RegisterRobot(RobotCombat robot)
    {
        if (robot == null || aliveRobots.Contains(robot)) return;
        aliveRobots.Add(robot);
    }

    public void NotifyRobotDestroyed(RobotCombat robot)
    {
        if (robot != null) aliveRobots.Remove(robot);
        if (!gameEnded) CheckWinCondition();
    }

    private void SpawnAllRobots()
    {
        if (mazeGenerator == null) return;

        List<Vector3> spawnPoints = mazeGenerator.GetSpawnPoints();
        List<RobotSpawnEntry> shuffled = new List<RobotSpawnEntry>(robotEntries);
        ShuffleEntries(shuffled);
        int count = Mathf.Min(shuffled.Count, spawnPoints.Count);

        for (int i = 0; i < count; i++)
        {
            RobotSpawnEntry entry = shuffled[i];
            if (entry == null || entry.prefab == null) continue;

            GameObject robotObj = Instantiate(entry.prefab, spawnPoints[i], Quaternion.identity);
            robotObj.name = string.IsNullOrWhiteSpace(entry.displayName) ? "Robot_" + i : entry.displayName;

            RobotCombat combat = robotObj.GetComponent<RobotCombat>();
            if (combat != null)
            {
                combat.displayName = robotObj.name;
            }
        }
    }

    private void ShuffleEntries(List<RobotSpawnEntry> entries)
    {
        for (int i = entries.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            RobotSpawnEntry temp = entries[i];
            entries[i] = entries[j];
            entries[j] = temp;
        }
    }

    private void CheckWinCondition()
    {
        aliveRobots.RemoveAll(r => r == null);
        if (aliveRobots.Count != 1) return;

        gameEnded = true;
        RobotCombat winner = aliveRobots[0];
        RegisterWin(winner.displayName);
        if (winText != null)
        {
            winText.gameObject.SetActive(true);
            winText.text = winner.displayName + " KAZANDI";
            winText.color = winner.baseColor;
        }
        if (winBackground != null) winBackground.SetActive(true);

        StartCoroutine(RestartSceneRoutine());
    }

    private IEnumerator RestartSceneRoutine()
    {
        yield return new WaitForSeconds(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void FitCameraToGrid()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic || mazeGenerator == null) return;

        float width = mazeGenerator.gridSizeX * mazeGenerator.cellSize;
        float height = mazeGenerator.gridSizeY * mazeGenerator.cellSize;
        Vector3 center = mazeGenerator.transform.position + new Vector3((width - mazeGenerator.cellSize) * 0.5f, (height - mazeGenerator.cellSize) * 0.5f, 0f);

        cam.transform.position = new Vector3(center.x, center.y, cam.transform.position.z);
        cam.orthographicSize = 8f;
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
    }

    private void RegisterWin(string winnerName)
    {
        if (string.IsNullOrWhiteSpace(winnerName)) return;
        if (!winCounts.ContainsKey(winnerName)) winCounts[winnerName] = 0;
        winCounts[winnerName]++;
        UpdateWinListText();
    }

    private void UpdateWinListText()
    {
        if (winListText == null) return;
        EnsureAllRobotNamesInWinCounts();
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

    private void EnsureAllRobotNamesInWinCounts()
    {
        for (int i = 0; i < robotEntries.Count; i++)
        {
            RobotSpawnEntry entry = robotEntries[i];
            if (entry == null) continue;

            string name = entry.displayName;
            if (string.IsNullOrWhiteSpace(name) && entry.prefab != null)
            {
                name = entry.prefab.name;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Robot_" + i;
            }

            if (!winCounts.ContainsKey(name))
            {
                winCounts[name] = 0;
            }
        }
    }
}
