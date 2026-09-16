using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CodexUsage {
// Persist before sending: a timeout must never turn a retry into a second redemption.
class ResetTicket {
 public string Key, AccountId, Home;
}
class ResetStore {
 readonly string path;
 public ResetStore(string path) { this.path=path; }
 public ResetTicket Load() {
  if(!File.Exists(path))return null;
  var data=Json.Read(File.ReadAllText(path));
  var ticket=new ResetTicket{Key=Json.S(data,"key"),AccountId=Json.S(data,"account"),Home=Json.S(data,"home")};
  Guid id;
  if(!Guid.TryParse(ticket.Key,out id)||string.IsNullOrWhiteSpace(ticket.AccountId)||string.IsNullOrWhiteSpace(ticket.Home))
   throw new InvalidDataException("未完成的重置记录损坏，请保留该文件并联系维护者。");
  return ticket;
 }
 public void Save(ResetTicket ticket) {
  if(ticket==null) { if(File.Exists(path))File.Delete(path);return; }
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   byte[] bytes=Encoding.UTF8.GetBytes(Json.Write(new{key=ticket.Key,account=ticket.AccountId,home=ticket.Home}));
   using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
    stream.Write(bytes,0,bytes.Length);stream.Flush(true);
   }
   if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
class ResetCoordinator {
 readonly Action<ResetTicket> persist;
 public ResetTicket Pending { get; private set; }
 public bool InFlight { get; private set; }
 public ResetCoordinator(ResetTicket pending,Action<ResetTicket> persist) {Pending=pending;this.persist=persist;}
 static string HomeKey(string home) {return Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
 public string UnavailableReason(AccountResult state,string home) {
  if(InFlight)return "正在处理重置，请稍候。";
  if(state==null||state.Quotas==null)return "连接实时账户额度后才能重置。";
  string account=Json.S(state.Quotas,"accountId");
  if(string.IsNullOrWhiteSpace(account))return "当前 Codex 未返回账户标识，请升级 Codex 后重试。";
  if(Pending!=null) {
   if(Pending.AccountId!=account||!string.Equals(Pending.Home,HomeKey(home),StringComparison.OrdinalIgnoreCase))
    return "另一账号或数据目录有结果未确认的重置，请先切回原账号和目录。";
   return null;
  }
  var credits=Json.Get(state.Quotas,"rateLimitResetCredits");
  if(credits==null||Json.Get(credits,"availableCount")==null)return "服务暂未返回可用重置次数。";
  if(Json.N(credits,"availableCount")<1)return "当前没有可用的重置次数。";
  return null;
 }
 public async Task<string> Execute(AccountResult state,string home,Func<string,Task<object>> send) {
  string reason=UnavailableReason(state,home);if(reason!=null)throw new InvalidOperationException(reason);
  InFlight=true;
  try {
   if(Pending==null) {
    var ticket=new ResetTicket{Key=Guid.NewGuid().ToString(),AccountId=Json.S(state.Quotas,"accountId"),Home=HomeKey(home)};
    persist(ticket); // Failure to persist must abort before any network mutation.
    Pending=ticket;
   }
   object result=await send(Pending.Key);
   string outcome=Json.S(result,"outcome");
   if(outcome!="reset"&&outcome!="alreadyRedeemed"&&outcome!="nothingToReset"&&outcome!="noCredit")
    throw new InvalidDataException("服务返回了未识别的结果。请以相同请求重试。");
   persist(null);Pending=null;
   return outcome;
  }finally{InFlight=false;}
 }
}
}
