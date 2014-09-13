using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;


namespace StreetSounds
{
    /// <summary>
    /// This page displays search results when a global search is directed to this application.
    /// </summary>
    public sealed partial class SearchResultsPage : StreetSounds.Common.LayoutAwarePage
    {
        // Search results collection
        private Dictionary<string, List<Track>> searchResults = new Dictionary<string, List<Track>>();

        const string clientId = "?client_id=14850cf5fe29529f021db5810755a9c9";
        HttpClient httpClient = null;
        HttpClientHandler handler = null;

        public SearchResultsPage()
        {
            this.InitializeComponent();

            // Setup HttpClient
            handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("http://api.soundcloud.com");
            httpClient.MaxResponseContentBufferSize = 256000;
            httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

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
        protected async override void LoadState(Object navigationParameter, Dictionary<String, Object> pageState)
        {
            // Store search string in search model
            var queryText = navigationParameter as String;
            var searchModel = SearchModel.Instance;
            searchModel.SearchString = queryText;
            searchModel.SearchModelOffset = 0;
            await Initialise();
        }

        /// <summary>
        /// Invoked when a filter is selected using the ComboBox in snapped view state.
        /// </summary>
        /// <param name="sender">The ComboBox instance.</param>
        /// <param name="e">Event data describing how the selected filter was changed.</param>
        void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Determine what filter was selected
            var selectedFilter = e.AddedItems.FirstOrDefault() as Filter;
            if (selectedFilter != null)
            {
                // Mirror the results into the corresponding Filter object to allow the
                // RadioButton representation used when not snapped to reflect the change
                selectedFilter.Active = true;

                // TODO: Respond to the change in active filter by setting this.DefaultViewModel["Results"]
                //       to a collection of items with bindable Image, Title, Subtitle, and Description properties
                this.DefaultViewModel["Results"] = searchResults[selectedFilter.Name];

                // Ensure results are found
                object results;
                ICollection resultsCollection;
                if (this.DefaultViewModel.TryGetValue("Results", out results) &&
                    (resultsCollection = results as ICollection) != null &&
                    resultsCollection.Count != 0)
                {
                    VisualStateManager.GoToState(this, "ResultsFound", true);
                    return;
                }
            }

            // Display informational text when there are no search results.
            VisualStateManager.GoToState(this, "NoResultsFound", true);
        }

        /// <summary>
        /// Invoked when a filter is selected using a RadioButton when not snapped.
        /// </summary>
        /// <param name="sender">The selected RadioButton instance.</param>
        /// <param name="e">Event data describing how the RadioButton was selected.</param>
        void Filter_Checked(object sender, RoutedEventArgs e)
        {
            // Mirror the change into the CollectionViewSource used by the corresponding ComboBox
            // to ensure that the change is reflected when snapped
            if (filtersViewSource.View != null)
            {
                var filter = (sender as FrameworkElement).DataContext;
                filtersViewSource.View.MoveCurrentTo(filter);
            }
        }

        /// <summary>
        /// View model describing one of the filters available for viewing search results.
        /// </summary>
        private sealed class Filter : StreetSounds.Common.BindableBase
        {
            private String _name;
            private int _count;
            private bool _active;

            public Filter(String name, int count, bool active = false)
            {
                this.Name = name;
                this.Count = count;
                this.Active = active;
            }

            public override String ToString()
            {
                return Description;
            }

            public String Name
            {
                get { return _name; }
                set { if (this.SetProperty(ref _name, value)) this.OnPropertyChanged("Description"); }
            }

            public int Count
            {
                get { return _count; }
                set { if (this.SetProperty(ref _count, value)) this.OnPropertyChanged("Description"); }
            }

            public bool Active
            {
                get { return _active; }
                set { this.SetProperty(ref _active, value); }
            }

            public String Description
            {
                get { return String.Format("{0} ({1})", _name, _count); }
            }
        }

