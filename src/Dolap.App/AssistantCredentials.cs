using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Dolap.App;

// Secrets stay in Windows Credential Manager, outside JSON, backups, logs and the model request body.
internal static class AssistantCredentials
{
    const string Target = "MasaustuDolabi/OpenRouter";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public uint Flags, Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist, AttributeCount;
        public IntPtr Attributes; public string? TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDelete(string target, uint type, uint flags);
    public static void Save(string key)
    {
        key = key.Trim(); if (key.Length is < 10 or > 2000) throw new InvalidOperationException("API anahtarı geçersiz.");
        var blob = Marshal.StringToCoTaskMemUni(key);
        try
        {
            var c = new Credential { Type = 1, TargetName = Target, UserName = "OpenRouter", CredentialBlob = blob, CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(key), Persist = 2 };
            if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public static string Read()
    {
        if (!CredRead(Target, 1, 0, out var pointer)) return "";
        try { var c = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2) ?? ""; }
        finally { CredFree(pointer); }
    }
    public static void Delete() => CredDelete(Target, 1, 0);
}
