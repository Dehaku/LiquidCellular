using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiquidSystem
{
    // Class to manage chunks and handle loading/unloading
    public class ChunkManager
    {
        // Dictionary of loaded chunks, keyed by chunk position
        private Dictionary<Vector3Int, LiquidChunk> loadedChunks;
        
        // Maximum number of chunks to keep loaded
        private int maxLoadedChunks;
        
        // Queue of chunks ordered by last access time
        private Queue<Vector3Int> chunkAccessQueue;
        
        // Constructor
        public ChunkManager(int maxLoadedChunks = 256)
        {
            this.maxLoadedChunks = maxLoadedChunks;
            loadedChunks = new Dictionary<Vector3Int, LiquidChunk>();
            chunkAccessQueue = new Queue<Vector3Int>();
        }
        
        // Get or create a chunk at the specified position
        public LiquidChunk GetChunk(Vector3Int chunkPosition)
        {
            // Check if chunk is already loaded
            if (loadedChunks.TryGetValue(chunkPosition, out LiquidChunk chunk))
            {
                // Update access time by removing and re-adding to queue
                if (chunkAccessQueue.Contains(chunkPosition))
                {
                    List<Vector3Int> tempList = new List<Vector3Int>(chunkAccessQueue);
                    tempList.Remove(chunkPosition);
                    chunkAccessQueue = new Queue<Vector3Int>(tempList);
                }
                chunkAccessQueue.Enqueue(chunkPosition);
                
                return chunk;
            }
            
            // If not loaded, create a new chunk
            chunk = new LiquidChunk(chunkPosition);
            loadedChunks.Add(chunkPosition, chunk);
            chunkAccessQueue.Enqueue(chunkPosition);
            
            // Update adjacent chunks references
            UpdateAdjacentChunks(chunk);
            
            // If we've exceeded the maximum, unload the oldest chunk
            while (loadedChunks.Count > maxLoadedChunks && chunkAccessQueue.Count > 0)
            {
                Vector3Int oldestPosition = chunkAccessQueue.Dequeue();
                UnloadChunk(oldestPosition);
            }
            
            return chunk;
        }
        
        // Check if a chunk exists at the specified position
        public bool ChunkExists(Vector3Int chunkPosition)
        {
            return loadedChunks.ContainsKey(chunkPosition);
        }
        
        // Unload a chunk at the specified position
        public void UnloadChunk(Vector3Int chunkPosition)
        {
            if (loadedChunks.TryGetValue(chunkPosition, out LiquidChunk chunk))
            {
                // Clean up chunk resources
                chunk.Dispose();
                loadedChunks.Remove(chunkPosition);
                
                // Update adjacent chunks since this one is gone
                UpdateAdjacentChunksAfterUnload(chunkPosition);
            }
        }
        
        // Get cell at a global position
        public LiquidCell GetCell(Vector3Int globalPosition)
        {
            // Convert global position to chunk position and local position
            Vector3Int chunkPosition = globalPosition.GetChunkPosition();
            Vector3Int localPosition = globalPosition.GetLocalPosition();
            
            // Get the chunk and then the cell
            LiquidChunk chunk = GetChunk(chunkPosition);
            return chunk.GetCell(localPosition);
        }
        
        // Set cell at a global position
        public void SetCell(Vector3Int globalPosition, LiquidCell cell)
        {
            Vector3Int chunkPosition = globalPosition.GetChunkPosition();
            Vector3Int localPosition = globalPosition.GetLocalPosition();
            
            LiquidChunk chunk = GetChunk(chunkPosition);
            chunk.SetCell(localPosition, cell);
        }
        
        // Add liquid at a global position
        public void AddLiquid(Vector3Int globalPosition, LiquidType liquidType, byte amount)
        {
            Vector3Int chunkPosition = globalPosition.GetChunkPosition();
            Vector3Int localPosition = globalPosition.GetLocalPosition();
            
            LiquidChunk chunk = GetChunk(chunkPosition);
            chunk.AddLiquid(localPosition, liquidType, amount);
        }
        
        // Add wall at a global position
        public void AddWall(Vector3Int globalPosition, WallType wallType)
        {
            Vector3Int chunkPosition = globalPosition.GetChunkPosition();
            Vector3Int localPosition = globalPosition.GetLocalPosition();
            
            LiquidChunk chunk = GetChunk(chunkPosition);
            chunk.AddWall(localPosition, wallType);
        }
        
        // Remove wall at a global position
        public void RemoveWall(Vector3Int globalPosition)
        {
            Vector3Int chunkPosition = globalPosition.GetChunkPosition();
            Vector3Int localPosition = globalPosition.GetLocalPosition();
            
            LiquidChunk chunk = GetChunk(chunkPosition);
            chunk.RemoveWall(localPosition);
        }
        
        // Get all loaded chunks
        public IEnumerable<LiquidChunk> GetAllLoadedChunks()
        {
            return loadedChunks.Values;
        }
        
        // Update adjacent chunks references for a given chunk
        private void UpdateAdjacentChunks(LiquidChunk chunk)
        {
            Vector3Int pos = chunk.ChunkPosition;
            
            // Define the six adjacent positions (+X, -X, +Y, -Y, +Z, -Z)
            Vector3Int[] adjacentPositions = new Vector3Int[]
            {
                new Vector3Int(pos.x + 1, pos.y, pos.z), // +X
                new Vector3Int(pos.x - 1, pos.y, pos.z), // -X
                new Vector3Int(pos.x, pos.y + 1, pos.z), // +Y
                new Vector3Int(pos.x, pos.y - 1, pos.z), // -Y
                new Vector3Int(pos.x, pos.y, pos.z + 1), // +Z
                new Vector3Int(pos.x, pos.y, pos.z - 1)  // -Z
            };
            
            // Check each adjacent position and update references
            for (int i = 0; i < 6; i++)
            {
                if (loadedChunks.TryGetValue(adjacentPositions[i], out LiquidChunk adjacentChunk))
                {
                    // Set reference in this chunk
                    chunk.AdjacentChunks[i] = adjacentChunk;
                    
                    // Set reference in adjacent chunk (using the opposite direction)
                    int oppositeIndex = i % 2 == 0 ? i + 1 : i - 1;
                    adjacentChunk.AdjacentChunks[oppositeIndex] = chunk;
                }
            }
        }
        
        // Update adjacent chunks after a chunk is unloaded
        private void UpdateAdjacentChunksAfterUnload(Vector3Int chunkPosition)
        {
            // Define the six adjacent positions (+X, -X, +Y, -Y, +Z, -Z)
            Vector3Int[] adjacentPositions = new Vector3Int[]
            {
                new Vector3Int(chunkPosition.x + 1, chunkPosition.y, chunkPosition.z), // +X
                new Vector3Int(chunkPosition.x - 1, chunkPosition.y, chunkPosition.z), // -X
                new Vector3Int(chunkPosition.x, chunkPosition.y + 1, chunkPosition.z), // +Y
                new Vector3Int(chunkPosition.x, chunkPosition.y - 1, chunkPosition.z), // -Y
                new Vector3Int(chunkPosition.x, chunkPosition.y, chunkPosition.z + 1), // +Z
                new Vector3Int(chunkPosition.x, chunkPosition.y, chunkPosition.z - 1)  // -Z
            };
            
            // Clear references in adjacent chunks
            for (int i = 0; i < 6; i++)
            {
                if (loadedChunks.TryGetValue(adjacentPositions[i], out LiquidChunk adjacentChunk))
                {
                    int oppositeIndex = i % 2 == 0 ? i + 1 : i - 1;
                    adjacentChunk.AdjacentChunks[oppositeIndex] = null;
                }
            }
        }
        
        // Process edge cells for all loaded chunks
        public void ProcessChunkEdges()
        {
            foreach (var chunk in loadedChunks.Values)
            {
                chunk.TransferLiquidToAdjacentChunks();
            }
        }
        
        // Clean up all resources
        public void Dispose()
        {
            foreach (var chunk in loadedChunks.Values)
            {
                chunk.Dispose();
            }
            
            loadedChunks.Clear();
            chunkAccessQueue.Clear();
        }
    }
}