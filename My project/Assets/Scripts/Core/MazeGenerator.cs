using System.Collections.Generic;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int gridSizeX = 15;
    public int gridSizeY = 15;
    public float cellSize = 1f;

    [Header("Visual")]
    public GameObject wallPrefab;
    public Transform wallParent;
    public Color wallColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    private bool[,] walkable;
    private readonly List<GameObject> spawnedWalls = new List<GameObject>();
    private readonly List<Vector2Int> walkableCells = new List<Vector2Int>();
    private readonly List<Vector3> spawnPoints = new List<Vector3>();

    public void GenerateMaze()
    {
        NormalizeSizes();
        ClearOldMaze();

        walkable = new bool[gridSizeX, gridSizeY];
        walkableCells.Clear();
        spawnPoints.Clear();

        // Odd-odd cells are main corridors. Connect all with right/up links.
        for (int y = 1; y < gridSizeY - 1; y += 2)
        {
            for (int x = 1; x < gridSizeX - 1; x += 2)
            {
                SetWalkable(x, y);
                if (x + 1 < gridSizeX - 1) SetWalkable(x + 1, y);
                if (y + 1 < gridSizeY - 1) SetWalkable(x, y + 1);
            }
        }

        Vector2Int[] spawnCells =
        {
            new Vector2Int(1, 1),
            new Vector2Int(1, gridSizeY - 2),
            new Vector2Int(gridSizeX - 2, 1),
            new Vector2Int(gridSizeX - 2, gridSizeY - 2)
        };

        for (int i = 0; i < spawnCells.Length; i++)
        {
            SetWalkable(spawnCells[i].x, spawnCells[i].y);
            spawnPoints.Add(CellToWorld(spawnCells[i]));
        }

        if (wallParent == null)
        {
            GameObject parent = new GameObject("GeneratedWalls");
            wallParent = parent.transform;
            wallParent.SetParent(transform, false);
        }

        for (int y = 0; y < gridSizeY; y++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {
                if (walkable[x, y]) continue;
                SpawnWall(x, y);
            }
        }
    }

    public List<Vector3> GetSpawnPoints()
    {
        return new List<Vector3>(spawnPoints);
    }

    public bool IsWalkableCell(Vector2Int cell)
    {
        if (!IsInside(cell)) return false;
        return walkable[cell.x, cell.y];
    }

    public Vector3 GetRandomWalkableWorldPosition()
    {
        if (walkableCells.Count == 0) return transform.position;
        int index = Random.Range(0, walkableCells.Count);
        return CellToWorld(walkableCells[index]);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return transform.position + new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
    }

    private void NormalizeSizes()
    {
        gridSizeX = Mathf.Max(7, gridSizeX);
        gridSizeY = Mathf.Max(7, gridSizeY);
        if (gridSizeX % 2 == 0) gridSizeX += 1;
        if (gridSizeY % 2 == 0) gridSizeY += 1;
    }

    private bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < gridSizeX && cell.y < gridSizeY;
    }

    private void SetWalkable(int x, int y)
    {
        if (x < 0 || y < 0 || x >= gridSizeX || y >= gridSizeY) return;
        if (!walkable[x, y])
        {
            walkable[x, y] = true;
            walkableCells.Add(new Vector2Int(x, y));
        }
    }

    private void SpawnWall(int x, int y)
    {
        if (wallPrefab == null) return;

        Vector3 position = CellToWorld(new Vector2Int(x, y));
        GameObject wall = Instantiate(wallPrefab, position, Quaternion.identity, wallParent);
        wall.name = "Wall_" + x + "_" + y;

        SpriteRenderer renderer = wall.GetComponent<SpriteRenderer>();
        if (renderer != null) renderer.color = wallColor;
        spawnedWalls.Add(wall);
    }

    private void ClearOldMaze()
    {
        for (int i = 0; i < spawnedWalls.Count; i++)
        {
            if (spawnedWalls[i] != null)
            {
                Destroy(spawnedWalls[i]);
            }
        }
        spawnedWalls.Clear();
    }
}
