using System;
using System.Runtime.InteropServices;

namespace Unlimotion;

public static class LibGit2Interop
{
    private const int GitOptSetOwnerValidation = 36;
    private const int GitOptSetSslCertLocations = 12;

    [DllImport("git2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_init();

    [DllImport("git2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_opts(int option, __arglist);

    [DllImport("git2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_opts(int option, IntPtr value1, IntPtr value2);

    internal static void DisableOwnerValidationOnAndroid()
    {
#if ANDROID
        try
        {
            git_libgit2_init();
            git_libgit2_opts(GitOptSetOwnerValidation, __arglist(0));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (Exception)
        {
        }
#endif
    }

    public static void SetSslCertificateLocations(string? certFile, string? certPath)
    {
#if ANDROID
        IntPtr filePtr = IntPtr.Zero;
        IntPtr pathPtr = IntPtr.Zero;

        try
        {
            git_libgit2_init();

            if (!string.IsNullOrWhiteSpace(certFile))
            {
                filePtr = Marshal.StringToHGlobalAnsi(certFile);
            }

            if (!string.IsNullOrWhiteSpace(certPath))
            {
                pathPtr = Marshal.StringToHGlobalAnsi(certPath);
            }

            git_libgit2_opts(GitOptSetSslCertLocations, filePtr, pathPtr);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            if (filePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filePtr);
            }

            if (pathPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pathPtr);
            }
        }
#endif
    }
}
