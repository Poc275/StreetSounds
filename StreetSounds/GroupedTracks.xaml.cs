using Bing.Maps;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace StreetSounds
{
    public sealed partial class GroupedTracks : StreetSounds.Common.LayoutAwarePage
    {
        Bing.Maps.Location mapLocation;
        Callisto.Controls.Flyout flyout;
        
        public GroupedTracks()
        {
            this.InitializeComponent();

            mapLocation = new Bing.Maps.Location();

            // App bar event handlers
            BottomAppBar.Closed += BottomAppBar_Closed;
            BottomAppBar.Opened += BottomAppBar_Opened;

            // Run-time xaml controls for flyout menu
            flyout = new Callisto.Controls.Flyout();
            flyout.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

            StackPanel stackPanel = new StackPanel();
            stackPanel.Orientation = Orientation.Horizontal;

            TextBox cityInputTextBox = new TextBox();
            cityInputTextBox.Text = "Enter city here...";
            cityInputTextBox.Width = 250;

            Button cityInputButton = new Button();
            cityInputButton.Content = "Go";
            cityInputButton.Width = 70;
            // Button click event delegate
            cityInputButton.Click += new RoutedEventHandler(async delegate(Object o, RoutedEventArgs args)
            {
                BottomAppBar.IsOpen = false;
                flyout.IsOpen = false;

                // Get input text and validate
                string city = cityInputTextBox.Text;
                city = city.Trim();
                if (string.IsNullOrEmpty(city) || Regex.IsMatch(city, "[0-9-!#@$%^&*()_+|~=`{}/\\:;'<>?.]"))
                {
                    MessageDialog dialog = new MessageDialog("Invalid city entered");
                    await dialog.ShowAsync();
                }
                else
                {
                    this.DataContext = null;
                    groupListView.ItemsSource = null;
                    var tracksModel = await TracksModel.GetModel();
                    tracksModel.ModelMode = "city";

                    progressRing.IsActive = true;
                    progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    await tracksModel.UpdateModel(city, 0, 0);
                    this.DataContext = tracksModel;
                    groupListView.ItemsSource = tracksModel.TrackGroups;
                    progressRing.IsActive = false;
                    progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                    // Plot the track locations on the map
                    await PlotTrackGroupsOnMap();
                }
            });

            stackPanel.Children.Add(cityInputTextBox);
            stackPanel.Children.Add(cityInputButton);
            flyout.Content = stackPanel;
        }

        protected override async void LoadState(object navigationParameter, Dictionary<string, object> pageState)
        {
            progressRing.IsActive = true;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
            var tracksModel = await TracksModel.GetModel();
            this.DataContext = tracksModel;
            // Set semantic zoom out view items source
            groupListView.ItemsSource = tracksModel.TrackGroups;
            progressRing.IsActive = false;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // Update map depending on which mode we are in
            if (tracksModel.ModelMode.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                await PlotTrackGroupsOnMap();
            }
            else if (tracksModel.ModelMode.Equals("location", StringComparison.OrdinalIgnoreCase))
            {
                Bing.Maps.Location loc = await GetUsersLocation();
                await PlotTracksOnMap(loc);
            }
            else if (tracksModel.ModelMode.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                // Get saved location
                if (pageState.ContainsKey("location"))
                {
                    object savedLocation = null;
                    
                    if (pageState.TryGetValue("location", out savedLocation))
                    {
                        mapLocation = (Bing.Maps.Location)savedLocation;
                        await PlotTracksOnMap(mapLocation);
                    }
                }
            }
            else if (tracksModel.ModelMode.Equals("city", StringComparison.OrdinalIgnoreCase))
            {
                await PlotTrackGroupsOnMap();
            }
        }

        protected override async void SaveState(Dictionary<string, object> pageState)
        {
            var tracksModel = await TracksModel.GetModel();

            if (tracksModel.ModelMode.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                // We need to save the point on the map where the
                // user is searching so we can navigate to it upon return
                pageState["location"] = mapLocation;
            }
        }

        // Pushpin Pointer Entered event handler
        void PushpinControl_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            PushpinControl p = sender as PushpinControl;
            p.DisplayInfo();
        }

        // Pushpin Pointer Exited event handler
        void PushpinControl_PointerExited(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            PushpinControl p = sender as PushpinControl;
            p.HideInfo();
        }


        // Item click event handler
        private void ItemGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            // Add track to beginning of playlist and go to player page
            var playlist = Playlist.Instance;
            playlist.PlaylistTracks.Insert(0, (Track)e.ClickedItem);
            this.Frame.Navigate(typeof(Player), (Track)e.ClickedItem);
        }

        // Group click event handler
        private void Header_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // Get the header that was clicked
            var group = (sender as FrameworkElement).DataContext;

            // Pass the group to the group detail page
            this.Frame.Navigate(typeof(TrackCity), ((TrackGroup)group));
        }

        // Item selection changed event handler
        private void ItemGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Check view state to see if we need to query the grid or list view
            ApplicationViewState viewState = ApplicationView.Value;
            if (viewState == ApplicationViewState.Snapped)
            {
                if (itemListView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    addToPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = itemListView.SelectedItems.Count > 0;
                }
            }
            else
            {
                if (itemGridView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    addToPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = itemGridView.SelectedItems.Count > 0;
                }
            }
        }

        // Add to playlist button click event handler
        private void AddToPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            IList<object> selectedItems = null;
            var playlist = Playlist.Instance;
            ApplicationViewState viewState = ApplicationView.Value;

            // Check view state
            if (viewState == ApplicationViewState.Snapped)
            {
                selectedItems = (IList<object>)itemListView.SelectedItems;
            }
            else
            {
                selectedItems = (IList<object>)itemGridView.SelectedItems;
            }

            foreach (object track in selectedItems)
            {
                Track castTrack = (Track)track;

                if (castTrack != null)
                {
                    // Add it to the playlist
                    playlist.PlaylistTracks.Add(castTrack);
                }
            }

            // Close bottom app bar after we've added to the playlist
            itemGridView.SelectedItem = null;
            itemListView.SelectedItem = null;
            BottomAppBar.IsOpen = false;
        }

        // Play playlist button click event handler
        private void PlayPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to player page with the 1st playlist track
            var playlist = Playlist.Instance;
            this.Frame.Navigate(typeof(Player), playlist.GetTrack(0));
        }

        // Clear playlist button click event handler
        private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear playlist and close app bar
            var playlist = Playlist.Instance;
            playlist.ClearPlaylist();
            playPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            clearPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            BottomAppBar.IsOpen = false;
        }

        // Bottom app bar closed event handler
        private void BottomAppBar_Closed(object sender, object e)
        {
            // Deselect any selected items if app bar is closed
            itemGridView.SelectedItem = null;
            itemListView.SelectedItem = null;
            // Revert app bar back to normal
            BottomAppBar.IsSticky = false;
            addToPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        // Bottom app bar opened event handler
        private void BottomAppBar_Opened(object sender, object e)
        {
            // Show play button if playlist is populated
            var playlist = Playlist.Instance;
            if (playlist.PlaylistCount > 0)
            {
                playPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                clearPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
        }

        // Base OnTapped method override for dismissing the app bar when clicking outisde of it
        protected override void OnTapped(Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            base.OnTapped(e);
            BottomAppBar.IsOpen = false;
        }

        // Default mode (City sounds) button click event handler
        private async void DefaultModeButton_Click(object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsOpen = false;
            this.DataContext = null;
            groupListView.ItemsSource = null;

            var tracksModel = await TracksModel.GetModel();
            tracksModel.Offset = 0;
            tracksModel.ModelMode = "default";

            progressRing.IsActive = true;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await tracksModel.UpdateModel("", 0, 0);
            this.DataContext = tracksModel;

            groupListView.ItemsSource = tracksModel.TrackGroups;

            progressRing.IsActive = false;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // Plot on map
            await PlotTrackGroupsOnMap();
        }

        // Sounds near me mode button click event handler
        private async void SoundsNearMeButton_Click(object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsOpen = false;
            this.DataContext = null;
            groupListView.ItemsSource = null;

            // This mode gets geo-tagged sounds near the user's current location
            var tracksModel = await TracksModel.GetModel();
            tracksModel.Offset = 0;
            tracksModel.ModelMode = "location";

            // Get current location
            Bing.Maps.Location currentLocation = await GetUsersLocation();

            progressRing.IsActive = true;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await tracksModel.UpdateModel("", Math.Round(currentLocation.Latitude, 1), Math.Round(currentLocation.Longitude, 1));
            this.DataContext = tracksModel;

            groupListView.ItemsSource = tracksModel.TrackGroups;

            progressRing.IsActive = false;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // Plot the track locations on the map
            await PlotTracksOnMap(currentLocation);
        }

        // Get sounds near here mode button click event handler
        private async void SoundsNearHereButton_Click(object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsOpen = false;
            this.DataContext = null;
            groupListView.ItemsSource = null;

            // This mode gets geo-tagged sounds from the centre of where the map has been navigated to
            var tracksModel = await TracksModel.GetModel();
            tracksModel.Offset = 0;
            tracksModel.ModelMode = "map";

            // Get map centre point
            mapLocation = myMap.Center;

            progressRing.IsActive = true;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await tracksModel.UpdateModel("", Math.Round(mapLocation.Latitude, 1), Math.Round(mapLocation.Longitude, 1));
            this.DataContext = tracksModel;

            groupListView.ItemsSource = tracksModel.TrackGroups;

            progressRing.IsActive = false;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // Plot the track locations on the map
            await PlotTracksOnMap(mapLocation);
        }

        // Get sounds from a specific city button click event handler
        private void SoundsFromCityButton_Click(object sender, RoutedEventArgs e)
        {
            // Using the callisto controls here which contains an easy to use
            // flyout control that acts as a secondary menu to allow city input
            flyout.PlacementTarget = sender as UIElement;
            flyout.Placement = Windows.UI.Xaml.Controls.Primitives.PlacementMode.Top;
            flyout.IsOpen = true;
        }

        // Get more sounds button click event handler
        private async void MoreSoundsButton_Click(object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsOpen = false;
            this.DataContext = null;
            groupListView.ItemsSource = null;

            var tracksModel = await TracksModel.GetModel();
            // Update offset
            tracksModel.Offset++;
            tracksModel.ModelMode = "default";

            progressRing.IsActive = true;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await tracksModel.UpdateModel("", 0, 0);
            this.DataContext = tracksModel;

            groupListView.ItemsSource = tracksModel.TrackGroups;

            progressRing.IsActive = false;
            progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // Plot on map
            await PlotTrackGroupsOnMap();
        }


        // Method that gets a user's current location
        private async Task<Bing.Maps.Location> GetUsersLocation()
        {
            Geolocator geolocator = new Geolocator();
            Geoposition pos = await geolocator.GetGeopositionAsync();
            return new Bing.Maps.Location(pos.Coordinate.Latitude, pos.Coordinate.Longitude);
        }


        // Method that plots individual tracks on the map
        private async Task PlotTracksOnMap(Bing.Maps.Location currentLoc)
        {
            var tracksModel = await TracksModel.GetModel();
            // Point to set anchor position of custom pushpin user control
            Point pushpinPoint = new Point(16, 42);
            myMap.Children.Clear();

            // Plot user's current position
            Pushpin p = new Pushpin();
            p.Text = "!";
            p.Background = new SolidColorBrush(Colors.Red);
            MapLayer.SetPosition(p, currentLoc);
            myMap.Children.Add(p);

            foreach (TrackGroup group in tracksModel.TrackGroups)
            {
                foreach (Track track in group.Tracks)
                {
                    Bing.Maps.Location location = new Bing.Maps.Location(track.Latitude, track.Longitude);
                    PushpinControl myPushpin = new PushpinControl();
                    myPushpin.PushpinTitle = track.Title;
                    myPushpin.PointerEntered += PushpinControl_PointerEntered;
                    myPushpin.PointerExited += PushpinControl_PointerExited;
                    MapLayer.SetPosition(myPushpin, location);
                    MapLayer.SetPositionAnchor(myPushpin, pushpinPoint);
                    myMap.Children.Add(myPushpin);
                }
            }

            myMap.SetView(currentLoc, 10);
        }

        // Method that plots track groups (cities) on the map
        private async Task PlotTrackGroupsOnMap()
        {
            var tracksModel = await TracksModel.GetModel();
            Point pushpinPoint = new Point(16, 42);
            myMap.Children.Clear();

            foreach (TrackGroup city in tracksModel.TrackGroups)
            {
                Bing.Maps.Location cityLocation = new Bing.Maps.Location(city.Latitude, city.Longitude);
                PushpinControl p = new PushpinControl();
                p.PushpinTitle = city.Title;
                p.PushpinSubtitle = string.Format("{0} tracks", city.Tracks.Count);
                p.PointerEntered += PushpinControl_PointerEntered;
                p.PointerExited += PushpinControl_PointerExited;
                MapLayer.SetPosition(p, cityLocation);
                MapLayer.SetPositionAnchor(p, pushpinPoint);
                myMap.Children.Add(p);
            }

            myMap.SetView(new LocationRect(new Bing.Maps.Location(0.0, 0.0), 0, 150));
        }

    }
}
