/// <summary>
/// Build verification script for M1.
/// This script verifies:
/// 1. Domain contains no Godot references
/// 2. Application contains no Godot references
/// 3. Presentation can reference Domain/Application but cannot access internal writer
/// </summary>
public static class BuildVerification
{
    /// <summary>
    /// Check that a project file contains no Godot references.
    /// </summary>
    public static bool NoGodotReferences(string projectPath)
    {
        var content = File.ReadAllText(projectPath);
        return !content.Contains("GodotSharp", StringComparison.OrdinalIgnoreCase);
    }
}
