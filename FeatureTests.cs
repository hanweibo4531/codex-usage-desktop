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
   try {Settings.FileName=Path.Combine(dir,"settings.json");new Settings{CodexHome=dir,Theme="light"}.Save();Check(Settings.Load().Theme=="light","theme persistence");}
   finally{Settings.FileName=previous;}
   output.AppendLine("PASS saved theme restored");

   string xaml;using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")))xaml=r.ReadToEnd();
   var window=(Window)XamlReader.Parse(xaml);
   Theme.Apply(window,"dark");
   foreach(Match match in Regex.Matches(xaml,@"\{DynamicResource (Brush[0-9A-Fa-f]{6})\}"))Check(window.Resources.Contains(match.Groups[1].Value),"unmapped theme token");
   var dark=((SolidColorBrush)window.Resources["Brush080D16"]).Color;
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
