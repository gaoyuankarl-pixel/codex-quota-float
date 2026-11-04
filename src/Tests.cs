using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodexQuotaFloat {
static class Tests {
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);count++;}
    static Snapshot Parse(string fields){return Snapshot.Parse(Json.Read("{\"accountId\":\"account-a\",\"rateLimits\":{\"limitId\":\"codex\","+fields+"},\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":[]}}"));}
    static System.Windows.Media.Color RingPixel(double? value,int x,int y) {
        var ring=new QuotaRing();ring.Set(value,new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(114,221,194)));
        ring.Measure(new System.Windows.Size(44,44));ring.Arrange(new System.Windows.Rect(0,0,44,44));
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(44,44,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(ring);
        var bytes=new byte[4];bitmap.CopyPixels(new System.Windows.Int32Rect(x,y,1,1),bytes,4,0);
        return System.Windows.Media.Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);
    }
    public static void Run(){var original=L10n.Language;try{L10n.Language="zh-CN";RunAsync().GetAwaiter().GetResult();}finally{L10n.Language=original;}}
    static async Task RunAsync(){
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt");
        try {
            Check(new Preferences().Language=="en","new installs default to English");
            var legacyPreferences=Preferences.Parse("{\"Left\":10,\"Top\":20,\"IdleOpacity\":0.45}");
            Check(legacyPreferences.Language=="zh-CN"&&legacyPreferences.IdleOpacity==.45&&legacyPreferences.Left==10,"legacy installs keep Chinese and existing preferences");
            Check(Preferences.Parse("{\"Language\":\"en\"}").Language=="en"&&Preferences.Parse("{\"Language\":\"unsupported\"}").Language=="en","language selection and invalid value fallback");
            L10n.Language="en";
            var localizedWindow=new QuotaWindow{DurationMinutes=300};
            Check(localizedWindow.DisplayLabel=="5 hours"&&L10n.T("可用重置")=="Resets","English card labels");
            Check(localizedWindow.CardCountdown(DateTimeOffset.Now)=="Reset time unknown"&&new ResetCoordinator(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()+".json")).Reason(null,"test")=="Refresh live quota first","English unknown time and reset eligibility");
            Check(Program.SafeError(new TimeoutException()).Contains("timed out"),"English connection failure");
            L10n.Language="zh-CN";Check(localizedWindow.DisplayLabel=="5 小时"&&L10n.T("可用重置")=="可用重置","existing snapshot labels update on language switch");
            var s=Parse("\"primary\":{\"usedPercent\":19,\"windowDurationMins\":10080},\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":300}");
            Check(s.Windows[0].Label=="5 小时" && s.Windows[0].Remaining==80,"classify by duration, not slot");
            Check(s.Windows[1].Label=="1 周"&&s.Windows[1].Remaining==81,"weekly remaining");
            Check(s.ResetCount==2,"count authoritative over empty detail list");
            Check(s.Windows[0].ResetLabel(DateTimeOffset.Now)=="恢复时间未提供","null reset is unknown");
            var refreshedAt=DateTimeOffset.FromUnixTimeSeconds(1800000000);
            var countdown=new QuotaWindow{DurationMinutes=300,ResetSeconds=refreshedAt.ToUnixTimeSeconds()+2*3600+7*60+45};
            Check(countdown.CardCountdown(refreshedAt)=="-02 h 07 min","five hour countdown has minus prefix and hours and minutes");
            Check(countdown.CardCountdown(refreshedAt.AddMinutes(1))=="-02 h 06 min","next card refresh advances countdown");
            Check(countdown.CardCountdown(refreshedAt.ToOffset(TimeSpan.FromHours(8)))=="-02 h 07 min","countdown independent of local time zone");
            countdown.DurationMinutes=10080;countdown.ResetSeconds=refreshedAt.ToUnixTimeSeconds()+3*86400+4*3600+59*60;
            Check(countdown.CardCountdown(refreshedAt)=="-03 d 04 h","weekly countdown has minus prefix and days and hours");
            countdown.ResetSeconds=refreshedAt.ToUnixTimeSeconds()+23*3600;
            Check(countdown.CardCountdown(refreshedAt)=="-00 d 23 h","weekly countdown below one day");
            countdown.DurationMinutes=300;countdown.ResetSeconds=refreshedAt.ToUnixTimeSeconds()+59;
            Check(countdown.CardCountdown(refreshedAt)=="-00 h 00 min","subminute countdown retains minus prefix without negative time components");
            countdown.ResetSeconds=refreshedAt.ToUnixTimeSeconds();
            Check(countdown.CardCountdown(refreshedAt)=="等待刷新"&&countdown.CardCountdown(refreshedAt.AddSeconds(1))=="等待刷新","elapsed window waits for server refresh");
            countdown.ResetSeconds=null;Check(countdown.CardCountdown(refreshedAt)=="恢复时间未知","unknown reset time is not zero countdown");
            var unknown=Parse("\"primary\":{\"usedPercent\":100}");Check(unknown.Windows[0].Label=="额度窗口"&&unknown.Windows[0].Remaining==0,"unknown window and zero distinguish");
            var empty=Parse("\"primary\":null,\"secondary\":null");Check(empty.Windows.Count==0,"no windows is not unlimited");
            var absent=Snapshot.Parse(Json.Read("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300}}}"));Check(absent.ResetCount==null&&absent.Windows[0].Remaining==100,"unknown reset count");
            bool rejected=false;try{Parse("\"primary\":{\"usedPercent\":101}");}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid percentage rejected");
            var multi=Snapshot.Parse(Json.Read("{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":95}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":25}}}}"));Check(multi.Windows[0].Remaining==75,"prefer codex bucket");
            Check(Native.IsCodexGui(@"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64__x\app\ChatGPT.exe"),"packaged GUI detection");
            Check(!Native.IsCodexGui(@"C:\Users\user\AppData\Local\OpenAI\Codex\bin\hash\codex.exe"),"CLI child is never GUI host");
            Check(!Native.IsCodexGui(@"C:\unrelated\ChatGPT.exe"),"unrelated ChatGPT excluded");
            var grace=new HostGrace();var now=DateTime.UtcNow;Check(!grace.ShouldExit(false,now)&&!grace.ShouldExit(false,now.AddSeconds(4))&&grace.ShouldExit(false,now.AddSeconds(5)),"five second exit grace");
            Check(!grace.ShouldExit(true,now.AddSeconds(6))&&!grace.ShouldExit(false,now.AddSeconds(7)),"restart cancels grace");
            var watch=new WatchPolicy();
            Check(watch.Update("100:session-a",false,false)==WatchAction.Start,"start for first host");
            Check(watch.Update("100:session-a",true,false)==WatchAction.None,"do not duplicate live widget");
            Check(watch.Update(null,true,false)==WatchAction.Stop,"host exit closes widget");
            Check(watch.Update("200:session-b",false,false)==WatchAction.Start,"host restart reopens widget");
            Check(watch.Update("200:session-b",false,true)==WatchAction.None,"manual close suppresses current host");
            Check(watch.Update("200:session-b",false,false)==WatchAction.None,"manual suppression persists");
            Check(watch.Update("300:session-c",false,false)==WatchAction.Start,"fast restart resets suppression without absent poll");
            Check(watch.Update("300:session-c",false,false)==WatchAction.Start,"unexpected widget crash restarts");
            Check(watch.Update("300:session-d",false,false)==WatchAction.Start,"PID reuse with different creation time is new host");
            string startupScript=StartupRegistration.Bootstrap(@"E:\Codex\a'b\CodexQuotaFloat.exe");
            Check(startupScript.Contains("a''b")&&startupScript.Contains("--startup"),"startup quotes paths and marks logon entry");
            Check(startupScript.Contains("1..24")&&startupScript.Contains("Test-Path -LiteralPath $p"),"startup waits for program drive");
            string encoded=StartupRegistration.Arguments(@"E:\Codex\app.exe").Split(' ').Last();
            Check(System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded))==StartupRegistration.Bootstrap(@"E:\Codex\app.exe"),"startup encoded command round trip");
            Check(StartupRegistration.Arguments(@"E:\Codex\Projects\CodexQuotaFloat\app\CodexQuotaFloat.exe").Length<1023,"startup shortcut arguments fit WSH limit");
            Check(!ForegroundPolicy.Show(new FocusState(),false,false),"no host stays hidden");
            Check(ForegroundPolicy.Show(new FocusState{CodexForeground=true,CodexVisible=true},false,false),"foreground Codex shows widget");
            Check(!ForegroundPolicy.Show(new FocusState{CodexVisible=true},false,true),"switch to another app hides widget");
            Check(!ForegroundPolicy.Show(new FocusState{CodexForeground=true,CodexVisible=false},false,true),"minimized Codex hides widget");
            Check(ForegroundPolicy.Show(new FocusState{CodexVisible=true,OwnForeground=true},true,true),"own menu remains usable while host visible");
            Check(!ForegroundPolicy.Show(new FocusState{OwnForeground=true},true,true),"minimize overrides own dialog visibility");
            Check(!ForegroundPolicy.Show(new FocusState{CodexVisible=true,OwnForeground=true},true,false),"tray cannot force hidden widget to appear");
            Check(new WatchPolicy().Update(null,false,false)==WatchAction.None,"startup without Codex does not launch widget or query service");
            var position=StripLayout.Dock(new System.Windows.Rect(0,0,1920,1040),StripLayout.Width,StripLayout.Height);
            Check(position.X+StripLayout.Width==1920&&position.Y+StripLayout.Height==1040,"strip flush with right work area and taskbar top");
            var leftMonitor=StripLayout.Dock(new System.Windows.Rect(-1920,100,1920,1000),312,76);
            Check(leftMonitor.X==-312&&leftMonitor.Y==1024,"dock handles negative monitor coordinates and work area offset");
            var p=new Preferences{Left=1091,Top=484,IdleOpacity=.45};StripLayout.Migrate(p);
            Check(double.IsNaN(p.Left)&&double.IsNaN(p.Top)&&p.IdleOpacity==.45&&p.LayoutRevision==1,"old vertical widget migrates to dock without changing opacity");
            p.Left=100;p.Top=200;StripLayout.Migrate(p);Check(p.Left==100&&p.Top==200,"subsequent launches retain user dragged position");
            var mint=System.Windows.Media.Color.FromRgb(114,221,194);var track=System.Windows.Media.Color.FromRgb(58,65,69);
            Check(RingPixel(0,22,3)==track,"zero remaining renders empty ring track");
            Check(RingPixel(null,22,3)==track,"unknown percentage keeps neutral ring");
            Check(RingPixel(100,3,22)==mint&&RingPixel(100,41,22)==mint,"100 percent renders complete circle instead of degenerate arc");
            Check(RingPixel(50,3,22)==mint&&RingPixel(50,41,22)==track,"half remaining leaves left semicircle after clockwise consumption");
            Check(RingPixel(80,41,22)==mint&&RingPixel(60,41,22)==track,"clockwise consumed gap passes three o'clock as quota falls");
            Check(RingPixel(60,22,41)==mint&&RingPixel(40,22,41)==track,"clockwise consumed gap then passes six o'clock");
            string dir=Path.Combine(Path.GetTempPath(),"CodexQuotaFloatTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try {
                string path=Path.Combine(dir,"pending.json");var r=new ResetCoordinator(path);Check(r.Reason(s,"ctx")==null,"eligible reset");
                string first=null;
                try{await r.Execute(s,"ctx",key=>{first=key;throw new TimeoutException();});}catch(TimeoutException){}
                Check(first!=null&&File.Exists(path),"persist before uncertain reset");
                var restored=new ResetCoordinator(path);Check(restored.Pending.Key==first,"operation survives restart");
                var b=Parse("\"primary\":{\"usedPercent\":90}");b.Account="account-b";Check(restored.Reason(b,"ctx")!=null,"prevent account mismatch");
                Check(restored.Reason(s,"other-context")!=null,"prevent directory mismatch");
                var completion=new TaskCompletionSource<object>();var running=restored.Execute(s,"ctx",key=>{Check(key==first,"retry reuses UUID");return completion.Task;});
                Check(restored.Reason(s,"ctx")!=null,"disable concurrent reset");completion.SetResult(Json.Read("{\"outcome\":\"alreadyRedeemed\"}"));
                Check(await running=="alreadyRedeemed"&&!File.Exists(path),"idempotent success clears pending");
                foreach(var outcome in new[]{"reset","nothingToReset","noCredit"}){var result=await restored.Execute(s,"ctx",key=>Task.FromResult(Json.Read("{\"outcome\":\""+outcome+"\"}")));Check(result==outcome&&restored.Pending==null,"terminal outcome "+outcome);}
                bool sent=false;var badPath=Path.Combine(dir,"missing","pending.json");Directory.CreateDirectory(badPath);
                var unreachable=new ResetCoordinator(badPath);
                try{await unreachable.Execute(s,"ctx",key=>{sent=true;return Task.FromResult(Json.Read("{\"outcome\":\"reset\"}"));});}catch(IOException){}catch(UnauthorizedAccessException){}
                Check(!sent,"persistence failure aborts before reset request");
                var unsure=new ResetCoordinator(path);try{await unsure.Execute(s,"ctx",key=>Task.FromResult(Json.Read("{\"outcome\":\"unknown\"}")));}catch(InvalidDataException){}
                Check(unsure.Pending!=null&&File.Exists(path),"unknown outcome retains pending key");
                File.Delete(path);Directory.Delete(badPath);Directory.Delete(Path.GetDirectoryName(badPath));
                s.Valid=false;Check(restored.Reason(s,"ctx")!=null,"offline blocks reset");s.Valid=true;s.ResetCount=null;Check(restored.Reason(s,"ctx")!=null,"unknown count blocks reset");
                s.ResetCount=0;Check(restored.Reason(s,"ctx")!=null,"zero count blocks reset");
            }finally{foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}
            File.WriteAllText(output,"PASS "+count+" assertions. Synthetic data only; no credentials, quota queries, or reset consumption.\r\n");
        }catch(Exception e){File.WriteAllText(output,e.ToString());throw;}
    }
}
}
