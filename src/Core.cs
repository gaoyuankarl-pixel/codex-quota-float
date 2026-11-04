using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuotaFloat {
static class Json {
    public static object Read(string s) { return new JavaScriptSerializer { MaxJsonLength = 8000000 }.DeserializeObject(s); }
    public static string Write(object o) { return new JavaScriptSerializer().Serialize(o); }
    public static object Get(object o, string key) { var d=o as IDictionary<string,object>; object v; return d!=null && d.TryGetValue(key,out v) ? v : null; }
    public static string Str(object o,string k) { return Convert.ToString(Get(o,k),CultureInfo.InvariantCulture); }
    public static double? Num(object o,string k) { var v=Get(o,k); double n; return v!=null && double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Any,CultureInfo.InvariantCulture,out n) ? (double?)n : null; }
    public static string Hash(string s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-",""); }
}
sealed class QuotaWindow {
    public string Label;
    public double Remaining;
    public long? DurationMinutes, ResetSeconds;
    public string DisplayLabel {get{return DurationMinutes==300?L10n.T("5 小时"):DurationMinutes==10080?L10n.T("1 周"):DurationMinutes.HasValue?DurationMinutes+L10n.T(" 分钟窗口"):L10n.T("额度窗口");}}
    public string ResetLabel(DateTimeOffset now) {
        if(!ResetSeconds.HasValue) return L10n.T("恢复时间未提供");
        var at=DateTimeOffset.FromUnixTimeSeconds(ResetSeconds.Value).ToLocalTime();
        if(at<=now) return L10n.T("等待服务确认恢复");
        if(DurationMinutes==300) return at.ToString("HH:mm")+L10n.T(" 恢复");
        return at.ToString(L10n.T("M月d日 HH:mm"),CultureInfo.InvariantCulture)+L10n.T(" 恢复");
    }
    public string Countdown(DateTimeOffset now) {
        if(!ResetSeconds.HasValue)return "";
        var span=DateTimeOffset.FromUnixTimeSeconds(ResetSeconds.Value)-now;
        if(span.TotalSeconds<=0)return L10n.T("等待刷新");
        if(span.TotalDays>=1)return ((int)span.TotalDays)+L10n.T("天 ")+span.Hours+L10n.T("小时后");
        return ((int)span.TotalHours).ToString("00")+":"+span.Minutes.ToString("00")+":"+span.Seconds.ToString("00")+L10n.T(" 后");
    }
    public string CardCountdown(DateTimeOffset refreshedAt) {
        if(!ResetSeconds.HasValue)return L10n.T("恢复时间未知");
        var span=DateTimeOffset.FromUnixTimeSeconds(ResetSeconds.Value)-refreshedAt;
        if(span.TotalSeconds<=0)return L10n.T("等待刷新");
        if(DurationMinutes==10080)return "-"+((int)span.TotalDays).ToString("00",CultureInfo.InvariantCulture)+" d "+span.Hours.ToString("00",CultureInfo.InvariantCulture)+" h";
        return "-"+((int)span.TotalHours).ToString("00",CultureInfo.InvariantCulture)+" h "+span.Minutes.ToString("00",CultureInfo.InvariantCulture)+" min";
    }
}
sealed class Snapshot {
    public List<QuotaWindow> Windows=new List<QuotaWindow>();
    public string Account, Plan;
    public int? ResetCount;
    public DateTimeOffset Observed;
    public bool Valid;
    public static Snapshot Parse(object response) {
        var q=new Snapshot { Account=Json.Str(response,"accountId"), Observed=DateTimeOffset.Now };
        object bucket=null;
        var map=Json.Get(response,"rateLimitsByLimitId") as IDictionary<string,object>;
        if(map!=null)map.TryGetValue("codex",out bucket);
        if(bucket==null) { var single=Json.Get(response,"rateLimits"); var id=Json.Str(single,"limitId"); if(id=="" || id=="codex")bucket=single; }
        if(bucket==null)throw new InvalidDataException(L10n.T("服务未提供 Codex 额度桶。"));
        q.Plan=Json.Str(bucket,"planType");
        foreach(var name in new[]{"primary","secondary"}) {
            var w=Json.Get(bucket,name); if(w==null)continue;
            double? used=Json.Num(w,"usedPercent"),duration=Json.Num(w,"windowDurationMins"),reset=Json.Num(w,"resetsAt");
            if(!used.HasValue || used<0 || used>100)throw new InvalidDataException(L10n.T("服务额度比例无效。"));
            long? mins=duration.HasValue?(long?)duration.Value:null;
            string label=mins==300?L10n.T("5 小时"):mins==10080?L10n.T("1 周"):mins.HasValue?mins+L10n.T(" 分钟窗口"):L10n.T("额度窗口");
            long? stamp=reset.HasValue?(long?)reset.Value:null;
            if(stamp.HasValue && (stamp<0 || stamp>253402300799))stamp=null;
            q.Windows.Add(new QuotaWindow{Label=label,Remaining=100-used.Value,DurationMinutes=mins,ResetSeconds=stamp});
        }
        q.Windows=q.Windows.OrderBy(w=>w.DurationMinutes??long.MaxValue).ToList();
        var count=Json.Num(Json.Get(response,"rateLimitResetCredits"),"availableCount");
        if(count.HasValue && count>=0 && count<=int.MaxValue)q.ResetCount=(int)count.Value;
        q.Valid=true; return q;
    }
}
sealed class Preferences {
    public string Language="en";
    public int LayoutRevision=0;
    public double Left=double.NaN, Top=double.NaN, IdleOpacity=.62;
    public bool Topmost=true;
    public string CliPath="";
    public static readonly string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
    static string PathName { get { return Path.Combine(Root,"settings.json"); } }
    public static Preferences Load() {
        try { return Parse(File.ReadAllText(PathName)); }catch{return new Preferences();}
    }
    public static Preferences Parse(string text) {
        var p=new JavaScriptSerializer().Deserialize<Preferences>(text);
        if(Json.Get(Json.Read(text),"Language")==null)p.Language="zh-CN";
        if(p.Language!="en"&&p.Language!="zh-CN")p.Language="en";
        p.IdleOpacity=Math.Max(.25,Math.Min(.95,p.IdleOpacity));return p;
    }
    public void Save() { Durable.Write(PathName,Json.Write(this)); }
}
static class Durable {
    public static void Write(string path,string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) { var b=Encoding.UTF8.GetBytes(text);f.Write(b,0,b.Length);f.Flush(true); }
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
sealed class ResetTicket { public string Key,Account,Context; }
sealed class ResetCoordinator {
    public ResetTicket Pending {get;private set;}
    readonly string path;
    int busy;
    public ResetCoordinator(string path) { this.path=path; if(File.Exists(path)) {
        Pending=new JavaScriptSerializer().Deserialize<ResetTicket>(File.ReadAllText(path));Guid g;
        if(Pending==null || !Guid.TryParse(Pending.Key,out g) || string.IsNullOrWhiteSpace(Pending.Account)||string.IsNullOrWhiteSpace(Pending.Context))throw new InvalidDataException(L10n.T("重置记录损坏，请保留 pending-reset.json。"));
    }}
    public string Reason(Snapshot s,string context) {
        if(busy!=0)return L10n.T("正在处理重置");
        if(s==null || !s.Valid || (DateTimeOffset.Now-s.Observed).TotalSeconds>120)return L10n.T("先刷新实时额度");
        if(string.IsNullOrEmpty(s.Account))return L10n.T("Codex 未返回账户标识");
        if(Pending!=null)return Pending.Account==Json.Hash(s.Account) && Pending.Context==context ? null : L10n.T("请切回上次重置的账户和数据目录");
        if(!s.ResetCount.HasValue)return L10n.T("未提供可用重置次数");
        return s.ResetCount>0?null:L10n.T("没有可用重置机会");
    }
    public async Task<string> Execute(Snapshot s,string context,Func<string,Task<object>> send) {
        var reason=Reason(s,context);if(reason!=null)throw new InvalidOperationException(reason);
        if(Interlocked.CompareExchange(ref busy,1,0)!=0)throw new InvalidOperationException(L10n.T("正在处理重置"));
        try {
            if(Pending==null) { var ticket=new ResetTicket { Key=Guid.NewGuid().ToString(),Account=Json.Hash(s.Account),Context=context }; Durable.Write(path,Json.Write(ticket)); Pending=ticket; }
            var result=await send(Pending.Key);var outcome=Json.Str(result,"outcome");
            if(!new[]{"reset","alreadyRedeemed","nothingToReset","noCredit"}.Contains(outcome))throw new InvalidDataException(L10n.T("重置结果不明，请用原请求重试。"));
            File.Delete(path);Pending=null;return outcome;
        }finally{Interlocked.Exchange(ref busy,0);}
    }
}
}
