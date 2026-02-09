using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Gdk;
using GLib;
using Debug = UnityEngine.Debug;
using Display = Gdk.Display;


public class GtkX11Helper
{
    public static GtkX11Helper Instance;
    
    // gdk_x11_window_foreign_new_for_display (gdk_display, xid) -> GdkWindow*
    [DllImport("libgdk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gdk_x11_window_foreign_new_for_display(IntPtr display, IntPtr windowXid);

    // gdk_x11_display_get_xdisplay (gdk_display) -> Display*
    [DllImport("libgdk-3.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gdk_x11_display_get_xdisplay(IntPtr gdkDisplay);
    
    public Gtk.Window DummyParent = new("");

    private Window gdkWindow;

    public static void Init(IntPtr window)
    {
        ExceptionManager.UnhandledException += args =>
        {
            Debug.LogError("Exception in Gtk# callback delegate.");
            Debug.LogError((UnhandledExceptionEventArgs)args.ExceptionObject);
            Debug.LogError(new StackTrace(true));
            UnityEngine.Application.Quit(1);
        };
        if (Instance != null)
        {
            Debug.LogError("Trying to create multiple Gtk instances.");
            return;
        }
        Instance = new GtkX11Helper
        {
            gdkWindow = ForeignNewForDisplay(window),
            DummyParent = new Gtk.Window("")
        };
        Instance.DummyParent.Realize();
        Instance.DummyParent.SkipTaskbarHint = true;
        Instance.DummyParent.SkipPagerHint = true;
        Instance.DummyParent.Decorated = false;
        Instance.DummyParent.Window.Reparent(Instance.gdkWindow, 0, 0);
    }

    private static Window ForeignNewForDisplay(IntPtr x11WindowXid)
    {
        var display = Display.Default;
        IntPtr gdkDisplayPtr = display.Handle;
        
        IntPtr foreign = gdk_x11_window_foreign_new_for_display(gdkDisplayPtr, x11WindowXid);
        if (foreign == IntPtr.Zero)
            throw new Exception("Failed to create foreign GdkWindow");

        return new Window(foreign);
    }
}
