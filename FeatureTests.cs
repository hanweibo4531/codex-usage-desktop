using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace CodexUsage {
static class FeatureTests {
 static void Check(bool ok,string label) {if(!ok)throw new Exception(label);}
 static AccountResult State(int? count,string account) {
  return new AccountResult{Time=DateTime.Now,Quotas=Json.Read(Json.Write(new{accountId=account,rateLimitResetCredits=count.HasValue?new{availableCount=count.Value}:null}))};
 }
 static object Outcome(string outcome) {return Json.Read(Json.Write(new{outcome=outcome}));}
 static void Fails(Action action,string label) {bool failed=false;try{action();}catch{failed=true;}Check(failed,label);}
 public static void Run(StringBuilder output) {
  string dir=Path.Combine(Path.GetTempPath(),"codex-feature-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  try {
   var moment=new DateTime(2026,9,17,12,0,0,DateTimeKind.Utc);
   Func<int,object> weekUsage=p=>Json.Read(Json.Write(new{plan_type="pro",rate_limit=new{secondary_window=new{used_percent=p,limit_window_seconds=604800,reset_at=1789948800}}}));
   var daily=Json.Read("{\"data\":[{\"date\":\"2026-09-17\",\"totals\":{\"credits\":30160.6,\"text_total_tokens\":123000,\"turns\":8}},{\"date\":\"2026-08-19\",\"totals\":{\"credits\":25}},{\"date\":\"2026-08-18\",\"totals\":{\"credits\":99}}]}");
   var week=WeeklyQuota.Parse(weekUsage(83),daily,moment);
   Check(week.UsedCredits==30160.6m&&Math.Round(week.TotalCredits.Value,1)==36338.1m&&Math.Round(week.Dollars.Value,2)==1453.52m,"reference screenshot weekly estimate");
   Check(week.Days.Count==2&&week.Days.Sum(d=>d.Credits)==30185.6m,"30-day range includes day 30 and excludes day 31");
   Check(!WeeklyQuota.Parse(weekUsage(0),daily,moment).TotalCredits.HasValue,"zero percentage never divides");
   Check(!WeeklyQuota.Parse(weekUsage(101),daily,moment).TotalCredits.HasValue,"invalid percentage");
   Check(!WeeklyQuota.Parse(weekUsage(83),Json.Read("{}"),moment).UsedCredits.HasValue,"missing credits not zero");
   var duplicates=Json.Read("{\"data\":[{\"date\":\"2026-09-17\",\"totals\":{\"credits\":1}},{\"date\":\"2026-09-17\",\"totals\":{\"credits\":1}}]}");
   Check(WeeklyQuota.Parse(weekUsage(83),duplicates,moment).Days.Count==0,"duplicate dates rejected without partial totals");
   Check(!WeeklyQuota.Parse(weekUsage(83),daily,moment.AddDays(8)).TotalCredits.HasValue,"expired cycle rejected");
    output.AppendLine("PASS weekly estimate matches screenshot; 30-day boundary, missing data, duplicate and reset guards");
    string quotaLog="{\"timestamp\":\"2026-09-17T00:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"credits\":{\"balance\":\"2500\",\"has_credits\":true,\"unlimited\":false}}}}";
    var historicalCredits=Json.Get(LogReader.Parse(new StringReader(quotaLog),"credit-test").Quota,"credits");
    Check(Json.S(historicalCredits,"balance")=="2500"&&Json.Get(historicalCredits,"hasCredits") as bool? ==true,"log credits normalization");
    output.AppendLine("PASS log credits normalization");
    var state=State(3,"test-account");
   foreach(string outcome in new[]{"reset","alreadyRedeemed","nothingToReset","noCredit"}) {
    ResetTicket saved=null;var coordinator=new ResetCoordinator(null,t=>saved=t);
    string result=coordinator.Execute(state,dir,k=>{Check(saved!=null&&saved.Key==k,"persist before send");return Task.FromResult(Outcome(outcome));}).GetAwaiter().GetResult();
    Check(result==outcome&&saved==null&&coordinator.Pending==null,"known reset result "+outcome);
   }
   output.AppendLine("PASS all four reset outcomes and persist-before-send");

   var store=new ResetStore(Path.Combine(dir,"pending-reset.json"));var first=new ResetCoordinator(null,store.Save);string sentKey=null;
   Fails(()=>first.Execute(state,dir,k=>{sentKey=k;var t=new TaskCompletionSource<object>();t.SetException(new TimeoutException());return t.Task;}).GetAwaiter().GetResult(),"timeout");
   var restarted=new ResetCoordinator(store.Load(),store.Save);
   Check(restarted.Pending!=null&&restarted.Pending.Key==sentKey,"persist timeout across restart");
   string retriedKey=null;restarted.Execute(State(0,"test-account"),dir,k=>{retriedKey=k;return Task.FromResult(Outcome("alreadyRedeemed"));}).GetAwaiter().GetResult();
   Check(sentKey==retriedKey&&store.Load()==null,"same key retry even when remaining count is zero");
   output.AppendLine("PASS timeout/restart retry reuses key without a new redemption");

   int calls=0;var failedStorage=new ResetCoordinator(null,t=>{throw new IOException();});
   Fails(()=>failedStorage.Execute(state,dir,k=>{calls++;return Task.FromResult(Outcome("reset"));}).GetAwaiter().GetResult(),"persist failure");
   Check(calls==0,"never send without durable intent");output.AppendLine("PASS storage failure prevents mutation");

   var waiting=new TaskCompletionSource<object>();var busy=new ResetCoordinator(null,t=>{});
   var operation=busy.Execute(state,dir,k=>{calls++;return waiting.Task;});int before=calls;
   Fails(()=>busy.Execute(state,dir,k=>{calls++;return Task.FromResult(Outcome("reset"));}).GetAwaiter().GetResult(),"parallel reset");
   Check(calls==before,"double click blocked");waiting.SetResult(Outcome("reset"));operation.GetAwaiter().GetResult();
   output.AppendLine("PASS concurrent redemption blocked");

   var plain=new ResetCoordinator(null,t=>{});
   Check(plain.UnavailableReason(new AccountResult(),dir)!=null,"offline");
   Check(plain.UnavailableReason(State(0,"test-account"),dir)!=null,"zero credits");
   Check(plain.UnavailableReason(State(null,"test-account"),dir)!=null,"unknown credits");
   Check(plain.UnavailableReason(State(1,""),dir)!=null,"unknown account");
   var pending=new ResetCoordinator(new ResetTicket{Key=Guid.NewGuid().ToString(),AccountId="other-account",Home=dir},t=>{});
   Check(pending.UnavailableReason(state,dir)!=null,"account mismatch");
   pending=new ResetCoordinator(new ResetTicket{Key=Guid.NewGuid().ToString(),AccountId="test-account",Home=dir+"-other"},t=>{});
   Check(pending.UnavailableReason(state,dir)!=null,"directory mismatch");
   output.AppendLine("PASS offline, missing credits, account and directory guards");

   var unknown=new ResetCoordinator(null,store.Save);
   Fails(()=>unknown.Execute(state,dir,k=>Task.FromResult(Outcome("unexpected"))).GetAwaiter().GetResult(),"unknown response");
   Check(store.Load().Key==unknown.Pending.Key,"unknown outcome preserves key");store.Save(null);
   File.WriteAllText(Path.Combine(dir,"pending-reset.json"),"{}");Fails(()=>store.Load(),"corrupt reset state");
   output.AppendLine("PASS unknown outcome retained and corrupted state rejected");

   string previous=Settings.FileName;
   try {Settings.FileName=Path.Combine(dir,"settings.json");new Settings{CodexHome=dir,Theme="light"}.Save();Check(Settings.Load().Theme=="light"&&!Settings.Load().AccountCredits,"theme persistence and credits opt-in default");new Settings{CodexHome=dir,AccountCredits=true}.Save();Check(Settings.Load().AccountCredits,"explicit credits opt-in persists");}
   finally{Settings.FileName=previous;}
   output.AppendLine("PASS saved theme restored");

   string xaml;using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")))xaml=r.ReadToEnd();
   var window=(Window)XamlReader.Parse(xaml);
   Theme.Apply(window,"dark");
   foreach(Match match in Regex.Matches(xaml,@"\{DynamicResource (Brush[0-9A-Fa-f]{6})\}"))Check(window.Resources.Contains(match.Groups[1].Value),"unmapped theme token");
   var dark=((SolidColorBrush)window.Resources["Brush080D16"]).Color;
   Check(window.FindName("MonthButton")!=null,"month filter exists");
   foreach(double width in new[]{390.0,660.0}) {
    var weeklyCard=App.BuildWeeklyQuota(week);weeklyCard.Measure(new Size(width,double.PositiveInfinity));weeklyCard.Arrange(new Rect(0,0,width,weeklyCard.DesiredSize.Height));weeklyCard.UpdateLayout();
    Check(weeklyCard.ActualWidth==width,"weekly card width");
   }
    // Exercise the actual local filter and chart at the 30-day boundary without network access.
    var instance=new App();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
    typeof(App).GetField("window",flags).SetValue(instance,window);
    var boundary=DateTime.Today;
    var samples=new[]{new Usage{Time=boundary.AddSeconds(-1),Total=1},new Usage{Time=boundary,Total=2},new Usage{Time=boundary.AddHours(5),Total=4}};
    Check(App.WindowRows(samples,boundary,boundary.AddHours(5)).Sum(r=>r.Total)==2,"quota windows use inclusive start and exclusive end");
    Check(!App.QuotaReset(Json.Read("{\"resetsAt\":1e100}")).HasValue,"invalid reset timestamp rejected");
    var fixture=new Snapshot{Found=true};fixture.Rows.AddRange(samples);
    typeof(App).GetField("snapshot",flags).SetValue(instance,fixture);
    foreach(string palette in new[]{"light","dark"}) {
     Theme.Apply(window,palette);
     foreach(double width in new[]{410.0,680.0}) {
      var sample=Json.Read(Json.Write(new{usedPercent=85,windowDurationMins=10080,resetsAt=(DateTime.UtcNow.AddDays(2)-new DateTime(1970,1,1)).TotalSeconds}));
      var card=(FrameworkElement)typeof(App).GetMethod("BuildQuotaWindow",flags).Invoke(instance,new[]{sample});
      card.Measure(new Size(width,double.PositiveInfinity));card.Arrange(new Rect(0,0,width,card.DesiredSize.Height));card.UpdateLayout();
      card.Measure(new Size(width,double.PositiveInfinity));card.Arrange(new Rect(0,0,width,card.DesiredSize.Height));card.UpdateLayout();
      Check(card.ActualWidth==width,"quota card responsive width");
      var body=(StackPanel)((Border)card).Child;var columns=(System.Windows.Controls.Primitives.UniformGrid)body.Children[2];
      Check(columns.Columns==(width<540?1:3),"quota detail columns adapt to window width");
      var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)width,(int)Math.Ceiling(card.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(card);
      var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
      using(var image=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quota-"+palette+"-"+width+".png")))encoder.Save(image);
     }
    }
    Theme.Apply(window,"dark");
    output.AppendLine("PASS quota window boundaries, invalid timestamps, responsive cards and both themes");
    var table=instance.BuildCreditTable(week.Days);Check(table.Children.Count==3,"monthly table has header, scroll area and totals");
    Check(((ScrollViewer)table.Children[1]).Style==(Style)window.FindResource("SlimScrollViewer"),"history table uses slim scrollbar");
    typeof(App).GetField("days",flags).SetValue(instance,30);
   var local=new Snapshot();local.Rows.Add(new Usage{Time=DateTime.Today.AddDays(-29),Model="test",Total=10});local.Rows.Add(new Usage{Time=DateTime.Today.AddDays(-30),Model="test",Total=90});
   typeof(App).GetField("snapshot",flags).SetValue(instance,local);
   typeof(App).GetMethod("RenderLocal",flags).Invoke(instance,null);
    Check(((TextBlock)window.FindName("TokenTotal")).Text=="10"&&((Grid)window.FindName("Chart")).ColumnDefinitions.Count==30,"month filter and 30 chart buckets");
    output.AppendLine("PASS monthly local filter, daily chart and account history table");
    Check(window.FindName("TabQuotaButton")!=null&&window.FindName("TabLocalButton")!=null&&window.FindName("QuotaScroll")!=null&&window.FindName("LocalScroll")!=null,"module tabs and pages exist");
    typeof(App).GetMethod("SwitchModule",flags).Invoke(instance,new object[]{"local"});
    Check(((ScrollViewer)window.FindName("QuotaScroll")).Visibility==Visibility.Collapsed&&((ScrollViewer)window.FindName("LocalScroll")).Visibility==Visibility.Visible,"local module shown");
    typeof(App).GetMethod("SwitchModule",flags).Invoke(instance,new object[]{"quota"});
    Check(((ScrollViewer)window.FindName("QuotaScroll")).Visibility==Visibility.Visible&&((ScrollViewer)window.FindName("LocalScroll")).Visibility==Visibility.Collapsed,"quota module restored");
    output.AppendLine("PASS tabbed modules switch account and local views");
    Theme.Apply(window,"light");var light=((SolidColorBrush)window.Resources["Brush080D16"]).Color;
   Check(light.R>dark.R&&Theme.Resolve("#EAF2FF")=="#172B46","light palette");
   Check(((SolidColorBrush)((Border)window.Content).Background).Color==light,"live light binding");
   foreach(bool retry in new[]{false,true}) {
    var dialog=App.BuildResetDialog(window,retry);
    var buttons=((StackPanel)((StackPanel)dialog.Content).Children[2]).Children.OfType<Button>().ToArray();
    Check(buttons[0].IsCancel&&buttons[0].IsDefault&&!buttons[1].IsDefault,"safe confirmation default");
    Check(((SolidColorBrush)dialog.Background).Color==(Color)ColorConverter.ConvertFromString(Theme.Resolve("#111B2B")),"confirmation theme");dialog.Close();
   }
   output.AppendLine("PASS first-attempt and retry confirmation default to cancel");
   foreach(string theme in new[]{"dark","light"}) {
    Theme.Apply(window,theme);var dialog=ScheduleDialog.Build(window,new RequestSchedule(),()=>{});
    var content=(StackPanel)dialog.Content;content.Measure(new Size(422,double.PositiveInfinity));
    Check(content.DesiredSize.Height<760,"schedule dialog fits common display height");
     Check(content.Children.OfType<TextBox>().Count()==2&&content.Children.OfType<CheckBox>().Count()==2,"schedule times/model and enable/wake controls");
     Check(dialog.TryFindResource(typeof(TextBox))!=null&&dialog.TryFindResource(typeof(CheckBox))!=null,"schedule inputs use themed styles");
    dialog.Close();
   }
   output.AppendLine("PASS schedule settings controls and both theme layouts");
   Theme.Apply(window,"dark");Check(Theme.Resolve("#EAF2FF")=="#EAF2FF","dark palette restored");
   Check(((SolidColorBrush)((Border)window.Content).Background).Color==dark,"live dark binding");window.Close();
   output.AppendLine("PASS XAML theme resources and both palettes");

   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))using(var r=new BinaryReader(s)){
    Check(r.ReadUInt16()==0&&r.ReadUInt16()==1&&r.ReadUInt16()==7,"seven-size icon resource");
   }
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))using(var icon=new System.Drawing.Icon(s,32,32))Check(icon.Width==32,"tray icon readable");
   output.AppendLine("PASS embedded multi-size Windows and tray icon");
  }finally {
   // The directory is generated above, and only files created by these checks are removed.
   foreach(var file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);
  }
 }
}
}
