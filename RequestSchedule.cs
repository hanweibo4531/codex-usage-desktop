using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexUsage {
class RequestSchedule {
 public bool Enabled,WakeComputer;
 public string Times="05:00, 10:00, 15:00",Model="";
 public DateTime EnabledFrom;
 public static string DirectoryName {get{return Path.GetDirectoryName(Settings.FileName);}}
 public static string FileName {get{return Path.Combine(DirectoryName,"request-schedule.json");}}
 public static string StateFile {get{return Path.Combine(DirectoryName,"request-schedule-state.json");}}
 internal const string MutexName="Local\\CodexUsageScheduledRequest";
 public static string[] ParseTimes(string value) {
  var times=(value??"").Split(new[]{',','，',';','；',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
  if(times.Length<1||times.Length>12)throw new FormatException("请输入 1 至 12 个时间，格式为 HH:mm，用逗号分隔。");
  foreach(string time in times)if(!Regex.IsMatch(time,@"^([01][0-9]|2[0-3]):[0-5][0-9]$"))throw new FormatException("时间须为 24 小时制 HH:mm，例如 05:00。");
  if(times.Distinct().Count()!=times.Length)throw new FormatException("请勿填写重复的时间。");
  return times.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
 }
 public void Validate() {
  Times=string.Join(", ",ParseTimes(Times));Model=(Model??"").Trim();
  if(Model!=""&&!Regex.IsMatch(Model,@"^[A-Za-z0-9_.-]{1,100}$"))throw new FormatException("模型名只能包含字母、数字、点、横线和下划线。");
 }
 public static RequestSchedule Load() {
  if(!File.Exists(FileName))return new RequestSchedule();
  var j=Json.Read(File.ReadAllText(FileName));DateTime enabled;
  var result=new RequestSchedule{Enabled=Json.Get(j,"enabled") as bool? ?? false,WakeComputer=Json.Get(j,"wake") as bool? ?? false,Times=Json.S(j,"times"),Model=Json.S(j,"model")};
  if(result.Times=="")throw new InvalidDataException("定时配置不完整，请重新保存。");
  if(!DateTime.TryParse(Json.S(j,"enabledFrom"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out enabled))throw new InvalidDataException("定时启用时间无法读取，请重新保存。");
  result.EnabledFrom=enabled;result.Validate();return result;
 }
 public void Save() {Validate();AtomicWrite(FileName,Json.Write(new{enabled=Enabled,wake=WakeComputer,times=Times,model=Model,enabledFrom=EnabledFrom.ToString("o",CultureInfo.InvariantCulture)}));}
 internal static void AtomicWrite(string path,string text) {
  Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {byte[] bytes=Encoding.UTF8.GetBytes(text);using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  finally {if(File.Exists(temp))File.Delete(temp);}
 }
 public DateTime? Next(DateTime now,IEnumerable<RequestRun> runs) {
  if(!Enabled)return null;var used=new HashSet<string>(runs.Select(r=>r.Slot));
  foreach(int day in new[]{0,1,2})foreach(string time in ParseTimes(Times)) {
   DateTime slot=now.Date.AddDays(day).Add(TimeSpan.ParseExact(time,@"hh\:mm",CultureInfo.InvariantCulture));
   if(slot>now&&slot>=EnabledFrom&&!used.Contains(Key(slot)))return slot;
  }
  return null;
 }
 internal static string Key(DateTime slot) {return slot.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);}
 public DateTime? Due(DateTime now,IEnumerable<RequestRun> runs) {
  if(!Enabled)return null;var used=new HashSet<string>(runs.Select(r=>r.Slot));
  // Small grace for Task Scheduler/startup latency; never catch up hours of missed requests.
  foreach(int day in new[]{0,-1})foreach(string time in ParseTimes(Times).Reverse()) {
   DateTime slot=now.Date.AddDays(day).Add(TimeSpan.ParseExact(time,@"hh\:mm",CultureInfo.InvariantCulture));
   if(slot>=EnabledFrom&&slot<=now&&now-slot<TimeSpan.FromMinutes(2)&&!used.Contains(Key(slot)))return slot;
  }
  return null;
 }
}
class RequestRun {public string Slot,Status,Message;public DateTime Time;}
static class RequestHistory {
 public static List<RequestRun> Load(string path) {
  if(!File.Exists(path))return new List<RequestRun>();
  var rows=Json.Get(Json.Read(File.ReadAllText(path)),"runs") as object[];
  if(rows==null)throw new InvalidDataException("定时记录损坏，为防止重复发送已暂停。");
  var result=new List<RequestRun>();var seen=new HashSet<string>();
  foreach(var row in rows) {
   DateTime time,slot;string key=Json.S(row,"slot"),status=Json.S(row,"status");
   if(!DateTime.TryParseExact(key,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out slot)||!seen.Add(key)||!DateTime.TryParse(Json.S(row,"time"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time)||!new[]{"running","success","failed","unknown"}.Contains(status))throw new InvalidDataException("定时记录损坏，为防止重复发送已暂停。");
   result.Add(new RequestRun{Slot=key,Status=status,Message=Json.S(row,"message"),Time=time});
  }
  return result;
 }
 public static void Save(string path,List<RequestRun> rows) {RequestSchedule.AtomicWrite(path,Json.Write(new{runs=rows.OrderByDescending(r=>r.Slot,StringComparer.Ordinal).Take(360).Select(r=>new{slot=r.Slot,status=r.Status,message=r.Message,time=r.Time.ToString("o",CultureInfo.InvariantCulture)}).ToArray()}));}
}
static class ScheduledRequest {
 internal static string Quote(string arg) {
  var s=new StringBuilder("\"");int slashes=0;
  foreach(char c in arg){if(c=='\\'){slashes++;continue;}if(c=='\"'){s.Append('\\',slashes*2+1);s.Append(c);}else{s.Append('\\',slashes);s.Append(c);}slashes=0;}
  s.Append('\\',slashes*2);return s.Append('"').ToString();
 }
 internal static string Arguments(string model) {
  var args=new List<string>{"exec","--ignore-user-config","--ephemeral","--skip-git-repo-check","--sandbox","read-only","--color","never","--json","-c","forced_login_method=\"chatgpt\"","-c","approval_policy=\"never\"","-c","features.shell_tool=false","-c","project_doc_max_bytes=0"};
  if(!string.IsNullOrEmpty(model)){args.Add("--model");args.Add(model);}args.Add("-");return string.Join(" ",args.Select(Quote));
 }
 internal static RequestRun Send(Settings settings,string model) {
  string exe=AccountClient.FindExe(settings.Executable);
  if(exe==null)return new RequestRun{Status="failed",Message="未找到 codex.exe，请在设置中指定"};
  string work=Path.Combine(RequestSchedule.DirectoryName,"request-workspace");Directory.CreateDirectory(work);
  var info=new ProcessStartInfo(exe,Arguments(model)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  info.EnvironmentVariables["CODEX_HOME"]=settings.CodexHome;
  foreach(string key in new[]{"OPENAI_API_KEY","CODEX_API_KEY","OPENAI_BASE_URL"})info.EnvironmentVariables.Remove(key);
  int completed=0,failed=0;
  using(var process=new Process{StartInfo=info}) {
   process.OutputDataReceived+=(s,e)=>{if(e.Data==null)return;try{string type=Json.S(Json.Read(e.Data),"type");if(type=="turn.completed")Interlocked.Exchange(ref completed,1);if(type=="turn.failed"||type=="error")Interlocked.Exchange(ref failed,1);}catch{}};
   process.ErrorDataReceived+=(s,e)=>{};
   try {
    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
    process.StandardInput.WriteLine("Reply with exactly OK. Do not use tools, read files, or perform any other task.");process.StandardInput.Close();
    if(!process.WaitForExit(120000)){try{process.Kill();process.WaitForExit(3000);}catch{}return new RequestRun{Status="unknown",Message="请求超时，结果未确认；此时间点不再重试"};}
    process.WaitForExit();
    return process.ExitCode==0&&completed==1&&failed==0?new RequestRun{Status="success",Message="请求已完成；实际额度重置时间以账户为准"}:new RequestRun{Status="failed",Message="请求未完成，请检查 Codex 登录、模型和网络；此时间点不再重试"};
   }catch {try{if(!process.HasExited)process.Kill();}catch{}return new RequestRun{Status="unknown",Message="请求启动或连接异常，此时间点不再重试"};}
  }
 }
 internal static bool Run(RequestSchedule options,DateTime now,List<RequestRun> rows,Action<List<RequestRun>> persist,Func<RequestRun> send) {
  var due=options.Due(now,rows);if(!due.HasValue)return false;
  var run=new RequestRun{Slot=RequestSchedule.Key(due.Value),Time=now,Status="running",Message="已开始请求；异常中断时不重复发送"};rows.Add(run);
  persist(rows); // Persist intent before launching a model request.
  try {var result=send();run.Status=result.Status;run.Message=result.Message;}catch{run.Status="unknown";run.Message="执行结果未确认，此时间点不再重试";}
  persist(rows);return true;
 }
 public static Task<bool> RunDue() {return Task.Run(()=> {
  using(var mutex=new Mutex(false,RequestSchedule.MutexName)) {
   bool held=false;try {
    try {held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)return false;
    var schedule=RequestSchedule.Load();var rows=RequestHistory.Load(RequestSchedule.StateFile);var settings=Settings.Load();
    return Run(schedule,DateTime.Now,rows,r=>RequestHistory.Save(RequestSchedule.StateFile,r),()=>Send(settings,schedule.Model));
   }finally{if(held)mutex.ReleaseMutex();}
  }
 });}
}
static class WindowsRequestTask {
 internal static string Xml(RequestSchedule options,string exe,string sid,DateTime today) {
  Func<string,string> escape=SecurityElement.Escape;var triggers=new StringBuilder();
  foreach(string time in RequestSchedule.ParseTimes(options.Times))triggers.Append("<CalendarTrigger><StartBoundary>"+today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"T"+time+":00</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>");
  return "<?xml version=\"1.0\" encoding=\"utf-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><Description>Codex Usage scheduled lightweight requests</Description></RegistrationInfo><Triggers>"+triggers+"</Triggers><Principals><Principal id=\"User\"><UserId>"+escape(sid)+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>false</StartWhenAvailable><Enabled>"+(options.Enabled?"true":"false")+"</Enabled><Hidden>true</Hidden><WakeToRun>"+(options.WakeComputer?"true":"false")+"</WakeToRun><ExecutionTimeLimit>PT4M</ExecutionTimeLimit></Settings><Actions Context=\"User\"><Exec><Command>"+escape(exe)+"</Command><Arguments>--scheduled-request</Arguments><WorkingDirectory>"+escape(Path.GetDirectoryName(exe))+"</WorkingDirectory></Exec></Actions></Task>";
 }
 public static void Save(RequestSchedule options,string executable) {
  options.Validate();using(var mutex=new Mutex(false,RequestSchedule.MutexName)) {
   bool held=false;try {
    try {held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("正在执行定时请求，请稍后再保存。");
    options.EnabledFrom=DateTime.Now;Directory.CreateDirectory(RequestSchedule.DirectoryName);
    string sid=WindowsIdentity.GetCurrent().User.Value;string path=Path.Combine(RequestSchedule.DirectoryName,"request-task.xml");
    File.WriteAllText(path,Xml(options,executable,sid,DateTime.Today),Encoding.Unicode);
    try {
     var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe"),"/Create /TN "+ScheduledRequest.Quote("CodexUsage-AutoRequest-"+sid)+" /XML "+ScheduledRequest.Quote(path)+" /F"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
     using(var process=Process.Start(info)){process.OutputDataReceived+=(s,e)=>{};process.ErrorDataReceived+=(s,e)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();if(!process.WaitForExit(15000)){try{process.Kill();}catch{}throw new IOException("保存 Windows 定时任务超时，请重试。");}if(process.ExitCode!=0)throw new IOException("Windows 未能保存定时任务，请检查任务计划程序服务和当前用户权限。");}
     options.Save();
    }finally{if(File.Exists(path))File.Delete(path);}
   }finally{if(held)mutex.ReleaseMutex();}
  }
 }
}
}
