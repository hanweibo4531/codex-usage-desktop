using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CodexUsage {
static class Theme {
 public static bool IsLight { get; private set; }
 // Each dark color has an explicit light counterpart; no color inversion.
 static readonly Dictionary<string,string> Light=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
  {"#080D16","#F0F4FA"},{"#0D1625","#F5F8FC"},{"#0E2438","#EAF5FF"},
  {"#102A42","#DEEDFC"},{"#111B2B","#FFFFFF"},{"#112B42","#E2EFFB"},
  {"#132036","#FFFFFF"},{"#163D5C","#CEE8FB"},{"#172338","#EAF0F8"},
  {"#1B2A40","#E4EBF5"},{"#21567B","#ABD2EE"},{"#23334A","#D8E2F0"},
  {"#23344D","#DBE5F2"},{"#234262","#D8EAFB"},{"#243954","#D2E0F0"},
  {"#263953","#CDDAEC"},{"#29425C","#A3B7CE"},{"#29638C","#A3C7E5"},
  {"#2A4461","#BED1E6"},{"#2C4B6B","#B7CDE5"},{"#314763","#BDCCE0"},
  {"#4BC9FF","#087CAF"},{"#506E94","#687D99"},{"#567293","#637992"},
  {"#56C5F5","#0877A8"},{"#587598","#657D98"},{"#61D0FF","#067CAD"},
  {"#6682A3","#607A98"},{"#68BEE8","#197AA8"},{"#6BA5CC","#427896"},
  {"#769BC2","#476F96"},{"#7F97B6","#5B7492"},{"#7F99BA","#5A7391"},
  {"#80D8FF","#146B9A"},{"#887BFF","#7561D7"},{"#92AACA","#506D8E"},
  {"#9BAEC8","#4A6483"},{"#ACA1FF","#6B50C9"},{"#B4CAE6","#314C6D"},
  {"#B8CBE3","#3A5575"},{"#BCEAFF","#145C87"},{"#DAE9FF","#294767"},
  {"#EAF2FF","#172B46"},{"#EAF6FF","#164B71"},{"#BDD7F4","#3F6186"},
  {"#869CB7","#5B7492"},{"#1D2D43","#DFE8F3"},{"#EDF5FF","#1D3451"},
  {"#849DBD","#627D9D"},{"#FFC178","#926019"},{"#90A5C0","#59728E"},
  {"#223148","#DFE8F3"},{"#F07783","#CF4254"},{"#FFBA69","#A76811"},
  {"#8CA3C2","#597593"},{"#A6BCD9","#4B688A"},{"#142034","#F2F6FB"}
 };
 public static string Resolve(string color) {string value;return IsLight&&Light.TryGetValue(color,out value)?value:color;}
 public static void Apply(Window window,string theme) {
  IsLight=theme=="light";
  foreach(var pair in Light) {
   var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(Resolve(pair.Key));brush.Freeze();
   window.Resources["Brush"+pair.Key.Substring(1)]=brush;
  }
 }
}
}
