using Callisto.Controls;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Json;
using Windows.Data.Xml.Dom;
using Windows.Media;
using Windows.Media.PlayTo;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Popups;
using Windows.UI.StartScreen;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace StreetSounds
{
    public sealed partial class Player : StreetSounds.Common.LayoutAwarePage
    {
        const string clientId = "?client_id=14850cf5fe29529f021db5810755a9c9";
        HttpClient httpClient = null;
        HttpClientHandler handler = null;
        PlayToManager ptm = null;
        CoreDispatcher dispatcher = null;
        DataTransferManager dtm = null;
        Track playingTrack = null;
        
        public Player()
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

            // Set up the slider and pointer event handlers
            timelineSlider.ValueChanged += timelineSlider_ValueChanged;
            PointerEventHandler pointerpressedhandler = new PointerEventHandler(slider_PointerEntered);
            timelineSlider.AddHandler(Control.PointerPressedEvent, pointerpressedhandler, true);
            PointerEventHandler pointerreleasedhandler = new PointerEventHandler(slider_PointerCaptureLost);
            timelineSlider.AddHandler(Control.PointerCaptureLostEvent, pointerreleasedhandler, true);

            // MediaController event handlers to handle background audio
            MediaControl.PlayPressed += MediaControl_PlayPressed;
            MediaControl.PausePressed += MediaControl_PausePressed;
            MediaControl.PlayPauseTogglePressed += MediaControl_PlayPauseTogglePressed;
            MediaControl.StopPressed += MediaControl_StopPressed;

            // If we have a playlist add next/previous button event handlers
            var playlist = Playlist.Instance;
            if (playlist.PlaylistCount > 0)
            {
                MediaControl.NextTrackPressed += MediaControl_NextTrackPressed;
                MediaControl.PreviousTrackPressed += MediaControl_PreviousTrackPressed;
            }

            // Window resize event for snapped view checking
            Window.Current.SizeChanged += Window_SizeChanged;
        }

        // Window resize event handler
        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            ApplicationViewState viewState = ApplicationView.Value;

            // if snapped view, change to a vertical layout
            if (viewState == ApplicationViewState.Snapped)
            {
                playerColumn.Width = new GridLength(0);
                playerRow.Height = new GridLength(1, GridUnitType.Auto);
                snappedViewRow.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetColumn(playerGrid, 0);
                Grid.SetRow(playerGrid, 1);
                Grid.SetRow(playlistGrid, 2);
                
            }
            
            // Revert back on other views
            if (viewState == ApplicationViewState.Filled ||
                viewState == ApplicationViewState.FullScreenLandscape ||
                viewState == ApplicationViewState.FullScreenPortrait)
            {
                snappedViewRow.Height = new GridLength(0);
                playerColumn.Width = new GridLength(1, GridUnitType.Star);
                playerRow.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetColumn(playerGrid, 1);
                Grid.SetRow(playerGrid, 1);
                Grid.SetRow(playlistGrid, 1);
            }
        }


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            // Set playlist data binding
            var playlist = Playlist.Instance;
            this.DataContext = playlist;

            // TODO - examine why an item is pre-selected when we navigate to the page
            BottomAppBar.IsOpen = false;
            playlistListView.SelectedItem = null;
            
            // PlayTo event handler
            dispatcher = Window.Current.CoreWindow.Dispatcher;
            ptm = PlayToManager.GetForCurrentView();
            ptm.SourceRequested += SourceRequested;

            // Share contract event handler
            dtm = DataTransferManager.GetForCurrentView();
            dtm.DataRequested += new Windows.Foundation.TypedEventHandler<DataTransferManager, DataRequestedEventArgs>(this.ShareLinkHandler);

            // Play clicked track from Tracks page
            if (e.Parameter != null)
            {
                PlayTrack((Track)e.Parameter);
            }
        }


        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Remove event handlers
            ptm.SourceRequested -= SourceRequested;
            dtm.DataRequested -= ShareLinkHandler;

            // Clear now playing tile
            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
        }


        /*
         * MEDIACONTROL EVENT HANDLERS - FOR BG AUDIO
        */
        private async void MediaControl_PlayPressed(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => audioElement.Play());
        }

        private async void MediaControl_PausePressed(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => audioElement.Pause());
        }

        private async void MediaControl_PlayPauseTogglePressed(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (audioElement.CurrentState == MediaElementState.Paused)
                {
                    audioElement.Play();
                }
                else
                {
                    audioElement.Pause();
                }
            });
        }

        private async void MediaControl_StopPressed(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => audioElement.Stop());
        }


        private async void MediaControl_NextTrackPressed(object sender, object e)
        {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var playlist = Playlist.Instance;
                    Track nextTrack = playlist.GetNextTrack(playingTrack);

                    if (nextTrack != null)
                    {
                        PlayTrack(nextTrack);
                    }
                });
        }

        private async void MediaControl_PreviousTrackPressed(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var playlist = Playlist.Instance;

                    // If we only have 1 playlist entry then just
                    // restart the currently playing track
                    if (playlist.PlaylistCount > 1)
                    {
                        Track previousTrack = playlist.GetPreviousTrack(playingTrack);
                        if (previousTrack != null)
                        {
                            PlayTrack(previousTrack);
                        }
                        else
                        {
                            // If returned track is null we must be
                            // at the 1st playlist track, so just restart playback
                            PlayTrack(playingTrack);
                        }
                    }
                    else
                    {
                        PlayTrack(playingTrack);
                    }
                });
        }


        /* 
         * MEDIAELEMENT EVENT HANDLERS
        */
        private void timelineSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_sliderpressed)
            {
                audioElement.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void audioElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Set the slider object's Max and StepFrequency properties
            double absvalue = (int)Math.Round(audioElement.NaturalDuration.TimeSpan.TotalSeconds,
                MidpointRounding.AwayFromZero);

            timelineSlider.Maximum = absvalue;
            timelineSlider.StepFrequency = SliderFrequency(audioElement.NaturalDuration.TimeSpan);
            SetupTimer();
        }

        private void audioElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            StopTimer();
            timelineSlider.Value = 0.0;

            // Play next track in playlist
            var playlist = Playlist.Instance;
            if (playlist.PlaylistCount > 1)
            {
                Track nextTrack = playlist.GetNextTrack(playingTrack);

                if (nextTrack != null)
                {
                    PlayTrack(nextTrack);
                }
                else
                {
                    // Must be at the end of the playlist
                    btnPlay.Style = Application.Current.Resources["PlayButtonStyle"] as Style;
                }
            }
            else
            {
                // Change button style to show play button
                btnPlay.Style = Application.Current.Resources["PlayButtonStyle"] as Style;
            }
        }

        // Media Failed event handler
        private async void audioElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // get HRESULT from event args 
            string hr = GetHresultFromErrorMessage(e);

            var dialog = new MessageDialog(hr);
            await dialog.ShowAsync();
        }

        private string GetHresultFromErrorMessage(ExceptionRoutedEventArgs e)
        {
            String hr = String.Empty;
            String token = "HRESULT - ";
            const int hrLength = 10;

            int tokenPos = e.ErrorMessage.IndexOf(token, StringComparison.Ordinal);
            if (tokenPos != -1)
            {
                hr = e.ErrorMessage.Substring(tokenPos + token.Length, hrLength);
            }

            return hr;
        }

        private void audioElement_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (audioElement.CurrentState == MediaElementState.Playing)
            {
                if (_sliderpressed)
                {
                    _timer.Stop();
                }
                else
                {
                    _timer.Start();
                }
            }

            if (audioElement.CurrentState == MediaElementState.Paused)
            {
                _timer.Stop();
            }

            if (audioElement.CurrentState == MediaElementState.Stopped)
            {
                _timer.Stop();
                timelineSlider.Value = 0;
            }
        }


        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (audioElement.CurrentState == MediaElementState.Paused)
            {
                audioElement.Play();
                // Change button style to show pause button
                btnPlay.Style = Application.Current.Resources["PauseButtonStyle"] as Style;
            }
            else
            {
                audioElement.Pause();
                // Change button style to show play button
                btnPlay.Style = Application.Current.Resources["PlayButtonStyle"] as Style;
            }
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            // Check status of play/pause button
            if (audioElement.CurrentState == MediaElementState.Paused ||
                audioElement.CurrentState == MediaElementState.Stopped)
            {
                btnPlay.Style = Application.Current.Resources["PauseButtonStyle"] as Style;
            }
            
            var playlist = Playlist.Instance;

            // If we only have 1 playlist entry then just
            // restart the currently playing track
            if (playlist.PlaylistCount > 1)
            {
                Track previousTrack = playlist.GetPreviousTrack(playingTrack);
                if (previousTrack != null)
                {
                    PlayTrack(previousTrack);
                }
                else
                {
                    // If returned track is null we must be
                    // at the 1st playlist track, so just restart playback
                    PlayTrack(playingTrack);
                }
            }
            else
            {
                PlayTrack(playingTrack);
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            // Check status of play/pause button
            if (audioElement.CurrentState == MediaElementState.Paused ||
                audioElement.CurrentState == MediaElementState.Stopped)
            {
                btnPlay.Style = Application.Current.Resources["PauseButtonStyle"] as Style;
            }
            
            var playlist = Playlist.Instance;
            Track nextTrack = playlist.GetNextTrack(playingTrack);

            if (nextTrack != null)
            {
                PlayTrack(nextTrack);
            }
        }


        /*
         * TIMELINE SLIDER CODE - Source: http://msdn.microsoft.com/en-gb/library/windows/apps/xaml/hh986967.aspx
        */
        private double SliderFrequency(TimeSpan timevalue)
        {
            double stepfrequency = -1;
            double absvalue = (int)Math.Round(timevalue.TotalSeconds, MidpointRounding.AwayFromZero);
            stepfrequency = (int)(Math.Round(absvalue / 100));

            if (timevalue.TotalMinutes >= 10 && timevalue.TotalMinutes < 30)
            {
                stepfrequency = 10;
            }
            else if (timevalue.TotalMinutes >= 30 && timevalue.TotalMinutes < 60)
            {
                stepfrequency = 30;
            }
            else if (timevalue.TotalHours >= 1)
            {
                stepfrequency = 60;
            }

            if (stepfrequency == 0)
            {
                stepfrequency += 1;
            }

            if (stepfrequency == 1)
            {
                stepfrequency = absvalue / 100;
            }

            return stepfrequency;
        }

        // A DispatchTimer object keeps the slider in sync with the media.
        private DispatcherTimer _timer;
        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(timelineSlider.StepFrequency);
            StartTimer();
        }

        private void _timer_Tick(object sender, object e)
        {
            if (!_sliderpressed)
            {
                timelineSlider.Value = audioElement.Position.TotalSeconds;
            }
        }

        private void StartTimer()
        {
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }

        private void StopTimer()
        {
            _timer.Stop();
            _timer.Tick -= _timer_Tick;
        }

        // Pointer position changes
        private bool _sliderpressed = false;
        void slider_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _sliderpressed = true;
        }

        void slider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            audioElement.Position = TimeSpan.FromSeconds(timelineSlider.Value);
            _sliderpressed = false;
        }


        // Marker reached event handler
        private void audioElement_MarkerReached(object sender, TimelineMarkerRoutedEventArgs e)
        {
            commentsTextBlock.Text = string.Format("{0} - {1}",
                                                    e.Marker.Time.Minutes.ToString() + ":" + e.Marker.Time.Seconds.ToString("D2"),
                                                    e.Marker.Text);
        }


        private void ConvertCommentsToTimelineMarkers(List<KeyValuePair<string, string>> comments)
        {
            foreach (var comment in comments)
            {
                bool parseResult;
                int commentTime;

                // Only add timed comments
                if (comment.Key != "null")
                {
                    // Convert timespan string to TimeSpan
                    parseResult = int.TryParse(comment.Key, out commentTime);

                    if (parseResult)
                    {
                        TimelineMarker timelineMarker = new TimelineMarker();
                        timelineMarker.Time = new TimeSpan(0, 0, 0, 0, commentTime);
                        timelineMarker.Text = comment.Value;
                        audioElement.Markers.Add(timelineMarker);

                        // Add a comment representation line to the slider canvas
                        double commentLocation = (double)playingTrack.Duration / (double)commentTime;
                        double normalisedCommentLocation = sliderCanvas.Width / commentLocation;

                        var path = XamlReader.Load("<Path Data=\"M51.9625,0 L51.9625,46\" Height=\"46\" Canvas.Left=\"" + normalisedCommentLocation + "\" Stretch=\"Fill\" Stroke=\"#FFEFEFEF\" Width=\"1\" Opacity=\"0.25\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");

                        sliderCanvas.Children.Add(path as UIElement);
                    }
                }
            }
        }


        // PlayTo Event Handler
        private async void SourceRequested(PlayToManager sender, PlayToSourceRequestedEventArgs e)
        {
            var deferral = e.SourceRequest.GetDeferral();
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                e.SourceRequest.SetSource(audioElement.PlayToSource);
                deferral.Complete();
            });
        }


        private async void PlayTrack(Track track)
        {
            playingTrack = track;

            // Set track duration text block
            TimeSpan durationTimeSpan = new TimeSpan(0, 0, 0, 0, (int)track.Duration);
            duration.Text = string.Format("{0:00}:{1:00}", durationTimeSpan.Minutes, durationTimeSpan.Seconds);

            // Clear comments and stop previous track playback
            audioElement.Markers.Clear();
            audioElement.Stop();
            commentsTextBlock.Text = string.Empty;

            // Remove comment marker lines (index 1 because 0 is the slider control)
            while (sliderCanvas.Children.Count > 1)
            {
                sliderCanvas.Children.RemoveAt(1);
            }

            // Set bg audio control information
            MediaControl.TrackName = track.Title;
            MediaControl.ArtistName = track.Username;

            // Get track comments
            if (track.CommentCount > 0)
            {
                await GetTrackComments(track.Id);
            }

            // Manipulate artwork url string to get the larger artwork to display
            string largeArtworkUrl = track.Image.Replace("-t200x200", "-t300x300");
            BitmapImage source = new BitmapImage(new Uri(largeArtworkUrl));
            audioElement.PosterSource = source;

            BitmapImage image = new BitmapImage(new Uri(track.AvatarUrl));
            avatarImage.Source = (ImageSource)image;

            BitmapImage waveform = new BitmapImage(new Uri(track.WaveformUrl));
            timelineSlider.BackgroundImage = waveform;

            usernameTextBlock.Text = track.Username;
            trackTitleTextBlock.Text = track.Title;
            playCountTextBlock.Text = track.PlaybackCount.ToString();
            commentCountTextBlock.Text = track.CommentCount.ToString();
            favouriteCountTextBlock.Text = track.FavouriteCount.ToString();

            // Update live tile with now playing information
            UpdateLiveTile(track.Title, track.Username, track.Image);

            if (track.StreamUrl != null)
            {
                Uri streamUri = new Uri(track.StreamUrl + "?" + clientId);
                audioElement.Source = streamUri;
            }
            else
            {
                // Track is not streamable, I've tried to filter the API call
                // wherever possible to only return streamable tracks but
                // certain API calls don't support this filter
                MessageDialog dialog = new MessageDialog("This track isn't streamable");
                await dialog.ShowAsync();
            }
        }


        // Method that gets the set of comments for a track
        private async Task GetTrackComments(string trackId)
        {
            string error = "";
            bool failed = false;
            
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync("/tracks/" + trackId + "/comments" + clientId);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                List<KeyValuePair<string, string>> trackComments = new List<KeyValuePair<string, string>>();

                var comments = JsonArray.Parse(responseBody);

                foreach (var comment in comments)
                {
                    string timestamp = "";
                    string body = "";
                    string username = "";
                    string commentText = "";

                    var obj = comment.GetObject();

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
                            switch (key)
                            {
                                case "timestamp":
                                    if (val.ValueType == JsonValueType.Null)
                                    {
                                        timestamp = "null";
                                    }
                                    else
                                    {
                                        timestamp = val.GetNumber().ToString();
                                    }

                                    break;

                                case "body":
                                    body = val.GetString();
                                    break;

                                case "user":
                                    if (val.ValueType != JsonValueType.Null)
                                    {
                                        var user = val.GetObject();
                                        username = user.GetNamedString("username");
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }

                    // Format comment body
                    commentText = string.Format("{0} - {1}", body, username);

                    // Add comment to list
                    trackComments.Add(new KeyValuePair<string, string>(timestamp, commentText));
                }

                // Convert comments to timeline markers
                ConvertCommentsToTimelineMarkers(trackComments);
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


        // Method that updates the live tile with now playing information
        private void UpdateLiveTile(string trackTitle, string trackUser, string trackArtwork)
        {
            // Get tile template - small tile
            XmlDocument tileXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileSquareText04);
            // Set tile info
            XmlNodeList tileText = tileXml.GetElementsByTagName("text");
            tileText[0].InnerText = "Now playing: " + trackTitle;

            // Wide tile
            XmlDocument wideTileXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileWideSmallImageAndText04);
            XmlNodeList wideTileText = wideTileXml.GetElementsByTagName("text");
            wideTileText[0].InnerText = "Now playing:";
            wideTileText[1].InnerText = trackTitle + " - " + trackUser;
            XmlNodeList wideTileImage = wideTileXml.GetElementsByTagName("image");
            ((XmlElement)wideTileImage[0]).SetAttribute("src", trackArtwork);
            ((XmlElement)wideTileImage[0]).SetAttribute("alt", "Track Artwork");

            // Add the wide tile to the square tiles payload by retrieving the binding element
            IXmlNode node = tileXml.ImportNode(wideTileXml.GetElementsByTagName("binding").Item(0), true);
            tileXml.GetElementsByTagName("visual").Item(0).AppendChild(node);

            // Apply the tile
            TileNotification tileNotification = new TileNotification(tileXml);

            // Send the notification to the app tile
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification);
        }


        // Share contract event handler
        private void ShareLinkHandler(DataTransferManager sender, DataRequestedEventArgs e)
        {
            DataRequest request = e.Request;
            request.Data.Properties.Title = "StreetSounds";
            request.Data.Properties.Description = "Check out this track on SoundCloud!";
            request.Data.SetUri(new Uri(playingTrack.PermalinkUrl));
        }

        // Playlist Item Click event handler
        private void PlaylistItem_Click(object sender, ItemClickEventArgs e)
        {
            // Just play the selected track
            PlayTrack((Track)e.ClickedItem);
        }

        // Playlist item selection changed event handler
        private void PlaylistItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (playlistListView.SelectedItem != null)
            {
                removeFromPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                // Keep app bar open whilst there are items selected
                BottomAppBar.IsOpen = playlistListView.SelectedItems.Count > 0;
            }
        }

        // Remove from playlist button click event handler
        private void RemoveFromPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            var playlist = Playlist.Instance;
            // Get selected track (only single selections allowed)
            Track selectedTrack = (Track)playlistListView.SelectedItem;
            playlist.RemoveTrackFromPlaylist(selectedTrack);
            BottomAppBar.IsOpen = false;
        }

        // Bottom app bar closed event handler
        private void BottomAppBar_Closed(object sender, object e)
        {
            // Deselect any selected items if app bar is closed
            playlistListView.SelectedItem = null;
            // Revert app bar back to normal
            removeFromPlaylistButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        // Bottom app bar opened event handler
        private void BottomAppBar_Opened(object sender, object e)
        {
            // Check if we can show the connection options
            var user = User.Instance;
            if (!string.IsNullOrEmpty(user.AccessToken))
            {
                loadFavouritesButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                addToFavouritesButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                followUserButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                commentButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
        }

        // Secondary tile pin button click event handler
        private async void pinTrackButton_Click(object sender, RoutedEventArgs e)
        {
            // Get playlist and format into a string to pass as the secondary tile id
            var playlist = Playlist.Instance;
            int playlistCount = playlist.PlaylistCount;
            string uniqueIdTimestamp = DateTime.Now.ToString("d_MMM_yyyy_HH_mm_ss");

            if (playlistCount > 0)
            {   
                Uri playlistLogo = new Uri("ms-appx:///Images/PlaylistIcon.png");
                var tile = new SecondaryTile(
                    uniqueIdTimestamp,                  // Tile ID
                    "Playlist",                         // Tile short name
                    "Street Sounds Playlist",           // Tile display name
                    playlist.ToString(),                // Activation argument (passed to OnLaunched method)
                    TileOptions.ShowNameOnLogo,         // Tile options
                    playlistLogo);                      // Tile logo uri

                await tile.RequestCreateAsync();
            }
        }

        // Load favourites button click event handler
        private async void loadFavouritesButton_Click(object sender, RoutedEventArgs e)
        {
            string error = "";
            bool failed = false;
            var user = User.Instance;

            BottomAppBar.IsOpen = false;
            // Get the connected user's favourite tracks
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync("https://api.soundcloud.com/me/favorites.json?oauth_token=" + user.AccessToken);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var tracks = JsonArray.Parse(responseBody);
                var playlist = Playlist.Instance;
                playlist.ParseJsonTracks(tracks);
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

        // Adds the track that is currently playing to the connected user's SC faves
        private async void AddToFavouritesButton_Click(object sender, RoutedEventArgs e)
        {
            string error = "";
            bool failed = false;
            var user = User.Instance;

            try
            {
                HttpResponseMessage response = await httpClient.PutAsync("https://api.soundcloud.com/me/favorites/" + playingTrack.Id + 
                    "?oauth_token=" + user.AccessToken, null);
                response.EnsureSuccessStatusCode();
                MessageDialog dialog = new MessageDialog(playingTrack.Title + " added to favorites successfully!");
                await dialog.ShowAsync();
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

        // Adds the owning user of the playing track to the connected user's following list
        private async void FollowUserButton_Click(object sender, RoutedEventArgs e)
        {
            string error = "";
            bool failed = false;
            var user = User.Instance;

            try
            {
                HttpResponseMessage response = await httpClient.PutAsync("https://api.soundcloud.com/me/followings/" + playingTrack.UserId +
                    "?oauth_token=" + user.AccessToken, null);
                response.EnsureSuccessStatusCode();
                MessageDialog dialog = new MessageDialog("You are now following " + playingTrack.Username);
                await dialog.ShowAsync();
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

        // Add a timed comment
        private void CommentButton_Click(object sender, RoutedEventArgs e)
        {
            // Get current song time
            TimeSpan commentTime = audioElement.Position;
            int totalMs = Convert.ToInt32(commentTime.TotalMilliseconds);

            // Create flyout control to enable user to enter comment
            Callisto.Controls.Flyout flyout = new Callisto.Controls.Flyout();
            flyout.Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

            StackPanel stackPanel = new StackPanel();
            stackPanel.Orientation = Orientation.Horizontal;

            TextBlock commentTimeTextBlock = new TextBlock();
            commentTimeTextBlock.Text = string.Format("{0:00}:{1:00}", commentTime.Minutes, commentTime.Seconds);
            commentTimeTextBlock.FontSize = 16.0;
            commentTimeTextBlock.HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Center;
            commentTimeTextBlock.VerticalAlignment = Windows.UI.Xaml.VerticalAlignment.Center;
            commentTimeTextBlock.Padding = new Thickness(2.0);
            commentTimeTextBlock.Width = 50;

            TextBox commentInputTextBox = new TextBox();
            commentInputTextBox.Text = "Enter comment...";
            commentInputTextBox.Width = 250;
            commentInputTextBox.TextWrapping = TextWrapping.Wrap;

            Button commentInputButton = new Button();
            commentInputButton.Content = "Submit";
            commentInputButton.Width = 100;

            stackPanel.Children.Add(commentTimeTextBlock);
            stackPanel.Children.Add(commentInputTextBox);
            stackPanel.Children.Add(commentInputButton);
            flyout.Content = stackPanel;

            flyout.PlacementTarget = sender as UIElement;
            flyout.Placement = Windows.UI.Xaml.Controls.Primitives.PlacementMode.Top;
            flyout.IsOpen = true;

            // Button click event delegate
            commentInputButton.Click += new RoutedEventHandler(async delegate(Object o, RoutedEventArgs args)
                {
                    using (HttpClient httpClient = new HttpClient())
                    {
                        bool failed = false;
                        string error = "";
                        
                        httpClient.MaxResponseContentBufferSize = 256000;
                        httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
                        httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                        var content = new FormUrlEncodedContent(new []
                            {
                                new KeyValuePair<string, string>("comment[body]", commentInputTextBox.Text),
                                new KeyValuePair<string, string>("comment[timestamp]", totalMs.ToString())
                            });

                        try
                        {
                            var user = User.Instance;

                            string uri = string.Format("https://api.soundcloud.com/tracks/{0}/comments.json?oauth_token={1}",
                                playingTrack.Id, user.AccessToken);
                            HttpResponseMessage response = await httpClient.PostAsync(uri, content);
                            response.EnsureSuccessStatusCode();

                            MessageDialog dialog = new MessageDialog("Comment submitted!");
                            await dialog.ShowAsync();
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
                });
        }
    }
}
