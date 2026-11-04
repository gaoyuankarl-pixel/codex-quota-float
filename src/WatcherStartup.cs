using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CodexQuotaFloat {
// Use the desktop view's Application, which lives inside Explorer. A newly
// created Shell.Application alone may execute in-process and inherit Codex's job.
static class WatcherStartup {
    static void Release(object value){if(value!=null&&Marshal.IsComObject(value))try{Marshal.ReleaseComObject(value);}catch(InvalidComObjectException){}}
    public static void Start() {
        dynamic shell=null,windows=null,desktop=null,view=null,application=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=shell.Windows();
            object location=0,root=0;int hwnd=0;
            desktop=windows.FindWindowSW(ref location,ref root,8,ref hwnd,1);
            if(desktop==null)throw new InvalidOperationException(L10n.T("Windows 桌面尚未就绪，无法独立启动监听器。"));
            view=desktop.Document;application=view.Application;
            string exe=Assembly.GetExecutingAssembly().Location;
            application.ShellExecute(exe,"--watch",Path.GetDirectoryName(exe),"open",0);
        }finally{Release(application);Release(view);Release(desktop);Release(windows);Release(shell);}
    }
}
enum WatchAction {None,Start,Stop}
sealed class WatchPolicy {
    string previousHost;
    bool dismissed;
    public WatchAction Update(string host,bool widgetAlive,bool dismiss) {
        if(host!=previousHost){previousHost=host;dismissed=false;}
        if(dismiss&&host!=null)dismissed=true;
        if(host==null)return widgetAlive?WatchAction.Stop:WatchAction.None;
        return !widgetAlive&&!dismissed?WatchAction.Start:WatchAction.None;
    }
}
}
