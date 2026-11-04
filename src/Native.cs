using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CodexQuotaFloat {
static class Native {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct PROCESSENTRY32 { public uint dwSize,cntUsage,th32ProcessID;public IntPtr th32DefaultHeapID;public uint th32ModuleID,cntThreads,th32ParentProcessID;public int pcPriClassBase;public uint dwFlags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string szExeFile; }
    public static bool IsCodexGui(string path) {
        if(string.IsNullOrEmpty(path))return false;
        string name=Path.GetFileName(path);
        return (name.Equals("ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("Codex.exe",StringComparison.OrdinalIgnoreCase)) &&
          (path.IndexOf("\\WindowsApps\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0 || path.IndexOf("\\OpenAI\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0 && path.IndexOf("\\bin\\",StringComparison.OrdinalIgnoreCase)<0);
    }
    public static bool HostRunning() {return HostInstance()!=null;}
    public static string HostInstance() {
        var parents=new Dictionary<int,int>();var h=CreateToolhelp32Snapshot(2,0);
        if(h!=new IntPtr(-1))try{var e=new PROCESSENTRY32();e.dwSize=(uint)Marshal.SizeOf(e);if(Process32First(h,ref e))do{parents[(int)e.th32ProcessID]=(int)e.th32ParentProcessID;}while(Process32Next(h,ref e));}finally{CloseHandle(h);}
        var ids=new Dictionary<int,long>();
        int session=Process.GetCurrentProcess().SessionId;
        foreach(string name in new[]{"ChatGPT","Codex"})foreach(var p in Process.GetProcessesByName(name))using(p)try{if(p.SessionId==session&&IsCodexGui(p.MainModule.FileName))ids[p.Id]=p.StartTime.ToUniversalTime().Ticks;}catch{}
        var roots=new List<string>();foreach(var pair in ids){int parent;if(!parents.TryGetValue(pair.Key,out parent)||!ids.ContainsKey(parent))roots.Add(pair.Key+":"+pair.Value);}
        roots.Sort(StringComparer.Ordinal);return roots.Count==0?null:string.Join(";",roots);
    }
    public static void NoActivate(Window w) {var h=new WindowInteropHelper(w).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x08000000|0x80);}
    public static bool OurForeground() {uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);return pid==(uint)Process.GetCurrentProcess().Id;}
    static IntPtr lastCodexWindow;
    static uint lastCodexPid;
    static uint cachedForegroundPid;
    static bool cachedIsCodex;
    static DateTime cachedAt;
    public static FocusState Focus() {
        IntPtr foreground=GetForegroundWindow();uint pid;GetWindowThreadProcessId(foreground,out pid);
        bool isCodex=false;
        if(pid!=0) {
            if(pid!=cachedForegroundPid || (DateTime.UtcNow-cachedAt).TotalSeconds>=1) {
                cachedForegroundPid=pid;cachedAt=DateTime.UtcNow;cachedIsCodex=false;
                try{using(var p=Process.GetProcessById((int)pid))cachedIsCodex=IsCodexGui(p.MainModule.FileName);}catch{}
            }
            isCodex=cachedIsCodex;
        }
        var root=foreground==IntPtr.Zero?IntPtr.Zero:GetAncestor(foreground,2);
        if(isCodex&&root!=IntPtr.Zero){lastCodexWindow=root;lastCodexPid=pid;}
        uint owner;GetWindowThreadProcessId(lastCodexWindow,out owner);
        bool available=lastCodexWindow!=IntPtr.Zero&&owner==lastCodexPid&&IsWindow(lastCodexWindow)&&IsWindowVisible(lastCodexWindow)&&!IsIconic(lastCodexWindow);
        return new FocusState{CodexForeground=isCodex&&available,CodexVisible=available,OwnForeground=pid==(uint)Process.GetCurrentProcess().Id};
    }
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    public static bool StartupEnabled {get{using(var k=Registry.CurrentUser.OpenSubKey(RunKey))return File.Exists(StartupRegistration.ShortcutPath)||k!=null && k.GetValue("CodexQuotaFloat")!=null;}}
    public static void SetStartup(bool enabled) {
        if(enabled)StartupRegistration.Install(System.Reflection.Assembly.GetExecutingAssembly().Location);
        else StartupRegistration.Remove();
        using(var k=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)k.SetValue("CodexQuotaFloat","\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --watch --startup");else k.DeleteValue("CodexQuotaFloat",false);}
        Lifecycle.Write(enabled?"startup-installed":"startup-removed",enabled?"Run + StartupFolder":"");
    }
    public static Process Launch(string arguments) {return Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,arguments){UseShellExecute=false,CreateNoWindow=true});}
}
sealed class HostGrace {
    DateTime? gone;
    public bool ShouldExit(bool present,DateTime now) {if(present){gone=null;return false;}if(!gone.HasValue)gone=now;return (now-gone.Value).TotalSeconds>=5;}
}
sealed class FocusState {public bool CodexForeground,CodexVisible,OwnForeground;}
static class ForegroundPolicy {
    public static bool Show(FocusState focus,bool interacting,bool wasShowing) {
        return focus.CodexVisible&&(focus.CodexForeground||focus.OwnForeground&&interacting&&wasShowing);
    }
}
}
