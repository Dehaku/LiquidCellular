using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiquidSystem
{
    // Main class for managing the liquid simulation
    public class LiquidManager : MonoBehaviour
    {
        // Static instance for easy access
        public static LiquidManager Instance { get; private set; }
        
        // Reference to compute shader
        [SerializeField] private ComputeShader liquidComputeShader;
        
        // Chunk manager
        private ChunkManager chunkManager;
        
        // Simulation parameters
        [Header("Simulation Parameters")]
        [SerializeField] private float simulationInterval = 0.1f; // Time between simulation steps
        [SerializeField] private int maxSimulationsPerFrame = 1;  // Max number of simulations per frame
        [SerializeField] private int simulationRadius = 2;        // Radius (in chunks) to simulate around viewer
        
        // Tracking variables
        private float lastSimulationTime;
        private Vector3Int lastViewerChunkPosition;
        
        // Kernel IDs for compute shader
        private int calculatePressureKernelId;
        private int simulateLiquidFlowKernelId;
        private int handleLiquidInteractionsKernelId;
        
        // Buffers for compute shader parameters
        private ComputeBuffer neighborsBuffer;
        
        // Unity lifecycle methods
        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            
            // Initialize chunk manager
            chunkManager = new ChunkManager();
            
            // Initialize compute shader kernels
            if (liquidComputeShader != null)
            {
                calculatePressureKernelId = liquidComputeShader.FindKernel("CalculatePressure");
                simulateLiquidFlowKernelId = liquidComputeShader.FindKernel("SimulateLiquidFlow");
                handleLiquidInteractionsKernelId = liquidComputeShader.FindKernel("HandleLiquidInteractions");
                
                // Create neighbors buffer for compute shader
                InitializeNeighborsBuffer();
            }
            else
            {
                Debug.LogError("LiquidManager: Compute shader not assigned!");
            }
        }
        
        private void Start()
        {
            lastSimulationTime = Time.time;
        }
        
        private void Update()
        {
            // Get viewer position (usually camera or player)
            Vector3 viewerPosition = Camera.main.transform.position;
            Vector3Int viewerChunkPosition = viewerPosition.WorldToCell().GetChunkPosition();
            
            // Check if it's time to run simulation
            if (Time.time - lastSimulationTime >= simulationInterval)
            {
                // Perform simulation steps
                int simulationsToRun = Mathf.Min(maxSimulationsPerFrame, Mathf.FloorToInt((Time.time - lastSimulationTime) / simulationInterval));
                
                for (int i = 0; i < simulationsToRun; i++)
                {
                    // If viewer has moved to a new chunk, load nearby chunks
                    if (viewerChunkPosition != lastViewerChunkPosition)
                    {
                        LoadChunksAroundViewer(viewerChunkPosition, simulationRadius);
                        lastViewerChunkPosition = viewerChunkPosition;
                    }
                    
                    // Run simulation
                    SimulateActiveChunks(viewerChunkPosition, simulationRadius);
                    
                    // Process chunk edges
                    chunkManager.ProcessChunkEdges();
                    
                    lastSimulationTime += simulationInterval;
                }
            }
        }
        
        private void OnDestroy()
        {
            // Clean up resources
            if (neighborsBuffer != null)
            {
                neighborsBuffer.Release();
                neighborsBuffer = null;
            }
            
            chunkManager?.Dispose();
            
            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        // Initialize neighbors buffer for compute shader
        private void InitializeNeighborsBuffer()
        {
            // Define the 6 neighbor directions (+X, -X, +Y, -Y, +Z, -Z)
            int[] neighborOffsets = new int[]
            {
                1, 0, 0,   // +X
                -1, 0, 0,  // -X
                0, 1, 0,   // +Y
                0, -1, 0,  // -Y
                0, 0, 1,   // +Z
                0, 0, -1   // -Z
            };
            
            // Create buffer
            neighborsBuffer = new ComputeBuffer(neighborOffsets.Length / 3, sizeof(int) * 3);
            
            // Convert to buffer-friendly format
            Vector3Int[] neighbors = new Vector3Int[neighborOffsets.Length / 3];
            for (int i = 0; i < neighbors.Length; i++)
            {
                neighbors[i] = new Vector3Int(
                    neighborOffsets[i * 3],
                    neighborOffsets[i * 3 + 1],
                    neighborOffsets[i * 3 + 2]
                );
            }
            
            // Set data
            neighborsBuffer.SetData(neighbors);
        }
        
        // Load chunks around viewer
        private void LoadChunksAroundViewer(Vector3Int viewerChunkPosition, int radius)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(
                            viewerChunkPosition.x + x,
                            viewerChunkPosition.y + y,
                            viewerChunkPosition.z + z
                        );
                        
                        // Load or get chunk (this will handle resource management)
                        chunkManager.GetChunk(chunkPos);
                    }
                }
            }
        }
        
        // Simulate all chunks within radius of viewer
        private void SimulateActiveChunks(Vector3Int viewerChunkPosition, int radius)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        Vector3Int chunkPos = new Vector3Int(
                            viewerChunkPosition.x + x,
                            viewerChunkPosition.y + y,
                            viewerChunkPosition.z + z
                        );
                        
                        if (chunkManager.ChunkExists(chunkPos))
                        {
                            LiquidChunk chunk = chunkManager.GetChunk(chunkPos);
                            SimulateChunk(chunk);
                        }
                    }
                }
            }
        }
        
        // Simulate a single chunk using compute shader
        private void SimulateChunk(LiquidChunk chunk)
        {
            if (liquidComputeShader == null)
                return;
                
            // Get compute buffer for this chunk
            ComputeBuffer cellBuffer = chunk.GetComputeBuffer();
            
            // Calculate chunk size for compute shader
            int chunkSize = LiquidParameters.ChunkSize;
            
            // Set common parameters for all kernels
            liquidComputeShader.SetBuffer(calculatePressureKernelId, "CellBuffer", cellBuffer);
            liquidComputeShader.SetInt("ChunkSize", chunkSize);
            liquidComputeShader.SetBuffer(calculatePressureKernelId, "NeighborsBuffer", neighborsBuffer);
            
            // Create dummy buffer for non-existent chunks
            ComputeBuffer dummyBuffer = new ComputeBuffer(1, sizeof(uint));
            uint[] dummyData = new uint[1] { 0 };
            dummyBuffer.SetData(dummyData);
            
            // Set up adjacent chunk buffers if available
            ComputeBuffer[] adjacentBuffers = new ComputeBuffer[6];
            for (int i = 0; i < 6; i++)
            {
                if (chunk.AdjacentChunks[i] != null)
                {
                    adjacentBuffers[i] = chunk.AdjacentChunks[i].GetComputeBuffer();
                    liquidComputeShader.SetInt("HasAdjacentChunk" + i, 1);
                }
                else
                {
                    adjacentBuffers[i] = dummyBuffer;
                    liquidComputeShader.SetInt("HasAdjacentChunk" + i, 0);
                }
                
                // Set buffer for all kernels
                liquidComputeShader.SetBuffer(calculatePressureKernelId, "AdjacentChunk" + i, adjacentBuffers[i]);
                liquidComputeShader.SetBuffer(simulateLiquidFlowKernelId, "AdjacentChunk" + i, adjacentBuffers[i]);
                liquidComputeShader.SetBuffer(handleLiquidInteractionsKernelId, "AdjacentChunk" + i, adjacentBuffers[i]);
            }
            
            // We'll release the dummy buffer after dispatch
            
            // Execute pressure calculation
            liquidComputeShader.Dispatch(calculatePressureKernelId, 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f));
            
            // Set buffers for liquid flow simulation
            liquidComputeShader.SetBuffer(simulateLiquidFlowKernelId, "CellBuffer", cellBuffer);
            liquidComputeShader.SetBuffer(simulateLiquidFlowKernelId, "NeighborsBuffer", neighborsBuffer);
            
            // Liquid type parameters
            liquidComputeShader.SetFloats("LiquidDensities", LiquidParameters.LiquidDensities);
            liquidComputeShader.SetFloats("LiquidFlowRates", LiquidParameters.LiquidFlowRates);
            
            // Execute liquid flow simulation
            liquidComputeShader.Dispatch(simulateLiquidFlowKernelId, 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f));
            
            // Set buffers for liquid interactions
            liquidComputeShader.SetBuffer(handleLiquidInteractionsKernelId, "CellBuffer", cellBuffer);
            liquidComputeShader.SetBuffer(handleLiquidInteractionsKernelId, "NeighborsBuffer", neighborsBuffer);
            
            // Set chunk parameters for the interactions kernel
            liquidComputeShader.SetInt("ChunkSize", chunkSize);
            
            // Execute liquid interactions
            liquidComputeShader.Dispatch(handleLiquidInteractionsKernelId, 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f), 
                Mathf.CeilToInt(chunkSize / 8f));
            
            // Update chunk data from compute buffer
            chunk.UpdateFromComputeBuffer(cellBuffer);
            
            // Release the dummy buffer
            dummyBuffer.Release();
        }
        
        // Public API methods for modifying liquid system
        
        // Add liquid at world position
        public void AddLiquid(Vector3 worldPosition, LiquidType liquidType, byte amount)
        {
            Vector3Int cellPosition = worldPosition.WorldToCell();
            chunkManager.AddLiquid(cellPosition, liquidType, amount);
        }
        
        // Add wall at world position
        public void AddWall(Vector3 worldPosition, WallType wallType)
        {
            Vector3Int cellPosition = worldPosition.WorldToCell();
            chunkManager.AddWall(cellPosition, wallType);
        }
        
        // Remove wall at world position
        public void RemoveWall(Vector3 worldPosition)
        {
            Vector3Int cellPosition = worldPosition.WorldToCell();
            chunkManager.RemoveWall(cellPosition);
        }
        
        // Get cell at world position
        public LiquidCell GetCellAtPosition(Vector3 worldPosition)
        {
            Vector3Int cellPosition = worldPosition.WorldToCell();
            return chunkManager.GetCell(cellPosition);
        }
    }
}