        // Method that initialises the page layout & model
        private async Task Initialise()
        {
            var searchModel = SearchModel.Instance;

            // Clear the search model ready for new set of search results
            searchModel.ClearSearchModel();
            searchResults.Clear();

            // Query SoundCloud to get search results
            await SearchSoundCloud(searchModel.SearchString.ToLower());

            var filterList = new List<Filter>();
            filterList.Add(new Filter("All", 0, true));

            // Get filters from groups
            var trackGroups = searchModel.SearchGroups;
            var all = new List<Track>();
            searchResults.Add("All", all);

            foreach (var group in trackGroups)
            {
                var items = new List<Track>();
                searchResults.Add(group.Title, items);

                foreach (var item in group.Tracks)
                {
                    all.Add(item);
                    items.Add(item);
                }

                filterList.Add(new Filter(group.Title, items.Count, false));
            }

            // Get total for "All" count
            filterList[0].Count = all.Count;

            // Communicate results through the view model
            this.DefaultViewModel["QueryText"] = '\u201c' + searchModel.SearchString + '\u201d';
            this.DefaultViewModel["Filters"] = filterList;
            this.DefaultViewModel["ShowFilters"] = filterList.Count > 1;
        }

        // Method that searches SoundCloud to get search results
        private async Task SearchSoundCloud(string query)
        {
            string error = "";
            bool failed = false;
            
            try
            {
                var searchModel = SearchModel.Instance;
                string apiCall = "";

                if (searchModel.SearchModelOffset > 0)
                {
                    int offset = searchModel.SearchModelOffset * 50;
                    apiCall = "/tracks" + clientId + "&q=" + query + "&offset=" + offset.ToString();
                }
                else
                {
                    apiCall = "/tracks" + clientId + "&q=" + query;
                }
                
                HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var results = JsonArray.Parse(responseBody);
                ParseSearchResults(results);
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
        }

        // Method that parses the search results
        private void ParseSearchResults(JsonArray array)
        {
            var searchModel = SearchModel.Instance;

            foreach (var item in array)
            {
                var obj = item.GetObject();
                Track track = new Track();
                TrackGroup trackGroup = null;
                bool newGroup = true;

                // for each key in an item
                foreach (var key in obj.Keys)
                {
                    IJsonValue val;

                    if (!obj.TryGetValue(key, out val))
                    {
                        continue;
                        // Output error
                    }
                    else
                    {
                        JsonValueType jsonType = val.ValueType;

                        switch (key)
                        {
                            case "artwork_url":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.Image = "ms-appx:///Images/no-artwork-t200x200.png";
                                }
                                else
                                {
                                    track.Image = val.GetString().Replace("-large", "-t200x200");
                                }
                                break;

                            case "title":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.Title = "";
                                }
                                else
                                {
                                    track.Title = val.GetString();
                                }
                                break;

                            case "stream_url":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.StreamUrl = "";
                                }
                                else
                                {
                                    track.StreamUrl = val.GetString();
                                }
                                break;

                            case "id":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.Id = "";
                                }
                                else
                                {
                                    track.Id = val.GetNumber().ToString();
                                }
                                break;

                            case "user_id":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.UserId = "";
                                }
                                else
                                {
                                    track.UserId = val.GetNumber().ToString();
                                }
                                break;

                            case "playback_count":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.PlaybackCount = 0;
                                }
                                else
                                {
                                    track.PlaybackCount = (uint)val.GetNumber();
                                }
                                break;

                            case "duration":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.Duration = 0;
                                }
                                else
                                {
                                    track.Duration = (uint)val.GetNumber();
                                }
                                break;

