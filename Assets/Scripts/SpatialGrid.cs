using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based spatial partitioning system for efficient neighbor queries.
/// Divides the world into cells and tracks which Characters (NPCs and Players) are in each cell.
/// </summary>
public class SpatialGrid : MonoBehaviour
{
    public static SpatialGrid Instance;

    [SerializeField] private float cellSize = 5f;
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private bool drawCellBoundaries = true;
    [SerializeField] private bool showCellPopulation = true;
    [SerializeField] private Color emptyCellColor = new Color(0, 1, 0, 0.1f);
    [SerializeField] private Color lowPopulationColor = new Color(1, 1, 0, 0.2f);
    [SerializeField] private Color mediumPopulationColor = new Color(1, 0.5f, 0, 0.3f);
    [SerializeField] private Color highPopulationColor = new Color(1, 0, 0, 0.4f);
    [SerializeField] private int lowPopThreshold = 3;
    [SerializeField] private int mediumPopThreshold = 7;

    private Dictionary<Vector2Int, List<BaseCharController>> grid = new Dictionary<Vector2Int, List<BaseCharController>>();
    private HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    
    // Stats tracking
    private int totalCharacters = 0;
    public int TotalCharacters => totalCharacters;
    public int OccupiedCellCount => occupiedCells.Count;
    
    /// <summary>
    /// Gets the size of each cell in the spatial grid.
    /// </summary>
    public float CellSize => cellSize;

    [Header("Debug UI")]
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private Vector2 debugUIPosition = new Vector2(10, 10);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnGUI()
    {
        if (!showDebugUI || !Application.isPlaying) return;

        GUI.Box(new Rect(debugUIPosition.x, debugUIPosition.y, 250, 100), "Spatial Grid Debug");
        
        GUI.Label(new Rect(debugUIPosition.x + 10, debugUIPosition.y + 25, 230, 20), 
            $"Total Characters: {totalCharacters}");
        GUI.Label(new Rect(debugUIPosition.x + 10, debugUIPosition.y + 45, 230, 20), 
            $"Occupied Cells: {occupiedCells.Count}");
        GUI.Label(new Rect(debugUIPosition.x + 10, debugUIPosition.y + 65, 230, 20), 
            $"Avg Characters/Cell: {(occupiedCells.Count > 0 ? (float)totalCharacters / occupiedCells.Count : 0):F1}");
    }

