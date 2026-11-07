using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DWS.Core.Storage;

public sealed partial class OfflineHiveEditor : IDisposable
{
    private const uint KEY_ALL_ACCESS = 0xF003F;
    private const uint REG_PROCESS_APPKEY = 0x00000001;

    private readonly SafeRegistryHandle _hiveHandle;
    private readonly RegistryKey _rootKey;
    private bool _disposed;

    private OfflineHiveEditor(SafeRegistryHandle handle, string hiveFilePath)
    {
        _hiveHandle = handle;
        _rootKey = RegistryKey.FromHandle(_hiveHandle);
        HiveFilePath = hiveFilePath;
    }

    public string HiveFilePath { get; }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegLoadAppKeyW(
        string lpFile,
        out SafeRegistryHandle phkResult,
        uint samDesired,
        uint dwOptions,
        uint reserved);

    public static OfflineHiveEditor OpenHive(string hiveFilePath)
    {
        if (!File.Exists(hiveFilePath))
        {
            throw new FileNotFoundException($"Hive file not found at: {hiveFilePath}", hiveFilePath);
        }

        int error = RegLoadAppKeyW(
            hiveFilePath,
            out SafeRegistryHandle hKeyResult,
            KEY_ALL_ACCESS,
            REG_PROCESS_APPKEY,
            0);

        if (error != 0 || hKeyResult.IsInvalid)
        {
            throw new Win32Exception(error, $"Failed to load private app hive for '{hiveFilePath}'.");
        }

        return new OfflineHiveEditor(hKeyResult, hiveFilePath);
    }

    public void SetDword(string subKeyPath, string valueName, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var key = _rootKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree)
            ?? throw new InvalidOperationException($"Failed to create or open subkey '{subKeyPath}' in offline hive '{HiveFilePath}'.");
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public void SetString(string subKeyPath, string valueName, string value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var key = _rootKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree)
            ?? throw new InvalidOperationException($"Failed to create or open subkey '{subKeyPath}' in offline hive '{HiveFilePath}'.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rootKey.Dispose();
        _hiveHandle.Dispose();
    }
}
