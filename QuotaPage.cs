using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CodexUsage {
partial class App {
 static TextBlock QuotaText(string text,double size,string color) {
  var value=Text(text,size,color);value.TextWrapping=TextWrapping.Wrap;return value;
 }
 static Border QuotaSurface(UIElement child,bool forecast) {
  return new Border{Child=child,Padding=new Thickness(12),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=Brush("#23334A"),Background=Brush(forecast?"#142034":"#111B2B")};
 }
 static string Compact(double value) {return value>=1000000?(value/1000000).ToString("0.0",CultureInfo.InvariantCulture)+"M":value>=1000?(value/1000).ToString("0.#",CultureInfo.InvariantCulture)+"K":value.ToString("0",CultureInfo.InvariantCulture);}
 static string[] MetricSymbols={"∿","01","$","✓"};
 static string[] MetricColors={"#4BC9FF","#35C9A0","#FFBA69","#35C9A0"};
 static FrameworkElement MetricIcon(int index) {
  var text=Text(MetricSymbols[index],index==1?11:17,MetricColors[index]);text.HorizontalAlignment=HorizontalAlignment.Center;
  return new Border{Width=27,Height=27,CornerRadius=new CornerRadius(7),Background=Brush("#142034"),Child=text,Margin=new Thickness(0,0,7,0)};
 }
 void RenderQuotaOverview() {
  var panel=C<StackPanel>("QuotaOverview");panel.Children.Clear();
  var heading=new WrapPanel{Margin=new Thickness(0,0,0,10)};var title=Text("用量总览",14,"#EDF5FF");title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,14,0);heading.Children.Add(title);
  heading.Children.Add(QuotaText("本机近 30 天 · "+DateTime.Today.AddDays(-29).ToString("MM/dd")+" — "+DateTime.Now.ToString("MM/dd HH:mm"),10,"#849DBD"));panel.Children.Add(heading);
  var cards=new UniformGrid{Columns=4,Margin=new Thickness(-4,0,-4,8)};
  string[] labels={"用量记录","Token 数","预估花费","成功率"};
  string[] values={snapshot.Found?Compact(snapshot.Rows.Count):"—",snapshot.Found?Compact(snapshot.Rows.Sum(r=>r.Total)):"—","—","—"};
  for(int i=0;i<4;i++) {
   var body=new StackPanel();var label=new StackPanel{Orientation=Orientation.Horizontal};label.Children.Add(MetricIcon(i));label.Children.Add(Text(labels[i],11,"#849DBD"));body.Children.Add(label);
   var value=Text(values[i],22,"#EDF5FF");value.FontWeight=FontWeights.SemiBold;value.Margin=new Thickness(0,5,0,0);body.Children.Add(value);
   var card=QuotaSurface(body,false);card.Margin=new Thickness(4,0,4,8);card.ToolTip=i<2?"本机日志记录，不代表完整账户请求量":"当前数据源未提供可靠的费用或请求结果";cards.Children.Add(card);
  }
  cards.SizeChanged+=(s,e)=>cards.Columns=cards.ActualWidth<530?2:4;panel.Children.Add(cards);
  panel.Children.Add(QuotaText("记录与 Token 来自本机日志；费用、成功率暂无可靠数据。",10,"#849DBD"));
  var standard=Text("标准额度",15,"#EDF5FF");standard.FontWeight=FontWeights.SemiBold;standard.Margin=new Thickness(0,18,0,8);panel.Children.Add(standard);
 }
 internal static DateTime? QuotaReset(object w) {
  double seconds=Json.N(w,"resetsAt");if(seconds<=0)return null;
  try{return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();}catch{return null;}
 }
 internal static List<Usage> WindowRows(IEnumerable<Usage> rows,DateTime start,DateTime end) {return rows.Where(r=>r.Time>=start&&r.Time<end).ToList();}
 static Border WindowDetail(string title,string period,List<Usage> rows,bool available,bool forecast) {
  var body=new StackPanel();var heading=Text(title,12,"#EDF5FF");heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
  var caption=QuotaText(period,9,"#849DBD");caption.MinHeight=30;caption.Margin=new Thickness(0,5,0,7);body.Children.Add(caption);
  string[] labels={forecast?"预计记录":"用量记录",forecast?"预计 Token":"Token","预估花费","成功率"};
  string[] values={available?Compact(rows.Count):"—",available?Compact(rows.Sum(r=>r.Total)):"—","—","—"};
  for(int i=0;i<4;i++) {
   var line=new Grid{Margin=new Thickness(0,3,0,3),MinHeight=29};line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   line.Children.Add(MetricIcon(i));var label=Text(labels[i],10,"#849DBD");Grid.SetColumn(label,1);line.Children.Add(label);
   var value=Text(values[i],12,"#EDF5FF");value.FontWeight=FontWeights.SemiBold;value.Margin=new Thickness(5,0,0,0);Grid.SetColumn(value,2);line.Children.Add(value);body.Children.Add(line);
  }
  if(forecast) {body.Children.Add(new Border{Height=1,Background=Brush("#23334A"),Margin=new Thickness(0,7,0,7)});body.Children.Add(QuotaText("账户额度与本机用量口径不同，暂不外推。",9,"#849DBD"));}
  return QuotaSurface(body,forecast);
 }
 FrameworkElement BuildQuotaWindow(object w) {
  double minutes=Json.N(w,"windowDurationMins"),used=Json.N(w,"usedPercent"),remaining=Math.Max(0,Math.Min(100,100-used));
  bool known=Json.Get(w,"usedPercent")!=null&&used>=0&&used<=100;var reset=QuotaReset(w);
  var body=new StackPanel();var header=new Grid{Margin=new Thickness(0,0,0,14)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var labels=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var title=Text((minutes==10080?"▦  周限额":"◷  "+WindowLabel(minutes)),14,"#EDF5FF");title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,8,0);labels.Children.Add(title);
  labels.Children.Add(new Border{Background=Brush("#142034"),BorderBrush=Brush("#23334A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(7,3,7,3),Child=Text(reset.HasValue?"重置于 "+reset.Value.ToString("MM/dd HH:mm"):"重置时间未知",10,"#849DBD")});header.Children.Add(labels);
  var percent=Text(known?"剩余 "+remaining.ToString("0.#")+"%":"剩余 —",23,"#4BC9FF");percent.Margin=new Thickness(8,0,0,0);percent.FontWeight=FontWeights.SemiBold;Grid.SetColumn(percent,1);header.Children.Add(percent);body.Children.Add(header);
  var track=new Grid{Height=8,Margin=new Thickness(0,0,0,16)};track.Children.Add(new Border{Background=Brush("#223148"),CornerRadius=new CornerRadius(4)});
  var fill=new Border{Background=Brush(remaining<=20?"#F07783":"#35C9A0"),CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Left};track.SizeChanged+=(s,e)=>fill.Width=known?track.ActualWidth*remaining/100:0;track.Children.Add(fill);body.Children.Add(track);
  DateTime start=default(DateTime),previous=default(DateTime);bool bounds=reset.HasValue&&minutes>0&&minutes<=43200;
  if(bounds){start=reset.Value.AddMinutes(-minutes);previous=start.AddMinutes(-minutes);}
  bool current=bounds&&DateTime.Now>=start&&DateTime.Now<reset.Value;
  Func<DateTime,DateTime,string> period=(a,b)=>a.ToString("MM/dd HH:mm")+" — "+b.ToString("MM/dd HH:mm");
  var columns=new UniformGrid{Columns=3,Margin=new Thickness(-4,0,-4,0)};
  var prior=WindowDetail("上个窗口用量",bounds?period(previous,start):"窗口边界未知",bounds?WindowRows(snapshot.Rows,previous,start):new List<Usage>(),bounds&&snapshot.Found,false);
  var now=WindowDetail("当前窗口已用",bounds?period(start,reset.Value):"窗口边界未知",current?WindowRows(snapshot.Rows,start,DateTime.Now):new List<Usage>(),current&&snapshot.Found,false);
  var prediction=WindowDetail("↗ 当前窗口预测","预测数据暂不可用",new List<Usage>(),false,true);
  foreach(var card in new[]{prior,now,prediction}){card.Margin=new Thickness(4,0,4,8);columns.Children.Add(card);}columns.SizeChanged+=(s,e)=>columns.Columns=columns.ActualWidth<540?1:3;body.Children.Add(columns);
  var stamp=live.Quotas!=null?live.Time:snapshot.QuotaTime;
  var note=QuotaText("◷  "+(stamp==default(DateTime)?"等待同步":stamp.ToString("MM/dd HH:mm"))+"  ·  额度来自 "+(live.Quotas!=null?"Codex 实时接口":"日志快照")+"  ·  窗口明细仅含本机日志"+(bounds&&!current?"  ·  窗口已过期或尚未开始":""),10,"#849DBD");note.Margin=new Thickness(0,14,0,0);body.Children.Add(note);
  var surface=QuotaSurface(body,false);surface.Margin=new Thickness(0,0,0,16);return surface;
 }
}
}
