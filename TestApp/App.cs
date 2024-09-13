using System;
using System.Threading.Tasks;
using TerraFX.Interop.DirectX;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI.Core;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_DEBUG_RLO_FLAGS;
using static TerraFX.Interop.DirectX.PIX;
using static TerraFX.Interop.Windows.IID;
using static TerraFX.Interop.Windows.Windows;

namespace TestApp
{
    // Main entry point for our app. Connects the app with the Windows shell and handles application lifecycle events.
    public sealed partial class App : IDisposable, IFrameworkView
    {
        #region Fields
        private DeviceResources _deviceResources;
        private App1Main _main;
        private bool _windowClosed;
        private bool _windowVisible;
        #endregion

        #region Constructors
        public App()
        {
            _windowClosed = false;
            _windowVisible = true;
        }

        ~App()
        {
            Dispose();
        }
        #endregion

        #region Properties
        private unsafe DeviceResources DeviceResources
        {
            get
            {
                // All references to the existing D3D device must be released before a new device
                // can be created.

                if ((_deviceResources != null) && _deviceResources.IsDeviceRemoved)
                {
                    _deviceResources.Dispose();
                    _deviceResources = null;
                    Main.OnDeviceRemoved();

#if DEBUG
                    IDXGIDebug1* dxgiDebug = null;

                    try
                    {
                        var iid = IID_IDXGIDebug1;
                        if (SUCCEEDED(DXGIGetDebugInterface1(0, &iid, (void**)&dxgiDebug)))
                        {
                            dxgiDebug->ReportLiveObjects(DXGI_DEBUG_ALL, DXGI_DEBUG_RLO_SUMMARY | DXGI_DEBUG_RLO_IGNORE_INTERNAL);
                        }
                    }
                    finally
                    {
                        if (dxgiDebug != null)
                        {
                            dxgiDebug->Release();
                        }
                    }
#endif
                }

                if (_deviceResources is null)
                {
                    _deviceResources = new DeviceResources();
                    _deviceResources.Window = CoreWindow.GetForCurrentThread();
                    Main.CreateRenderers(_deviceResources);
                }
                return _deviceResources;
            }
        }

        private App1Main Main
        {
            get
            {
                return _main;
            }

            set
            {
                if (_main != null)
                {
                    _main.Dispose();
                }
                _main = value;
            }
        }
        #endregion

        #region Methods
        // Application lifecycle event handlers.

        private void OnActivated(CoreApplicationView applicationView, IActivatedEventArgs args)
        {
            // Run() won't start until the CoreWindow is activated.
            CoreWindow.GetForCurrentThread().Activate();
        }

        private void OnSuspending(object sender, SuspendingEventArgs args)
        {
            // Save app state asynchronously after requesting a deferral. Holding a deferral
            // indicates that the application is busy performing suspending operations. Be
            // aware that a deferral may not be held indefinitely. After about five seconds,
            // the app will be forced to exit.
            var deferral = args.SuspendingOperation.GetDeferral();

            Task.Run(() => {
                Main.OnSuspending();
                deferral.Complete();
            });
        }

        private void OnResuming(object sender, object args)
        {
            // Restore any data or state that was unloaded on suspend. By default, data
            // and state are persisted when resuming from suspend. Note that this event
            // does not occur if the app was previously terminated.

            Main.OnResuming();
        }

        // Window event handlers.

        private void OnWindowSizeChanged(CoreWindow sender, WindowSizeChangedEventArgs args)
        {
            if (Main is null)
            {
                return;
            }

            DeviceResources.LogicalSize = new Size(sender.Bounds.Width, sender.Bounds.Height);
            Main.OnWindowSizeChanged();
        }

        private void OnVisibilityChanged(CoreWindow sender, VisibilityChangedEventArgs args)
        {
            _windowVisible = args.Visible;
        }

        private void OnWindowClosed(CoreWindow sender, CoreWindowEventArgs args)
        {
            _windowClosed = true;
        }

        // DisplayInformation event handlers.

        private void OnDpiChanged(DisplayInformation sender, object args)
        {
            // Note: The value for LogicalDpi retrieved here may not match the effective DPI of the app
            // if it is being scaled for high resolution devices. Once the DPI is set on DeviceResources,
            // you should always retrieve it using the GetDpi method.
            // See DeviceResources.cpp for more details.
            DeviceResources.Dpi = sender.LogicalDpi;
            Main.OnWindowSizeChanged();
        }

        private void OnOrientationChanged(DisplayInformation sender, object args)
        {
            DeviceResources.CurrentOrientation = sender.CurrentOrientation;
            Main.OnWindowSizeChanged();
        }

        private void OnDisplayContentsInvalidated(DisplayInformation sender, object args)
        {
            DeviceResources.ValidateDevice();
        }
        #endregion

        #region System.IDisposable
        public void Dispose()
        {
            Main = null;
        }
        #endregion

        #region Windows.ApplicationModel.Core.IFrameworkView
        // The first method called when the IFrameworkView is being created.
        public void Initialize(CoreApplicationView applicationView)
        {
            // Register event handlers for app lifecycle. This example includes Activated, so that we
            // can make the CoreWindow active and start rendering on the window.
            applicationView.Activated += OnActivated;

            CoreApplication.Suspending += OnSuspending;

            CoreApplication.Resuming += OnResuming;
        }

        // Called when the CoreWindow object is created (or re-created).
        public void SetWindow(CoreWindow window)
        {
            window.SizeChanged += OnWindowSizeChanged;

            window.VisibilityChanged += OnVisibilityChanged;

            window.Closed += OnWindowClosed;

            var currentDisplayInformation = DisplayInformation.GetForCurrentView();

            currentDisplayInformation.DpiChanged += OnDpiChanged;

            currentDisplayInformation.OrientationChanged += OnOrientationChanged;

            DisplayInformation.DisplayContentsInvalidated += OnDisplayContentsInvalidated;
        }

        // Initializes scene resources, or loads a previously saved app state.
        public void Load(string entryPoint)
        {
            if (Main is null)
            {
                Main = new App1Main();
            }
        }

        // This method is called after the window becomes active.
        public unsafe void Run()
        {
            while (!_windowClosed)
            {
                if (_windowVisible)
                {
                    CoreWindow.GetForCurrentThread().Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessAllIfPresent);

                    var commandQueue = DeviceResources.CommandQueue;

                    PIXBeginEvent(commandQueue, 0, "Update");
                    {
                        Main.Update();
                    }
                    PIXEndEvent(commandQueue);

                    PIXBeginEvent(commandQueue, 0, "Render");
                    {
                        if (Main.Render())
                        {
                            DeviceResources.Present();
                        }
                    }
                    PIXEndEvent(commandQueue);
                }
                else
                {
                    CoreWindow.GetForCurrentThread().Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessOneAndAllPending);
                }
            }
        }

        // Required for IFrameworkView.
        // Terminate events do not cause Uninitialize to be called. It will be called if your IFrameworkView
        // class is torn down while the app is in the foreground.
        public void Uninitialize()
        {
        }
        #endregion
    }
}
