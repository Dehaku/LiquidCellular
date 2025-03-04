using System;
using UnityEngine;

namespace LiquidSystem
{
    // Class representing a single chunk of the liquid simulation
    public class LiquidChunk
    {
        // Chunk position in the world (in chunk coordinates)
        public readonly Vector3Int ChunkPosition;
        
        // Size of the chunk (usually a constant)
        private readonly int chunkSize;
        
        // 3D array of liquid cells
        private LiquidCell[,,] cells;
        
        // Buffer for compute shader
        private ComputeBuffer cellBuffer;
        
        // Flag to mark if chunk has changed and needs updating
        public bool IsDirty { get; private set; }
        
        // Adjacent chunks references (null if not loaded)
        public LiquidChunk[] AdjacentChunks { get; set; }
        
        // Constructor
        public LiquidChunk(Vector3Int chunkPosition, int chunkSize = LiquidParameters.ChunkSize)
        {
            ChunkPosition = chunkPosition;
            this.chunkSize = chunkSize;
            cells = new LiquidCell[chunkSize, chunkSize, chunkSize];
            AdjacentChunks = new LiquidChunk[6]; // +X, -X, +Y, -Y, +Z, -Z
            
            // Initialize all cells to empty
            InitializeCells();
        }
        
        // Initialize all cells to empty
        private void InitializeCells()
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        cells[x, y, z] = new LiquidCell
                        {
                            liquidType = LiquidType.None,
                            liquidAmount = 0,
                            wallType = WallType.None,
                            pressure = 0f
                        };
                    }
                }
            }
        }
        
        // Get cell at local position
        public LiquidCell GetCell(Vector3Int localPosition)
        {
            if (IsValidLocalPosition(localPosition))
            {
                return cells[localPosition.x, localPosition.y, localPosition.z];
            }
            
            return default;
        }
        
        // Set cell at local position
        public void SetCell(Vector3Int localPosition, LiquidCell cell)
        {
            if (IsValidLocalPosition(localPosition))
            {
                cells[localPosition.x, localPosition.y, localPosition.z] = cell;
                IsDirty = true;
            }
        }
        
        // Add liquid to a cell
        public void AddLiquid(Vector3Int localPosition, LiquidType liquidType, byte amount)
        {
            if (!IsValidLocalPosition(localPosition))
                return;
                
            var cell = GetCell(localPosition);
            
            // If cell has a wall or is already full, do nothing
            if (cell.HasWall || cell.IsFull)
                return;
                
            // If cell is empty, set the liquid type
            if (!cell.HasLiquid)
            {
                cell.liquidType = liquidType;
                cell.liquidAmount = amount;
            }
            // If cell has the same liquid type, add to amount
            else if (cell.liquidType == liquidType)
            {
                cell.liquidAmount = (byte)Mathf.Min(LiquidParameters.MaxLiquidAmount, cell.liquidAmount + amount);
            }
            // Handle mixing of different liquids (simplified for now)
            else
            {
                // Use the liquid with higher amount, or the one being added if equal
                if (amount >= cell.liquidAmount)
                {
                    cell.liquidType = liquidType;
                    cell.liquidAmount = amount;
                }
            }
            
            SetCell(localPosition, cell);
        }
        
        // Add a wall to a cell
        public void AddWall(Vector3Int localPosition, WallType wallType)
        {
            if (!IsValidLocalPosition(localPosition))
                return;
                
            var cell = GetCell(localPosition);
            cell.wallType = wallType;
            
            // Walls displace any liquid in the cell
            cell.liquidType = LiquidType.None;
            cell.liquidAmount = 0;
            
            SetCell(localPosition, cell);
        }
        
        // Remove wall from a cell
        public void RemoveWall(Vector3Int localPosition)
        {
            if (!IsValidLocalPosition(localPosition))
                return;
                
            var cell = GetCell(localPosition);
            cell.wallType = WallType.None;
            SetCell(localPosition, cell);
        }
        
        // Check if local position is valid within chunk bounds
        private bool IsValidLocalPosition(Vector3Int localPosition)
        {
            return localPosition.x >= 0 && localPosition.x < chunkSize &&
                   localPosition.y >= 0 && localPosition.y < chunkSize &&
                   localPosition.z >= 0 && localPosition.z < chunkSize;
        }
        
        // Create or update the compute buffer with current cell data
        public ComputeBuffer GetComputeBuffer()
        {
            // If buffer doesn't exist or has been disposed, create a new one
            if (cellBuffer == null || !cellBuffer.IsValid())
            {
                int totalCells = chunkSize * chunkSize * chunkSize;
                cellBuffer = new ComputeBuffer(totalCells, sizeof(uint)); // Size of LiquidCellData
            }
            
            // Convert cells to compute-friendly format
            LiquidCellData[] cellData = new LiquidCellData[chunkSize * chunkSize * chunkSize];
            int index = 0;
            
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        cellData[index++] = new LiquidCellData(cells[x, y, z]);
                    }
                }
            }
            
            // Update the buffer with new data
            cellBuffer.SetData(cellData);
            IsDirty = false;
            
            return cellBuffer;
        }
        
        // Update cells from compute buffer after simulation
        public void UpdateFromComputeBuffer(ComputeBuffer buffer)
        {
            // Read data back from compute buffer
            LiquidCellData[] cellData = new LiquidCellData[chunkSize * chunkSize * chunkSize];
            buffer.GetData(cellData);
            
            // Update cells with new data
            int index = 0;
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        cells[x, y, z] = cellData[index++].ToLiquidCell();
                    }
                }
            }
            
            IsDirty = true;
        }
        
        // Clean up resources when chunk is unloaded
        public void Dispose()
        {
            if (cellBuffer != null && cellBuffer.IsValid())
            {
                cellBuffer.Release();
                cellBuffer = null;
            }
        }
        
        // Transfer liquid between adjacent chunks
        public void TransferLiquidToAdjacentChunks()
        {
            // Process each edge of the chunk
            ProcessXEdges();
            ProcessYEdges();
            ProcessZEdges();
        }
        
        // Process X edges (transfer to +X and -X adjacent chunks)
        private void ProcessXEdges()
        {
            // Get adjacent chunks
            LiquidChunk positiveX = AdjacentChunks[0]; // +X
            LiquidChunk negativeX = AdjacentChunks[1]; // -X
            
            // Process +X edge
            if (positiveX != null)
            {
                int x = chunkSize - 1;
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            // Calculate pressure-based flow from this cell to adjacent chunk
                            float pressureDiff = sourceCell.pressure - positiveX.GetCell(new Vector3Int(0, y, z)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    positiveX.AddLiquid(new Vector3Int(0, y, z), sourceCell.liquidType, amountToTransfer);
                                    
                                    // Update source cell
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
            
            // Process -X edge (similar to +X but with opposite indices)
            if (negativeX != null)
            {
                int x = 0;
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            float pressureDiff = sourceCell.pressure - negativeX.GetCell(new Vector3Int(chunkSize - 1, y, z)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    negativeX.AddLiquid(new Vector3Int(chunkSize - 1, y, z), sourceCell.liquidType, amountToTransfer);
                                    
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Process Y edges (similar to X edges)
        private void ProcessYEdges()
        {
            // Implementation similar to ProcessXEdges but for Y axis
            LiquidChunk positiveY = AdjacentChunks[2]; // +Y
            LiquidChunk negativeY = AdjacentChunks[3]; // -Y
            
            // Process +Y edge (similar pattern as X)
            if (positiveY != null)
            {
                int y = chunkSize - 1;
                for (int x = 0; x < chunkSize; x++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            float pressureDiff = sourceCell.pressure - positiveY.GetCell(new Vector3Int(x, 0, z)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    positiveY.AddLiquid(new Vector3Int(x, 0, z), sourceCell.liquidType, amountToTransfer);
                                    
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
            
            // Process -Y edge (similar implementation)
            if (negativeY != null)
            {
                // Similar implementation to +Y but with different indices
                int y = 0;
                for (int x = 0; x < chunkSize; x++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        // Similar logic to other edge processing
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            float pressureDiff = sourceCell.pressure - negativeY.GetCell(new Vector3Int(x, chunkSize - 1, z)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    negativeY.AddLiquid(new Vector3Int(x, chunkSize - 1, z), sourceCell.liquidType, amountToTransfer);
                                    
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Process Z edges (similar to X and Y edges)
        private void ProcessZEdges()
        {
            // Implementation similar to ProcessXEdges but for Z axis
            LiquidChunk positiveZ = AdjacentChunks[4]; // +Z
            LiquidChunk negativeZ = AdjacentChunks[5]; // -Z
            
            // Similar implementation pattern as X and Y edges
            if (positiveZ != null)
            {
                int z = chunkSize - 1;
                for (int x = 0; x < chunkSize; x++)
                {
                    for (int y = 0; y < chunkSize; y++)
                    {
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            float pressureDiff = sourceCell.pressure - positiveZ.GetCell(new Vector3Int(x, y, 0)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    positiveZ.AddLiquid(new Vector3Int(x, y, 0), sourceCell.liquidType, amountToTransfer);
                                    
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
            
            if (negativeZ != null)
            {
                int z = 0;
                for (int x = 0; x < chunkSize; x++)
                {
                    for (int y = 0; y < chunkSize; y++)
                    {
                        LiquidCell sourceCell = cells[x, y, z];
                        if (sourceCell.HasLiquid)
                        {
                            float pressureDiff = sourceCell.pressure - negativeZ.GetCell(new Vector3Int(x, y, chunkSize - 1)).pressure;
                            if (pressureDiff > 0)
                            {
                                byte amountToTransfer = CalculateFlowAmount(sourceCell, pressureDiff);
                                if (amountToTransfer > 0)
                                {
                                    negativeZ.AddLiquid(new Vector3Int(x, y, chunkSize - 1), sourceCell.liquidType, amountToTransfer);
                                    
                                    sourceCell.liquidAmount = (byte)Mathf.Max(0, sourceCell.liquidAmount - amountToTransfer);
                                    if (sourceCell.liquidAmount == 0)
                                        sourceCell.liquidType = LiquidType.None;
                                        
                                    cells[x, y, z] = sourceCell;
                                    IsDirty = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Helper method to calculate flow amount based on pressure difference
        private byte CalculateFlowAmount(LiquidCell sourceCell, float pressureDiff)
        {
            // Scale the flow based on pressure difference and liquid type
            float flowRate = LiquidParameters.LiquidFlowRates[(int)sourceCell.liquidType];
            float flowAmount = pressureDiff * flowRate * sourceCell.liquidAmount;
            
            // Convert to byte with a minimum of 1 if there's any flow
            return (byte)Mathf.Max(0, Mathf.Min(sourceCell.liquidAmount, Mathf.CeilToInt(flowAmount)));
        }
    }
}