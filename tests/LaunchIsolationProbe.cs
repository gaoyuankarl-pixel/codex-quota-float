using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class LaunchIsolationProbe {
 [StructLayout(LayoutKind.Sequential)] struct BasicLimits {public long PerProcess,PerJob;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong A,B,C,D,E,F;}
 [StructLayout(LayoutKind.Sequential)] struct Limits {public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {public int Size;public string Reserved,Desktop,Title;public int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;public short Show,Reserved2;public IntPtr ReservedPtr,In,Out,Error;}
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr Process,Thread;public int Id,ThreadId;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int type,ref Limits limits,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inJob);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string exe,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo info);
 [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
 [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 static void Require(bool ok,string reason){if(!ok)throw new Exception(reason+" ("+Marshal.GetLastWin32Error()+")");}
 static int Main(string[] args) {
  IntPtr job=IntPtr.Zero;ProcessInfo child=new ProcessInfo();
  try {
   string exe=args[0];job=CreateJobObject(IntPtr.Zero,null);Require(job!=IntPtr.Zero,"create test-only cleanup job");
   var limits=new Limits();limits.Basic.Flags=0x2000;Require(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(limits)),"configure kill-on-close");
   var startup=new Startup();startup.Size=Marshal.SizeOf(startup);
   Require(CreateProcess(exe,new StringBuilder("\""+exe+"\" --install"),IntPtr.Zero,IntPtr.Zero,false,0x08000004,IntPtr.Zero,Path.GetDirectoryName(exe),ref startup,out child),"create isolated installer");
   Require(AssignProcessToJobObject(job,child.Process),"assign isolated installer");ResumeThread(child.Thread);
   Require(WaitForSingleObject(child.Process,15000)==0,"installer completion");uint code;GetExitCodeProcess(child.Process,out code);Require(code==0,"installer succeeded");
   Process watcher=null;
   for(int i=0;i<40&&watcher==null;i++){watcher=Process.GetProcessesByName("CodexQuotaFloat").FirstOrDefault(p=>{try{bool inside;return p.MainModule.FileName.Equals(exe,StringComparison.OrdinalIgnoreCase)&&IsProcessInJob(p.Handle,job,out inside)&&!inside;}catch{return false;}});if(watcher==null)Thread.Sleep(200);}
   Require(watcher!=null,"watcher exists outside installer cleanup job");
   int pid=watcher.Id;CloseHandle(job);job=IntPtr.Zero;Thread.Sleep(1200);
   using(var survivor=Process.GetProcessById(pid))Require(!survivor.HasExited,"watcher survived launcher-tree cleanup");
   Console.WriteLine("PASS: installer ran in kill-on-close job; independent process "+pid+" was outside that job and survived job closure.");return 0;
  }catch(Exception e){Console.WriteLine("FAIL: "+e.Message);return 1;}
  finally{if(child.Thread!=IntPtr.Zero)CloseHandle(child.Thread);if(child.Process!=IntPtr.Zero){uint code;GetExitCodeProcess(child.Process,out code);if(code==259)TerminateProcess(child.Process,1);CloseHandle(child.Process);}if(job!=IntPtr.Zero)CloseHandle(job);}
 }
}
