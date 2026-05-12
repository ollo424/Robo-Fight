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
    public GameObject groundPrefab;
    public Transform wallParent;
    public Transform groundParent;

    private bool[,] walkable;
    private readonly List<GameObject> spawnedWalls = new List<GameObject>();
    private readonly List<GameObject> spawnedOuterWalls = new List<GameObject>();
    private readonly List<GameObject> spawnedGrounds = new List<GameObject>();
    private readonly List<Vector2Int> walkableCells = new List<Vector2Int>();
    private readonly List<Vector3> spawnPoints = new List<Vector3>();

    public void GenerateMaze()
    {
        NormalizeSizes();
        ClearOldMaze();

        walkable = new bool[gridSizeX, gridSizeY];
        walkableCells.Clear();
        spawnPoints.Clear();

        GenerateRandomMazeLayout();

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
        if (groundParent == null)
        {
            GameObject parent = new GameObject("GeneratedGround");
            groundParent = parent.transform;
            groundParent.SetParent(transform, false);
        }

        for (int y = 0; y < gridSizeY; y++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {
                if (walkable[x, y])
                {
                    SpawnGround(x, y);
                }
                else
                {
                    SpawnWall(x, y);
                }
            }
        }
    }

    private void GenerateRandomMazeLayout()
    {
        bool[,] visitedNodes = new bool[gridSizeX, gridSizeY];
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        Vector2Int start = new Vector2Int(1, 1);

        SetWalkable(start.x, start.y);
        visitedNodes[start.x, start.y] = true;
        stack.Push(start);

        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        while (stack.Count > 0)
        {
            Vector2Int current = stack.Peek();
            List<Vector2Int> unvisited = new List<Vector2Int>();

            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int node = current + directions[i] * 2;
                if (!IsInnerNode(node)) continue;
                if (visitedNodes[node.x, node.y]) continue;
                unvisited.Add(node);
            }

            if (unvisited.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int next = unvisited[Random.Range(0, unvisited.Count)];
            Vector2Int between = (current + next) / 2;

            SetWalkable(between.x, between.y);
            SetWalkable(next.x, next.y);
            visitedNodes[next.x, next.y] = true;
            stack.Push(next);
        }

        RemoveDeadEnds();
        CreateOpenPockets();
        RemoveDeadEnds();
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

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - transform.position;
        int x = Mathf.RoundToInt(local.x / cellSize);
        int y = Mathf.RoundToInt(local.y / cellSize);
        return new Vector2Int(x, y);
    }

    public void FillOutsideCameraWithWalls(Camera cam)
    {
        if (cam == null || wallPrefab == null) return;

        for (int i = 0; i < spawnedOuterWalls.Count; i++)
        {
            if (spawnedOuterWalls[i] != null) Destroy(spawnedOuterWalls[i]);
        }
        spawnedOuterWalls.Clear();

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;
        Vector3 camPos = cam.transform.position;

        int minX = Mathf.FloorToInt((camPos.x - halfWidth - transform.position.x) / cellSize) - 1;
        int maxX = Mathf.CeilToInt((camPos.x + halfWidth - transform.position.x) / cellSize) + 1;
        int minY = Mathf.FloorToInt((camPos.y - halfHeight - transform.position.y) / cellSize) - 1;
        int maxY = Mathf.CeilToInt((camPos.y + halfHeight - transform.position.y) / cellSize) + 1;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (x >= 0 && y >= 0 && x < gridSizeX && y < gridSizeY) continue;
                Vector3 position = CellToWorld(new Vector2Int(x, y));
                GameObject wall = Instantiate(wallPrefab, position, Quaternion.identity, wallParent);
                wall.name = "OuterWall_" + x + "_" + y;
                spawnedOuterWalls.Add(wall);
            }
        }
    }

    private void NormalizeSizes()
    {
        gridSizeX = Mathf.Max(7, gridSizeX);
        gridSizeY = Mathf.Max(7, gridSizeY);
        if (gridSizeX % 2 == 0) gridSizeX += 1;
        if (gridSizeY % 2 == 0) gridSizeY += 1;
    }

    private bool IsInnerNode(Vector2Int cell)
    {
        return cell.x > 0 && cell.y > 0 && cell.x < gridSizeX - 1 && cell.y < gridSizeY - 1;
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
        spawnedWalls.Add(wall);
    }

    private void SpawnGround(int x, int y)
    {
        if (groundPrefab == null) return;

        Vector3 position = CellToWorld(new Vector2Int(x, y));
        GameObject ground = Instantiate(groundPrefab, position, Quaternion.identity, groundParent);
        ground.name = "Ground_" + x + "_" + y;
        spawnedGrounds.Add(ground);
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

        for (int i = 0; i < spawnedOuterWalls.Count; i++)
        {
            if (spawnedOuterWalls[i] != null)
            {
                Destroy(spawnedOuterWalls[i]);
            }
        }
        spawnedOuterWalls.Clear();

        for (int i = 0; i < spawnedGrounds.Count; i++)
        {
            if (spawnedGrounds[i] != null)
            {
                Destroy(spawnedGrounds[i]);
            }
        }
        spawnedGrounds.Clear();
    }

    private void RemoveDeadEnds()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        bool changed = true;
        int safety = 0;

        while (changed && safety < 1000)
        {
            safety++;
            changed = false;

            for (int y = 1; y < gridSizeY - 1; y++)
            {
                for (int x = 1; x < gridSizeX - 1; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!walkable[x, y]) continue;

                    int openCount = CountOpenNeighbors(cell);
                    if (openCount > 1) continue;

                    List<Vector2Int> closedNeighbors = new List<Vector2Int>();
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        Vector2Int neighbor = cell + dirs[i];
                        if (!IsInside(neighbor)) continue;
                        if (neighbor.x == 0 || neighbor.y == 0 || neighbor.x == gridSizeX - 1 || neighbor.y == gridSizeY - 1) continue;
                        if (!walkable[neighbor.x, neighbor.y]) closedNeighbors.Add(neighbor);
                    }

                    if (closedNeighbors.Count == 0) continue;
                    Vector2Int carve = closedNeighbors[Random.Range(0, closedNeighbors.Count)];
                    SetWalkable(carve.x, carve.y);
                    changed = true;
                }
            }
        }
    }

    private int CountOpenNeighbors(Vector2Int cell)
    {
        int count = 0;
        if (IsWalkableCell(cell + Vector2Int.up)) count++;
        if (IsWalkableCell(cell + Vector2Int.down)) count++;
        if (IsWalkableCell(cell + Vector2Int.left)) count++;
        if (IsWalkableCell(cell + Vector2Int.right)) count++;
        return count;
    }

    private void CreateOpenPockets()
    {
        int pocketCount = Random.Range(2, 6);
        for (int i = 0; i < pocketCount; i++)
        {
            Vector2Int center = new Vector2Int(
                Random.Range(2, gridSizeX - 2),
                Random.Range(2, gridSizeY - 2)
            );
            int radius = Random.Range(1, 4);

            for (int y = center.y - radius; y <= center.y + radius; y++)
            {
                for (int x = center.x - radius; x <= center.x + radius; x++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    if (!IsInside(c)) continue;
                    if (c.x == 0 || c.y == 0 || c.x == gridSizeX - 1 || c.y == gridSizeY - 1) continue;

                    float dist = Vector2Int.Distance(c, center);
                    float chance = Mathf.Lerp(0.85f, 0.25f, dist / Mathf.Max(1f, radius));
                    if (Random.value <= chance) SetWalkable(c.x, c.y);
                }
            }
        }
    }
}
