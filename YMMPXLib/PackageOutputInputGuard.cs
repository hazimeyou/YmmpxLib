using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YmmpxLib;

internal sealed class PackageOutputInputGuard
{
    private readonly string outputPath;
    private readonly string[] inputPaths;
    private readonly FileIdentity? originalOutput;

    private PackageOutputInputGuard(string outputPath, string[] inputPaths)
    {
        this.outputPath = outputPath;
        this.inputPaths = inputPaths;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (inputPaths.Any(input => string.Equals(input, outputPath, comparison)))
            throw InputCollision();

        originalOutput = Identify(outputPath);
        if (originalOutput is { } existingOutput)
            CheckInputIdentities(existingOutput);
    }

    internal static PackageOutputInputGuard Create(
        string outputPath, string projectPath, IEnumerable<string> referencedInputPaths)
    {
        // A Windows directory may use case-sensitive names even though the lexical guard is conservative.
        var inputs = referencedInputPaths.Prepend(projectPath).Distinct(StringComparer.Ordinal).ToArray();
        return new PackageOutputInputGuard(outputPath, inputs);
    }

    // Return whether the original, unrelated destination can be replaced.
    internal bool Validate()
    {
        var output = Identify(outputPath);
        if (output is not { } existingOutput)
        {
            if (originalOutput is not null)
                throw OutputChanged();
            return false;
        }

        CheckInputIdentities(existingOutput);
        // Repeat the destination probe after the input scan to limit the check-to-move window.
        if (output != originalOutput || Identify(outputPath) != output)
            throw OutputChanged();

        return true;
    }

    private void CheckInputIdentities(FileIdentity output)
    {
        foreach (var input in inputPaths)
        {
            if (Identify(input) == output)
                throw InputCollision();
        }
    }

    private static ArgumentException InputCollision() =>
        new("Output path must be different from the project file and every referenced input file.", "outputPath");

    private static IOException OutputChanged() =>
        new("The package output file changed while the package was being created.");

    // Follow links (including aliases in parent directories) and compare file identities, not file contents.
    // Failure to determine identity never authorizes overwriting an existing output.
    private static FileIdentity? Identify(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return IdentifyWindows(path);
            if (OperatingSystem.IsLinux())
                return IdentifyLinux(path);
            if (OperatingSystem.IsMacOS())
                return IdentifyMacOS(path);
        }
        catch (EntryPointNotFoundException)
        {
            // A runtime or C library may lack the OS identity API.
        }
        catch (DllNotFoundException)
        {
        }

        return IdentifyWithoutNativeIdentity(path);
    }

    private static FileIdentity? IdentifyWithoutNativeIdentity(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        throw new IOException($"File identity is unavailable for '{path}'.");
    }

    private static FileIdentity? IdentifyWindows(string path)
    {
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        const int fileIdInfo = 18;
        using var handle = OpenWindowsFile(
            ToWindowsApiPath(path), 0, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            const int fileNotFound = 2;
            const int pathNotFound = 3;
            const int invalidName = 123;
            const int invalidDirectory = 267;
            return error is fileNotFound or pathNotFound or invalidName or invalidDirectory
                ? null
                : throw IdentityError(path, error);
        }

        if (!GetFileInformationByHandleEx(handle, fileIdInfo, out var identity, (uint)Marshal.SizeOf<FileIdentity>()))
            throw IdentityError(path, Marshal.GetLastPInvokeError());
        return VerifyIdentity(path, identity);
    }

    private static string ToWindowsApiPath(string path)
    {
        // Match the extended-path conditions used by File.Move on Windows.
        if ((path.Length < 260 && !path.EndsWith('.') && !path.EndsWith(' ')) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    private static FileIdentity? IdentifyLinux(string path)
    {
        const int currentWorkingDirectory = -100;
        const uint inodeMask = 0x100;
        if (StatLinux(currentWorkingDirectory, path, 0, inodeMask, out var file) != 0)
        {
            const int notImplemented = 38;
            var error = Marshal.GetLastPInvokeError();
            return error == notImplemented ? IdentifyWithoutNativeIdentity(path) : ResolveUnixError(path, error);
        }

        if ((file.Mask & inodeMask) == 0)
            throw new IOException($"The file system did not report an inode for '{path}'.");

        var device = ((ulong)file.DeviceMajor << 32) | file.DeviceMinor;
        return VerifyIdentity(path, new FileIdentity(device, file.Inode, 0));
    }

    private static FileIdentity? IdentifyMacOS(string path)
    {
        const uint deviceId = 0x00000002;
        const uint fileId = 0x02000000;
        var requested = new MacOSAttributeList { BitmapCount = 5, CommonAttributes = deviceId | fileId };
        var size = (uint)Marshal.SizeOf<MacOSFileAttributes>();
        if (GetMacOSAttributes(path, in requested, out var file, size, 0) != 0)
            return ResolveUnixError(path, Marshal.GetLastPInvokeError());
        if (file.Length != size)
            throw new IOException($"The file system did not report complete file identity for '{path}'.");

        return VerifyIdentity(path, new FileIdentity(file.Device, file.Inode, 0));
    }

    private static FileIdentity? ResolveUnixError(string path, int error)
    {
        const int noEntry = 2;
        const int notDirectory = 20;
        return error is noEntry or notDirectory ? null : throw IdentityError(path, error);
    }

    private static FileIdentity VerifyIdentity(string path, FileIdentity identity)
    {
        if (identity.Low == 0 && identity.High == 0)
            throw new IOException($"The file system did not report a usable file ID for '{path}'.");

        return identity;
    }

    private static IOException IdentityError(string path, int error) =>
        new($"Unable to verify file identity for '{path}'.", new Win32Exception(error));

    // Native layouts: FILE_ID_INFO, Linux struct statx and macOS getattrlist(2).
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileIdentity(ulong Device, ulong Low, ulong High);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStatus
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct MacOSAttributeList
    {
        [FieldOffset(0)] internal ushort BitmapCount;
        [FieldOffset(4)] internal uint CommonAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacOSFileAttributes
    {
        internal uint Length;
        internal uint Device;
        internal ulong Inode;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle OpenWindowsFile(
        string path, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int informationClass, out FileIdentity identity, uint bufferSize);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int StatLinux(
        int directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out LinuxFileStatus status);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getattrlist", SetLastError = true)]
    private static extern int GetMacOSAttributes(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        in MacOSAttributeList attributes,
        out MacOSFileAttributes buffer,
        nuint bufferSize,
        nuint options);
}
