using System.Collections.Generic;
using UnityEngine;

namespace LiquidSystem
{
    public class LiquidRenderer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LiquidManager liquidManager;
        [SerializeField] private GameObject liquidCellPrefab;
        
        [Header("Render Settings")]
        [SerializeField] private Vector3 cellSize = Vector3.one;
        [SerializeField] private float updateInterval = 0.1f;
        [SerializeField] private int renderDistance = 10;
        
        [Header("Liquid Materials")]
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material oilMaterial;
        [SerializeField] private Material lavaMaterial;
        
        // Keep track of rendered cells
        private Dictionary<Vector3Int, GameObject> renderedCells = new Dictionary<Vector3Int, GameObject>();
        
        // Keep track of when we need to update
        private float lastUpdateTime;
        private Vector3Int lastViewerChunkPosition;
        
        private void Awake()
        {
            if (liquidManager == null)
                liquidManager = FindObjectOfType<LiquidManager>();
                
            if (liquidManager == null)
            {
                Debug.LogError("LiquidRenderer: No LiquidManager found!");
                enabled = false;
                return;
            }
        }
        
        private void Update()
        {
            // Only update rendering at specified interval
            if (Time.time - lastUpdateTime < updateInterval)
                return;
                
            // Update rendering
            lastUpdateTime = Time.time;
            
            // Get viewer position (camera)
            Vector3 viewerPosition = Camera.main.transform.position;
            Vector3Int viewerChunkPosition = viewerPosition.WorldToCell().GetChunkPosition();
            
            // Only do a full update if moved to a new chunk
            bool fullUpdate = (viewerChunkPosition != lastViewerChunkPosition);
            lastViewerChunkPosition = viewerChunkPosition;
            
            UpdateRendering(viewerPosition, fullUpdate);
        }
        
        private void UpdateRendering(Vector3 viewerPosition, bool fullUpdate)
        {
            // Get bounds around the viewer
            Vector3Int viewerCellPosition = viewerPosition.WorldToCell();
            int renderDistanceSqr = renderDistance * renderDistance;
            
            // Build a list of cells to render
            Dictionary<Vector3Int, LiquidCell> cellsToRender = new Dictionary<Vector3Int, LiquidCell>();
            
            // Find all visible cells with liquid
            for (int x = -renderDistance; x <= renderDistance; x++)
            {
                for (int y = -renderDistance; y <= renderDistance; y++)
                {
                    for (int z = -renderDistance; z <= renderDistance; z++)
                    {
                        Vector3Int offset = new Vector3Int(x, y, z);
                        
                        // Skip cells outside of spherical render distance
                        if (offset.sqrMagnitude > renderDistanceSqr)
                            continue;
                            
                        Vector3Int cellPosition = viewerCellPosition + offset;
                        
                        // Get cell data
                        LiquidCell cell = liquidManager.GetCellAtPosition(cellPosition);
                        
                        // Only render cells with liquid
                        if (cell.HasLiquid)
                        {
                            cellsToRender[cellPosition] = cell;
                        }
                    }
                }
            }
            
            // Remove cells that are no longer visible or have no liquid
            if (fullUpdate)
            {
                List<Vector3Int> cellsToRemove = new List<Vector3Int>();
                
                foreach (var cellPosition in renderedCells.Keys)
                {
                    if (!cellsToRender.ContainsKey(cellPosition))
                    {
                        cellsToRemove.Add(cellPosition);
                    }
                }
                
                foreach (var cellPosition in cellsToRemove)
                {
                    RemoveRenderedCell(cellPosition);
                }
            }
            
            // Update or add new visible cells
            foreach (var kvp in cellsToRender)
            {
                Vector3Int cellPosition = kvp.Key;
                LiquidCell cell = kvp.Value;
                
                // If cell is already rendered, update it
                if (renderedCells.TryGetValue(cellPosition, out GameObject cellObject))
                {
                    UpdateRenderedCell(cellPosition, cell, cellObject);
                }
                else
                {
                    // Otherwise create a new rendered cell
                    AddRenderedCell(cellPosition, cell);
                }
            }
        }
        
        private void AddRenderedCell(Vector3Int cellPosition, LiquidCell cell)
        {
            // Create new cell object
            GameObject cellObject = Instantiate(liquidCellPrefab, transform);
            
            // Position at the bottom of the cell
            Vector3 worldPosition = new Vector3(
                cellPosition.x + 0.5f,
                cellPosition.y,
                cellPosition.z + 0.5f
            );
            cellObject.transform.position = worldPosition;
            
            // Set name for easy identification
            cellObject.name = $"LiquidCell_{cellPosition.x}_{cellPosition.y}_{cellPosition.z}";
            
            // Set material based on liquid type
            Renderer renderer = cellObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                switch (cell.liquidType)
                {
                    case LiquidType.Water:
                        renderer.material = waterMaterial;
                        break;
                    case LiquidType.Oil:
                        renderer.material = oilMaterial;
                        break;
                    case LiquidType.Lava:
                        renderer.material = lavaMaterial;
                        break;
                }
            }
            
            // Scale the cell based on liquid amount
            UpdateRenderedCell(cellPosition, cell, cellObject);
            
            // Add to dictionary
            renderedCells[cellPosition] = cellObject;
        }
        
        private void UpdateRenderedCell(Vector3Int cellPosition, LiquidCell cell, GameObject cellObject)
        {
            // Calculate scale based on liquid amount (0-12)
            float heightPercent = cell.liquidAmount / (float)LiquidParameters.MaxLiquidAmount;
            
            // Scale the object
            Vector3 scale = new Vector3(
                cellSize.x,
                cellSize.y * heightPercent,
                cellSize.z
            );
            
            cellObject.transform.localScale = scale;
            
            // Adjust position so it sits at the bottom of the cell
            Vector3 worldPosition = new Vector3(
                cellPosition.x + 0.5f,
                cellPosition.y + (cellSize.y * heightPercent * 0.5f),
                cellPosition.z + 0.5f
            );
            cellObject.transform.position = worldPosition;
            
            // Update material if liquid type changed
            Renderer renderer = cellObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material currentMaterial = null;
                
                // Determine the correct material
                switch (cell.liquidType)
                {
                    case LiquidType.Water:
                        currentMaterial = waterMaterial;
                        break;
                    case LiquidType.Oil:
                        currentMaterial = oilMaterial;
                        break;
                    case LiquidType.Lava:
                        currentMaterial = lavaMaterial;
                        break;
                }
                
                // Change material if needed
                if (currentMaterial != null && renderer.material != currentMaterial)
                {
                    renderer.material = currentMaterial;
                }
            }
        }
        
        private void RemoveRenderedCell(Vector3Int cellPosition)
        {
            if (renderedCells.TryGetValue(cellPosition, out GameObject cellObject))
            {
                // Destroy the cell object
                Destroy(cellObject);
                
                // Remove from dictionary
                renderedCells.Remove(cellPosition);
            }
        }
        
        // Clean up when destroyed
        private void OnDestroy()
        {
            // Destroy all rendered cells
            foreach (var cellObject in renderedCells.Values)
            {
                if (cellObject != null)
                {
                    Destroy(cellObject);
                }
            }
            
            renderedCells.Clear();
        }
    }
}