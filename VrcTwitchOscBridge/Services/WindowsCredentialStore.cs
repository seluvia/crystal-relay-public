using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Thin wrapper around Windows Credential Manager.
/// Crystal Relay uses this for OAuth tokens and the VRChat auth cookie so secrets stay out of normal JSON files.
/// </summary>
public sealed class WindowsCredentialStore
{
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int NotFoundError = 1168;

    // Load returns an empty string when the secret is missing so callers can treat
    // "not found" as a normal state instead of an exception.
    public string LoadSecret(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return string.Empty;
        }

        if (!CredRead(targetName, GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFoundError)
            {
                return string.Empty;
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    // Save overwrites or removes the Windows credential entry for the requested target.
    public void SaveSecret(string targetName, string value)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("A credential target name is required.", nameof(targetName));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            DeleteSecret(targetName);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var blobPointer = IntPtr.Zero;

        try
        {
            blobPointer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, blobPointer, bytes.Length);

            var credential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPointer,
                Persist = LocalMachinePersistence,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = null,
                TargetAlias = null,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (blobPointer != IntPtr.Zero)
            {
                ZeroMemory(blobPointer, bytes.Length);
                Marshal.FreeCoTaskMem(blobPointer);
            }
        }
    }

    // Delete is safe to call even when the credential does not exist.
    public void DeleteSecret(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        if (!CredDelete(targetName, GenericCredentialType, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFoundError)
            {
                throw new Win32Exception(error);
            }
        }
    }

    // Clears the unmanaged memory buffer before free so secret bytes do not linger longer than needed.
    private static void ZeroMemory(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return;
        }

        var zeros = new byte[length];
        Marshal.Copy(zeros, 0, pointer, length);
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