                            case "comment_count":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.CommentCount = 0;
                                }
                                else
                                {
                                    track.CommentCount = (uint)val.GetNumber();
                                }
                                break;

                            case "favoritings_count":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.FavouriteCount = 0;
                                }
                                else
                                {
                                    track.FavouriteCount = (uint)val.GetNumber();
                                }
                                break;

                            case "waveform_url":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.WaveformUrl = "";
                                }
                                else
                                {
                                    track.WaveformUrl = val.GetString();
                                }
                                break;

                            case "user":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.AvatarUrl = "";
                                    track.Username = "";
                                }
                                else
                                {
                                    var user = val.GetObject();
                                    track.AvatarUrl = user.GetNamedString("avatar_url");
                                    track.Username = user.GetNamedString("username");
                                }
                                break;

                            case "permalink_url":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.PermalinkUrl = "";
                                }
                                else
                                {
                                    track.PermalinkUrl = val.GetString();
                                }
                                break;

                            case "genre":
                                if (jsonType == JsonValueType.Null)
                                {
                                    track.Genre = "Unknown";
                                }
                                else
                                {
                                    if (val.GetString() == "")
                                    {
                                        track.Genre = "Unknown";
                                    }
                                    else
                                    {
                                        track.Genre = val.GetString();
                                    }
                                }

                                // Linear search to see if we need to create a new genre group
                                foreach (TrackGroup group in searchModel.SearchGroups)
                                {
                                    if (group.Title.Equals(track.Genre, StringComparison.OrdinalIgnoreCase))
                                    {
                                        trackGroup = group;
                                        newGroup = false;
                                    }
                                }
                                break;
                            
                            default:
                                break;
                        }
                    }
                }

                // Add track to group
                if (!newGroup)
                {
                    trackGroup.Tracks.Add(track);
                }
                else
                {
                    trackGroup = new TrackGroup(track.Genre);
                    trackGroup.Tracks.Add(track);
                    searchModel.Add(trackGroup);
                }
            }
        }

        // Event handler for when a search item is clicked
        private void ResultsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            // Add track to playlist
            var playlist = Playlist.Instance;
            playlist.PlaylistTracks.Insert(0, (Track)e.ClickedItem);
            // Navigate to the player page and play the track
            this.Frame.Navigate(typeof(Player), ((Track)e.ClickedItem));
        }

        // Bottom app bar closed event handler
        private void BottomAppBar_Closed(object sender, object e)
        {
            // Deselect any selected items if app bar is closed
            resultsGridView.SelectedItem = null;
            resultsListView.SelectedItem = null;
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

        // Selection changed event handler
        private void ResultsGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationViewState viewState = ApplicationView.Value;
            if (viewState == ApplicationViewState.Snapped)
            {
                if (resultsListView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    playlistMenu.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = resultsListView.SelectedItems.Count > 0;
                }
            }
            else
            {
                if (resultsGridView.SelectedItem != null)
                {
                    BottomAppBar.IsSticky = true;
                    playlistMenu.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    // Keep app bar open whilst there are items selected
                    BottomAppBar.IsOpen = resultsGridView.SelectedItems.Count > 0;
                }
            }
        }

        private void AddToPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            IList<object> selectedItems = null;
            var playlist = Playlist.Instance;
            ApplicationViewState viewState = ApplicationView.Value;

            // Check view state
            if (viewState == ApplicationViewState.Snapped)
            {
                selectedItems = (IList<object>)resultsListView.SelectedItems;
            }
            else
            {
                selectedItems = (IList<object>)resultsGridView.SelectedItems;
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
            resultsGridView.SelectedItem = null;
            resultsListView.SelectedItem = null;
            BottomAppBar.IsOpen = false;
        }

        private void PlayPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            var playlist = Playlist.Instance;
            this.Frame.Navigate(typeof(Player), playlist.GetTrack(0));
        }

        private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            var playlist = Playlist.Instance;
            playlist.ClearPlaylist();
            playlistFunctions.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            BottomAppBar.IsOpen = false;
        }

        protected override void OnTapped(Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            base.OnTapped(e);
            BottomAppBar.IsOpen = false;
        }

        private async void MoreSoundsButton_Click(object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsOpen = false;
            var searchModel = SearchModel.Instance;
            searchModel.SearchModelOffset++;
            await Initialise();
        }

    }
}
