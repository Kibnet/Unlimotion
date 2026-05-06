using System;
using System.Runtime.InteropServices;
using LibGit2Sharp;

namespace Unlimotion.Services;

internal sealed class SshPrivateKeyCredentials : Credentials
{
    public SshPrivateKeyCredentials(
        string username,
        string privateKeyPath,
        string? publicKeyPath,
        string? passphrase = null)
    {
        Username = username;
        PrivateKeyPath = privateKeyPath;
        PublicKeyPath = publicKeyPath;
        Passphrase = passphrase;
    }

    public string Username { get; }
    public string PrivateKeyPath { get; }
    public string? PublicKeyPath { get; }
    public string? Passphrase { get; }

    protected override int GitCredentialHandler(out IntPtr credential)
    {
        credential = IntPtr.Zero;

        using var username = NativeUtf8String.From(Username);
        using var publicKeyPath = NativeUtf8String.From(PublicKeyPath);
        using var privateKeyPath = NativeUtf8String.From(PrivateKeyPath);
        using var passphrase = NativeUtf8String.From(Passphrase);

        return git_credential_ssh_key_new(
            out credential,
            username.Pointer,
            publicKeyPath.Pointer,
            privateKeyPath.Pointer,
            passphrase.Pointer);
    }

    [DllImport(LibGit2Interop.NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_credential_ssh_key_new(
        out IntPtr credential,
        IntPtr username,
        IntPtr publicKey,
        IntPtr privateKey,
        IntPtr passphrase);

    private sealed class NativeUtf8String : IDisposable
    {
        private NativeUtf8String(IntPtr pointer)
        {
            Pointer = pointer;
        }

        public IntPtr Pointer { get; private set; }

        public static NativeUtf8String From(string? value)
        {
            return new NativeUtf8String(string.IsNullOrEmpty(value)
                ? IntPtr.Zero
                : Marshal.StringToCoTaskMemUTF8(value));
        }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeCoTaskMem(Pointer);
            Pointer = IntPtr.Zero;
        }
    }
}
