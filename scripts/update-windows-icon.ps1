param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$IconPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$inputExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$inputIcon = (Resolve-Path -LiteralPath $IconPath).Path
$outputExecutable = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if ($inputExecutable -eq $outputExecutable) { throw 'Use a separate output file so the original remains available' }
if (Test-Path -LiteralPath $outputExecutable) { throw 'Output already exists; choose a new output path' }
if ((Get-AuthenticodeSignature -LiteralPath $inputExecutable).SignerCertificate) { throw 'Rebuild signed applications instead of modifying their resources' }

Add-Type @'
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class EasyagIconResources {
    delegate bool NamesCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr state);
    delegate bool LanguagesCallback(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr state);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", EntryPoint="EnumResourceNamesW", SetLastError=true)] static extern bool EnumNames(IntPtr module, IntPtr type, NamesCallback callback, IntPtr state);
    [DllImport("kernel32.dll", EntryPoint="EnumResourceLanguagesW", SetLastError=true)] static extern bool EnumLanguages(IntPtr module, IntPtr type, IntPtr name, LanguagesCallback callback, IntPtr state);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr BeginUpdateResource(string path, bool deleteExisting);
    [DllImport("kernel32.dll", EntryPoint="UpdateResourceW", SetLastError=true)] static extern bool Update(IntPtr handle, IntPtr type, IntPtr name, ushort language, byte[] bytes, uint size);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool EndUpdateResource(IntPtr handle, bool discard);
    class Resource { public ushort Id; public string Name; public ushort Language; }
    static Exception Failure(string operation) { return new Win32Exception(Marshal.GetLastWin32Error(), operation); }
    static List<Resource> Resources(IntPtr module, ushort type) {
        var result = new List<Resource>();
        NamesCallback names = delegate(IntPtr m, IntPtr t, IntPtr n, IntPtr s) {
            bool numeric = ((ulong)n.ToInt64() >> 16) == 0;
            ushort id = numeric ? (ushort)n.ToInt64() : (ushort)0;
            string name = numeric ? null : Marshal.PtrToStringUni(n);
            LanguagesCallback languages = delegate(IntPtr lm, IntPtr lt, IntPtr ln, ushort language, IntPtr ls) {
                result.Add(new Resource { Id=id, Name=name, Language=language }); return true;
            };
            if (!EnumLanguages(m, t, n, languages, IntPtr.Zero)) throw Failure("Enumerating resource languages");
            return true;
        };
        if (!EnumNames(module, new IntPtr(type), names, IntPtr.Zero)) throw Failure("Enumerating icon resources");
        return result;
    }
    static void Write(IntPtr handle, ushort type, Resource resource, byte[] bytes) {
        IntPtr name = resource.Name == null ? new IntPtr(resource.Id) : Marshal.StringToHGlobalUni(resource.Name);
        try {
            if (!Update(handle, new IntPtr(type), name, resource.Language, bytes, bytes == null ? 0 : (uint)bytes.Length)) throw Failure("Updating icon resource");
        } finally { if (resource.Name != null) Marshal.FreeHGlobal(name); }
    }
    public static int Replace(string executable, byte[] ico) {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1) throw new InvalidDataException("Invalid ICO header");
        ushort count = BitConverter.ToUInt16(ico, 4);
        if (count == 0 || count > 256 || ico.Length < 6 + count * 16) throw new InvalidDataException("Invalid ICO entry count");
        var frames = new List<byte[]>();
        byte[] group = new byte[6 + count * 14];
        Array.Copy(ico, group, 6);
        for (int index=0; index<count; index++) {
            int source = 6 + index * 16;
            uint length = BitConverter.ToUInt32(ico, source + 8), offset = BitConverter.ToUInt32(ico, source + 12);
            if ((long)offset + length > ico.Length) throw new InvalidDataException("ICO frame outside file");
            var frame = new byte[length]; Array.Copy(ico, (int)offset, frame, 0, (int)length); frames.Add(frame);
            Array.Copy(ico, source, group, 6 + index * 14, 12);
            Array.Copy(BitConverter.GetBytes((ushort)(1000 + index)), 0, group, 6 + index * 14 + 12, 2);
        }
        IntPtr module = LoadLibraryEx(executable, IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) throw Failure("Opening executable resources");
        List<Resource> groups, oldIcons;
        try { groups = Resources(module, 14); oldIcons = Resources(module, 3); }
        finally { FreeLibrary(module); }
        if (groups.Count == 0) throw new InvalidDataException("Executable has no icon groups");
        IntPtr update = BeginUpdateResource(executable, false);
        if (update == IntPtr.Zero) throw Failure("Starting resource update");
        try {
            foreach (var old in oldIcons) Write(update, 3, old, null);
            var languages = new HashSet<ushort>();
            foreach (var existing in groups) languages.Add(existing.Language);
            foreach (ushort language in languages) {
                for (int index=0; index<count; index++) Write(update, 3, new Resource { Id=(ushort)(1000+index), Language=language }, frames[index]);
            }
            foreach (var existing in groups) Write(update, 14, existing, group);
            if (!EndUpdateResource(update, false)) throw Failure("Saving resource update");
            update = IntPtr.Zero;
        } finally { if (update != IntPtr.Zero) EndUpdateResource(update, true); }
        return groups.Count;
    }
}
'@

Copy-Item -LiteralPath $inputExecutable -Destination $outputExecutable
$updatedGroups = [EasyagIconResources]::Replace($outputExecutable, [IO.File]::ReadAllBytes($inputIcon))

# PE executable code must remain byte-identical after a resource-only update.
function Read-ExecutableCode([string]$File) {
    $bytes = [IO.File]::ReadAllBytes($File)
    $pe = [BitConverter]::ToInt32($bytes, 60)
    $sections = [BitConverter]::ToUInt16($bytes, $pe + 6)
    $table = $pe + 24 + [BitConverter]::ToUInt16($bytes, $pe + 20)
    for ($index=0; $index -lt $sections; $index++) {
        $section = $table + $index * 40
        $name = [Text.Encoding]::ASCII.GetString($bytes, $section, 8).Trim([char]0)
        if ($name -eq '.text') {
            $size = [BitConverter]::ToInt32($bytes, $section + 16)
            $offset = [BitConverter]::ToInt32($bytes, $section + 20)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { return [BitConverter]::ToString($sha.ComputeHash($bytes, $offset, $size)) } finally { $sha.Dispose() }
        }
    }
    throw 'Executable code section was not found'
}
if ((Read-ExecutableCode $inputExecutable) -ne (Read-ExecutableCode $outputExecutable)) { throw 'Executable code changed during icon update' }
[pscustomobject]@{Output=$outputExecutable;UpdatedIconGroups=$updatedGroups;ExecutableCodeUnchanged=$true} | ConvertTo-Json
