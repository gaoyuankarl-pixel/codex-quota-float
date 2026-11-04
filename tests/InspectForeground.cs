using System;
using System.Diagnostics;
using System.Reflection;
class InspectForeground {
 static int Main(string[] args) {
  var assembly=Assembly.LoadFrom(args[0]);var native=assembly.GetType("CodexQuotaFloat.Native");
  var window=native.GetMethod("GetForegroundWindow").Invoke(null,null);var parameters=new object[]{window,(uint)0};
  native.GetMethod("GetWindowThreadProcessId").Invoke(null,parameters);
  uint pid=(uint)parameters[1];Console.WriteLine("Foreground PID: "+pid);
  if(pid!=0)try{using(var p=Process.GetProcessById((int)pid))Console.WriteLine("Foreground process: "+p.ProcessName);}catch{}
  var focus=native.GetMethod("Focus").Invoke(null,null);
  foreach(var field in focus.GetType().GetFields())Console.WriteLine(field.Name+": "+field.GetValue(focus));
  return 0;
 }
}
