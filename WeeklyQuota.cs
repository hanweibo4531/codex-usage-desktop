using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace CodexUsage {
class CreditDay {public DateTime Date;public decimal Credits;public decimal? Tokens,Turns;}
class WeeklyQuota {
 public List<CreditDay> Days=new List<CreditDay>();
 public decimal? UsedPercent,UsedCredits,TotalCredits;
 public decimal? Dollars { get { return TotalCredits/25m; } }
 public DateTime Start,End,Time;
 public string Note="等待账户用量连接";
 internal static bool Number(object value,out decimal number) {
  return decimal.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number)&&number>=-1000000000000000000m&&number<=1000000000000000000m;
 }
 public static WeeklyQuota Parse(object usage,object daily,DateTime now) {
  var result=new WeeklyQuota{Time=now};
  var limits=Json.Get(usage,"rate_limit");object week=null;
  foreach(var key in new[]{"secondary_window","primary_window"}) {
   var candidate=Json.Get(limits,key);
   if(Json.N(candidate,"limit_window_seconds")==604800) {week=candidate;break;}
  }
  if(week==null) {result.Note="账户未提供 7 天额度窗口";return result;}
  decimal percent;
  if(!Number(Json.Get(week,"used_percent"),out percent)||percent<0||percent>100) {result.Note="账户未提供有效的已用比例";return result;}
  result.UsedPercent=percent;
  try {result.End=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Json.N(week,"reset_at"));result.Start=result.End.AddDays(-7);}
  catch {result.Note="额度周期时间不可用";return result;}
  if(result.End<=now||result.Start>now) {result.Note="额度周期已变化，请刷新后重试";return result;}
  string plan=Json.S(usage,"plan_type");
  if(plan!="free"&&plan!="go"&&plan!="plus"&&plan!="pro"&&plan!="prolite") {
   result.Note="当前套餐无法确认用量与额度为同一统计范围，暂不推算";return result;
  }
  var rows=Json.Get(daily,"data") as object[];
  if(rows==null) {result.Note="账户未提供每日 Credits 明细";return result;}
  decimal total=0;bool found=false;var dates=new HashSet<DateTime>();var parsedDays=new List<CreditDay>();
  foreach(var row in rows) {
   DateTime date;decimal credits;
   if(!DateTime.TryParseExact(Json.S(row,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date)) {result.Note="用量日期格式无法识别";return result;}
   if(date<now.Date.AddDays(-29)||date>now.Date)continue;
   if(!dates.Add(date)||!Number(Json.Get(Json.Get(row,"totals"),"credits"),out credits)||credits<0) {result.Note="Credits 明细缺失或重复，暂不推算";return result;}
   decimal tokens,turns;var totals=Json.Get(row,"totals");
   parsedDays.Add(new CreditDay{Date=date,Credits=credits,Tokens=Number(Json.Get(totals,"text_total_tokens"),out tokens)&&tokens>=0?(decimal?)tokens:null,Turns=Number(Json.Get(totals,"turns"),out turns)&&turns>=0?(decimal?)turns:null});
   if(date>=result.Start.Date&&date<result.End) {
    try {total+=credits;}catch(OverflowException){result.Note="Credits 数值超出范围";return result;}
    found=true;
   }
  }
  result.Days=parsedDays;
  if(!found) {result.Note="本周期暂无 Credits 明细，请稍后刷新";return result;}
  result.UsedCredits=total;
  if(percent==0||total==0) {result.Note="用量不足或统计尚未同步，暂不能推算总额";return result;}
  try {result.TotalCredits=total/(percent/100m);}catch(OverflowException){result.Note="推算结果超出范围";return result;}
  result.Note="按账户重置周期统计 · 每日 Credits 可能延迟，周期首日可能含周期前用量\n总额 = 已用 Credits ÷ 已用比例；25 Credits ≈ $1，仅供参考";
  return result;
 }
}
static class WeeklyQuotaClient {
 // Only these fixed first-party HTTPS endpoints receive the existing Codex credential.
 static object Get(string path,string token,string account) {
  ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  var request=(HttpWebRequest)WebRequest.Create("https://chatgpt.com/backend-api/wham/"+path);
  request.Method="GET";request.AllowAutoRedirect=false;request.Timeout=12000;request.ReadWriteTimeout=12000;
  request.Accept="application/json";request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
  request.Headers["ChatGPT-Account-Id"]=account;
  using(var response=(HttpWebResponse)request.GetResponse()) {
   if(response.StatusCode!=HttpStatusCode.OK)throw new IOException();
   using(var reader=new StreamReader(response.GetResponseStream())) {
    var buffer=new char[4*1024*1024+1];int count=0,n;
    while(count<buffer.Length&&(n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;
    if(count==buffer.Length)throw new IOException();
    return Json.Read(new string(buffer,0,count));
   }
  }
 }
 public static Task<WeeklyQuota> Read(string home,string expectedAccount) {
  return Task.Run(()=> {
   try {
    var tokens=Json.Get(Json.Read(File.ReadAllText(Path.Combine(home,"auth.json"))),"tokens");
    string token=Json.S(tokens,"access_token"),account=Json.S(tokens,"account_id");
    if(token==""||account=="")return new WeeklyQuota{Note="无法读取本机 ChatGPT 登录状态，请先在 Codex 登录"};
    if(!string.IsNullOrEmpty(expectedAccount)&&expectedAccount!=account)return new WeeklyQuota{Note="账户已切换，请刷新后重试"};
    var usage=Get("usage",token,account);var now=DateTime.UtcNow;
    string range="start_date="+now.AddDays(-29).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"&end_date="+now.AddDays(1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"&group_by=day";
    var daily=Get("analytics/daily-workspace-usage-counts?"+range,token,account);
    var current=Json.Get(Json.Read(File.ReadAllText(Path.Combine(home,"auth.json"))),"tokens");
    if(Json.S(current,"account_id")!=account)return new WeeklyQuota{Note="账户已切换，请刷新后重试"};
    return WeeklyQuota.Parse(usage,daily,now);
   }catch(WebException ex) {
    var response=ex.Response as HttpWebResponse;int status=response==null?0:(int)response.StatusCode;if(response!=null)response.Dispose();
    return new WeeklyQuota{Note=status==401?"用量登录已过期，请在 Codex 重新登录后刷新":status==403?"账户用量接口暂不允许访问，无法读取本周 Credits":status==429?"用量查询频繁，请稍后刷新":"账户用量查询未连接，请检查网络后刷新"};
   }catch {return new WeeklyQuota{Note="未能读取账户 Credits 明细，请检查 Codex 登录状态"};}
  });
 }
}
}
