using System;
using System.Runtime.InteropServices;

namespace Unlimotion;

internal static class LibGit2Interop
{
    private const int GitOptSetOwnerValidation = 36;

    [DllImport("git2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_init();

    [DllImport("git2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_opts(int option, __arglist);

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
}
