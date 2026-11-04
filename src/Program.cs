using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace CodexQuotaFloat {
static class Program {
    public const string Version="0.2.0";
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Contains("--self-test")){Tests.Run();return 0;}
            L10n.Language=Preferences.Load().Language;
            var language=args.FirstOrDefault(a=>a.StartsWith("--lang=",StringComparison.Ordinal));
            if(language!=null){L10n.Language=language.Substring(7);if(L10n.Language!="en"&&L10n.Language!="zh-CN")throw new InvalidOperationException("Use --lang=en or --lang=zh-CN.");}
            Lifecycle.Write("entry",args.Contains("--watch")?(args.Contains("--startup")?"watch startup":"watch"):args.Contains("--install")?"install":args.Contains("--managed")?"widget managed":"command");
            if(args.Contains("--diagnose")){Diagnose().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--install")){Native.SetStartup(true);WatcherStartup.Start();return 0;}
            if(args.Contains("--stop")){Signal("StopWatcher");Signal("StopWidget");return 0;}
            if(args.Contains("--uninstall")){Native.SetStartup(false);Signal("StopWatcher");Signal("StopWidget");return 0;}
            if(args.Contains("--watch")){Watch();return 0;}
            bool fresh;using(var mutex=new Mutex(true,"Local\\CodexQuotaFloat.Widget",out fresh)) {
                if(!fresh){Signal("Show");return 0;}
                var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
                app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;MessageBox.Show(L10n.T("操作未完成，请稍后重试。\n")+SafeError(e.Exception),L10n.T("Codex 额度浮窗"));};
                app.Run(new QuotaFloat(args.Contains("--demo"),args.Contains("--managed"),args.Contains("--qa")));
            }
            return 0;
        }catch(Exception e){Lifecycle.Write("fatal",e.GetType().Name+" HRESULT="+e.HResult);try{Directory.CreateDirectory(Preferences.Root);Durable.Write(Path.Combine(Preferences.Root,"last-error.txt"),DateTimeOffset.Now.ToString("O")+" "+SafeError(e)+"\r\n"+e.GetType().FullName+"\r\n"+e.StackTrace);}catch{}return 1;}
    }
    public static string SafeError(Exception e) {
        if(e is FileNotFoundException)return L10n.T("未找到 Codex CLI，请在设置中指定 codex.exe。");
        if(e is TimeoutException)return L10n.T("查询超时，稍后自动重试。");
        if(e is IOException && e.Message.StartsWith("Codex "))return e.Message;
        if(e is InvalidDataException || e is InvalidOperationException)return e.Message;
        return L10n.T("查询不可用，请确认 Codex 已用 ChatGPT 账号登录，或稍后重试。");
    }
    public static EventWaitHandle Event(string name){return new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexQuotaFloat."+name);}
    static void Signal(string name){try{using(var h=EventWaitHandle.OpenExisting("Local\\CodexQuotaFloat."+name))h.Set();}catch{}}
    static void Watch() {
        bool fresh;using(var mutex=new Mutex(true,"Local\\CodexQuotaFloat.Watcher",out fresh)) {
            if(!fresh){Lifecycle.Write("watch-duplicate","");Signal("Show");return;}
            Lifecycle.Write("watch-start","");
            using(var stop=Event("StopWatcher")) using(var keep=Event("StopWatcherKeepWidget")) using(var dismiss=Event("DismissWidget")) {
                Process child=null;bool keepWidget=false;var policy=new WatchPolicy();string lastHost=null;DateTimeOffset lastHealth=DateTimeOffset.MinValue;
                try {while(!stop.WaitOne(0)) {
                    if(keep.WaitOne(0)){keepWidget=true;break;}
                    try {
                        string instance=Native.HostInstance();bool widgetAlive=WidgetAlive();
                        if(instance!=lastHost){Lifecycle.Write("host-change",instance==null?"absent":"present");lastHost=instance;}
                        var action=policy.Update(instance,widgetAlive,dismiss.WaitOne(0));
                        if(action==WatchAction.Start){if(child!=null)child.Dispose();child=Native.Launch("--managed");Lifecycle.Write("widget-launch","pid="+child.Id);}
                        if(action==WatchAction.Stop){Signal("StopWidget");Lifecycle.Write("widget-stop","host absent");}
                        if((DateTimeOffset.Now-lastHealth).TotalSeconds>=30){lastHealth=DateTimeOffset.Now;Durable.Write(Path.Combine(Preferences.Root,"watcher-health.json"),Json.Write(new{version=Version,observed=lastHealth.ToString("O"),watcherPid=Process.GetCurrentProcess().Id,hostPresent=instance!=null,widgetPresent=widgetAlive}));}
                    }catch(Exception e){Lifecycle.Write("watch-retry",e.GetType().Name+" HRESULT="+e.HResult);}
                    if(stop.WaitOne(2500))break;
                }}finally{Lifecycle.Write("watch-exit",keepWidget?"disabled keep widget":"stop signal");if(!keepWidget)Signal("StopWidget");if(child!=null)child.Dispose();}
            }
        }
    }
    static bool WidgetAlive(){try{using(var m=Mutex.OpenExisting("Local\\CodexQuotaFloat.Widget"))return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
    static async Task Diagnose() {
        using(var rpc=new CodexRpc {ExplicitPath=Preferences.Load().CliPath}) {
            var q=await rpc.Read();
            Durable.Write(Path.Combine(Preferences.Root,"diagnostic.json"),Json.Write(new{version=Version,hostDetected=Native.HostRunning(),observed=q.Observed.ToString("O"),plan=q.Plan,accountIdPresent=!string.IsNullOrEmpty(q.Account),resetCount=q.ResetCount,windows=q.Windows.Select(w=>new{label=w.Label,remaining=w.Remaining,duration=w.DurationMinutes,resetsAt=w.ResetSeconds})}));
        }
    }
}

sealed class Row {
    public TextBlock Label,Percent,Reset;
    public QuotaRing Ring;
    public Border Root;
}
sealed class QuotaFloat : Window {
    readonly Preferences settings;
    readonly CodexRpc rpc;
    readonly bool demo,managed;
    readonly List<Row> rows=new List<Row>();
    readonly HostGrace grace=new HostGrace();
    readonly DispatcherTimer tick=new DispatcherTimer();
    readonly DispatcherTimer leave=new DispatcherTimer();
    readonly DispatcherTimer visibilityTimer=new DispatcherTimer();
    readonly EventWaitHandle stop,show;
    Forms.NotifyIcon tray;
    Snapshot snapshot;
    ResetCoordinator resets;
    TextBlock status,resetText,resetAction;
    Button resetButton,refreshButton;
    Grid rowsPanel;
    Border card;
    Border connectionDot;
    bool reading,resetting,closing,dialogOpen,everHost;
    bool menuOpen;
    bool visibilityReady;
    bool loaded;
    bool? recordedVisibility;
    DateTimeOffset lastAttempt=DateTimeOffset.MinValue,nextPoll=DateTimeOffset.MinValue;
    int failures;
    string connectionError;
    DateTime lastHostCheck=DateTime.MinValue;
    bool host;
    ContextMenu menu;
    static readonly SolidColorBrush BackgroundBrush=Brush("#202326"),Muted=Brush("#9AA4AD"),MainText=Brush("#F4F6F7"),Accent=Brush("#72DDC2");
    static SolidColorBrush Brush(string hex){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
    static TextBlock Text(string content,double size,Brush color){return new TextBlock{Text=content,FontSize=size,Foreground=color,VerticalAlignment=VerticalAlignment.Center};}
    static Button Btn(string text,string tooltip,double disabledOpacity=.35) {
        var b=new Button{Content=text,ToolTip=tooltip,Height=18,MinWidth=18,Padding=new Thickness(0),Margin=new Thickness(1,0,0,0),Foreground=Muted,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,FontSize=12};
        var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);presenter.SetValue(ContentPresenter.MarginProperty,new Thickness(2,0,2,0));border.AppendChild(presenter);template.VisualTree=border;
        var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(BackgroundProperty,Brush("#353C40")));hover.Setters.Add(new Setter(ForegroundProperty,MainText));template.Triggers.Add(hover);
        var disabled=new Trigger{Property=IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(OpacityProperty,disabledOpacity));template.Triggers.Add(disabled);b.Template=template;return b;
    }
    public QuotaFloat(bool demo,bool managed,bool qa) {
        this.demo=demo;this.managed=managed;settings=Preferences.Load();settings.Language=L10n.Language;StripLayout.Migrate(settings);rpc=new CodexRpc{ExplicitPath=settings.CliPath};
        stop=Program.Event("StopWidget");show=Program.Event("Show");
        try{resets=new ResetCoordinator(Path.Combine(Preferences.Root,"pending-reset.json"));}catch(Exception e){connectionError=Program.SafeError(e);}
        Title=L10n.T("Codex 额度浮窗")+(demo?L10n.T(" · 演示"):"");Width=StripLayout.Width;Height=StripLayout.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=qa;ShowActivated=qa;Topmost=settings.Topmost;FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
        Build();
        SourceInitialized+=(s,e)=>{if(!qa)Native.NoActivate(this);};
        Loaded+=async(s,e)=>{if(loaded)return;loaded=true;Position();CreateTray();host=Native.HostRunning();everHost=host;visibilityReady=true;ApplyForegroundVisibility();visibilityTimer.Start();tick.Start();Lifecycle.Write("widget-loaded",demo?"demo":"live");if(demo){snapshot=Sample();Render();}else if(host)await Refresh(false);};
        MouseEnter+=(s,e)=>{leave.Stop();Fade(1);};
        MouseLeave+=(s,e)=>leave.Start();
        leave.Interval=TimeSpan.FromMilliseconds(550);leave.Tick+=(s,e)=>{leave.Stop();if(!IsMouseOver && !dialogOpen && !menuOpen && !Native.OurForeground())Fade(settings.IdleOpacity);};
        Opacity=0;
        visibilityTimer.Interval=TimeSpan.FromMilliseconds(200);visibilityTimer.Tick+=(s,e)=>ApplyForegroundVisibility();
        rpc.Changed=(accountChanged)=>{if(closing)return;try{Dispatcher.BeginInvoke(new Action(async()=>{if(accountChanged){snapshot=null;connectionError=L10n.T("账号发生变化，正在核对");Render();}if((DateTimeOffset.Now-lastAttempt).TotalSeconds>=5)await Refresh(false);else nextPoll=DateTimeOffset.Now.AddSeconds(5);}));}catch{}};
        tick.Interval=TimeSpan.FromSeconds(1);tick.Tick+=async(s,e)=>{
            if(stop.WaitOne(0)){Close();return;}
            if(show.WaitOne(0)){ApplyForegroundVisibility();Topmost=settings.Topmost;}
            if(!demo && (DateTime.UtcNow-lastHostCheck).TotalSeconds>=2){lastHostCheck=DateTime.UtcNow;host=Native.HostRunning();everHost|=host;if((managed||everHost)&&grace.ShouldExit(host,DateTime.UtcNow)){Close();return;}}
            Render();
            if(!demo && !resetting && DateTimeOffset.Now>=nextPoll)await Refresh(false);
            if(!IsMouseOver&&!menuOpen&&!dialogOpen&&!Native.OurForeground()&&Opacity>.99)leave.Start();
        };
        Closing+=(s,e)=>{Lifecycle.Write("widget-exit","");closing=true;tick.Stop();leave.Stop();visibilityTimer.Stop();rpc.Changed=null;rpc.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}stop.Dispose();show.Dispose();settings.Left=Left;settings.Top=Top;try{settings.Save();}catch{}};
    }
    void ApplyForegroundVisibility() {
        if(closing||!visibilityReady)return;
        var focus=Native.Focus();bool visible=demo||ForegroundPolicy.Show(focus,menuOpen||dialogOpen||resetting,IsVisible);
        if(visible) {
            if(!IsVisible){Show();Fade(IsMouseOver?1:settings.IdleOpacity);}
            else if(Opacity==0)Fade(settings.IdleOpacity);
        } else if(IsVisible) {
            if(menu!=null)menu.IsOpen=false;Hide();
        }
        if(recordedVisibility!=visible){recordedVisibility=visible;RecordVisibility(visible,focus);}
    }
    void RecordVisibility(bool visible,FocusState focus) {
        Lifecycle.Write("widget-visibility",visible?"shown":"hidden");
        try{Durable.Write(Path.Combine(Preferences.Root,"widget-visibility.json"),Json.Write(new{version=Program.Version,observed=DateTimeOffset.Now.ToString("O"),visible=visible,codexForeground=focus.CodexForeground,codexVisible=focus.CodexVisible}));}catch{}
    }
    void Build() {
        rows.Clear();Title=L10n.T("Codex 额度浮窗")+(demo?L10n.T(" · 演示"):"");
        card=new Border{Background=BackgroundBrush,BorderBrush=Brush("#41494E"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,4,10,4)};
        var layout=new Grid();card.Child=layout;Content=card;
        layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(172)});
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});
        rowsPanel=new Grid();rowsPanel.ColumnDefinitions.Add(new ColumnDefinition());rowsPanel.ColumnDefinitions.Add(new ColumnDefinition());layout.Children.Add(rowsPanel);
        for(int i=0;i<2;i++)AddRow();
        var controls=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(controls,2);
        connectionDot=new Border{Width=4,Height=4,CornerRadius=new CornerRadius(2),Background=Accent,Margin=new Thickness(0,0,0,3),HorizontalAlignment=HorizontalAlignment.Center};controls.Children.Add(connectionDot);
        refreshButton=Btn("↻",L10n.T("刷新额度"));refreshButton.Click+=async(s,e)=>await Refresh(true);controls.Children.Add(refreshButton);
        var options=Btn("⋯",L10n.T("设置与启动方式"));options.Click+=(s,e)=>OpenMenu(options);controls.Children.Add(options);
        var close=Btn("×",L10n.T("关闭浮窗，本次 Codex 运行期间不再自动出现"));close.Click+=(s,e)=>Dismiss();controls.Children.Add(close);layout.Children.Add(controls);
        var resetContent=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        resetContent.Children.Add(Text(L10n.T("可用重置"),9,Muted));
        resetText=Text(L10n.T("— 次"),21,MainText);resetText.FontWeight=FontWeights.SemiBold;resetText.HorizontalAlignment=HorizontalAlignment.Center;resetContent.Children.Add(resetText);
        resetAction=Text(L10n.T("点击使用"),9,Accent);resetAction.HorizontalAlignment=HorizontalAlignment.Center;resetContent.Children.Add(resetAction);
        resetButton=Btn("",L10n.T("消耗一次已获得的重置机会"),1);resetButton.Content=resetContent;resetButton.Height=54;resetButton.Margin=new Thickness(7,0,3,0);resetButton.Click+=async(s,e)=>await Reset();
        var resetArea=new Border{BorderBrush=Brush("#363D42"),BorderThickness=new Thickness(1,0,0,0),Margin=new Thickness(5,4,0,4),Child=resetButton};Grid.SetColumn(resetArea,1);layout.Children.Add(resetArea);
        status=Text(L10n.T("正在连接 Codex…"),10,Muted);
        card.MouseLeftButtonDown+=(s,e)=>{if(e.Handled)return;try{DragMove();settings.Left=Left;settings.Top=Top;settings.Save();}catch{}};
    }
    void AddRow() {
        var row=new Row();var content=new StackPanel{VerticalAlignment=VerticalAlignment.Center};row.Root=new Border{Child=content,Background=Brushes.Transparent};
        var ring=new Grid{Width=52,Height=52,HorizontalAlignment=HorizontalAlignment.Center};
        row.Ring=new QuotaRing();ring.Children.Add(row.Ring);
        var center=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        row.Label=Text("—",9,Muted);row.Label.HorizontalAlignment=HorizontalAlignment.Center;center.Children.Add(row.Label);
        row.Percent=Text("—",14,MainText);row.Percent.FontWeight=FontWeights.SemiBold;row.Percent.HorizontalAlignment=HorizontalAlignment.Center;center.Children.Add(row.Percent);ring.Children.Add(center);content.Children.Add(ring);
        row.Reset=Text(L10n.T("读取中…"),9,Muted);row.Reset.HorizontalAlignment=HorizontalAlignment.Center;row.Reset.Margin=new Thickness(0,1,0,0);content.Children.Add(row.Reset);
        Grid.SetColumn(row.Root,rows.Count);rows.Add(row);rowsPanel.Children.Add(row.Root);
    }
    void Render() {
        if(closing)return;
        int count=2;
        for(int i=0;i<rows.Count;i++) {
            var r=rows[i];r.Root.Visibility=i<count?Visibility.Visible:Visibility.Collapsed;if(i>=count)continue;
            var w=snapshot==null?null:snapshot.Windows.FirstOrDefault(window=>window.DurationMinutes==(i==0?300:10080));
            r.Label.Text=w!=null?w.DisplayLabel:i==0?L10n.T("5 小时"):L10n.T("1 周");
            r.Percent.Text=w!=null?w.Remaining.ToString("0",CultureInfo.InvariantCulture)+"%":"—";
            r.Ring.Set(w!=null?(double?)w.Remaining:null,w==null?Muted:w.Remaining<=5?Brush("#FF8888"):w.Remaining<=20?Brush("#EDC078"):Accent);
            r.Reset.Text=w!=null?w.CardCountdown(snapshot.Observed):connectionError!=null?L10n.T("暂时无法读取"):snapshot!=null?L10n.T("未提供"):L10n.T("读取中…");
            r.Root.ToolTip=w!=null?w.ResetLabel(DateTimeOffset.Now)+"\n"+w.Countdown(DateTimeOffset.Now):L10n.T("等待 Codex 官方额度");
        }
        resetText.Text=snapshot!=null&&snapshot.ResetCount.HasValue?snapshot.ResetCount.Value+L10n.T(" 次"):L10n.T("— 次");
        var reason=resets!=null?resets.Reason(snapshot,rpc.Context):L10n.T("重置记录需要检查");
        resetAction.Text=resets!=null&&resets.Pending!=null?L10n.T("点击重试"):snapshot!=null&&snapshot.ResetCount==0?L10n.T("暂无次数"):L10n.T("点击使用");
        resetButton.IsEnabled=!demo&&!reading&&!resetting&&reason==null;resetButton.Cursor=resetButton.IsEnabled?Cursors.Hand:Cursors.Arrow;
        resetAction.Opacity=resetButton.IsEnabled?1:.4;
        resetButton.ToolTip=reason??L10n.T("仅重置服务端允许的额度窗口；提交前会再次确认");
        refreshButton.IsEnabled=!reading&&!resetting;
        var age=snapshot==null?double.MaxValue:(DateTimeOffset.Now-snapshot.Observed).TotalSeconds;
        if(demo)status.Text=L10n.T("演示数据 · 不会查询或消耗额度");
        else if(reading)status.Text=L10n.T("正在同步官方额度…");
        else if(connectionError!=null)status.Text=(snapshot!=null?L10n.T("旧数据 · "):"")+connectionError;
        else if(snapshot!=null)status.Text=age>120?L10n.T("数据已过期 · 等待重新连接"):L10n.T("实时 · ")+(age<10?L10n.T("刚刚更新"):((int)age)+L10n.T(" 秒前更新"))+L10n.T(" · 每分钟同步");
        else status.Text=L10n.T("正在连接 Codex…");
        status.ToolTip=status.Text;
        card.ToolTip=status.Text;connectionDot.ToolTip=(demo?L10n.T("演示 · "):"CODEX · ")+(snapshot!=null?snapshot.Plan:L10n.T("剩余用量"))+"\n"+status.Text;connectionDot.Background=connectionError!=null||age>120&&!reading?Brush("#EDC078"):Accent;
        if(tray!=null){var tip=L10n.T("Codex 额度浮窗")+(snapshot!=null?" · "+string.Join(" / ",snapshot.Windows.Select(w=>w.DisplayLabel+" "+w.Remaining.ToString("0")+"%")):"");tray.Text=tip.Length>63?tip.Substring(0,63):tip;}
    }
    async Task Refresh(bool manual) {
        if(demo||closing||reading||resetting)return;
        if(!Native.HostRunning())return;
        if(manual&&(DateTimeOffset.Now-lastAttempt).TotalSeconds<5)return;
        reading=true;lastAttempt=DateTimeOffset.Now;Render();
        try {var q=await rpc.Read();if(closing)return;snapshot=q;connectionError=null;failures=0;nextPoll=DateTimeOffset.Now.AddSeconds(60);}
        catch(Exception e){if(snapshot!=null)snapshot.Valid=false;connectionError=Program.SafeError(e);failures++;nextPoll=DateTimeOffset.Now.AddSeconds(Math.Min(300,15*Math.Pow(2,Math.Min(failures-1,5))));rpc.Dispose();}
        finally{reading=false;if(!closing)Render();}
    }
    async Task Reset() {
        if(demo||resetting||reading||resets==null)return;
        await Refresh(true);if(closing)return;
        var reason=resets.Reason(snapshot,rpc.Context);if(reason!=null){MessageBox.Show(this,reason,L10n.T("无法重置"));return;}
        var expectedAccount=snapshot.Account;var expectedContext=rpc.Context;
        dialogOpen=true;Fade(1);
        var answer=MessageBox.Show(this,resets.Pending!=null?L10n.T("将使用同一个请求标识核实或重试上次重置，避免重复消耗。\n\n继续？"):L10n.T("将消耗 1 次可用重置机会。\n仅恢复服务端认定符合条件的 Codex 额度窗口。\n\n确认使用？"),L10n.T("使用 Codex 重置机会"),MessageBoxButton.OKCancel,MessageBoxImage.Question);
        dialogOpen=false;if(answer!=MessageBoxResult.OK){leave.Start();return;}
        resetting=true;Render();
        try {var verified=await rpc.Read();if(verified.Account!=expectedAccount||rpc.Context!=expectedContext)throw new InvalidOperationException(L10n.T("账户已变化，本次操作已取消。"));snapshot=verified;var outcome=await resets.Execute(snapshot,rpc.Context,rpc.Consume);
            MessageBox.Show(this,outcome=="reset"||outcome=="alreadyRedeemed"?L10n.T("重置已完成，正在重新查询额度。"):outcome=="nothingToReset"?L10n.T("当前没有符合条件的窗口需要重置。"):L10n.T("当前已没有可用重置机会。"),L10n.T("Codex 额度浮窗"));}
        catch(Exception e){MessageBox.Show(this,L10n.T("结果尚未确认，保留了本次操作标识。\n请使用“重试重置”继续核实。\n\n")+Program.SafeError(e),L10n.T("重置未确认"));}
        finally{resetting=false;}
        if(!closing){await Refresh(false);Render();leave.Start();}
    }
    void Fade(double value) {BeginAnimation(OpacityProperty,new DoubleAnimation(value,TimeSpan.FromMilliseconds(150)){FillBehavior=FillBehavior.HoldEnd});}
    void Position() {
        var screen=Forms.Screen.PrimaryScreen.WorkingArea;var scale=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
        var start=scale.Transform(new Point(screen.Left,screen.Top));var bounds=scale.Transform(new Point(screen.Right,screen.Bottom));
        var dock=StripLayout.Dock(new Rect(start,bounds),Width,ActualHeight);
        Left=double.IsNaN(settings.Left)?dock.X:settings.Left;Top=double.IsNaN(settings.Top)?dock.Y:settings.Top;
        bool visible=Forms.Screen.AllScreens.Any(s=>{var a=scale.Transform(new Point(s.WorkingArea.Left,s.WorkingArea.Top));var b=scale.Transform(new Point(s.WorkingArea.Right,s.WorkingArea.Bottom));return Left>=a.X && Top>=a.Y && Left+Width<=b.X && Top+ActualHeight<=b.Y;});
        if(!visible){Left=dock.X;Top=dock.Y;}
        settings.Left=Left;settings.Top=Top;settings.Save();
    }
    void CreateTray() {
        var icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        tray=new Forms.NotifyIcon{Icon=icon,Visible=true,Text=L10n.T("Codex 额度浮窗")};
        Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32,32));
        tray.MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left){ApplyForegroundVisibility();}else if(e.Button==Forms.MouseButtons.Right)OpenMenu(null);};
    }
    MenuItem Item(string label,Action action){var i=new MenuItem{Header=label};i.Click+=(s,e)=>action();return i;}
    void SavePreferences(){settings.Left=Left;settings.Top=Top;settings.Save();}
    void Dismiss(){using(var dismissed=Program.Event("DismissWidget"))dismissed.Set();Close();}
    void OpenMenu(FrameworkElement owner) {
        menu=new ContextMenu{Background=BackgroundBrush,Foreground=MainText,Padding=new Thickness(4),FontFamily=FontFamily,FontSize=12};
        menu.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><Border Background='#202326' BorderBrush='#4B555B' BorderThickness='1' CornerRadius='9' Padding='5'><StackPanel IsItemsHost='True'/></Border></ControlTemplate>");
        menu.Resources[typeof(MenuItem)]=System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'><Setter Property='Foreground' Value='#EFF3F4'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'><Grid><Border x:Name='Surface' Background='Transparent' CornerRadius='5' Padding='8,7'><Grid MinWidth='200'><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition/><ColumnDefinition Width='12'/></Grid.ColumnDefinitions><TextBlock x:Name='Check' Text='✓' Visibility='Collapsed' Foreground='#72DDC2'/><ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'/><TextBlock x:Name='Arrow' Grid.Column='2' Text='›' Visibility='Collapsed' Foreground='#9AA4AD'/></Grid></Border><Popup x:Name='PART_Popup' Placement='Right' IsOpen='{TemplateBinding IsSubmenuOpen}' AllowsTransparency='True' Focusable='False'><Border Background='#202326' BorderBrush='#4B555B' BorderThickness='1' CornerRadius='7' Padding='4'><ItemsPresenter/></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Surface' Property='Background' Value='#353D42'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        menu.Items.Add(Item(L10n.T("立即刷新"),async()=>await Refresh(true)));
        var languages=new MenuItem{Header="Language / 语言"};
        foreach(var code in new[]{"en","zh-CN"}){string value=code;var choice=Item(code=="en"?"English":"简体中文",()=>{L10n.Language=value;settings.Language=value;SavePreferences();Build();Render();});choice.IsCheckable=true;choice.IsChecked=L10n.Language==code;languages.Items.Add(choice);}menu.Items.Add(languages);
        var top=new MenuItem{Header=L10n.T("始终置顶"),IsCheckable=true,IsChecked=settings.Topmost};top.Click+=(s,e)=>{settings.Topmost=top.IsChecked;Topmost=top.IsChecked;SavePreferences();};menu.Items.Add(top);
        var opacity=new MenuItem{Header=L10n.T("平时不透明度")};foreach(double value in new[]{.45,.62,.8}){double v=value;var sub=Item((v*100).ToString("0")+"%",()=>{settings.IdleOpacity=v;SavePreferences();});sub.IsCheckable=true;sub.IsChecked=Math.Abs(settings.IdleOpacity-v)<.01;opacity.Items.Add(sub);}menu.Items.Add(opacity);
        var auto=new MenuItem{Header=L10n.T("自动监听 Codex 启停"),IsCheckable=true,IsChecked=Native.StartupEnabled};auto.Click+=(s,e)=>{Native.SetStartup(auto.IsChecked);if(auto.IsChecked)WatcherStartup.Start();else using(var keep=Program.Event("StopWatcherKeepWidget"))keep.Set();};menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L10n.T("指定 Codex CLI…"),()=>{var d=new Microsoft.Win32.OpenFileDialog{Title=L10n.T("选择 codex.exe"),Filter="Codex CLI|codex.exe"};if(d.ShowDialog(this)==true){settings.CliPath=d.FileName;SavePreferences();rpc.ExplicitPath=d.FileName;rpc.Dispose();snapshot=null;nextPoll=DateTimeOffset.Now;}}));
        menu.Items.Add(Item(L10n.T("停靠到屏幕右下角"),()=>{settings.Left=double.NaN;settings.Top=double.NaN;Position();}));
        menu.Items.Add(Item(L10n.T("打开程序目录"),()=>Process.Start(new ProcessStartInfo{FileName=AppDomain.CurrentDomain.BaseDirectory,UseShellExecute=true})));
        menu.Items.Add(Item(L10n.T("关于 / v")+Program.Version,()=>MessageBox.Show(this,L10n.T("Codex 额度浮窗 v")+Program.Version+L10n.T("\n\n查询 Codex 账户额度，不发送模型请求。\n平时半透明，鼠标悬浮时不透明。\n仅在 Codex 桌面窗口位于前台时显示。\n\n重置仅由你主动确认触发。\n配置保存在程序旁的 data 文件夹。\nMIT 开源，非 OpenAI 官方产品。"),L10n.T("关于"))));
        menu.Items.Add(new Separator());menu.Items.Add(Item(L10n.T("关闭本次浮窗"),()=>Dismiss()));
        menu.Items.Add(Item(L10n.T("退出浮窗及后台监听"),()=>{using(var h=Program.Event("StopWatcher"))h.Set();Close();}));
        menu.Opened+=(s,e)=>{menuOpen=true;Fade(1);};menu.Closed+=(s,e)=>{menuOpen=false;leave.Start();};
        if(owner!=null){menu.PlacementTarget=owner;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;}
        menu.IsOpen=true;
    }
    static Snapshot Sample(){return Snapshot.Parse(Json.Read("{\"accountId\":\"demo\",\"rateLimits\":{\"limitId\":\"codex\",\"planType\":\"plus\",\"primary\":{\"usedPercent\":20,\"windowDurationMins\":300,\"resetsAt\":"+(DateTimeOffset.Now.ToUnixTimeSeconds()+7200)+"},\"secondary\":{\"usedPercent\":19,\"windowDurationMins\":10080,\"resetsAt\":"+(DateTimeOffset.Now.ToUnixTimeSeconds()+604800)+"}},\"rateLimitResetCredits\":{\"availableCount\":0}}"));}
}
}
