// UNNAMED Persistence Tests - FileSaveSystem Tests
// Tests for D-05: Sparse-delta save with atomic write and corruption recovery
// No Godot references - this is pure C# test logic

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UNNAMED.Domain;
using UNNAMED.Persistence;
using Xunit;

namespace UNNAMED.Persistence.Tests;

// Use ClassData to share test context between tests
public class FileSaveSystemTests : IDisposable
{
    private readonly string _testDirectory;

    public FileSaveSystemTests()
    {
        // Use a constant directory name per test run
        _testDirectory = Path.Combine(Path.GetTempPath(), "UNNAMED_Test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); }
            catch { /* ignore deletion errors */ }
        }
    }

    private SaveData CreateTestSaveData()
    {
        var manifest = new SaveManifest
        {
            Version = 1,
            WorldSeed = 12345,
            WorldGenVersion = 1,
            ContentHash = "abc123",
            GameTick = 1000,
            ChangedCellCount = 2,
            EntityCount = 5,
            SavedAt = DateTime.Parse("2026-01-01T00:00:00Z"),
            Checksum = ""
        };

        var playerState = new PlayerState
        {
            X = 10.0,
            Y = 0.0,
            Z = -5.0,
            Rotation = 45.0,
            InventoryItems = new EntityId[]
            {
                EntityId.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAV"),
                EntityId.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAW")
            }
        };

        var changedCells = new ChangedCell[]
        {
            new ChangedCell { X = 0, Y = 0, Data = Encoding.UTF8.GetBytes("cell_0_0") },
            new ChangedCell { X = 1, Y = 0, Data = Encoding.UTF8.GetBytes("cell_1_0") }
        };

        return new SaveData
        {
            Manifest = manifest,
            PlayerState = playerState,
            ChangedCells = changedCells
        };
    }

    [Fact]
    public void Save_Load_Roundtrip_PreservesData()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Debug: print test directory
        Console.WriteLine($"Test directory: {_testDirectory}");
        Console.WriteLine($"Directory exists: {Directory.Exists(_testDirectory)}");

        // Save
        Console.WriteLine($"Calling saveSystem.Save()");
        saveSystem.Save("test_save", saveData);
        Console.WriteLine($"Save completed");

        // Debug: read the file to see what was written
        string savePath = Path.Combine(_testDirectory, "test_save.json");
        Console.WriteLine($"Save path: {savePath}, exists: {File.Exists(savePath)}");
        
        if (!File.Exists(savePath))
        {
            // List directory contents for debugging
            try
            {
                string[] files = Directory.GetFiles(_testDirectory);
                Console.WriteLine($"Directory contents: {string.Join(", ", files)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing directory: {ex.Message}");
            }
        }
        
        string json = File.ReadAllText(savePath);
        Console.WriteLine("JSON file contents:\n" + json);

        // Load
        SaveData? loadedData = saveSystem.Load("test_save");
        Assert.NotNull(loadedData);

