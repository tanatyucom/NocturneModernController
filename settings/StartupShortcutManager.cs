using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Windows Startup Folder integration for NocturneModernController.Broker.exe.
// Uses only a per-user .lnk in Environment.SpecialFolder.Startup - no admin
// rights, no Registry Run key, no Scheduled Task, no Service, and no
// PowerShell fallback. The .lnk's own existence + TargetPath is the only
// source of truth; no boolean is persisted in settings JSON. Broker liveness
// for MOD/game purposes remains governed exclusively by the named Mutex
// documented in docs/broker-liveness-architecture.md - this class never
// touches that decision and never starts Broker.exe itself, here or anywhere
// else in this project.
internal static class StartupShortcutManager
{
    private const string ShortcutFileName = "NocturneModernController.Broker.lnk";

    private const string DescriptionMarker =
        "NocturneModernController Broker startup shortcut - managed by NocturneModernController.Settings.exe; safe to delete.";

    internal static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutFileName);

    internal static string? ResolveBrokerPath()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "NocturneModernController.Broker.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    internal static bool IsRegistered(string brokerPath)
    {
        string path = ShortcutPath;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string? target = ReadTargetPath(path);
            return target != null && SamePath(target, brokerPath);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryCreate(string brokerPath, out string? error)
    {
        error = null;
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = CreateShellObject();
            shortcut = InvokeShell(shell, "CreateShortcut", ShortcutPath);
            Type shortcutType = shortcut!.GetType();
            SetProperty(shortcutType, shortcut, "TargetPath", brokerPath);
            SetProperty(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(brokerPath) ?? string.Empty);
            SetProperty(shortcutType, shortcut, "Description", DescriptionMarker);
            SetProperty(shortcutType, shortcut, "WindowStyle", 7); // SW_SHOWMINNOACTIVE
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    internal static bool TryRemoveOwned(string brokerPath, out bool ownershipMismatch, out string? error)
    {
        ownershipMismatch = false;
        error = null;
        string path = ShortcutPath;
        if (!File.Exists(path))
        {
            return true; // already absent, nothing to do
        }

        try
        {
            string? target = ReadTargetPath(path);
            if (target == null || !SamePath(target, brokerPath))
            {
                ownershipMismatch = true;
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string? ReadTargetPath(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = CreateShellObject();
            shortcut = InvokeShell(shell, "CreateShortcut", shortcutPath);
            object? value = shortcut!.GetType().InvokeMember(
                "TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            return value as string;
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    // Late-bound WScript.Shell (Windows Script Host, present on every Windows
    // install) - avoids a COM reference/NuGet package and avoids spawning
    // powershell.exe or cmd.exe. Only CreateShortcut/property set/Save/
    // TargetPath-get are ever invoked; .Run and similar are never used.
    private static object CreateShellObject()
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell is not available on this system.");
        }
        return Activator.CreateInstance(shellType)!;
    }

    private static object? InvokeShell(object shell, string method, string arg) =>
        shell.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, shell, new object[] { arg });

    private static void SetProperty(Type type, object instance, string name, object value) =>
        type.InvokeMember(name, BindingFlags.SetProperty, null, instance, new object[] { value });
}
