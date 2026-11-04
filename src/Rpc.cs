using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexQuotaFloat {
sealed class CodexRpc : IDisposable {
    readonly ConcurrentDictionary<int,TaskCompletionSource<object>> calls=new ConcurrentDictionary<int,TaskCompletionSource<object>>();
    readonly object writeGate=new object();
    readonly SemaphoreSlim connectGate=new SemaphoreSlim(1,1);
    Process process; int sequence;
    public Action<bool> Changed;
    public string ExplicitPath;
    public string Context { get { return Json.Hash(Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex")).TrimEnd('\\').ToUpperInvariant()); } }
    public static string FindCli(string configured) {
        if(!string.IsNullOrEmpty(configured) && File.Exists(configured))return configured;
        string bin=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(bin)) { var f=Directory.GetFiles(bin,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(f!=null)return f; }
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) { try {var p=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(p))return p;}catch{} }
        throw new FileNotFoundException("未找到 Codex CLI，请在菜单中指定 codex.exe。");
    }
    public async Task EnsureConnected() {
        await connectGate.WaitAsync();
        try {
            if(process!=null && !process.HasExited)return;
            DisposeProcess();var cli=FindCli(ExplicitPath);
            var si=new ProcessStartInfo(cli,"app-server --listen stdio://") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory };
            var child=new Process {StartInfo=si,EnableRaisingEvents=true};
            child.OutputDataReceived+=(sender,e)=>ReadLine(e.Data);
            child.ErrorDataReceived+=(sender,e)=>{}; // Never retain credential-bearing diagnostics.
            child.Exited+=(sender,e)=>{if(ReferenceEquals(process,child))FailPending(new IOException("Codex 查询连接已关闭。"));};
            process=child;child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
            try { await Call("initialize",new { clientInfo=new {name="codex_quota_float",title="Codex Quota Float",version=Program.Version} });Send(new{method="initialized"}); }
            catch {DisposeProcess();throw;}
        }finally{connectGate.Release();}
    }
    public async Task<Snapshot> Read() { await EnsureConnected();return Snapshot.Parse(await Call("account/rateLimits/read",new{})); }
    public Task<object> Consume(string key) {return Call("account/rateLimitResetCredit/consume",new{idempotencyKey=key});}
    void ReadLine(string line) {
        if(string.IsNullOrWhiteSpace(line))return;
        try {
            var obj=Json.Read(line);var id=Json.Num(obj,"id");
            if(id.HasValue) { TaskCompletionSource<object> completion;
                if(calls.TryRemove((int)id.Value,out completion)) { if(Json.Get(obj,"error")!=null)completion.TrySetException(new IOException("Codex 服务拒绝请求（"+Json.Num(Json.Get(obj,"error"),"code")+"），请检查登录或稍后重试。"));else completion.TrySetResult(Json.Get(obj,"result")); }
                else if(Json.Str(obj,"method")!="")Send(new{id=(int)id.Value,error=new{code=-32601,message="This quota-only client does not support this request."}});
            }else{var method=Json.Str(obj,"method");if(Changed!=null && (method=="account/rateLimits/updated" || method=="account/updated"))Changed(method=="account/updated");}
        }catch { /* Ignore malformed/unrelated notifications, never log raw protocol data. */ }
    }
    void Send(object obj) { lock(writeGate) { if(process==null || process.HasExited)throw new IOException("Codex 查询连接不可用。");var bytes=Encoding.UTF8.GetBytes(Json.Write(obj)+"\n");var stream=process.StandardInput.BaseStream;stream.Write(bytes,0,bytes.Length);stream.Flush(); } }
    async Task<object> Call(string method,object parameters) {
        int id=Interlocked.Increment(ref sequence);var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);calls[id]=completion;
        try { Send(new{id=id,method=method,@params=parameters});if(await Task.WhenAny(completion.Task,Task.Delay(15000))!=completion.Task)throw new TimeoutException("查询超时，请稍后重试。");return await completion.Task; }
        finally{TaskCompletionSource<object> removed;calls.TryRemove(id,out removed);}
    }
    void FailPending(Exception error) { foreach(var pair in calls){TaskCompletionSource<object> c;if(calls.TryRemove(pair.Key,out c))c.TrySetException(error);} }
    void DisposeProcess() {
        var p=process;process=null;FailPending(new IOException("查询连接已结束。"));if(p==null)return;
        try {if(!p.HasExited){p.StandardInput.Close();if(!p.WaitForExit(800))p.Kill();}}catch{}finally{p.Dispose();}
    }
    public void Dispose() { DisposeProcess(); }
}
}
