using System.Runtime.InteropServices;
using System.Text;
using Zinc.Shell;

namespace Zinc;

/// <summary>
/// Writes the taskbar button's jump list (with ICustomDestinationList, because the WinRT JumpList needs a packaged app).
/// Each item is a shell link to zinc.exe with LaunchRequest's flags. A click starts zinc.exe, which hands the flags to
/// the running Zinc. Items the user removed from the list come back from BeginList and must be left out, because Windows
/// refuses a category holding one.
/// </summary>
internal static class JumpListWriter
{
    /// <summary>
    /// Replaces the list. <paramref name="build"/> gets the arguments of the items the user removed (to leave out) and
    /// returns what the list holds. Returns false if Windows refused (no Explorer, say), and the old list stays.
    /// </summary>
    public static bool Write(string exe, Func<IReadOnlySet<string>, JumpListContent> build)
    {
        ICustomDestinationList? list = null;
        try
        {
            list = (ICustomDestinationList)new DestinationList();
            var arrayId = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref arrayId, out var removedObject);
            var removed = new HashSet<string>(StringComparer.Ordinal);
            if (removedObject is IObjectArray removedItems)
            {
                removedItems.GetCount(out var count);
                var linkId = typeof(IShellLinkW).GUID;
                for (uint i = 0; i < count; i++)
                {
                    removedItems.GetAt(i, ref linkId, out var item);
                    if (item is IShellLinkW link)
                    {
                        var args = new StringBuilder(1024);
                        link.GetArguments(args, args.Capacity);
                        removed.Add(args.ToString());
                    }
                }
            }

            var content = build(removed);
            foreach (var category in content.Categories)
            {
                if (Collection(exe, category.Items.Where(i => !removed.Contains(i.Arguments))) is { } items)
                {
                    list.AppendCategory(category.Title, items);
                }
            }

            if (content.Tasks.Count > 0)
            {
                list.AddUserTasks(Collection(exe, content.Tasks)!);
            }

            list.CommitList();
            return true;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Write($"jump list: not written ({ex.Message})");
            try
            {
                list?.AbortList();
            }
            catch (COMException)
            {
            }

            return false;
        }
    }

    /// <summary>The items as shell links in a collection (null if there are none).</summary>
    private static IObjectArray? Collection(string exe, IEnumerable<JumpItem> items)
    {
        var collection = (IObjectCollection)new EnumerableObjectCollection();
        var any = false;
        foreach (var item in items)
        {
            collection.AddObject(Link(exe, item));
            any = true;
        }

        return any ? (IObjectArray)collection : null;
    }

    private static IShellLinkW Link(string exe, JumpItem item)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetArguments(item.Arguments);
        link.SetIconLocation(item.Icon.Path ?? exe, item.Icon.Path == null ? 0 : item.Icon.Index);
        if (item.Description is { Length: > 0 } description)
        {
            link.SetDescription(description.Length > 259 ? description[..259] : description);
        }

        // The title the list shows is the link's PKEY_Title.
        var store = (IPropertyStore)link;
        var key = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
        var value = new PropVariant { Type = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(item.Title) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }

        return link;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Pointer;
    }

    [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"), ClassInterface(ClassInterfaceType.None)]
    private class DestinationList;

    [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"), ClassInterface(ClassInterfaceType.None)]
    private class EnumerableObjectCollection;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class ShellLink;

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? appId);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray(IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
