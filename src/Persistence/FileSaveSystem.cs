// UNNAMED Persistence - Save System Implementation
// Implements D-05: Sparse-delta save with atomic write and corruption recovery
// No Godot references - this is pure C# domain logic

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UNNAMED.Persistence;
using UNNAMED.Domain;

/// <summary>
/// File-based save system implementation with atomic writes and corruption recovery.
/// Implements Windows-safe temp+rename pattern with one rolling backup slot.
/// </summary>
public class FileSaveSystem : ISaveSystem
{
    private readonly string _saveDirectory;
    private const string _autosaveSlot = "autosave";
    private const int _saveFormatVersion = 1;
    private const string _manifestFilename = "manifest.json";
    private const string _backupExtension = ".bak";

    /// <summary>
    /// Create a new file-based save system.
    /// </summary>
    /// <param name="saveDirectory">Directory to store save files (null uses default)</param>
    public FileSaveSystem(string? saveDirectory = null)
    {
        _saveDirectory = saveDirectory ?? Path.Combine(Environment.CurrentDirectory, "saves");
        Directory.CreateDirectory(_saveDirectory);
    }

    /// <summary>
    /// Get the autosave slot name.
    /// </summary>
    public string AutosaveSlot => _autosaveSlot;

    /// <summary>
    /// Get full path for a save file.
    /// </summary>
    private string GetSavePath(string saveName) => Path.Combine(_saveDirectory, $"{saveName}.json");

    /// <summary>
    /// Get full path for backup file.
    /// </summary>
    private string GetBackupPath(string saveName) => Path.Combine(_saveDirectory, $"{saveName}{_backupExtension}");

