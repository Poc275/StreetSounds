using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Controls;


namespace StreetSounds
{
    public sealed partial class TrackCity : StreetSounds.Common.LayoutAwarePage
    {
        const string flickrClientId = "&api_key=5940f0c5724c95c349654f5a5ca862fe";
        
        public TrackCity()
        {
            this.InitializeComponent();

            // App bar event handlers
            BottomAppBar.Closed += BottomAppBar_Closed;
            BottomAppBar.Opened += BottomAppBar_Opened;
        }

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="navigationParameter">The parameter value passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested.
        /// </param>
        /// <param name="pageState">A dictionary of state preserved by this page during an earlier
        /// session.  This will be null the first time a page is visited.</param>
        protected override async void LoadState(Object navigationParameter, Dictionary<String, Object> pageState)
        {
            var group = ((TrackGroup)navigationParameter);

            // Get flickr image for city for display
            if (group.Image == null)
            {
                progressRing.IsActive = true;
                progressRing.Visibility = Windows.UI.Xaml.Visibility.Visible;
                string imageUrl = await GetFlickrImage(group.Title, group.Latitude, group.Longitude);

                if (string.IsNullOrEmpty(imageUrl))
                {
                    // Couldn't get a flickr image - set to a sensible default
                    group.Image = new Uri("ms-appx:///Images/world_map.png");
                }
                else
                {
                    group.Image = new Uri(imageUrl);
                }

                progressRing.IsActive = false;
                progressRing.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }

            this.DefaultViewModel["TrackGroups"] = group;
            this.DefaultViewModel["Tracks"] = group.Tracks;
        }

        // Method that gets city images from flickr
        private async Task<string> GetFlickrImage(string city, double latitude, double longitude)
        {
            string flickrUri = "";
            string error = "";
            bool failed = false;

            try
            {
                HttpClient flickrHttpClient = new HttpClient();
                flickrHttpClient.MaxResponseContentBufferSize = 256000;
                flickrHttpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
                flickrHttpClient.BaseAddress = new Uri("http://api.flickr.com/services/rest/");

                string apiCall = string.Format("?method=flickr.photos.search{0}&tags={1}&text={2}&sort=interestingness-desc&privacy_filter=1&content_type=1&media=photos&lat={3}&lon={4}&per_page=1&page=1&format=json&nojsoncallback=1",
                                                flickrClientId, city, city, latitude, longitude);

                HttpResponseMessage response = await flickrHttpClient.GetAsync(apiCall);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var photos = JsonObject.Parse(responseBody);
                flickrUri = ParseFlickrData(photos);
            }
            catch (HttpRequestException hre)
            {
                error = hre.Message.ToString();
                failed = true;
            }
            catch (Exception ex)
            {
                error = ex.Message.ToString();
                failed = true;
            }

            if (failed)
            {
                MessageDialog dialog = new MessageDialog(error);
                await dialog.ShowAsync();
            }

            return flickrUri;
        }

        // Method that parses the flickr returned JSON data
        private string ParseFlickrData(JsonObject obj)
        {
            string flickrUrl = "";

            var photosObj = obj.GetNamedObject("photos");
            var photoArray = photosObj.GetNamedArray("photo");

            // Will only be a single result as we filtered the API to return only 1 image
            foreach (var item in photoArray)
            {
                var photoDetail = item.GetObject();
                string farm = "";
                string serverId = "";
                string photoId = "";
                string photoSecret = "";

                foreach (var key in photoDetail.Keys)
                {
                    IJsonValue val;

                    if (!photoDetail.TryGetValue(key, out val))
                    {
                        continue;
                        // Output error
                    }
                    else
                    {
                        JsonValueType jsonType = val.ValueType;

                        switch (key)
                        {
                            case "id":
                                if (jsonType != JsonValueType.Null)
                                {
                                    photoId = val.GetString();
                                }
                                break;

                            case "farm":
                                if (jsonType != JsonValueType.Null)
                                {
                                    farm = val.GetNumber().ToString();
                                }
                                break;

                            case "server":
                                if (jsonType != JsonValueType.Null)
                                {
                                    serverId = val.GetString();
                                }
                                break;

                            case "secret":
                                if (jsonType != JsonValueType.Null)
                                {
                                    photoSecret = val.GetString();
                                }
                                break;

                            default:
                                break;
                        }
                    }
                }

                // Build url string
                flickrUrl = string.Format("http://farm{0}.staticflickr.com/{1}/{2}_{3}.jpg",
                                          farm, serverId, photoId, photoSecret);
            }

            return flickrUrl;
        }

        // Item click event handler
        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var playlist = Playlist.Instance;
            playlist.PlaylistTracks.Insert(0, (Track)e.ClickedItem);
            this.Frame.Navigate(typeof(Player), (Track)e.ClickedItem);
        }

        // Bottom app bar closed event handler
        private void BottomAppBar_Closed(object sender, object e)
        {
            // Deselect any selected items if app bar is closed
            itemGridView.SelectedItem = null;
            itemListView.SelectedItem = null;
            // Revert app bar back to normal
            BottomAppBar.IsSticky = false;
            playlistMenu.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        // Bottom app bar opened event handler
        private void BottomAppBar_Opened(object sender, object e)
        {
            // Show play button if playlist is populated
            var playlist = Playlist.Instance;
            if (playlist.PlaylistCount > 0)
            {
                playlistFunctions.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
        }

        protected override void OnTapped(Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            base.OnTapped(e);
            BottomAppBar.IsOpen = false;
        }

        // Selection changed event handler
        private void ItemGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationViewState viewState = ApplicationView.Value;
            if (viewState == ApplicationViewState.Snapped)
            {
                if (itemListView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    playlistMenu.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = itemListView.SelectedItems.Count > 0;
                }
            }
            else
            {
                if (itemGridView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    playlistMenu.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = itemGridView.SelectedItems.Count > 0;
                }
            }
        }

        private void AddToPlaylistButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
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

        private void PlayPlaylistButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var playlist = Playlist.Instance;
            this.Frame.Navigate(typeof(Player), playlist.GetTrack(0));
        }

        private void ClearPlaylistButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var playlist = Playlist.Instance;
            playlist.ClearPlaylist();
            playlistFunctions.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            BottomAppBar.IsOpen = false;
        }
    }
}
