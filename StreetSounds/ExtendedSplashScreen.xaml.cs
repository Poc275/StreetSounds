using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;


namespace StreetSounds
{
    public sealed partial class ExtendedSplashScreen : Page
    {
        Frame rootFrame;
        
        public ExtendedSplashScreen(SplashScreen splash)
        {
            this.InitializeComponent();

            // Position the extended splash screen image in same place as initial splash screen
            extendedSplashImage.SetValue(Canvas.LeftProperty, splash.ImageLocation.X);
            extendedSplashImage.SetValue(Canvas.TopProperty, splash.ImageLocation.Y);
            extendedSplashImage.Height = splash.ImageLocation.Height;
            extendedSplashImage.Width = splash.ImageLocation.Width;

            // Position progress ring
            extendedSplashProgressRing.SetValue(Canvas.TopProperty, splash.ImageLocation.Y +
                splash.ImageLocation.Height + 32);
            extendedSplashProgressRing.SetValue(Canvas.LeftProperty, splash.ImageLocation.X +
                (splash.ImageLocation.Width / 2) - 15);

            rootFrame = new Frame();
        }

        public async void OnSplashScreenDismissed(Windows.ApplicationModel.Activation.SplashScreen sender, object e)
        {
            if (await TracksModel.GetModel() == null)
            {
                // An error occurred
            }
            else
            {
                // We can navigate to the main page now the content has loaded
                var task = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    rootFrame.Navigate(typeof(GroupedTracks));
                    Window.Current.Content = rootFrame;
                });
            }
        }
    }
}