        Assert.Equal(saveData.Manifest.WorldSeed, loadedData!.Manifest.WorldSeed);
        Assert.Equal(saveData.Manifest.ContentHash, loadedData.Manifest.ContentHash);
        Assert.Equal(saveData.PlayerState.X, loadedData.PlayerState.X);
        Assert.Equal(saveData.PlayerState.Y, loadedData.PlayerState.Y);
        Assert.Equal(saveData.ChangedCells.Length, loadedData.ChangedCells.Length);
        Assert.Equal(saveData.ChangedCells[0].Data, loadedData.ChangedCells[0].Data);
    }

    [Fact]
    public void Save_DeterministicChecksum_VerifiesIntegrity()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Save
        saveSystem.Save("checksum_test", saveData);

        // Load and verify checksum
        var loadedData = saveSystem.Load("checksum_test");
        Assert.NotNull(loadedData);
        Assert.Equal(saveData.Manifest.Checksum, loadedData!.Manifest.Checksum);

        // Verify checksum matches actual file content
        string savePath = Path.Combine(_testDirectory, "checksum_test.json");
        string actualChecksum = saveSystem.ComputeChecksum(savePath);
        Assert.Equal(saveData.Manifest.Checksum, actualChecksum);
    }

    [Fact]
    public void AtomicWrite_Rename_PreservedOnSuccess()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Save
        saveSystem.Save("rename_test", saveData);

        // Verify save file exists
        string savePath = Path.Combine(_testDirectory, "rename_test.json");
        Assert.True(File.Exists(savePath), "Save file should exist");

        // Verify temp file is cleaned up
        string tempPath = Path.Combine(_testDirectory, "rename_test.tmp.json");
        Assert.False(File.Exists(tempPath), "Temp file should be cleaned up");
    }

    [Fact]
    public void Corruption_ChecksumMismatch_ThrowsCorruptionException()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Save
        saveSystem.Save("corrupt_test", saveData);

        // Verify file exists before corrupting
        string savePath = Path.Combine(_testDirectory, "corrupt_test.json");
        Assert.True(File.Exists(savePath), "Save file should exist before corruption");

        // Corrupt the file
        byte[] fileBytes = File.ReadAllBytes(savePath);
        fileBytes[0] = (byte)(fileBytes[0] ^ 0xFF); // Flip a bit
        File.WriteAllBytes(savePath, fileBytes);

        // Load should fail with corruption exception
        Assert.Throws<SaveCorruptionException>(() => saveSystem.Load("corrupt_test"));
    }

    [Fact]
    public void Corruption_TruncatedFile_ThrowsCorruptionException()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Save
        saveSystem.Save("truncate_test", saveData);

        // Verify file exists before truncating
        string savePath = Path.Combine(_testDirectory, "truncate_test.json");
        Assert.True(File.Exists(savePath), "Save file should exist before truncation");

        // Truncate the file
        byte[] fileBytes = File.ReadAllBytes(savePath);
        byte[] truncated = new byte[fileBytes.Length / 2];
        Array.Copy(fileBytes, truncated, truncated.Length);
        File.WriteAllBytes(savePath, truncated);

        // Load should fail with corruption exception
        Assert.Throws<SaveCorruptionException>(() => saveSystem.Load("truncate_test"));
    }

    [Fact]
    public void Backup_RollingSlot_Preserved()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // First save
        saveSystem.Save("backup_test", saveData);
        string backupPath = Path.Combine(_testDirectory, "backup_test.json.bak");
        Assert.True(File.Exists(backupPath));
        long firstBackupSize = new FileInfo(backupPath).Length;

        // Second save - should update backup
        saveSystem.Save("backup_test", saveData);
        Assert.True(File.Exists(backupPath));
        long secondBackupSize = new FileInfo(backupPath).Length;

        // Third save - should update backup again
        saveSystem.Save("backup_test", saveData);
        Assert.True(File.Exists(backupPath));
        long thirdBackupSize = new FileInfo(backupPath).Length;

        // Backup should be preserved (rolling one slot)
        Assert.True(thirdBackupSize > 0, "Backup file should exist after multiple saves");
    }

    [Fact]
    public void Delete_CleansUpFiles()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Save
        saveSystem.Save("delete_test", saveData);
        string savePath = Path.Combine(_testDirectory, "delete_test.json");
        string backupPath = Path.Combine(_testDirectory, "delete_test.json.bak");

        Assert.True(File.Exists(savePath), "Save file should exist before delete");
        Assert.True(File.Exists(backupPath), "Backup file should exist before delete");

        // Delete
        saveSystem.Delete("delete_test");

        // Verify files are removed
        Assert.False(File.Exists(savePath), "Save file should be deleted");
        Assert.False(File.Exists(backupPath), "Backup file should be deleted");
    }

    [Fact]
    public void ListSaves_ReturnsAllSaves()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        // Create multiple saves
        saveSystem.Save("save1", saveData);
        saveSystem.Save("save2", saveData);
        saveSystem.Save("save3", saveData);

        // List saves
        string[] saves = saveSystem.ListSaves();
        Assert.Contains("save1", saves);
        Assert.Contains("save2", saves);
        Assert.Contains("save3", saves);
        Assert.Equal(3, saves.Length);
    }

    [Fact]
    public void Load_NonexistentSave_ReturnsNull()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        SaveData? result = saveSystem.Load("nonexistent_save");
        Assert.Null(result);
    }

    [Fact]
    public void AtomicWrite_TempFileDeletion_OnSuccess()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        var saveData = CreateTestSaveData();

        saveSystem.Save("temp_delete_test", saveData);

        // Temp file should be deleted after successful save
        string tempPath = Path.Combine(_testDirectory, "temp_delete_test.tmp.json");
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void AutosaveSlot_ReturnsCorrectName()
    {
        var saveSystem = new FileSaveSystem(_testDirectory);
        Assert.Equal("autosave", saveSystem.AutosaveSlot);
    }
}
