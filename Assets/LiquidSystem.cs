using System;
using UnityEngine;

namespace LiquidSystem
{
    // Enum for different liquid types
    public enum LiquidType : byte
    {
        None = 0,
        Water = 1,
        Oil = 2,
        Lava = 3
    }

    // Enum for different wall types
    public enum WallType : byte
    {
        None = 0,
        Dirt = 1,
        Stone = 2
    }

    // Struct for a single cell of liquid
    public struct LiquidCell
    {
        public LiquidType liquidType;  // Type of liquid in the cell
        public byte liquidAmount;      // Amount of liquid (0-12)
        public WallType wallType;      // Type of wall in the cell
        public float pressure;         // Pressure value for flow calculation

        // Helper property to check if cell is full
        public bool IsFull => liquidAmount >= 12;
        
        // Helper property to check if cell has a wall
        public bool HasWall => wallType != WallType.None;
        
        // Helper property to check if cell has any liquid
        public bool HasLiquid => liquidType != LiquidType.None && liquidAmount > 0;
    }

    // Struct for serializing liquid cell data to compute shader
    public struct LiquidCellData
    {
        public uint packedData;  // Packed data for compute shader

        // Constructor to pack data
        public LiquidCellData(LiquidCell cell)
        {
            packedData = 0;
            packedData |= (uint)cell.liquidType;
            packedData |= (uint)cell.liquidAmount << 8;
            packedData |= (uint)cell.wallType << 16;
            
            // Convert pressure to fixed-point for integer packing
            uint pressureFixed = (uint)(Mathf.Clamp01(cell.pressure) * 255);
            packedData |= pressureFixed << 24;
        }

        // Methods to unpack data
        public LiquidType GetLiquidType() => (LiquidType)(packedData & 0xFF);
        public byte GetLiquidAmount() => (byte)((packedData >> 8) & 0xFF);
        public WallType GetWallType() => (WallType)((packedData >> 16) & 0xFF);
        public float GetPressure() => ((packedData >> 24) & 0xFF) / 255f;

        // Method to convert back to LiquidCell
        public LiquidCell ToLiquidCell()
        {
            return new LiquidCell
            {
                liquidType = GetLiquidType(),
                liquidAmount = GetLiquidAmount(),
                wallType = GetWallType(),
                pressure = GetPressure()
            };
        }
    }

    // Constants and parameters for liquid simulation
    public static class LiquidParameters
    {
        // Maximum amount of liquid in a cell
        public const int MaxLiquidAmount = 12;
        
        // Chunk size (must be power of 2 for efficient compute shader dispatch)
        public const int ChunkSize = 16;
        
        // Densities of different liquids (for layering)
        public static readonly float[] LiquidDensities = {
            0.0f,       // None
            1.0f,       // Water
            0.8f,       // Oil (floats on water)
            1.5f        // Lava (sinks in water)
        };
        
        // Flow rates for different liquids
        public static readonly float[] LiquidFlowRates = {
            0.0f,       // None
            1.0f,       // Water
            0.7f,       // Oil (flows slower than water)
            0.5f        // Lava (flows slowest)
        };
        
        // Interactions between different fluid types
        public static LiquidInteraction GetInteraction(LiquidType a, LiquidType b)
        {
            if (a == LiquidType.None || b == LiquidType.None)
                return LiquidInteraction.None;
                
            if (a == LiquidType.Lava && b == LiquidType.Water)
                return LiquidInteraction.LavaWater;
                
            if (a == LiquidType.Oil && b == LiquidType.Lava)
                return LiquidInteraction.OilLava;
                
            return LiquidInteraction.None;
        }
    }
    
    // Enum for special interactions between liquid types
    public enum LiquidInteraction
    {
        None,
        LavaWater,  // Creates stone and steam
        OilLava     // Creates fire
    }

    // Vector3Int extension methods for working with grid coordinates
    public static class Vector3IntExtensions
    {
        // Convert a world position to a cell position
        public static Vector3Int WorldToCell(this Vector3 worldPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x),
                Mathf.FloorToInt(worldPosition.y),
                Mathf.FloorToInt(worldPosition.z)
            );
        }
        
        // Get the chunk coordinate from a cell position
        public static Vector3Int GetChunkPosition(this Vector3Int cellPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(cellPosition.x / (float)LiquidParameters.ChunkSize),
                Mathf.FloorToInt(cellPosition.y / (float)LiquidParameters.ChunkSize),
                Mathf.FloorToInt(cellPosition.z / (float)LiquidParameters.ChunkSize)
            );
        }
        
        // Get the local position within a chunk
        public static Vector3Int GetLocalPosition(this Vector3Int cellPosition)
        {
            return new Vector3Int(
                Mathf.Abs(cellPosition.x % LiquidParameters.ChunkSize),
                Mathf.Abs(cellPosition.y % LiquidParameters.ChunkSize),
                Mathf.Abs(cellPosition.z % LiquidParameters.ChunkSize)
            );
        }
    }
}