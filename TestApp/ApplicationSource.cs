using System;
using Windows.ApplicationModel.Core;

namespace TestApp
{
    public sealed partial class ApplicationSource : IFrameworkViewSource
    {
        // The main function is only used to initialize our IFrameworkView class.
        [MTAThread]
        public static int Main(string[] args)
        {
            var testAppSource = new ApplicationSource();
            CoreApplication.Run(testAppSource);
            return 0;
        }

        #region Windows.ApplicationModel.Core.IFrameworkViewSource
        public IFrameworkView CreateView()
        {
            return new App();
        }
        #endregion
    }
}
