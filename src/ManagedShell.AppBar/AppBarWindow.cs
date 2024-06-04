using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ManagedShell.Common.Extensions;

namespace ManagedShell.AppBar
{
    public class AppBarWindow : Window, INotifyPropertyChangedExtended
    {
        protected readonly AppBarManager _appBarManager;
        protected readonly ExplorerHelper _explorerHelper;
        protected readonly FullScreenHelper _fullScreenHelper;

        public AppBarScreen Screen;
        protected bool ProcessScreenChanges = true;

        // needs to set correct!!!
        private double _dpiScale = 1.0;
        public double DpiScale
        {
            get => _dpiScale;
            set => this.SetProperty(ref _dpiScale, ref value);
        }

        // Window properties
        private WindowInteropHelper helper;
        private bool IsRaising;
        public IntPtr Handle;
        public bool AllowClose;
        public bool IsClosing;
        public bool IsOpening = true;
        public double DesiredHeight { get; set; }
        public double DesiredWidth { get; set; }
        private bool EnableBlur;

        // AppBar properties
        private int AppBarMessageId = -1;

        private AppBarEdge _appBarEdge = AppBarEdge.Left;
        public AppBarEdge AppBarEdge
        {
            get => _appBarEdge;
            set
            {
                this.SetProperty(ref _appBarEdge, ref value);
                Orientation = value is AppBarEdge.Left or AppBarEdge.Right
                    ? Orientation.Vertical
                    : Orientation.Horizontal;
            }
        }
        private AppBarMode _appBarMode = AppBarMode.Normal;
        public AppBarMode AppBarMode
        {
            get => _appBarMode;
            set => this.SetProperty(ref _appBarMode, ref value);
        }
        
        protected internal bool RequiresScreenEdge;
        
        private Orientation _orientation = Orientation.Vertical;
        public Orientation Orientation
        {
            get => _orientation;
            set => this.SetProperty(ref _orientation, ref value);
        }

        public AppBarWindow(AppBarManager appBarManager, ExplorerHelper explorerHelper, FullScreenHelper fullScreenHelper, AppBarScreen screen, AppBarEdge edge, AppBarMode mode, double size)
        {
            _explorerHelper = explorerHelper;
            _fullScreenHelper = fullScreenHelper;
            _appBarManager = appBarManager;

            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;

            PropertyChanged += AppBarWindow_PropertyChanged;

            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Title = "";
            Topmost = true;
            UseLayoutRounding = true;
            WindowStyle = WindowStyle.None;

            Screen = screen;
            AppBarEdge = edge;
            AppBarMode = mode;

            if (Orientation == Orientation.Vertical)
            {
                DesiredWidth = size;
            }
            else
            {
                DesiredHeight = size;
            }
        }

        private void AppBarWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsOpening)
            {
                return;
            }

