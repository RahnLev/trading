using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Gui;       // ControlCenter
using NinjaTrader.Gui.Tools; // NTWindow base type (your CBASTerminalWindow derives from this)
using System.Windows.Input;


namespace NinjaTrader.NinjaScript.AddOns
{
    public class CBASTerminalAddOn : AddOnBase
    {
		private static readonly RoutedUICommand OpenTerminalCommand =
    new RoutedUICommand("Open CBAS Terminal", "OpenCBASTerminal", typeof(CBASTerminalAddOn));
private static bool HasShortcut(ControlCenter cc, Key key, ModifierKeys mods)
{
    return cc.InputBindings.OfType<KeyBinding>().Any(kb => kb.Key == key && kb.Modifiers == mods);
}

        private MenuItem menuItem;                     // reference to remove on shutdown
        private static CBASTerminalWindow terminalWindow; // single-instance guard
        private static CBASTerminalAddOn s_instance;   // for static helper

        // -------- Simple logging (build-agnostic) --------
        private static void SafeLog(string msg)
        {
            try { Debug.WriteLine(msg); } catch { }
        }

        // -------- UI thread helpers --------
        private static void RunOnUi(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var d = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (d.CheckAccess())
                action();
            else
                d.Invoke(priority, action);
        }

        private static void RunOnUiAsync(Action action, DispatcherPriority priority = DispatcherPriority.Background)
        {
            var d = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (d.CheckAccess())
                action();
            else
                d.BeginInvoke(priority, action);
        }

        // -------- Public helper to hook existing ControlCenters --------
        public static void EnsureMenuForAllControlCenters()
        {
            RunOnUiAsync(() =>
            {
                if (Application.Current == null || s_instance == null)
                    return;

                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ControlCenter cc)
                        s_instance.HookControlCenter(cc);
                }
            });
        }

        // -------- AddOn lifecycle --------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "CBAS Terminal AddOn";
                SafeLog("CBAS AddOn SetDefaults");
            }
            else if (State == State.Active)
            {
                s_instance = this;
                SafeLog("CBAS AddOn Active");

                // Hook any already-open ControlCenter windows (important if compiling while NT is running)
                EnsureMenuForAllControlCenters();
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            if (window is ControlCenter cc)
            {
                // Defer to ensure the menu is constructed
                window.Dispatcher.BeginInvoke(new Action(() => HookControlCenter(cc)));
            }
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (window is ControlCenter cc)
            {
                try
                {
                    var topLevel = cc.MainMenu as ObservableCollection<object>;
                    if (topLevel == null)
                        return;

                    if (menuItem != null)
                    {
                        // Remove from top-level if present
                        if (topLevel.Contains(menuItem))
                        {
                            topLevel.Remove(menuItem);
                        }
                        else
                        {
                            // Or from any submenu we might have added to
                            foreach (var mi in topLevel.OfType<MenuItem>())
                            {
                                if (mi.Items.Contains(menuItem))
                                {
                                    mi.Items.Remove(menuItem);
                                    break;
                                }
                            }
                        }
                        menuItem = null;
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }

        // -------- Menu wiring + window open --------
        private void HookControlCenter(ControlCenter controlCenter)
        {
            try
            {
                var topLevel = controlCenter.MainMenu as ObservableCollection<object>;
                if (topLevel == null)
                {
                    // If menu not available, at least allow opening the window
                    ShowOrActivateTerminal();
                    return;
                }

                // Avoid duplicates
                bool exists = topLevel.OfType<MenuItem>()
                                      .Any(mi => HeaderText(mi.Header) == "CBAS Terminal");
                if (exists)
                    return;

                menuItem = new MenuItem { Header = "CBAS Terminal" };
                menuItem.Click += (s, e) => ShowOrActivateTerminal();

                // Add as a top-level item (avoids localization issues with "New")
                topLevel.Add(menuItem);
				
				
				var key = Key.T;
    var mods = ModifierKeys.Control | ModifierKeys.Alt;

    if (!HasShortcut(controlCenter, key, mods))
    {
        // Bind the command execution
        controlCenter.CommandBindings.Add(new CommandBinding(OpenTerminalCommand, (s, e) => ShowOrActivateTerminal()));

        // Add the actual key gesture
        controlCenter.InputBindings.Add(new KeyBinding(OpenTerminalCommand, key, mods));
	}
				
				
				
				
            }
            catch (Exception ex)
            {
                SafeLog("CBAS menu hook error: " + ex);
            }
        }

        private static string HeaderText(object header)
        {
            var s = header as string ?? header?.ToString() ?? string.Empty;
            return s.Replace("_", "").Trim();
        }

        private static void ShowOrActivateTerminal()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (terminalWindow != null && terminalWindow.IsLoaded)
                    {
                        if (!terminalWindow.IsVisible)
                            terminalWindow.Show();
                        terminalWindow.Activate();
                        return;
                    }

                    terminalWindow = new CBASTerminalWindow
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Topmost = true // temporary to ensure visibility during testing
                    };
                    terminalWindow.Closed += (s, e) => terminalWindow = null;

                    terminalWindow.Show();
                    terminalWindow.Activate();
                }
                catch (Exception ex)
                {
                    SafeLog("ShowOrActivateTerminal error: " + ex);
                    try { MessageBox.Show("Failed to open CBAS window:\n" + ex.Message); } catch { }
                }
            });
        }
    }
}
