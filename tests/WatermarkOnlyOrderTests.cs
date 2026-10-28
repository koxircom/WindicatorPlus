using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WindicatorPlus;
class WatermarkOnlyOrderTests {
 [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h,int n,StringBuilder b,int len,out int needed);
 class Provider:ITaskbarSpaceProvider {
#pragma warning disable 0067
 public event Action<TaskbarSpaceSnapshot> SpaceChanged;
#pragma warning restore 0067
 public TaskbarSpaceSnapshot GetLatestSnapshot(){return new TaskbarSpaceSnapshot(500,1500,new Rectangle(0,1000,1920,48),new Rectangle(1500,1000,420,48),true,Stopwatch.GetTimestamp());}
 public void Start(){} public void Stop(){} public void Dispose(){}
 }
 class Shell:Form { public Shell(){FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; Bounds=new Rectangle(0,1000,1920,48);} protected override bool ShowWithoutActivation{get{return true;}} protected override void OnShown(EventArgs e){base.OnShown(e); NativeMethods.SetWindowPos(Handle,NativeMethods.HWND_TOPMOST,0,1000,1920,48,NativeMethods.SWP_NOACTIVATE);} }
 class Tracker:NativeWindow {public int Count;protected override void WndProc(ref Message m){if(m.Msg==0x46)Count++;base.WndProc(ref m);}}
 static void Assert(bool ok,string s){if(!ok)throw new Exception(s);}
 static void Above(IntPtr a,IntPtr b){Assert(TrayIconManager.GetZOrderRelation(a,b)==TrayIconManager.ZOrderRelation.Above,"Incorrect window order");}
 [STAThread] static int Main(){var name=new StringBuilder(256);int n;GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,name,512,out n);if(!name.ToString().StartsWith("WindicatorTest-"))return 2;
 try {Application.EnableVisualStyles(); var cfg=new AppConfig();
 using(var shell=new Shell()) using(var w=new WatermarkWindow()) using(var provider=new Provider()) {
 w.SetTaskbarSpaceProvider(provider);shell.Show();w.HasTaskbarIntent=true;w.Show();w.SetDesktop(1,1,"Desk1");w.UpdateWatermark();Application.DoEvents();
 Assert(w.ActualScreenRect.Width>1,"Watermark must render");Rectangle initial=w.ActualScreenRect;
 using(var tray=new TrayIconManager(cfg,w,null)){
 var menu=(ContextMenuStrip)typeof(TrayIconManager).GetField("_contextMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tray);
 var show=typeof(TrayIconManager).GetMethod("ShowContextMenuAboveTaskbar",BindingFlags.Instance|BindingFlags.NonPublic);
 IntPtr fg=RuntimeTrace.GetForegroundWindow();
 for(int i=0;i<30;i++){
 if(i<15&&!menu.Visible){show.Invoke(tray,null);Application.DoEvents();}
 if(i>=15&&menu.Visible){menu.Close();Application.DoEvents();}
 Assert(menu.Visible==(i<15),"Menu visibility mismatch");
 NativeMethods.SetWindowPos(shell.Handle,NativeMethods.HWND_TOPMOST,0,0,0,0,NativeMethods.SWP_NOMOVE|NativeMethods.SWP_NOSIZE|NativeMethods.SWP_NOACTIVATE);
 tray.ReconcileTaskbarOverlayOrder(shell.Handle);Above(w.Handle,shell.Handle);if(menu.Visible)Above(menu.Handle,w.Handle);Assert(w.ActualScreenRect==initial,"Geometry drift");
 }
 Console.WriteLine("PASS: 30 real Shell contention cycles, open/closed menu, unchanged geometry");
 var tracker=new Tracker();tracker.AssignHandle(w.Handle);try{for(int i=0;i<5;i++)tray.ReconcileTaskbarOverlayOrder(shell.Handle);Assert(tracker.Count==0,"Correct order must be no-op");}finally{tracker.ReleaseHandle();}
 Console.WriteLine("PASS: already-correct no-op");
 using(var sentinel=new Shell()){sentinel.Show(); NativeMethods.SetWindowPos(sentinel.Handle,NativeMethods.HWND_TOPMOST,0,0,0,0,NativeMethods.SWP_NOMOVE|NativeMethods.SWP_NOSIZE|NativeMethods.SWP_NOACTIVATE); Assert(TrayIconManager.GetZOrderRelation(sentinel.Handle,w.Handle)==TrayIconManager.ZOrderRelation.Above,"Sentinel setup must be above watermark");tray.ReconcileTaskbarOverlayOrder(shell.Handle); Assert(TrayIconManager.GetZOrderRelation(sentinel.Handle,w.Handle)==TrayIconManager.ZOrderRelation.Above,"Coordinator must preserve sentinel");}
 Console.WriteLine("PASS: sentinel retained above watermark");
 Assert(RuntimeTrace.GetForegroundWindow()==fg,"Unexpected foreground activation");Console.WriteLine("PASS: no foreground activation");
 w.Hide();tray.ReconcileTaskbarOverlayOrder(shell.Handle);Assert(!w.Visible,"Hidden watermark reshown");Console.WriteLine("PASS: hidden watermark remains hidden");
 }}return 0;}catch(Exception e){Console.WriteLine(e);return 1;}
 }
}
