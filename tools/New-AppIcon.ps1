$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class UsageIconBuilder {
 static GraphicsPath Rounded(float x,float y,float w,float h,float r) {
  var p=new GraphicsPath();p.AddArc(x,y,r,r,180,90);p.AddArc(x+w-r,y,r,r,270,90);
  p.AddArc(x+w-r,y+h-r,r,r,0,90);p.AddArc(x,y+h-r,r,r,90,90);p.CloseFigure();return p;
 }
 static byte[] Draw(int size) {
  using(var bitmap=new Bitmap(size,size))using(var g=Graphics.FromImage(bitmap)) {
   g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);g.ScaleTransform(size/256f,size/256f);
   using(var shape=Rounded(8,8,240,240,64))
   using(var fill=new LinearGradientBrush(new Point(20,10),new Point(240,256),Color.FromArgb(25,54,87),Color.FromArgb(9,17,34)))
   using(var edge=new Pen(Color.FromArgb(70,117,162),5)) {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
   using(var pen=new Pen(Color.FromArgb(85,215,255),19)) {
    pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
    g.DrawLines(pen,new[]{new Point(65,76),new Point(106,111),new Point(65,146)});
    g.DrawLine(pen,128,146,180,146);
   }
   using(var pen=new Pen(Color.FromArgb(54,75,105),12)) {pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,61,192,195,192);}
   using(var pen=new Pen(Color.FromArgb(153,138,255),12)) {pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,61,192,152,192);}
   using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();}
  }
 }
 public static void Write(string directory) {
  Directory.CreateDirectory(directory);int[] sizes={16,24,32,48,64,128,256};var images=new byte[sizes.Length][];
  for(int i=0;i<sizes.Length;i++)images[i]=Draw(sizes[i]);
  File.WriteAllBytes(Path.Combine(directory,"app.png"),images[images.Length-1]);
  using(var file=File.Create(Path.Combine(directory,"app.ico")))using(var w=new BinaryWriter(file)) {
   w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
   for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;}
   foreach(var image in images)w.Write(image);
  }
 }
}
'@ -ReferencedAssemblies System.Drawing
[UsageIconBuilder]::Write((Join-Path $PSScriptRoot '..\assets'))