    /// <summary>
    /// Compute SHA-256 hash of data.
    /// </summary>
    private static string ComputeChecksum(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compute SHA-256 hash of file contents.
    /// </summary>
    public string ComputeChecksum(string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        return ComputeChecksum(fileBytes);
    }

    /// <summary>
    /// Serialize save data to JSON.
    /// </summary>
    private static string SerializeSaveData(SaveData data)
    {
        // Debug: print what's being serialized
        Console.WriteLine($"[SERIALIZE] data.Manifest.Checksum = '{data.Manifest.Checksum}'");
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        string result = JsonSerializer.Serialize(data, options);
        
        // Debug: print serialized JSON
        Console.WriteLine($"[SERIALIZE] Result:\n{result}");
        
        return result;
    }

    /// <summary>
    /// Deserialize save data from JSON.
    /// Validates manifest version, checksum, and data integrity.
    /// </summary>
    private static SaveData DeserializeSaveFile(byte[] fileBytes)
    {
        try
        {
            string json = Encoding.UTF8.GetString(fileBytes);
            Console.WriteLine($"Deserializing JSON:\n{json}");
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            
            SaveData saveData = JsonSerializer.Deserialize<SaveData>(json, options) ?? new SaveData();
            Console.WriteLine($"Deserialized saveData manifest version: {saveData.Manifest.Version}");
            Console.WriteLine($"Deserialized saveData manifest WorldSeed: {saveData.Manifest.WorldSeed}");

            // Validate manifest has required fields
            var manifest = saveData.Manifest;
            if (manifest.Version <= 0)
            {
                // Debug: log manifest values for troubleshooting
                string debugInfo = $"Manifest: Version={manifest.Version}, ContentHash='{manifest.ContentHash}', WorldSeed={manifest.WorldSeed}";
                throw new SaveCorruptionException($"Invalid manifest: version is zero or negative. {debugInfo}");
            }

            // Validate checksum by computing checksum over file (excluding checksum field)
            // This matches the save behavior where checksum is computed over JSON with empty checksum
            
            // Create a clone of the manifest with empty checksum
            var checksumFree = new SaveManifest
            {
                Version = manifest.Version,
                WorldSeed = manifest.WorldSeed,
                WorldGenVersion = manifest.WorldGenVersion,
                ContentHash = manifest.ContentHash,
                GameTick = manifest.GameTick,
                ChangedCellCount = manifest.ChangedCellCount,
                EntityCount = manifest.EntityCount,
                SavedAt = manifest.SavedAt,
                Checksum = string.Empty
            };
            
            // Serialize with empty checksum and compute hash (no duplicate 'options' variable needed - reusing from lines 108-114)
            // Create a copy of the save data with empty checksum manifest
            var saveDataForHash = new SaveData
            {
                Manifest = checksumFree,
                PlayerState = saveData.PlayerState,
                ChangedCells = saveData.ChangedCells
            };
            
            string jsonForHash = JsonSerializer.Serialize(saveDataForHash, options);
            string computedChecksum = ComputeChecksum(Encoding.UTF8.GetBytes(jsonForHash));
            
            if (manifest.Checksum != computedChecksum)
            {
                throw new SaveCorruptionException($"Checksum mismatch. Expected: {manifest.Checksum}, Got: {computedChecksum}");
            }

            // Validate player state - must have a valid position (not all zeros)
            var playerState = saveData.PlayerState;
            if (playerState.X == 0 && playerState.Y == 0 && playerState.Z == 0)
            {
                throw new SaveCorruptionException("Invalid player state: position at origin");
            }

            return saveData;
        }
        catch (SaveCorruptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SaveCorruptionException($"Failed to deserialize save file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Compute manifest checksum (zero out checksum field, compute hash, then restore).
    /// </summary>
    private static string ComputeManifestChecksum(SaveManifest manifest)
    {
        // Clone manifest with zeroed checksum for hash computation
        var checksumFree = new SaveManifest
        {
            Version = manifest.Version,
            WorldSeed = manifest.WorldSeed,
            WorldGenVersion = manifest.WorldGenVersion,
            ContentHash = manifest.ContentHash,
            GameTick = manifest.GameTick,
            ChangedCellCount = manifest.ChangedCellCount,
            EntityCount = manifest.EntityCount,
            SavedAt = manifest.SavedAt,
            Checksum = string.Empty
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        
        string manifestJson = JsonSerializer.Serialize(checksumFree, options);
        return ComputeChecksum(Encoding.UTF8.GetBytes(manifestJson));
    }

    /// <summary>
    /// Save game state to file with atomic write and backup preservation.
    /// </summary>
    public void Save(string saveName, SaveData data)
    {
        string logPath = Path.Combine(_saveDirectory, "save_debug.log");
        File.AppendAllText(logPath, $"[SAVE START] saveName='{saveName}'\n");
        if (string.IsNullOrWhiteSpace(saveName))
            throw new ArgumentException("Save name cannot be null or whitespace", nameof(saveName));

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        // Serialize to JSON first with empty checksum
        data.Manifest.Checksum = string.Empty;
        string jsonFirstPass = SerializeSaveData(data);
        byte[] dataBytesFirstPass = Encoding.UTF8.GetBytes(jsonFirstPass);
        
        Console.WriteLine($"Step 1 - After emptying checksum: '{data.Manifest.Checksum}'");
        File.AppendAllText(logPath, $"Step 1 - After emptying checksum: '{data.Manifest.Checksum}'\n");
        Console.WriteLine($"JSON first pass checksum field:");
        File.AppendAllText(logPath, "JSON first pass checksum field:\n");
        if (jsonFirstPass.Contains("\"checksum\":"))
        {
            int checksumStart = jsonFirstPass.IndexOf("\"checksum\":") + 12;
            int checksumEnd = jsonFirstPass.IndexOf('"', checksumStart);
            string foundChecksum = jsonFirstPass.Substring(checksumStart, checksumEnd - checksumStart);
            Console.WriteLine($"Found checksum in first pass: '{foundChecksum}'");
            File.AppendAllText(logPath, $"Found checksum in first pass: '{foundChecksum}'\n");
        }
        
        // Compute checksum over the JSON without checksum field
        string manifestChecksum = ComputeChecksum(dataBytesFirstPass);
        
        Console.WriteLine($"Computed checksum: '{manifestChecksum}'");
        File.AppendAllText(logPath, $"Computed checksum: '{manifestChecksum}'\n");
        
        // Set the checksum in the manifest
        data.Manifest.Checksum = manifestChecksum;
        
        Console.WriteLine($"Step 2 - After setting checksum: '{data.Manifest.Checksum}'");
        File.AppendAllText(logPath, $"Step 2 - After setting checksum: '{data.Manifest.Checksum}'\n");
        
        // Inline debug: print what we're about to serialize
        Console.WriteLine($"[DEBUG] About to serialize, manifest.Checksum = '{data.Manifest.Checksum}'");
        File.AppendAllText(logPath, $"[DEBUG] About to serialize, manifest.Checksum = '{data.Manifest.Checksum}'\n");
        
        // Serialize again with checksum set (now for file storage)
        string jsonSecondPass = SerializeSaveData(data);
        
        Console.WriteLine($"[DEBUG] jsonSecondPass was generated");
        File.AppendAllText(logPath, "[DEBUG] jsonSecondPass was generated\n");
        Console.WriteLine($"JSON second pass:\n{jsonSecondPass}");
        File.AppendAllText(logPath, $"JSON second pass:\n{jsonSecondPass}\n");
        
        // Debug: Check if JSON contains checksum
        if (jsonSecondPass.Contains("\"checksum\":"))
        {
            int checksumStart = jsonSecondPass.IndexOf("\"checksum\":") + 12;
            int checksumEnd = jsonSecondPass.IndexOf('"', checksumStart);
            string foundChecksum = jsonSecondPass.Substring(checksumStart, checksumEnd - checksumStart);
            Console.WriteLine($"[DEBUG] Checksum found in jsonSecondPass: '{foundChecksum}'");
            File.AppendAllText(logPath, $"[DEBUG] Checksum found in jsonSecondPass: '{foundChecksum}'\n");
        }
        
        // Store the checksum in the manifest (computed from JSON without checksum field)
        data.Manifest.Checksum = manifestChecksum;
        
        Console.WriteLine($"Final manifest.Checksum (from first pass): '{manifestChecksum}'");
        File.AppendAllText(logPath, $"Final manifest.Checksum (from first pass): '{manifestChecksum}'\n");
        
        // Serialize final version to write to disk
        byte[] dataBytes = Encoding.UTF8.GetBytes(jsonSecondPass);
        
        // Write to temp file first (Windows-safe atomic write)
        string tempPath = GetSavePath($"{saveName}.tmp");
        string savePath = GetSavePath(saveName);
        string backupPath = GetBackupPath(saveName);
        
        // Debug: verify JSON was written correctly by reading temp file
        if (File.Exists(tempPath))
        {
            string tempJson = File.ReadAllText(tempPath);
            Console.WriteLine($"[DEBUG] Temp file contains checksum: '{manifestChecksum}'");
            File.AppendAllText(logPath, $"[DEBUG] Temp file contains checksum: '{manifestChecksum}'\n");
            Console.WriteLine($"[DEBUG] Temp file JSON:\n{tempJson}");
            File.AppendAllText(logPath, $"[DEBUG] Temp file JSON:\n{tempJson}\n");
        }
        
        try
        {
            // Debug: verify directory exists
            Console.WriteLine($"Save directory: {_saveDirectory}, exists: {Directory.Exists(_saveDirectory)}");
            File.AppendAllText(logPath, $"Save directory: {_saveDirectory}, exists: {Directory.Exists(_saveDirectory)}\n");
            
            // Write temp file (WriteAllBytes flushes to disk before returning)
            Console.WriteLine($"Writing temp file: {tempPath}");
            File.AppendAllText(logPath, $"Writing temp file: {tempPath}\n");
            File.WriteAllBytes(tempPath, dataBytes);
            
            // Debug: verify temp file contents
            if (File.Exists(tempPath))
            {
                string tempJson = File.ReadAllText(tempPath);
                Console.WriteLine($"[DEBUG] After WriteAllBytes, temp file checksum field:");
                File.AppendAllText(logPath, "[DEBUG] After WriteAllBytes, temp file checksum field:\n");
                if (tempJson.Contains("\"checksum\":"))
                {
                    int checksumStart = tempJson.IndexOf("\"checksum\":") + 12;
                    int checksumEnd = tempJson.IndexOf('"', checksumStart);
                    string foundChecksum = tempJson.Substring(checksumStart, checksumEnd - checksumStart);
                    Console.WriteLine($"[DEBUG] Found checksum in temp file: '{foundChecksum}'");
                File.AppendAllText(logPath, $"[DEBUG] Found checksum in temp file: '{foundChecksum}'\n");
                }
            }
            
            Console.WriteLine($"Temp file written, size: {dataBytes.Length} bytes");
            File.AppendAllText(logPath, $"Temp file written, size: {dataBytes.Length} bytes\n");

            // Delete backup if exists (will be recreated from current save)
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            // Atomic rename (temp -> final)
            Console.WriteLine($"Renaming {tempPath} -> {savePath}");
            File.Move(tempPath, savePath, overwrite: true);
            Console.WriteLine($"Rename complete, save file exists: {File.Exists(savePath)}");
            File.AppendAllText(logPath, $"Rename complete, save file exists: {File.Exists(savePath)}\n");
        }
        catch (Exception ex)
        {
            // Debug: log exception details
            Console.WriteLine($"Exception during save: {ex.GetType().Name}: {ex.Message}");
            File.AppendAllText(logPath, $"Exception during save: {ex.GetType().Name}: {ex.Message}\n");
            
            // Cleanup temp file on failure
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }

        // Create backup (one rolling backup slot)
        if (File.Exists(savePath))
        {
            File.Copy(savePath, backupPath, overwrite: true);
        }
    }

    /// <summary>
    /// Load game state from file.
    /// </summary>
    public SaveData Load(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
            throw new ArgumentException("Save name cannot be null or whitespace", nameof(saveName));

        string savePath = GetSavePath(saveName);

        // Try main save file
        if (File.Exists(savePath))
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(savePath);
                return DeserializeSaveFile(fileBytes);
            }
            catch (SaveCorruptionException)
            {
                // Corrupt main save, try backup
                string backupPathLoad = GetBackupPath(saveName);
                if (File.Exists(backupPathLoad))
                {
                    byte[] fileBytes = File.ReadAllBytes(backupPathLoad);
                    return DeserializeSaveFile(fileBytes);
                }
                throw;
            }
        }

        // Try backup file if main doesn't exist
        string backupPathLoadLoad = GetBackupPath(saveName);
        if (File.Exists(backupPathLoadLoad))
        {
            byte[] fileBytes = File.ReadAllBytes(backupPathLoadLoad);
            return DeserializeSaveFile(fileBytes);
        }

        // Return null if save not found (interface expects nullable return)
        return null;
    }

    /// <summary>
    /// Delete a save file.
    /// </summary>
    public void Delete(string saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
            throw new ArgumentException("Save name cannot be null or whitespace", nameof(saveName));

        string savePath = GetSavePath(saveName);
        string backupPath = GetBackupPath(saveName);

        if (File.Exists(savePath))
        {
            File.Delete(savePath);
        }
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
    }

    /// <summary>
    /// List all available saves.
    /// </summary>
    public string[] ListSaves()
    {
        string savePath = GetSavePath("*");
        string backupPath = GetBackupPath("*");

        var saves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add main save names
        foreach (string file in Directory.GetFiles(_saveDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Skip temp files
            }
            saves.Add(name);
        }

        // Add backup save names
        foreach (string file in Directory.GetFiles(_saveDirectory, "*.bak"))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4); // Remove .bak extension
                saves.Add(name);
            }
        }

        return saves.ToArray();
    }
}
