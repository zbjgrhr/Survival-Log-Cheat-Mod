using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
class PanelTest {
 [STAThread] static void Main(string[] args) {
  try { Run(args); } catch(Exception e) { Console.WriteLine(e.GetType().FullName); Console.WriteLine(e.Message); if(e.InnerException!=null) Console.WriteLine(e.InnerException.Message); Environment.ExitCode=1; }
 }
 static void Run(string[] args) {
  Application.EnableVisualStyles();
  var assembly=Assembly.LoadFrom(args[0]);
  var type=assembly.GetType("TrainerPanel");
  using(var form=(Form)Activator.CreateInstance(type,true)) {
   var timer=(Timer)type.GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);timer.Stop();
   form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();form.PerformLayout();
   var tabs=(TabControl)type.GetField("tabs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
   int checkedControls=0;
   foreach(TabPage tab in tabs.TabPages) {
    tabs.SelectedTab=tab;Application.DoEvents();tab.PerformLayout();
    foreach(Control c in tab.Controls) {
     if(c.Left<0 || c.Top<0 || c.Right>tab.ClientSize.Width || c.Bottom>tab.ClientSize.Height)
      throw new Exception("Outside tab: "+tab.Text+" / "+c.Text+" "+c.Bounds+" in "+tab.ClientSize);
     checkedControls++;
    }
   }
   Console.WriteLine("PASS: panel constructed; "+checkedControls+" controls fit within their tabs; no game commands sent.");
  }
 }
}