            if (e.PropertyName == nameof(AppBarMode))
            {
                if (AppBarMode == AppBarMode.Normal)
                {
                    RegisterAppBar();
                }
                else
                {
                    UnregisterAppBar();
                }

                if (AppBarMode == AppBarMode.AutoHide)
                {
                    _appBarManager.RegisterAutoHideBar(this);
                }
                else
                {
                    _appBarManager.UnregisterAutoHideBar(this);
                }
            }
        }

        #region Events
        protected virtual void OnSourceInitialized(object sender, EventArgs e)
        {
            // set up helper and get handle
            helper = new WindowInteropHelper(this);
            Handle = helper.Handle;

            // set up window procedure
            HwndSource source = HwndSource.FromHwnd(Handle);
            source.AddHook(WndProc);

            // set initial DPI. We do it here so that we get the correct value when DPI has changed since initial user logon to the system.
            if (Screen.Primary)
            {
                DpiHelper.DpiScale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
            }

            // use system DPI initially; when we set position we will get WM_DPICHANGED and set it correctly
            DpiScale = DpiHelper.DpiScale;

            SetPosition();

            if (EnvironmentHelper.IsAppRunningAsShell)
            {
                // set position again, on a delay, in case one display has a different DPI. for some reason the system overrides us if we don't wait
                DelaySetPosition();
            }

            if (AppBarMode == AppBarMode.Normal)
            {
                RegisterAppBar();
            }
            else if (AppBarMode == AppBarMode.AutoHide)
            {
                _appBarManager.RegisterAutoHideBar(this);
            }

            // hide from alt-tab etc
            WindowHelper.HideWindowFromTasks(Handle);

            // register for full-screen notifications
            _fullScreenHelper.FullScreenApps.CollectionChanged += FullScreenApps_CollectionChanged;

            IsOpening = false;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            IsClosing = true;

            CustomClosing();

            if (AllowClose)
            {
                UnregisterAppBar();
                _appBarManager.UnregisterAutoHideBar(this);

                // unregister full-screen notifications
                _fullScreenHelper.FullScreenApps.CollectionChanged -= FullScreenApps_CollectionChanged;
            }
            else
            {
                IsClosing = false;
                e.Cancel = true;
            }
        }

        private void FullScreenApps_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            bool found = false;

            foreach (FullScreenApp app in _fullScreenHelper.FullScreenApps)
            {
                if (app.screen.DeviceName == Screen.DeviceName || app.screen.IsVirtualScreen)
                {
                    // we need to not be on top now
                    found = true;
                    break;
                }
            }

            if (found && Topmost)
            {
                setFullScreenMode(true);
            }
            else if (!found && !Topmost)
            {
                setFullScreenMode(false);
            }
        }

       
        protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == AppBarMessageId && AppBarMessageId != -1)
            {
                switch ((NativeMethods.AppBarNotifications)wParam.ToInt32())
                {
                    case NativeMethods.AppBarNotifications.PosChanged:
                        if (Orientation == Orientation.Vertical)
                        {
                            _appBarManager.ABSetPos(this, DesiredWidth * DpiScale, ActualHeight * DpiScale, AppBarEdge);
                        }
                        else
                        {
                            _appBarManager.ABSetPos(this, ActualWidth * DpiScale, DesiredHeight * DpiScale, AppBarEdge);
                        }
                        break;

                    case NativeMethods.AppBarNotifications.WindowArrange:
                        if ((int)lParam != 0) // before
                        {
                            Visibility = Visibility.Collapsed;
                        }
                        else // after
                        {
                            Visibility = Visibility.Visible;
                        }

                        break;
                }
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.ACTIVATE && AppBarMode == AppBarMode.Normal && !EnvironmentHelper.IsAppRunningAsShell && !AllowClose)
            {
                _appBarManager.AppBarActivate(hwnd);
            }
            else if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING)
            {
                // Extract the WINDOWPOS structure corresponding to this message
                NativeMethods.WINDOWPOS wndPos = NativeMethods.WINDOWPOS.FromMessage(lParam);

                // Determine if the z-order is changing (absence of SWP_NOZORDER flag)
                // If we are intentionally trying to become topmost, make it so
                if (IsRaising && (wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOZORDER) == 0)
                {
                    // Sometimes Windows thinks we shouldn't go topmost, so poke here to make it happen.
                    wndPos.hwndInsertAfter = (IntPtr)NativeMethods.WindowZOrder.HWND_TOPMOST;
                    wndPos.UpdateMessage(lParam);
                }
            }
            else if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGED && AppBarMode == AppBarMode.Normal && !EnvironmentHelper.IsAppRunningAsShell && !AllowClose)
            {
                _appBarManager.AppBarWindowPosChanged(hwnd);
            }
            else if (msg == (int)NativeMethods.WM.DPICHANGED)
            {
                DpiScale = (wParam.ToInt32() & 0xFFFF) / 96d;

                if (Screen.Primary)
                {
                    DpiHelper.DpiScale = DpiScale;
                }

                // suppress this if we are opening, because we're getting this message as a result of positioning
                if (!IsOpening)
                {
                    ProcessScreenChange(ScreenSetupReason.DpiChange);
                }
            }
            else if (msg == (int)NativeMethods.WM.DISPLAYCHANGE)
            {
                ProcessScreenChange(ScreenSetupReason.DisplayChange);
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.DEVICECHANGE && (int)wParam == 0x0007)
            {
                ProcessScreenChange(ScreenSetupReason.DeviceChange);
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.DWMCOMPOSITIONCHANGED)
            {
                ProcessScreenChange(ScreenSetupReason.DwmChange);
                handled = true;
            }
            
            return IntPtr.Zero;
        }
        #endregion

        #region Helpers
        private void DelaySetPosition()
        {
            // delay changing things when we are shell. it seems that explorer AppBars do this too.
            // if we don't, the system moves things to bad places
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.1) };
            timer.Start();
            timer.Tick += (sender1, args) =>
            {
                SetPosition();
                timer.Stop();
            };
        }

        public void SetScreenPosition()
        {
            // set our position if running as shell, otherwise let AppBar do the work
            if (EnvironmentHelper.IsAppRunningAsShell || AppBarMode != AppBarMode.Normal)
            {
                DelaySetPosition();
            }
            else if (AppBarMode == AppBarMode.Normal)
            {
                if (Orientation == Orientation.Vertical)
                {
                    _appBarManager.ABSetPos(this, DesiredWidth * DpiScale, Screen.Bounds.Height, AppBarEdge);
                }
                else
                {
                    _appBarManager.ABSetPos(this, Screen.Bounds.Width, DesiredHeight * DpiScale, AppBarEdge);
                }
            }
        }

        internal void SetAppBarPosition(NativeMethods.Rect rect)
        {
            int swp = (int)NativeMethods.SetWindowPosFlags.SWP_NOZORDER | (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE;

            if (rect.Width < 0 || rect.Height < 0)
            {
                swp |= (int)NativeMethods.SetWindowPosFlags.SWP_NOSIZE;
            }

            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, swp);
        }


        private void ProcessScreenChange(ScreenSetupReason reason)
        {
            // process screen changes if we are on the primary display and the designated window
            // (or any display in the case of a DPI change, since only the changed display receives that message and not all windows receive it reliably)
            // suppress this if we are shutting down (which can trigger this method on multi-dpi setups due to window movements)
            if (((Screen.Primary && ProcessScreenChanges) || reason == ScreenSetupReason.DpiChange) && !AllowClose)
            {
                SetScreenProperties(reason);
            }
        }

        private void setFullScreenMode(bool entering)
        {
            if (entering)
            {
                ShellLogger.Debug($"AppBarWindow: {Name} on {Screen.DeviceName} conceding to full-screen app");

                Topmost = false;
                WindowHelper.ShowWindowBottomMost(Handle);
            }
            else
            {
                ShellLogger.Debug($"AppBarWindow: {Name} on {Screen.DeviceName} returning to normal state");

                IsRaising = true;
                Topmost = true;
                WindowHelper.ShowWindowTopMost(Handle);
                IsRaising = false;
            }
        }

        private bool HasContextMenu(FrameworkElement fe)
        {
            if (fe == null)
            {
                return false;
            }

            if (fe.ContextMenu != null)
            {
                return true;
            }
            else
            {
                var parent = VisualTreeHelper.GetParent(fe) as FrameworkElement;
                return HasContextMenu(parent);
            }
        }


        protected void RegisterAppBar()
        {
            if (AppBarMode != AppBarMode.Normal || _appBarManager.AppBars.Contains(this))
            {
                return;
            }

            if (Orientation == Orientation.Vertical)
            {
                AppBarMessageId = _appBarManager.RegisterBar(this, DesiredWidth * DpiScale, ActualHeight * DpiScale, AppBarEdge);
            }
            else
            {
                AppBarMessageId = _appBarManager.RegisterBar(this, ActualWidth * DpiScale, DesiredHeight * DpiScale, AppBarEdge);
            }
        }

        protected void UnregisterAppBar()
        {
            if (!_appBarManager.AppBars.Contains(this))
            {
                return;
            }

            if (Orientation == Orientation.Vertical)
            {
                _appBarManager.RegisterBar(this, DesiredWidth * DpiScale, ActualHeight * DpiScale);
            }
            else
            {
                _appBarManager.RegisterBar(this, ActualWidth * DpiScale, DesiredHeight * DpiScale);
            }
        }
        #endregion

        #region Virtual methods
        public virtual void AfterAppBarPos(bool isSameCoords, NativeMethods.Rect rect)
        {
            if (!isSameCoords)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.1) };
                timer.Tick += (sender1, args) =>
                {
                    // set position again, since WPF may have overridden the original change from AppBarHelper
                    SetAppBarPosition(rect);

                    timer.Stop();
                };
                timer.Start();
            }
        }
        
        protected virtual void CustomClosing() { }

        protected virtual void SetScreenProperties(ScreenSetupReason reason)
        {
            _fullScreenHelper.NotifyScreensChanged();

            if (Screen.Primary && reason != ScreenSetupReason.DpiChange)
            {
                Screen = AppBarScreen.FromPrimaryScreen();
            }
            SetScreenPosition();
        }

        public virtual void SetPosition()
        {
            double edgeOffset = 0;
            int left;
            int top;
            int height;
            int width;

            if (!RequiresScreenEdge)
            {
                edgeOffset = _appBarManager.GetAppBarEdgeWindowsHeight(AppBarEdge, Screen);
            }

            if (Orientation == Orientation.Vertical)
            {
                top = Screen.Bounds.Top;
                height = Screen.Bounds.Height;
                width = Convert.ToInt32(DesiredWidth * DpiScale);

                if (AppBarEdge == AppBarEdge.Left)
                {
                    left = Screen.Bounds.Left + Convert.ToInt32(edgeOffset * DpiScale);
                }
                else
                {
                    left = Screen.Bounds.Right - width - Convert.ToInt32(edgeOffset * DpiScale);
                }
            }
            else
            {
                left = Screen.Bounds.Left;
                width = Screen.Bounds.Width;
                height = Convert.ToInt32(DesiredHeight * DpiScale);

                if (AppBarEdge == AppBarEdge.Top)
                {
                    top = Screen.Bounds.Top + Convert.ToInt32(edgeOffset * DpiScale);
                }
                else
                {
                    top = Screen.Bounds.Bottom - height - Convert.ToInt32(edgeOffset * DpiScale);
                }
            }

            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, left, top, width, height, (int)NativeMethods.SetWindowPosFlags.SWP_NOZORDER | (int)NativeMethods.SetWindowPosFlags.SWP_NOACTIVATE);


            if (EnvironmentHelper.IsAppRunningAsShell)
            {
                _appBarManager.SetWorkArea(Screen);
            }
        }
        #endregion

        #region INotifyPropertyChanged
        
        public event PropertyChangedEventHandler PropertyChanged;
        public void InvokePropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        #endregion
    }
}