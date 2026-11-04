using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CodexQuotaFloat {
static class StartupRegistration {
    public static string ShortcutPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"CodexQuotaFloat.lnk");}}
    public static string PowerShell {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");}}
    public static string Bootstrap(string exe) {
        string literal="'"+exe.Replace("'","''")+"'";
        return "$p="+literal+";1..24|%{if(Test-Path -LiteralPath $p){try{$c=Start-Process $p -ArgumentList '--watch','--startup' -WindowStyle Hidden -PassThru -ErrorAction Stop;if(-not $c.WaitForExit(1500) -or $c.ExitCode -eq 0){exit}}catch{}};Start-Sleep 5}";
    }
    public static string Arguments(string exe) {
        string encoded="-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(Bootstrap(exe)));
        return encoded.Length<=1000?encoded:"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command \""+Bootstrap(exe)+"\"";
    }
    public static void Install(string exe) {
        dynamic shell=null,link=null;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));link=shell.CreateShortcut(ShortcutPath);
            link.TargetPath=PowerShell;link.Arguments=Arguments(exe);link.WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.Windows);link.WindowStyle=7;link.Description=L10n.T("Codex 额度浮窗：登录后等待程序目录就绪并启动监听器");link.IconLocation=exe+",0";link.Save();
            if(!File.Exists(ShortcutPath)||!string.Equals((string)link.Arguments,Arguments(exe),StringComparison.Ordinal))throw new IOException(L10n.T("Windows 启动文件夹入口保存失败或参数不完整。"));
        }finally{if(link!=null&&Marshal.IsComObject(link))Marshal.ReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}
    }
    public static void Remove() {if(File.Exists(ShortcutPath))File.Delete(ShortcutPath);}
}
static class Lifecycle {
    public static void Write(string phase,string detail) {
        try {using(var mutex=new Mutex(false,"Local\\CodexQuotaFloat.Log")) {
            bool held=false;try{try{held=mutex.WaitOne(300);}catch(AbandonedMutexException){held=true;}if(!held)return;
                Directory.CreateDirectory(Preferences.Root);string path=Path.Combine(Preferences.Root,"lifecycle.log");
                if(File.Exists(path)&&new FileInfo(path).Length>65536)File.WriteAllText(path,"");
                using(var p=Process.GetCurrentProcess())File.AppendAllText(path,DateTimeOffset.Now.ToString("O")+" v"+Program.Version+" pid="+p.Id+" session="+p.SessionId+" "+phase+" "+detail+Environment.NewLine,Encoding.UTF8);
            }finally{if(held)mutex.ReleaseMutex();}
        }}catch{}
    }
}
}
