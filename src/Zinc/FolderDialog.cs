using System.Runtime.InteropServices;

namespace Zinc;

/// <summary>
/// Windows' folder dialog (IFileOpenDialog with FOS_PICKFOLDERS), which can open in any folder,
/// a WSL distro's share included. The WinRT FolderPicker can only start at a few known folders.
/// Modal: returns the chosen folder, or null if cancelled.
/// </summary>
internal static class FolderDialog
{
    private const uint PickFolders = 0x20, ForceFileSystem = 0x40, PathMustExist = 0x800;
    private const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH
    private const int Cancelled = unchecked((int)0x800704C7);

    public static string? Pick(nint owner, string? startIn, string title)
    {
        var dialog = (IFileDialog)new FileOpenDialog();
        try
        {
            dialog.SetOptions(PickFolders | ForceFileSystem | PathMustExist);
            dialog.SetTitle(title);
            if (startIn != null && Directory.Exists(startIn))
            {
                var iid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(startIn, 0, ref iid, out var folder);
                dialog.SetFolder(folder);
            }

            var shown = dialog.Show(owner);
            if (shown == Cancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(shown);
            dialog.GetResult(out var result);
            result.GetDisplayName(FileSystemPath, out var name);
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7"), ClassInterface(ClassInterfaceType.None)]
    private class FileOpenDialog;

    // IModalWindow's Show, then IFileDialog's methods in vtable order (only the used ones matter,
    // but every slot before the last one used must be declared).
    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint parent);
        void SetFileTypes(uint count, nint specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, ref Guid handler, ref Guid riid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
