using Callisto.Controls;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI;
using Windows.UI.ApplicationSettings;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;


namespace StreetSounds
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            ExtendedSplashScreen extendedSplashScreen = null;

            // Secondary tile click state check - if app is already running
            if (args.PreviousExecutionState == ApplicationExecutionState.Running || 
                 args.PreviousExecutionState == ApplicationExecutionState.Suspended)
            {
                if (!string.IsNullOrEmpty(args.Arguments))
                {
                    // args.Arguments contains the activation argument passed in the 4th parameter
                    // to the SecondaryTile constructor, so if it is not null, we have clicked a secondary tile
                    var playlist = Playlist.Instance;
                    await playlist.BuildPlaylistFromSecondaryTile(args.Arguments);

                    // Navigate to player page and start playing the playlist
                    ((Frame)Window.Current.Content).Navigate(typeof(Player), playlist.PlaylistTracks[0]);
                }
            }

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                // Settings contract event handler
                SettingsPane.GetForCurrentView().CommandsRequested += App_CommandsRequested;

                // Check if we are launching the app from a secondary tile or not
                if (!string.IsNullOrEmpty(args.Arguments))
                {
                    // We are launching from cold, from a secondary tile
                    var playlist = Playlist.Instance;
                    await playlist.BuildPlaylistFromSecondaryTile(args.Arguments);

                    rootFrame.Navigate(typeof(Player), playlist.PlaylistTracks[0]);
                    Window.Current.Content = rootFrame;
                }
                else
                {
                    // We are launching from cold, from the main tile
                    // Get current splash screen and register for the dismissed event so we can show our extended splash screen
                    SplashScreen splashScreen = args.SplashScreen;
                    extendedSplashScreen = new ExtendedSplashScreen(splashScreen);
                    splashScreen.Dismissed += new Windows.Foundation.TypedEventHandler<SplashScreen, object>(extendedSplashScreen.OnSplashScreenDismissed);
                    Window.Current.Content = extendedSplashScreen;
                }
            }

            // Ensure the current window is active
            Window.Current.Activate();
        }

        // Settings contract event handler, uses the Callisto toolkit to generate the settings panel 'flyout' control
        // Source: http://timheuer.com/blog/archive/2012/05/31/introducing-callisto-a-xaml-toolkit-for-metro-apps.aspx
        void App_CommandsRequested(SettingsPane sender, SettingsPaneCommandsRequestedEventArgs args)
        {
            SettingsCommand preferences = new SettingsCommand("preferences", "Preferences", (handler) =>
                {
                    var settings = new Callisto.Controls.SettingsFlyout();
                    settings.Content = new PreferencesUserControl();
                    settings.HeaderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 77, 96));
                    settings.Background = new SolidColorBrush(Color.FromArgb(255, 0, 77, 96));
                    settings.HeaderText = "Preferences";
                    settings.IsOpen = true;
                });

            args.Request.ApplicationCommands.Add(preferences);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity.
            // Clear live tile as nothing is playing anymore
            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            deferral.Complete();
        }

        /// <summary>
        /// Invoked when the application is activated to display search results.
        /// </summary>
        /// <param name="args">Details about the activation request.</param>
        protected async override void OnSearchActivated(Windows.ApplicationModel.Activation.SearchActivatedEventArgs args)
        {
            // TODO: Register the Windows.ApplicationModel.Search.SearchPane.GetForCurrentView().QuerySubmitted
            // event in OnWindowCreated to speed up searches once the application is already running

            // If the Window isn't already using Frame navigation, insert our own Frame
            var previousContent = Window.Current.Content;
            var frame = previousContent as Frame;

            // If the app does not contain a top-level frame, it is possible that this 
            // is the initial launch of the app. Typically this method and OnLaunched 
            // in App.xaml.cs can call a common method.
            if (frame == null)
            {
                // Create a Frame to act as the navigation context and associate it with
                // a SuspensionManager key
                frame = new Frame();
                StreetSounds.Common.SuspensionManager.RegisterFrame(frame, "AppFrame");

                if (args.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    // Restore the saved session state only when appropriate
                    try
                    {
                        await StreetSounds.Common.SuspensionManager.RestoreAsync();
                    }
                    catch (StreetSounds.Common.SuspensionManagerException)
                    {
                        //Something went wrong restoring state.
                        //Assume there is no state and continue
                    }
                }
            }

            frame.Navigate(typeof(SearchResultsPage), args.QueryText);
            Window.Current.Content = frame;

            // Ensure the current window is active
            Window.Current.Activate();
        }
    }
}
