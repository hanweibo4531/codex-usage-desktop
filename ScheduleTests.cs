using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace CodexUsage {
static class ScheduleTests {
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 static void Fails(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
 public static void Run(StringBuilder output) {
  var now=new DateTime(2026,9,18,5,0,10);var options=new RequestSchedule{Enabled=true,EnabledFrom=now.Date.AddDays(-1)};
  Check(string.Join(",",RequestSchedule.ParseTimes("15:00，05:00 10:00"))=="05:00,10:00,15:00","schedule canonical order");
  foreach(string invalid in new[]{"","5:00","24:00","05:60","05:00,05:00","05:00 & cmd"})Fails(()=>RequestSchedule.ParseTimes(invalid),"invalid schedule "+invalid);
  Fails(()=>new RequestSchedule{Model="a --dangerously-bypass-approvals-and-sandbox"}.Validate(),"model argument injection");
  var rows=new List<RequestRun>();Check(options.Due(now,rows)==now.Date.AddHours(5),"05:00 request due");
  Check(options.Due(now.Date.AddHours(5).AddMinutes(2),rows)==null,"two-minute boundary skips late request");
  Check(options.Due(now.Date.AddHours(8),rows)==null,"08:00 startup never catches up 05:00");
  Check(options.Next(now,rows)==now.Date.AddHours(10),"next request 10:00");
  Check(options.Next(now.Date.AddHours(20),rows)==now.Date.AddDays(1).AddHours(5),"next request tomorrow");
  var recent=new RequestSchedule{Enabled=true,EnabledFrom=now};Check(recent.Due(now,rows)==null,"saving at 05:00:10 does not send immediately");
  options.Enabled=false;Check(!options.Due(now,rows).HasValue&&!options.Next(now,rows).HasValue,"disabled never sends");options.Enabled=true;
  int sends=0,persists=0;
  bool ran=ScheduledRequest.Run(options,now,rows,list=>{persists++;Check(list.Count==1,"one intent");},()=>{Check(persists==1&&rows[0].Status=="running","durable intent before request");sends++;return new RequestRun{Status="success",Message="test"};});
  Check(ran&&sends==1&&persists==2&&rows[0].Status=="success","successful scheduled send recorded");
  Check(!ScheduledRequest.Run(options,now.AddSeconds(20),rows,list=>{},()=>{sends++;return null;})&&sends==1,"duplicate timer and scheduler invocation suppressed");
  Check(options.Due(now.AddHours(5),rows).HasValue,"second daily slot independent");
  var pending=new List<RequestRun>{new RequestRun{Slot=RequestSchedule.Key(now.Date.AddHours(5)),Status="running",Time=now}};
  Check(!options.Due(now,pending).HasValue,"restart after uncertain request does not retry");
  var failure=new List<RequestRun>();ScheduledRequest.Run(options,now,failure,list=>{},()=>{throw new IOException();});Check(failure[0].Status=="unknown"&&!options.Due(now,failure).HasValue,"failed request not retried");
  Fails(()=>ScheduledRequest.Run(options,now,new List<RequestRun>(),list=>{throw new IOException();},()=>{sends++;return null;}),"storage failure");Check(sends==1,"no request without durable intent");
  var midnight=new RequestSchedule{Enabled=true,Times="23:59",EnabledFrom=now.Date.AddDays(-1)};
  Check(midnight.Due(now.Date.AddSeconds(30),new List<RequestRun>())==now.Date.AddDays(-1).AddHours(23).AddMinutes(59),"midnight grace and previous-day key");
  output.AppendLine("PASS scheduled times, next-run, grace, disabled state, persist-before-send and duplicate/restart guards");
  string dir=Path.Combine(Path.GetTempPath(),"codex-schedule-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string previous=Settings.FileName;
  try {
   Settings.FileName=Path.Combine(dir,"settings.json");options.Save();var restored=RequestSchedule.Load();Check(restored.Enabled&&restored.Times=="05:00, 10:00, 15:00","schedule persistence");
   RequestHistory.Save(RequestSchedule.StateFile,rows);var persisted=RequestHistory.Load(RequestSchedule.StateFile);Check(!restored.Due(now,persisted).HasValue,"disk deduplication");
   File.WriteAllText(RequestSchedule.StateFile,"{}");Fails(()=>RequestHistory.Load(RequestSchedule.StateFile),"corrupt state fails closed");
   File.WriteAllText(RequestSchedule.FileName,"{}");Fails(()=>RequestSchedule.Load(),"corrupt schedule fails closed");
  }finally {Settings.FileName=previous;foreach(var path in Directory.GetFiles(dir))File.Delete(path);Directory.Delete(dir);}
  string xml=WindowsRequestTask.Xml(options,@"C:\A & B\CodexUsage.exe","S-1-5-21-test",now.Date);var doc=new XmlDocument();doc.LoadXml(xml);var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");
  Check(doc.SelectNodes("//t:CalendarTrigger",ns).Count==3,"three Windows daily triggers");
  Check(doc.SelectSingleNode("//t:Command",ns).InnerText==@"C:\A & B\CodexUsage.exe","XML executable escaping");
  Check(doc.SelectSingleNode("//t:RunLevel",ns).InnerText=="LeastPrivilege"&&doc.SelectSingleNode("//t:LogonType",ns).InnerText=="InteractiveToken","no passwords or elevated task");
  Check(doc.SelectSingleNode("//t:Arguments",ns).InnerText=="--scheduled-request"&&doc.SelectSingleNode("//t:StartWhenAvailable",ns).InnerText=="false","worker and no delayed catch-up");
  Check(ScheduledRequest.Arguments("").Contains("read-only")&&ScheduledRequest.Arguments("").Contains("--ignore-user-config")&&ScheduledRequest.Arguments("").Contains("--ephemeral"),"isolated lightweight request flags");
  Check(ScheduledRequest.Quote("a b\\")=="\"a b\\\\\""&&ScheduledRequest.Quote("a\"b")=="\"a\\\"b\"","Windows argument quoting");
  output.AppendLine("PASS schedule persistence, corrupt-state protection, Task Scheduler XML and CLI argument isolation");
 }
}
}
