using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexUsage {
static class Json {
 public static Dictionary<string,object> D(object x) { return x as Dictionary<string,object> ?? new Dictionary<string,object>(); }
 public static object Get(object x,string k) { object v; return D(x).TryGetValue(k,out v)?v:null; }
 public static string S(object x,string k) { return Convert.ToString(Get(x,k),CultureInfo.InvariantCulture); }
 public static double N(object x,string k) { double n; return double.TryParse(S(x,k),NumberStyles.Any,CultureInfo.InvariantCulture,out n)?n:0; }
 public static object Read(string s) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.DeserializeObject(s); }
 public static string Write(object o) { return new JavaScriptSerializer().Serialize(o); }
}
class Usage {
 public DateTime Time; public string Model,Key; public long Input,Cache,Output,Total;
}
class Parsed {
 public List<Usage> Rows = new List<Usage>(); public object Quota; public DateTime QuotaTime; public bool Modern; public int Bad;
}
class Snapshot {
 public List<Usage> Rows=new List<Usage>(); public object Quota; public DateTime QuotaTime; public int Bad,Unreadable; public bool Found;
}
class LogReader {
 class Entry { public long Length; public DateTime Modified; public Parsed Parsed; }
 Dictionary<string,Entry> cache=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
 public static Parsed Parse(TextReader reader,string identity) {
  var result=new Parsed(); var modern=new List<Usage>(); var legacy=new List<Usage>(); string model="未知模型",line; long total=0,input=0,output=0,cached=0;
  while((line=reader.ReadLine())!=null) {
   if(!(line.Contains("turn_context")||line.Contains("token_count")||line.Contains("token_usage_record"))) continue;
   try {
    var j=Json.Read(line); var type=Json.S(j,"type"); var p=Json.Get(j,"payload"); DateTimeOffset stamp;
    if(type=="turn_context") { var m=Json.S(p,"model"); if(m!="")model=m; continue; }
    if(!DateTimeOffset.TryParse(Json.S(j,"timestamp"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out stamp)) continue;
    var time=stamp.LocalDateTime;
    if(type=="token_usage_record") {
     result.Modern=true; var u=Json.Get(p,"usage"); if(u==null)continue;
     string id=Json.S(p,"response_id");
     modern.Add(new Usage {Time=time,Model=model,Key=id!=""?id:identity+":"+Json.S(j,"timestamp")+":"+modern.Count,Input=(long)Json.N(u,"input_tokens"),Cache=(long)Json.N(u,"cached_input_tokens"),Output=(long)Json.N(u,"output_tokens"),Total=(long)Json.N(u,"total_tokens")});
    } else if(type=="event_msg" && Json.S(p,"type")=="token_count") {
     var quota=Json.Get(p,"rate_limits"); if(quota!=null && time>=result.QuotaTime) { result.Quota=NormalizeQuota(quota); result.QuotaTime=time; }
     var u=Json.Get(Json.Get(p,"info"),"total_token_usage"); if(u==null)continue;
     long nt=(long)Json.N(u,"total_tokens"),ni=(long)Json.N(u,"input_tokens"),no=(long)Json.N(u,"output_tokens"),nc=(long)Json.N(u,"cached_input_tokens");
     // A decrease starts a fresh cumulative segment (e.g. context compaction).
     if(nt<total) { total=input=output=cached=0; }
     if(nt>total)legacy.Add(new Usage {Time=time,Model=model,Key=identity+":"+Json.S(j,"timestamp")+":"+nt,Input=Math.Max(0,ni-input),Cache=Math.Max(0,nc-cached),Output=Math.Max(0,no-output),Total=nt-total});
     total=nt;input=ni;output=no;cached=nc;
    }
   } catch { result.Bad++; }
  }
  result.Rows=result.Modern?modern:legacy; return result;
 }
 static object NormalizeWindow(object x) { if(x==null)return null;return new {usedPercent=Json.Get(x,"used_percent"),windowDurationMins=Json.Get(x,"window_minutes"),resetsAt=Json.Get(x,"resets_at")}; }
 static object NormalizeQuota(object q) { return Json.Read(Json.Write(new {limitId=Json.S(q,"limit_id"),limitName=Json.Get(q,"limit_name"),planType=Json.Get(q,"plan_type"),primary=NormalizeWindow(Json.Get(q,"primary")),secondary=NormalizeWindow(Json.Get(q,"secondary"))})); }
 public Snapshot Read(string home,DateTime from) {
  var snapshot=new Snapshot(); var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var sub in new[]{"sessions","archived_sessions"}) {
   string dir=Path.Combine(home,sub); if(!Directory.Exists(dir))continue; snapshot.Found=true;
   try { foreach(var f in Directory.EnumerateFiles(dir,"*.jsonl",SearchOption.AllDirectories))files.Add(f); } catch {snapshot.Unreadable++;}
  }
  foreach(var missing in cache.Keys.Where(k=>!files.Contains(k)).ToList())cache.Remove(missing);
  var seen=new HashSet<string>();
  foreach(var file in files) {
   try {
    var info=new FileInfo(file);
    // UTC-named session folders do not determine the accounting date. Include old sessions modified this week.
    if(info.LastWriteTime<from.AddDays(-1))continue;
    Entry entry;
    if(!cache.TryGetValue(file,out entry)||entry.Length!=info.Length||entry.Modified!=info.LastWriteTimeUtc) {
     using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
     using(var r=new StreamReader(stream,Encoding.UTF8))entry=new Entry{Length=info.Length,Modified=info.LastWriteTimeUtc,Parsed=Parse(r,Path.GetFileName(file))};
     cache[file]=entry;
    }
    var p=entry.Parsed; snapshot.Bad+=p.Bad;
    if(p.Quota!=null&&p.QuotaTime>snapshot.QuotaTime){snapshot.Quota=p.Quota;snapshot.QuotaTime=p.QuotaTime;}
    foreach(var row in p.Rows)if(row.Time>=from && row.Time<=DateTime.Now.AddMinutes(1)&&seen.Add(row.Key))snapshot.Rows.Add(row);
   } catch {snapshot.Unreadable++;}
  }
  snapshot.Rows=snapshot.Rows.OrderByDescending(r=>r.Time).ToList(); return snapshot;
 }
}
class Settings {
 public string CodexHome; public string Executable; public bool AutoRefresh=true;
 public static string FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsage","settings.json");
 public static Settings Load() {
  var s=new Settings {CodexHome=Environment.GetEnvironmentVariable("CODEX_HOME")};
  if(string.IsNullOrEmpty(s.CodexHome))s.CodexHome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
  try { var j=Json.Read(File.ReadAllText(FileName)); if(Json.S(j,"home")!="")s.CodexHome=Json.S(j,"home");s.Executable=Json.S(j,"exe");s.AutoRefresh=Json.Get(j,"auto") as bool? ?? true; }catch{}
  return s;
 }
 public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FileName));File.WriteAllText(FileName,Json.Write(new{home=CodexHome,exe=Executable,auto=AutoRefresh}),Encoding.UTF8); }
}
class AccountResult { public object Quotas; public DateTime Time; public string Error; }
class AccountClient : IDisposable {
 Process process; int counter; Dictionary<int,TaskCompletionSource<object>> pending=new Dictionary<int,TaskCompletionSource<object>>(); readonly object gate=new object(); string signature;
 public static string FindExe(string configured) {
  if(!string.IsNullOrEmpty(configured)&&File.Exists(configured))return configured;
  foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) { try {string p=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(p))return p;}catch{} }
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
  if(Directory.Exists(root)) {var f=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(f!=null)return f;}
  return null;
 }
 async Task<object> Call(string method,object args) {
  int id=Interlocked.Increment(ref counter);var tcs=new TaskCompletionSource<object>();
  lock(gate)pending[id]=tcs;
  try {
   process.StandardInput.WriteLine(Json.Write(new{id=id,method=method,@params=args}));process.StandardInput.Flush();
   if(await Task.WhenAny(tcs.Task,Task.Delay(15000))!=tcs.Task)throw new TimeoutException("连接超时");
   var response=await tcs.Task;
   if(Json.Get(response,"error")!=null)throw new InvalidOperationException("接口暂不可用，请确认 Codex 已登录");
   return Json.Get(response,"result");
  }finally{lock(gate)pending.Remove(id);}
 }
 public async Task<AccountResult> Read(Settings settings) {
  try {
   string exe=FindExe(settings.Executable);if(exe==null)return new AccountResult{Error="未找到 codex.exe，可在设置中指定"};
   if(process==null||process.HasExited||signature!=exe+settings.CodexHome) {
    Dispose();signature=exe+settings.CodexHome;
    var si=new ProcessStartInfo(exe,"app-server --listen stdio://") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(exe)};
    si.EnvironmentVariables["CODEX_HOME"]=settings.CodexHome;
    process=new Process{StartInfo=si,EnableRaisingEvents=true};
    process.OutputDataReceived+=(s,e)=>{if(e.Data==null)return;try {var j=Json.Read(e.Data); int id=(int)Json.N(j,"id");TaskCompletionSource<object> t;lock(gate){if(pending.TryGetValue(id,out t))t.TrySetResult(j);}}catch{}};
    process.ErrorDataReceived+=(s,e)=>{};
    process.Exited+=(s,e)=>{lock(gate){foreach(var t in pending.Values)t.TrySetException(new IOException("Codex 服务已退出"));}};
    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
    await Call("initialize",new{clientInfo=new{name="codex_usage_desktop",title="Codex Usage",version="1.1.0"}});
    process.StandardInput.WriteLine("{\"method\":\"initialized\"}");process.StandardInput.Flush();
   }
   var result=await Call("account/rateLimits/read",null);
   return new AccountResult{Quotas=result,Time=DateTime.Now};
  } catch(Exception ex) {Dispose();return new AccountResult{Error=ex is TimeoutException?"实时额度读取超时": "实时额度未连接，请确认 Codex 登录与网络"};}
 }
 public void Dispose() { var p=process;process=null;if(p!=null){try {if(!p.HasExited){p.StandardInput.Close();if(!p.WaitForExit(700))p.Kill();}}catch{}p.Dispose();} }
}
class App {
 Window window; Settings settings; AccountClient account=new AccountClient(); LogReader logs=new LogReader(); Snapshot snapshot=new Snapshot(); AccountResult live=new AccountResult();
 DispatcherTimer timer; Forms.NotifyIcon tray; bool busy,updatingFilter; int days=1;
 T C<T>(string name) where T:FrameworkElement {return (T)window.FindName(name);}
 static SolidColorBrush Brush(string color) {return (SolidColorBrush)new BrushConverter().ConvertFromString(color);}
 static TextBlock Text(string value,double size,string color) {return new TextBlock{Text=value,FontSize=size,Foreground=Brush(color),VerticalAlignment=VerticalAlignment.Center};}
 static string Number(long n) {return n>=100000000?(n/100000000.0).ToString("0.0")+" 亿":n>=10000?(n/10000.0).ToString("0.0")+" 万":n.ToString("N0");}
 [STAThread] public static void Main(string[] args) {
  if(args.Contains("--self-test")){Tests.Run();return;}
  if(args.Contains("--diagnose")){Diagnose();return;}
  bool created;using(var mutex=new Mutex(true,"Local\\CodexUsageDesktop",out created)){
   if(!created){MessageBox.Show("Codex 用量已在运行，请从系统托盘打开。","Codex 用量");return;}
   var application=new Application();application.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("操作失败，请稍后重试。\n"+e.Exception.GetType().Name,"Codex 用量");e.Handled=true;};
   var app=new App();app.Start();application.Run(app.window);
  }
 }
 void Start() {
  settings=Settings.Load(); using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))window=(Window)XamlReader.Load(stream);
  window.MaxHeight=SystemParameters.WorkArea.Height;window.Height=Math.Min(830,SystemParameters.WorkArea.Height-30);
  C<Grid>("TitleBar").MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource is TextBlock||e.OriginalSource==s)try{window.DragMove();}catch{}};
  C<Button>("CloseButton").Click+=(s,e)=>window.Close();C<Button>("HideButton").Click+=(s,e)=>window.Hide();
  C<Button>("PinButton").Click+=(s,e)=>{window.Topmost=!window.Topmost;C<Button>("PinButton").Content=window.Topmost?"已置顶":"置顶";};
  C<Button>("RefreshButton").Click+=async(s,e)=>await Refresh();
  C<Button>("TodayButton").Click+=(s,e)=>{days=1;RenderLocal();};C<Button>("WeekButton").Click+=(s,e)=>{days=7;RenderLocal();};
  C<ComboBox>("ModelFilter").SelectionChanged+=(s,e)=>{if(!updatingFilter)RenderLocal();};
  C<Button>("ExportButton").Click+=(s,e)=>Export();C<Button>("SettingsButton").Click+=(s,e)=>ShowSettings();
  tray=new Forms.NotifyIcon{Text="Codex 用量",Icon=MakeIcon(),Visible=true};tray.MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)ShowWindow();};
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开用量面板",null,(s,e)=>ShowWindow());menu.Items.Add("立即刷新",null,async(s,e)=>await Refresh());menu.Items.Add("退出",null,(s,e)=>window.Close());tray.ContextMenuStrip=menu;
  timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(60)};timer.Tick+=async(s,e)=>{if(settings.AutoRefresh)await Refresh();};timer.Start();
  window.Closed+=(s,e)=>{timer.Stop();tray.Visible=false;tray.Icon.Dispose();tray.Dispose();account.Dispose();};
  window.Loaded+=async(s,e)=>await Refresh();RenderQuotas();
 }
 static System.Drawing.Icon MakeIcon() {
  using(var bitmap=new System.Drawing.Bitmap(32,32))using(var g=System.Drawing.Graphics.FromImage(bitmap)){
   g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(System.Drawing.Color.Transparent);
   using(var b=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(75,201,255)))g.FillEllipse(b,1,1,30,30);
   using(var p=new System.Drawing.Pen(System.Drawing.Color.FromArgb(8,13,22),3)){g.DrawLines(p,new[]{new System.Drawing.Point(13,10),new System.Drawing.Point(7,16),new System.Drawing.Point(13,22)});g.DrawLines(p,new[]{new System.Drawing.Point(19,10),new System.Drawing.Point(25,16),new System.Drawing.Point(19,22)});}
   var handle=bitmap.GetHicon();try{return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
  }
 }
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
 void ShowWindow() {window.Show();window.WindowState=WindowState.Normal;window.Activate();}
 async Task Refresh() {
  if(busy)return;busy=true;C<Button>("RefreshButton").IsEnabled=false;C<Button>("RefreshButton").Content="同步中…";
  try {
   var fetch=account.Read(settings);
   snapshot=await Task.Run(()=>logs.Read(settings.CodexHome,DateTime.Today.AddDays(-6)));
   if(!window.IsLoaded)return;
   UpdateModels();RenderLocal();
   live=await fetch;
   if(window.IsLoaded)RenderQuotas();
  }catch{if(window.IsLoaded)C<TextBlock>("StatusText").Text="读取失败，请检查数据目录";}
  finally{busy=false;if(window.IsLoaded){C<Button>("RefreshButton").IsEnabled=true;C<Button>("RefreshButton").Content="↻  刷新";}}
 }
 void UpdateModels() {
  updatingFilter=true;var combo=C<ComboBox>("ModelFilter");string selected=combo.SelectedItem as string;
  var models=new[]{"全部模型"}.Concat(snapshot.Rows.Select(r=>r.Model).Distinct().OrderBy(x=>x)).ToList();combo.ItemsSource=models;combo.SelectedItem=models.Contains(selected)?selected:"全部模型";updatingFilter=false;
 }
 List<Usage> Filtered() {string m=C<ComboBox>("ModelFilter").SelectedItem as string;return snapshot.Rows.Where(r=>r.Time>=DateTime.Today.AddDays(1-days)&&(m==null||m=="全部模型"||r.Model==m)).ToList();}
 void RenderLocal() {
  var rows=Filtered();long input=rows.Sum(r=>r.Input),cache=rows.Sum(r=>r.Cache),total=rows.Sum(r=>r.Total);
  C<TextBlock>("RequestCount").Text=rows.Count.ToString("N0");C<TextBlock>("CacheRatio").Text=input>0?(100.0*cache/input).ToString("0.0")+"%":"—";C<TextBlock>("TokenTotal").Text=Number(total);C<TextBlock>("TokenTotal").ToolTip=total.ToString("N0")+" tokens";
  C<Button>("TodayButton").Background=Brush(days==1?"#163D5C":"#111B2B");C<Button>("WeekButton").Background=Brush(days==7?"#163D5C":"#111B2B");
  C<TextBlock>("LocalUpdated").Text="更新 "+DateTime.Now.ToString("HH:mm:ss");var panel=C<StackPanel>("UsageRows");panel.Children.Clear();
  foreach(var row in rows.Take(8)) {
   var g=new Grid{Height=29};foreach(var width in new[]{20.0,-1,80,76})g.ColumnDefinitions.Add(new ColumnDefinition{Width=width<0?new GridLength(1,GridUnitType.Star):new GridLength(width)});
   var dot=Text("●",9,"#4BC9FF");g.Children.Add(dot);var m=Text(row.Model,11,"#EAF2FF");m.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(m,1);g.Children.Add(m);
   if(panel.Children.Count%2==0)g.Background=Brush("#142034");
   var n=Text(Number(row.Total),11,"#BDD7F4");n.FontFamily=new FontFamily("Consolas");n.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(n,2);g.Children.Add(n);
   var time=Text(row.Time.ToString(days==1?"HH:mm:ss":"MM/dd HH:mm"),10,"#869CB7");time.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(time,3);g.Children.Add(time);
   g.ToolTip="输入 "+row.Input.ToString("N0")+" · 缓存 "+row.Cache.ToString("N0")+" · 输出 "+row.Output.ToString("N0");panel.Children.Add(g);
  }
  C<TextBlock>("EmptyState").Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;C<TextBlock>("EmptyState").Text=snapshot.Found?"所选时段暂无用量记录。使用 Codex 后会自动更新。":"未找到会话日志。请在设置中选择 Codex 数据目录。";
  C<TextBlock>("LocalNote").Text="仅统计本机已记录用量 · 不等于账户账单"+(snapshot.Unreadable>0?"\n有 "+snapshot.Unreadable+" 个文件或目录暂不可读":"")+(snapshot.Bad>0?"\n已跳过 "+snapshot.Bad+" 条不完整记录":"");
  var chart=C<Grid>("Chart");chart.Children.Clear();chart.ColumnDefinitions.Clear();int count=days==1?24:7;long[] buckets=new long[count];
  foreach(var r in rows){int index=days==1?r.Time.Hour:(r.Time.Date-DateTime.Today.AddDays(-6)).Days;if(index>=0&&index<count)buckets[index]+=r.Total;}
  long max=Math.Max(1,buckets.Max());
  for(int i=0;i<count;i++) {chart.ColumnDefinitions.Add(new ColumnDefinition());var bar=new Border{CornerRadius=new CornerRadius(2),Height=Math.Max(3,45.0*buckets[i]/max),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(2,0,2,0),Background=Brush(buckets[i]>0?"#4BC9FF":"#1D2D43"),ToolTip=(days==1?i.ToString("00")+":00":DateTime.Today.AddDays(i-6).ToString("MM/dd"))+" · "+buckets[i].ToString("N0")+" tokens"};Grid.SetColumn(bar,i);chart.Children.Add(bar);}
  C<TextBlock>("ChartTitle").Text=days==1?"今日用量分布":"近 7 天用量分布";C<TextBlock>("ChartStart").Text=days==1?"00:00":DateTime.Today.AddDays(-6).ToString("MM/dd");C<TextBlock>("ChartEnd").Text=days==1?"23:00":DateTime.Today.ToString("MM/dd");
 }
 void RenderQuotas() {
  var panel=C<StackPanel>("QuotaCards");panel.Children.Clear();bool real=live.Quotas!=null;
  var quotas=real?Json.D(Json.Get(live.Quotas,"rateLimitsByLimitId")):new Dictionary<string,object>();
  if(quotas.Count==0) {object q=real?Json.Get(live.Quotas,"rateLimits"):snapshot.Quota;if(q!=null)quotas["codex"]=q;}
  C<TextBlock>("ResetCount").Text=real&&Json.Get(live.Quotas,"rateLimitResetCredits")!=null?"可重置 "+Json.N(Json.Get(live.Quotas,"rateLimitResetCredits"),"availableCount")+" 次":"";
  foreach(var pair in quotas) {
   var q=pair.Value;var stack=new StackPanel();string name=Json.S(q,"limitName");if(name=="")name=Json.S(q,"limitId");if(name==""||name=="codex")name="Codex";
   string plan=Json.S(q,"planType");var heading=new Grid();var label=Text(name+(plan==""?"":"  "+CultureInfo.InvariantCulture.TextInfo.ToTitleCase(plan)),14,"#EDF5FF");label.FontWeight=FontWeights.SemiBold;heading.Children.Add(label);stack.Children.Add(heading);
   var stamp=real?live.Time:snapshot.QuotaTime;stack.Children.Add(new TextBlock{Text=(real?"实时同步":"日志快照")+" · "+stamp.ToString("MM/dd HH:mm:ss"),Foreground=Brush(real?"#849DBD":"#FFC178"),FontSize=10,Margin=new Thickness(0,5,0,12)});
   bool has=false;foreach(var key in new[]{"primary","secondary"}) {var w=Json.Get(q,key);if(w!=null){stack.Children.Add(QuotaWindow(w));has=true;}}
   if(!has)stack.Children.Add(Text("当前账户未返回额度窗口",12,"#90A5C0"));
   panel.Children.Add(new Border{Style=(Style)window.FindResource("Card"),Child=stack});
  }
  if(quotas.Count==0) {var s=new StackPanel();s.Children.Add(Text("Codex",14,"#EDF5FF"));s.Children.Add(new TextBlock{Text=busy?"正在读取账户额度…":"暂无额度数据\n请确认 Codex 已登录，再点击刷新。",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#90A5C0"),Margin=new Thickness(0,10,0,0)});panel.Children.Add(new Border{Style=(Style)window.FindResource("Card"),Child=s});}
  C<TextBlock>("StatusText").Text=real?"●  已连接 · "+(settings.AutoRefresh?"每 60 秒刷新":"自动刷新已暂停"):"●  "+(snapshot.Quota!=null?"本机快照 · 实时额度未连接":"等待额度连接");
  C<TextBlock>("StatusText").Foreground=Brush(real?"#4BC9FF":"#FFC178");C<TextBlock>("StatusText").ToolTip=live.Error??"账户额度来自 Codex";
 }
 FrameworkElement QuotaWindow(object w) {
  double minutes=Json.N(w,"windowDurationMins"),remaining=Math.Max(0,Math.Min(100,100-Json.N(w,"usedPercent")));bool known=Json.Get(w,"usedPercent")!=null;
  string label=minutes>=1440?(minutes/1440).ToString("0.#")+" 天额度":minutes>=60?(minutes/60).ToString("0.#")+" 小时额度":minutes>0?minutes+" 分钟额度":"额度窗口";
  var stack=new StackPanel{Margin=new Thickness(0,0,0,12)};
  string accent=remaining<=10?"#F07783":remaining<=25?"#FFBA69":"#4BC9FF";
  var header=new Grid{Margin=new Thickness(0,0,0,8)};header.Children.Add(Text(label,12,"#A6BCD9"));
  var right=Text(known?remaining.ToString("0.#")+"%":"—",27,accent);right.FontFamily=new FontFamily("Consolas");right.FontWeight=FontWeights.Bold;right.HorizontalAlignment=HorizontalAlignment.Right;right.ToolTip="剩余额度";header.Children.Add(right);stack.Children.Add(header);
  var g=new Grid{Height=5};g.Children.Add(new Border{Background=Brush("#223148"),CornerRadius=new CornerRadius(2)});
  var fill=new Border{Background=Brush(accent),CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left};
  g.SizeChanged+=(s,e)=>fill.Width=known?Math.Max(0,g.ActualWidth*remaining/100):0;g.Children.Add(fill);stack.Children.Add(g);
  string reset="重置时间未知";double unix=Json.N(w,"resetsAt");if(unix>0){try {var date=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();var delta=date-DateTime.Now;reset=(delta.TotalSeconds<=0?"已到重置时间，等待最新数据":delta.TotalDays>=1?(int)delta.TotalDays+" 天后重置":delta.TotalHours>=1?(int)delta.TotalHours+" 小时后重置":Math.Max(1,(int)delta.TotalMinutes)+" 分钟后重置")+" · "+date.ToString("MM/dd HH:mm");}catch{}}
  stack.Children.Add(new TextBlock{Text="◷  "+reset,Foreground=Brush("#8CA3C2"),FontSize=10,Margin=new Thickness(0,6,0,0)});return stack;
 }
 void Export() {
  var dialog=new Microsoft.Win32.SaveFileDialog{Filter="CSV 文件|*.csv",FileName="codex-usage-"+DateTime.Now.ToString("yyyyMMdd")+".csv"};if(dialog.ShowDialog(window)!=true)return;
  var sb=new StringBuilder("时间,模型,输入Tokens,缓存输入Tokens,输出Tokens,总Tokens\r\n");
  foreach(var r in Filtered())sb.AppendLine(string.Join(",",new[]{Csv(r.Time.ToString("yyyy-MM-dd HH:mm:ss")),Csv(r.Model),r.Input.ToString(),r.Cache.ToString(),r.Output.ToString(),r.Total.ToString()}));
  try{File.WriteAllText(dialog.FileName,sb.ToString(),new UTF8Encoding(true));C<TextBlock>("StatusText").Text="●  已导出所选时段与模型的记录";}catch{MessageBox.Show(window,"无法保存文件，请选择可写入的位置。","导出失败");}
 }
 static string Csv(string value) {if(value.Length>0&&"=+-@".Contains(value[0]))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
 void ShowSettings() {
  var menu=new ContextMenu();var auto=new MenuItem{Header="每 60 秒自动刷新",IsCheckable=true,IsChecked=settings.AutoRefresh};auto.Click+=(s,e)=>{settings.AutoRefresh=auto.IsChecked;settings.Save();RenderQuotas();};menu.Items.Add(auto);
  var folder=new MenuItem{Header="选择 Codex 数据目录…",IsEnabled=!busy};folder.Click+=async(s,e)=>{using(var d=new Forms.FolderBrowserDialog{Description="选择包含 sessions 的 .codex 目录",SelectedPath=settings.CodexHome}){if(d.ShowDialog()==Forms.DialogResult.OK){settings.CodexHome=d.SelectedPath;settings.Save();logs=new LogReader();await Refresh();}}};menu.Items.Add(folder);
  var exe=new MenuItem{Header="指定 codex.exe…",IsEnabled=!busy};exe.Click+=async(s,e)=>{var d=new Microsoft.Win32.OpenFileDialog{Filter="Codex 程序|codex.exe"};if(d.ShowDialog(window)==true){settings.Executable=d.FileName;settings.Save();await Refresh();}};menu.Items.Add(exe);
  var about=new MenuItem{Header="关于与统计口径"};about.Click+=(s,e)=>MessageBox.Show(window,"Codex 用量 1.1\n\n账户额度来自 Codex 官方接口；离线时显示带时间的日志快照。\n本机 Token 包含缓存输入，不代表账户账单。绿色圆点代表用量记录，不代表请求成功率。\n日志缺失时统计可能不完整。\n\n只读取用量，不发送提示词，不执行额度重置。\n数据目录："+settings.CodexHome,"关于 Codex 用量");menu.Items.Add(about);menu.PlacementTarget=C<Button>("SettingsButton");menu.IsOpen=true;
 }
 static void Diagnose() {
  var settings=Settings.Load();var logs=new LogReader().Read(settings.CodexHome,DateTime.Today.AddDays(-6));AccountResult live;using(var a=new AccountClient())live=a.Read(settings).GetAwaiter().GetResult();
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics.json"),Json.Write(new{records=logs.Rows.Count,tokens=logs.Rows.Sum(r=>r.Total),unreadable=logs.Unreadable,bad=logs.Bad,live=live.Quotas!=null,error=live.Error,quota=live.Quotas!=null?Json.Get(live.Quotas,"rateLimits"):logs.Quota}),Encoding.UTF8);
 }
}
static class Tests {
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(){var result=new StringBuilder();try{
  string t="2026-09-16T03:00:00Z";
  Func<long,string> legacy=n=>Json.Write(new{timestamp=t,type="event_msg",payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=n-10,cached_input_tokens=20,output_tokens=10,total_tokens=n}},rate_limits=new{limit_id="codex",primary=new{used_percent=34,window_minutes=300,resets_at=1789554076}}}});
  var p=LogReader.Parse(new StringReader(legacy(100)+"\n"+legacy(100)+"\n"+legacy(150)),"test");Assert(p.Rows.Count==2&&p.Rows.Sum(x=>x.Total)==150,"cumulative de-duplication");result.AppendLine("PASS cumulative deltas and duplicate token_count");
  Assert(Json.N(Json.Get(p.Quota,"primary"),"usedPercent")==34,"quota normalization");result.AppendLine("PASS quota normalization");
  string modern=Json.Write(new{timestamp=t,type="token_usage_record",payload=new{response_id="response-test",usage=new{input_tokens=40,cached_input_tokens=10,output_tokens=5,total_tokens=45}}});
  p=LogReader.Parse(new StringReader(legacy(100)+"\n"+modern),"test");Assert(p.Rows.Count==1&&p.Rows[0].Total==45,"mixed format double count");result.AppendLine("PASS prefer authoritative token_usage_record");
  p=LogReader.Parse(new StringReader(legacy(100)+"\n"+legacy(50)+"\n{\"type\":\"token_count\""),"test");Assert(p.Rows.Sum(x=>x.Total)==150&&p.Bad==1,"reset and truncated line");result.AppendLine("PASS counter reset and partial log");
  var dir=Path.Combine(Path.GetTempPath(),"codex-usage-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(dir,"sessions"));Directory.CreateDirectory(Path.Combine(dir,"archived_sessions"));
  try{var now=DateTimeOffset.Now.ToString("o");var current=modern.Replace(t,now);File.WriteAllText(Path.Combine(dir,"sessions","a.jsonl"),current);File.WriteAllText(Path.Combine(dir,"archived_sessions","b.jsonl"),current);var reader=new LogReader();var s=reader.Read(dir,DateTime.Today);Assert(s.Rows.Count==1,"response dedupe across files");result.AppendLine("PASS response deduplication across session/archive");File.AppendAllText(Path.Combine(dir,"sessions","a.jsonl"),"\n"+current.Replace("response-test","response-second"));s=reader.Read(dir,DateTime.Today);Assert(s.Rows.Count==2,"cache invalidation");result.AppendLine("PASS changed-file refresh");}finally{foreach(var f in Directory.GetFiles(dir,"*.jsonl",SearchOption.AllDirectories))File.Delete(f);Directory.Delete(Path.Combine(dir,"sessions"));Directory.Delete(Path.Combine(dir,"archived_sessions"));Directory.Delete(dir);}
  Assert(new LogReader().Read(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()),DateTime.Today).Found==false,"missing directory");result.AppendLine("PASS missing data directory");result.AppendLine("ALL 7 CHECKS PASSED");
 }catch(Exception ex){result.AppendLine("FAIL: "+ex.Message);Environment.ExitCode=1;}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),result.ToString());}
}
}
