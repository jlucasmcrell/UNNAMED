// UNNAMED Persistence - Save System Interfaces
// This file defines the persistence layer interfaces
// No Godot references - this is pure C# domain logic

using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using UNNAMED.Domain;

namespace UNNAMED.Persistence;

/// <summary>
/// Save manifest metadata for the world state.
/// Contains deterministic generation parameters and versioning info.
/// </summary>
public class SaveManifest
{
    public SaveManifest() { }

    public int Version { get; set; }
    public long WorldSeed { get; set; }
    public int WorldGenVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public long GameTick { get; set; }
    public int ChangedCellCount { get; set; }
    public int EntityCount { get; set; }
    public DateTime SavedAt { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

/// <summary>
/// Player state that can be serialized.
/// This is separate from world state to enable hot-swapping worlds.
/// </summary>
public class PlayerState
{
    public PlayerState() { }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Rotation { get; set; }
    public EntityId[] InventoryItems { get; set; } = Array.Empty<EntityId>();
}

/// <summary>
/// A cell in the sparse save.
/// Only changed cells are saved (baseline contributes zero bytes).
/// </summary>
public class ChangedCell
{
    public ChangedCell() { }

    public int X { get; set; }
    public int Y { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Complete save data including manifest, player state, and changed cells.
/// </summary>
public class SaveData
{
    public SaveData() { }

    public SaveManifest Manifest { get; set; } = new();
    public PlayerState PlayerState { get; set; } = new();
    public ChangedCell[] ChangedCells { get; set; } = Array.Empty<ChangedCell>();
}

/// <summary>
/// Interface for persistent save/load operations.
/// </summary>
public interface ISaveSystem
{
    string AutosaveSlot { get; }

    void Save(string saveName, SaveData data);
    SaveData? Load(string saveName);
    string[] ListSaves();
    void Delete(string saveName);

    /// <summary>
    /// Compute SHA-256 checksum of file contents.
    /// </summary>
    string ComputeChecksum(string filePath);
}

/// <summary>
/// Exception thrown when save data is corrupted.
/// </summary>
public class SaveCorruptionException : Exception
{
    public SaveCorruptionException(string message) : base(message) { }
    public SaveCorruptionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a save is not found.
/// </summary>
public class SaveNotFoundException : Exception
{
    public SaveNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when save version is incompatible.
/// </summary>
public class SaveVersionMismatchException : Exception
{
    public SaveVersionMismatchException(string message) : base(message) { }
}