    /// <summary>
    /// Converts a world position to grid cell coordinates.
    /// </summary>
    public Vector2Int GetCellCoords(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.z / cellSize)
        );
    }

    /// <summary>
    /// Registers a Character in the grid at the specified cell.
    /// </summary>
    public void RegisterCharacter(BaseCharController character, Vector2Int cellCoords)
    {
        if (character == null) return;

        if (!grid.ContainsKey(cellCoords))
        {
            grid[cellCoords] = new List<BaseCharController>();
        }

        if (!grid[cellCoords].Contains(character))
        {
            grid[cellCoords].Add(character);
            occupiedCells.Add(cellCoords);
            totalCharacters++;
        }
    }

    /// <summary>
    /// Updates a Character's cell membership when it moves between cells.
    /// </summary>
    public void UpdateCharacter(BaseCharController character, Vector2Int oldCell, Vector2Int newCell)
    {
        if (character == null || oldCell == newCell) return;

        // Remove from old cell
        if (grid.ContainsKey(oldCell))
        {
            grid[oldCell].Remove(character);
            totalCharacters--;
            
            // Clean up empty cells
            if (grid[oldCell].Count == 0)
            {
                grid.Remove(oldCell);
                occupiedCells.Remove(oldCell);
            }
        }

        // Add to new cell
        RegisterCharacter(character, newCell);
    }

    /// <summary>
    /// Unregisters a Character from the grid.
    /// </summary>
    public void UnregisterCharacter(BaseCharController character, Vector2Int cellCoords)
    {
        if (character == null) return;

        if (grid.ContainsKey(cellCoords))
        {
            grid[cellCoords].Remove(character);
            totalCharacters--;
            
            // Clean up empty cells
            if (grid[cellCoords].Count == 0)
            {
                grid.Remove(cellCoords);
                occupiedCells.Remove(cellCoords);
            }
        }
    }

    /// <summary>
    /// Gets all Characters in the current cell and 8 adjacent cells (3x3 grid).
    /// </summary>
    public List<BaseCharController> GetNearbyCharacters(Vector2Int cellCoords)
    {
        List<BaseCharController> nearby = new List<BaseCharController>();

        // Check 3x3 grid
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int checkCell = cellCoords + new Vector2Int(x, z);
                if (grid.TryGetValue(checkCell, out var charactersInCell))
                {
                    nearby.AddRange(charactersInCell);
                }
            }
        }

        return nearby;
    }

    /// <summary>
    /// Gets the number of Characters in a specific cell.
    /// </summary>
    public int GetCellPopulation(Vector2Int cellCoords)
    {
        if (grid.TryGetValue(cellCoords, out var charactersInCell))
        {
            return charactersInCell.Count;
        }
        return 0;
    }

    /// <summary>
    /// Gets the total population of characters across multiple cells.
    /// </summary>
    /// <param name="cells">List of cell coordinates to check</param>
    /// <returns>Total number of characters across all specified cells</returns>
    public int GetCellsPopulation(List<Vector2Int> cells)
    {
        int total = 0;
        foreach (var cell in cells)
        {
            total += GetCellPopulation(cell);
        }
        return total;
    }

    /// <summary>
    /// Gets all characters in a specific cell.
    /// </summary>
    /// <param name="cellCoords">The cell coordinates to check</param>
    /// <returns>List of characters in the cell, or empty list if cell is empty</returns>
    public List<BaseCharController> GetCharactersInCell(Vector2Int cellCoords)
    {
        if (grid.TryGetValue(cellCoords, out var charactersInCell))
        {
            return new List<BaseCharController>(charactersInCell);
        }
        return new List<BaseCharController>();
    }

    private Color GetCellColor(int population)
    {
        if (population == 0) return emptyCellColor;
        if (population < lowPopThreshold) return lowPopulationColor;
        if (population < mediumPopThreshold) return mediumPopulationColor;
        return highPopulationColor;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos || !Application.isPlaying) return;

        // Draw all occupied cells
        foreach (var cellCoords in occupiedCells)
        {
            Vector3 cellCenter = new Vector3(
                cellCoords.x * cellSize + cellSize * 0.5f,
                0.1f,
                cellCoords.y * cellSize + cellSize * 0.5f
            );

            int population = GetCellPopulation(cellCoords);
            Color cellColor = GetCellColor(population);

            // Draw filled cell with color based on population
            Gizmos.color = cellColor;
            Gizmos.DrawCube(cellCenter + Vector3.up * 3f, new Vector3(cellSize, 0.1f, cellSize));

            // Draw cell boundaries
            if (drawCellBoundaries)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(cellCenter + Vector3.up * 3f, new Vector3(cellSize, 0.1f, cellSize));
            }

            // Draw population indicator spheres
            if (showCellPopulation && population > 0)
            {
                Gizmos.color = Color.yellow;
                float radius = 0.15f + (population * 0.15f);
                Gizmos.DrawSphere(cellCenter + Vector3.up * 3f, Mathf.Min(radius, 0.5f));
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        // Draw a larger visualization when selected
        Gizmos.color = new Color(1, 1, 1, 0.1f);
        
        // Draw a grid around the origin
        int gridExtent = 20;
        for (int x = -gridExtent; x <= gridExtent; x++)
        {
            for (int z = -gridExtent; z <= gridExtent; z++)
            {
                Vector3 cellCenter = new Vector3(
                    x * cellSize + cellSize * 0.5f,
                    0,
                    z * cellSize + cellSize * 0.5f
                );
                Gizmos.DrawWireCube(cellCenter, new Vector3(cellSize, 0.05f, cellSize));
            }
        }
    }
}
