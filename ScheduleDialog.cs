using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsage {
static class ScheduleDialog {
 static SolidColorBrush Brush(string color){return (SolidColorBrush)new BrushConverter().ConvertFromString(Theme.Resolve(color));}
 static TextBlock Label(string text,double size){return new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#EAF2FF"),Margin=new Thickness(0,0,0,8)};}
 static TextBox Input(string text){return new TextBox{Text=text,FontSize=14,Padding=new Thickness(10,8,10,8),Background=Brush("#0D1625"),Foreground=Brush("#EAF2FF"),BorderBrush=Brush("#23334A"),Margin=new Thickness(0,0,0,12)};}
 public static Window Build(Window owner,RequestSchedule current,Action saved) {
  var dialog=new Window{Title="定时发送请求",Width=470,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#111B2B"),Foreground=Brush("#EAF2FF"),FontFamily=owner.FontFamily,ShowInTaskbar=false,Icon=owner.Icon};
  if(owner.IsVisible)dialog.Owner=owner;
  dialog.Resources.MergedDictionaries.Add(owner.Resources);
  var body=new StackPanel{Margin=new Thickness(24)};dialog.Content=body;
  body.Children.Add(Label("定时发送轻量请求",20));
  body.Children.Add(Label("到点请 Codex 回复一次 OK，使用现有 ChatGPT 登录状态。请求会消耗少量用量，实际重置时间由服务端决定。",12));
  var enabled=new CheckBox{Content="启用每日定时请求",IsChecked=current.Enabled,Foreground=Brush("#EAF2FF"),Margin=new Thickness(0,8,0,18)};body.Children.Add(enabled);
  body.Children.Add(Label("每天发送时间（本机时区 · 24 小时制）",12));var times=Input(current.Times);body.Children.Add(times);
  var presets=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};
  var morning=new Button{Content="仅 05:00",Padding=new Thickness(10,6,10,6)};morning.Click+=(s,e)=>times.Text="05:00";presets.Children.Add(morning);
  var three=new Button{Content="05:00 / 10:00 / 15:00",Padding=new Thickness(10,6,10,6),Margin=new Thickness(8,0,0,0)};three.Click+=(s,e)=>times.Text="05:00, 10:00, 15:00";presets.Children.Add(three);body.Children.Add(presets);
  body.Children.Add(Label("模型（可选，留空使用 Codex 默认模型）",12));var model=Input(current.Model);body.Children.Add(model);
  var wake=new CheckBox{Content="尝试唤醒睡眠中的电脑",IsChecked=current.WakeComputer,Foreground=Brush("#EAF2FF"),Margin=new Thickness(0,0,0,12)};body.Children.Add(wake);
  body.Children.Add(Label("关闭面板后仍由 Windows 执行，需要当前用户保持登录且网络可用。关机时不执行；唤醒需系统支持。错过超过 2 分钟的时间点不补发，失败不自动重试。",11));
  var result=Label("保存后从下一个时间点开始，不会立即发送。",11);result.Foreground=Brush("#849DBD");body.Children.Add(result);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
  var cancel=new Button{Content="取消",IsCancel=true,Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,8,0)};
  var save=new Button{Content="保存计划",IsDefault=true,Padding=new Thickness(16,8,16,8)};buttons.Children.Add(cancel);buttons.Children.Add(save);body.Children.Add(buttons);
  save.Click+=async(s,e)=> {
   var options=new RequestSchedule{Enabled=enabled.IsChecked==true,Times=times.Text,Model=model.Text,WakeComputer=wake.IsChecked==true};
   try {
    options.Validate();if(options.Enabled&&AccountClient.FindExe(Settings.Load().Executable)==null)throw new InvalidOperationException("未找到 codex.exe，请先在设置中指定。");
    save.IsEnabled=false;cancel.IsEnabled=false;result.Text="正在保存 Windows 定时任务…";
    await Task.Run(()=>WindowsRequestTask.Save(options,Assembly.GetExecutingAssembly().Location));
    saved();if(dialog.IsLoaded)dialog.DialogResult=true;
   }catch(Exception ex){result.Text=ex is FormatException||ex is InvalidOperationException||ex is System.IO.IOException?ex.Message:"保存失败，请检查本地文件和任务计划程序权限。";save.IsEnabled=true;cancel.IsEnabled=true;}
  };
  return dialog;
 }
}
}
