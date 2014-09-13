using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Popups;

namespace StreetSounds
{
    // Singleton class to manage the app playlist
    public class Playlist
    {
        static Playlist instance = null;
        const string scClientId = "?client_id=14850cf5fe29529f021db5810755a9c9";

        private Playlist()
        {
            PlaylistTracks = new ObservableCollection<Track>();
        }

        public static Playlist Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Playlist();
                }

                return instance;
            }
        }

        public int PlaylistCount
        {
            get
            {
                return this.PlaylistTracks.Count;
            }
        } 

        public ObservableCollection<Track> PlaylistTracks { get; set; }

        // Method that returns a track at a specified index
        public Track GetTrack(int index)
        {
            return this.PlaylistTracks[index];
        }

        // Method that gets the next playlist track
        public Track GetNextTrack(Track track)
        {
            Track nextTrack = null;
            int nextTrackIndex = this.PlaylistTracks.IndexOf(track) + 1;

            if (nextTrackIndex < this.PlaylistCount)
            {
                nextTrack = this.PlaylistTracks[nextTrackIndex];
            }

            return nextTrack;
        }

        // Method that gets the previous playlist track
        public Track GetPreviousTrack(Track track)
        {
            Track previousTrack = null;
            int previousTrackIndex = this.PlaylistTracks.IndexOf(track) - 1;

            if (previousTrackIndex >= 0)
            {
                previousTrack = this.PlaylistTracks[previousTrackIndex];
            }

            return previousTrack;
        }

        // Method that removes a specified track from the playlist
        public void RemoveTrackFromPlaylist(Track track)
        {
            this.PlaylistTracks.Remove(track);
        }

        // Method that clears the playlist
        public void ClearPlaylist()
        {
            this.PlaylistTracks.Clear();
        }

        // Override ToString() method to create comma separated playlist for the secondary tile
        public override string ToString()
        {
            // The secondary tile arguments string is truncated at 2048 characters so we
            // don't have to worry about checking the size of the string in terms of crashing the app:
            // http://msdn.microsoft.com/en-gb/library/windows/apps/hh701602.aspx
            // Although a future improvement would be to warn the user in such a scenario

            string[] playlistArray = new string[this.PlaylistCount];

            for (int i = 0; i < this.PlaylistCount; i++)
            {
                playlistArray[i] = this.PlaylistTracks[i].Id;
            }

            string tracksPlaylist = string.Join(",", playlistArray);

            return tracksPlaylist;
        }

        // Method that builds a playlist that was saved as a secondary tile
        public async Task BuildPlaylistFromSecondaryTile(string savedTrackIds)
        {
            this.ClearPlaylist();

            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            HttpClient httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("http://api.soundcloud.com");
            httpClient.MaxResponseContentBufferSize = 256000;
            httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            bool failed = false;
            string error = "";

            try
            {
                string apiCall = string.Format("/tracks{0}&ids={1}", scClientId, savedTrackIds);
                HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var tracks = JsonArray.Parse(responseBody);
                ParseJsonTracks(tracks);
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

        // Method that parses a JsonArray of tracks
        public void ParseJsonTracks(JsonArray array)
        {
            foreach (var item in array)
            {
                var obj = item.GetObject();
                Track track = new Track();

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
                                    // No artwork provided, set to default provided image
                                    track.Image = "ms-appx:///Images/no-artwork-t200x200.png";
                                }
                                else
                                {
                                    // Provided image is 100x100 by default (-large) get
                                    // the 200x200 version to match our grid view size
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
                                break;

                            default:
                                break;
                        }
                    }
                }

                // Add to playlist
                this.PlaylistTracks.Add(track);

            }
        }
    }
}
