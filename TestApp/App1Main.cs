using System;

namespace TestApp
{
    public unsafe class App1Main : IDisposable
    {
        #region Fields
        // TODO: Replace with your own content renderers.
        private SceneRenderer _sceneRenderer;

        // Rendering loop timer.
        private StepTimer _timer;
        #endregion

        #region Constructors
        // Loads and initializes application assets when the application is loaded.
        public App1Main()
        {
            _timer = new StepTimer();

            // TODO: Change the timer settings if you want something other than the default variable timestep mode.
            // e.g. for 60 FPS fixed timestep update logic, call:
            /*
                m_timer.SetFixedTimeStep(true);
                m_timer.SetTargetElapsedSeconds(1.0 / 60);
            */
        }

        ~App1Main()
        {
            Dispose();
        }
        #endregion

        #region Properties
        private SceneRenderer SceneRenderer
        {
            get
            {
                return _sceneRenderer;
            }

            set
            {
                if (_sceneRenderer != null)
                {
                    _sceneRenderer.Dispose();
                }
                _sceneRenderer = value;
            }
        }
        #endregion

        #region Methods
        // Creates and initializes the renderers.
        public void CreateRenderers(DeviceResources deviceResources)
        {
            // TODO: Replace this with your app's content initialization.
            SceneRenderer = new SceneRenderer(deviceResources);

            OnWindowSizeChanged();
        }

        // Updates the application state once per frame.
        public void Update()
        {
            // Update scene objects.
            _timer.Tick(() =>
            {
                // TODO: Replace this with your app's content update functions.
                SceneRenderer.Update(_timer);
            });
        }

        // Renders the current frame according to the current application state.
        // Returns true if the frame was rendered and is ready to be displayed.
        public bool Render()
        {
            // Don't try to render anything before the first Update.
            if (_timer.FrameCount == 0)
            {
                return false;
            }

            // Render the scene objects.
            // TODO: Replace this with your app's content rendering functions.
            return SceneRenderer.Render();
        }

        // Updates application state when the window's size changes (e.g. device orientation change)
        public void OnWindowSizeChanged()
        {
            // TODO: Replace this with the size-dependent initialization of your app's content.
            SceneRenderer.CreateWindowSizeDependentResources();
        }

        // Notifies the app that it is being suspended.
        public void OnSuspending()
        {
            // TODO: Replace this with your app's suspending logic.

            // Process lifetime management may terminate suspended apps at any time, so it is
            // good practice to save any state that will allow the app to restart where it left off.

            SceneRenderer.SaveState();

            // If your application uses video memory allocations that are easy to re-create,
            // consider releasing that memory to make it available to other applications.
        }

        // Notifes the app that it is no longer suspended.
        public void OnResuming()
        {
            // TODO: Replace this with your app's resuming logic.
        }

        // Notifies renderers that device resources need to be released.
        public void OnDeviceRemoved()
        {
            // TODO: Save any necessary application or renderer state and release the renderer
            // and its resources which are no longer valid.
            SceneRenderer.SaveState();
            SceneRenderer = null;
        }
        #endregion

        #region System.IDisposable
        public void Dispose()
        {
            SceneRenderer = null;
        }
        #endregion
    }
}
