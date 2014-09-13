using StreetSounds.GeocodeReference;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Popups;

namespace StreetSounds
{
    // Class to define a music track retrieved from SoundCloud
    public class Track
    {
        // Constructors
        public Track()
        {
        }

        // Properties
        public string Title { get; set; }
        public string Image { get; set; }
        public string StreamUrl { get; set; }
        public string Id { get; set; }
        public uint PlaybackCount { get; set; }
        public uint CommentCount { get; set; }
        public uint FavouriteCount { get; set; }
        public string WaveformUrl { get; set; }
        public string AvatarUrl { get; set; }
        public string Username { get; set; }
        public string UserId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string PermalinkUrl { get; set; }
        public string Genre { get; set; }
        public string City { get; set; }
        public uint Duration { get; set; }
    }


    // Class to define the track grouping
    public class TrackGroup
    {
        // Constructors
        public TrackGroup()
        {
            Title = "";
            Tracks = null;
            Latitude = 0;
            Longitude = 0;
            Image = null;
        }

        public TrackGroup(string title)
        {
            Title = title;
            Tracks = new ObservableCollection<Track>();
            Latitude = 0;
            Longitude = 0;
            Image = null;
        }

        public string Title { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public Uri Image { get; set; }
        public ObservableCollection<Track> Tracks { get; set; }

        // Property that returns a subset of tracks for optimisation because
        // grouped items are not virtualised: http://msdn.microsoft.com/en-gb/library/windows/apps/hh994637.aspx
        public IEnumerable<Track> TopItems
        {
            get { return this.Tracks.Take(12); }
        }

        // Property that returns the amount of tracks in a group as a string
        public string TracksCount
        {
            get
            {
                if (this.Tracks.Count == 1)
                {
                    return "1 track";
                }
                else
                {
                    return string.Format("{0} tracks", this.Tracks.Count);
                }
            }
        }
    }

    
    // Class to define the data model
    public class TracksModel : ObservableCollection<TrackGroup>
    {
        static TracksModel instance = null;
        HttpClientHandler handler = null;
        HttpClient httpClient = null;
        const string scClientId = "?client_id=14850cf5fe29529f021db5810755a9c9";
        const string mapsKey = "Amtb1dMrbwAnN2nax0fK5uGlcTfAJHf_K7B3hT1yrBvsHu3OEti_uH9P1XkBIVt-";

        private TracksModel()
        {
            // Setup HttpClient
            handler = new HttpClientHandler();
            // Need to set redirect to true to get audio stream - http://developers.soundcloud.com/docs#playing
            handler.AllowAutoRedirect = true;
            httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("http://api.soundcloud.com");
            httpClient.MaxResponseContentBufferSize = 256000;
            httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        public ObservableCollection<TrackGroup> TrackGroups
        {
            get
            {
                return this;
            }
        }

        // Property that keeps track of which mode the user is interacting with the app in
        public string ModelMode { get; set; }

        // Property that keeps track of the current offset for pagination
        public int Offset { get; set; }

        public async static Task<TracksModel> GetModel()
        {
            if (instance == null)
            {
                instance = new TracksModel();
                // Set mode for model initialisation
                instance.ModelMode = "default";
                string apiCall = instance.ApiCallBuilder();
                await instance.InitialiseModel(apiCall);
            }
            return instance;
        }

        private async Task InitialiseModel(string apiCall)
        {
            bool failed = false;
            string error = "";

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var tracks = JsonArray.Parse(responseBody);
                await ParseJsonTracks(tracks, 0, 0);

                // Call geolocation service to create a location for each track group
                foreach (TrackGroup group in this.TrackGroups)
                {
                    string location = await GeocodeLocation(group.Title);

                    if (!string.IsNullOrEmpty(location))
                    {
                        // We have a geocoded result
                        string[] locations = location.Split(',');
                        group.Latitude = double.Parse(locations[0]);
                        group.Longitude = double.Parse(locations[1]);
                    }
                }
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

        // Method that constructs the API call to SoundCloud depending on user preferences
        private string ApiCallBuilder()
        {
            string apiCall = "";
            string genrePreference = "Any";
            // order preference = hotness has now been disabled
            string orderPreference = "hotness";

            // Get user preferences
            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("order"))
            {
                if ((bool)ApplicationData.Current.RoamingSettings.Values["order"])
                {
                    // Toggle switch is on = order by date
                    orderPreference = "created_at";
                }
            }

            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("genre"))
            {
                genrePreference = ((string)ApplicationData.Current.RoamingSettings.Values["genre"]).ToLower();
            }

            // Check if there is a specific genre preference
            if (genrePreference.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                // No genre preference, omit from API call
                apiCall = string.Format("/tracks{0}&filter=streamable", scClientId);
            }
            else
            {
                apiCall = string.Format("/tracks{0}&filter=streamable&genres={1}", scClientId, genrePreference);
            }

            // Check offset for pagination
            if (this.Offset > 0)
            {
                // Multiply offset by number of tracks an API call returns (50 by default)
                int offset = this.Offset * 50;
                // Max limit for offset is 8000 - http://developers.soundcloud.com/docs#pagination
                if (offset > 8000)
                {
                    // Return to beginning
                    this.Offset = 0;
                }
                else
                {
                    apiCall += string.Format("&offset={0}", offset);
                }
            }

            return apiCall;
        }

        // Method that updates the data model if the user changes to a different mode
        public async Task UpdateModel(string city, double lat, double lon)
        {
            string apiCall = "";
            string error = "";
            bool failed = false;

            // Clear current model
            instance.ClearTracksModel();

            if (instance.ModelMode.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                apiCall = instance.ApiCallBuilder();
                await instance.InitialiseModel(apiCall);
            }
            else if (instance.ModelMode.Equals("location", StringComparison.OrdinalIgnoreCase) || 
                       instance.ModelMode.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                apiCall = string.Format("/tracks{0}&tags=geo:lat={1}*,geo:lon={2}*", scClientId, lat, lon);

                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var tracks = JsonArray.Parse(responseBody);
                    await ParseJsonTracks(tracks, lat, lon);

                    // Call geolocation service to create a location for each track group
                    foreach (TrackGroup group in this.TrackGroups)
                    {
                        string location = await GeocodeLocation(group.Title);

                        if (!string.IsNullOrEmpty(location))
                        {
                            // We have a geocoded result
                            string[] locations = location.Split(',');
                            group.Latitude = double.Parse(locations[0]);
                            group.Longitude = double.Parse(locations[1]);
                        }
                    }
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
            else if (instance.ModelMode.Equals("city", StringComparison.OrdinalIgnoreCase))
            {
                apiCall = string.Format("/users{0}&q={1}", scClientId, city);
                
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var users = JsonArray.Parse(responseBody);
                    await ParseJsonUsers(users, city);

                    // Call geolocation service to create a location for each track group
                    foreach (TrackGroup group in this.TrackGroups)
                    {
                        string location = await GeocodeLocation(group.Title);

                        if (!string.IsNullOrEmpty(location))
                        {
                            // We have a geocoded result
                            string[] locations = location.Split(',');
                            group.Latitude = double.Parse(locations[0]);
                            group.Longitude = double.Parse(locations[1]);
                        }
                    }
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
        }

        // Method that parses a JsonArray of tracks
        private async Task ParseJsonTracks(JsonArray array, double latOrigin, double lonOrigin)
        {
            foreach (var item in array)
            {
                var obj = item.GetObject();
                Track track = new Track();
                TrackGroup trackGroup = null;
                bool newGroup = true;
                bool isValidTrack = true;

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

                                    // Get city information
                                    track.City = await ResolveUserEndpoint(user.GetNamedString("uri"));
                                    track.City = track.City.Trim();

                                    // Don't add tracks without city information or that aren't valid strings
                                    if (string.IsNullOrEmpty(track.City) || Regex.IsMatch(track.City, "[0-9-!#@$%^&*()_+|~=`{}/\\:;'<>?.]"))
                                    {
                                        isValidTrack = false;
                                    }
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

                            case "tag_list":
                                // Only parse tags if we are in a geo-tagging mode
                                if (instance.ModelMode.Equals("location", StringComparison.OrdinalIgnoreCase) || 
                                        instance.ModelMode.Equals("map", StringComparison.OrdinalIgnoreCase))
                                {
                                    string tagList = val.GetString();
                                    // tags are separated by a space
                                    string[] tags = tagList.Split(' ');
                                    double parsedVal;

                                    for (int i = 0; i < tags.Length; i++)
                                    {
                                        // Find latitude and longitude values from tags
                                        if (tags[i].StartsWith("geo:lat"))
                                        {
                                            // Split string at equals sign to get value
                                            string[] latitude = tags[i].Split('=');

                                            if (Double.TryParse(latitude[1], out parsedVal))
                                            {
                                                track.Latitude = parsedVal;
                                            }
                                            else
                                            {
                                                // Set latitude to a sensible default
                                                track.Latitude = 51.5432;
                                            }

                                        }
                                        else if (tags[i].StartsWith("geo:lon"))
                                        {
                                            string[] longitude = tags[i].Split('=');

                                            if (Double.TryParse(longitude[1], out parsedVal))
                                            {
                                                track.Longitude = parsedVal;
                                            }
                                            else
                                            {
                                                // Set latitude to a sensible default
                                                track.Longitude = -0.01519;
                                            }
                                        }
                                    }

                                    // Soundcloud API returns a logical OR query on latitude and longitude
                                    // http://developers.soundcloud.com/docs#uploading
                                    // so I need to filter the result to make sure it matches both (logical AND)
                                    // If it doesn't then we dismiss this track
                                    if (track.Latitude >= latOrigin - 0.25 && track.Latitude < latOrigin + 0.25 &&
                                         track.Longitude >= lonOrigin - 0.5 && track.Longitude < lonOrigin + 0.5)
                                    {
                                        // Track location matches both lat and lon
                                    }
                                    else
                                    {
                                        // Track location only matches lat or lon
                                        isValidTrack = false;
                                    }
                                }
                                break;

                            default:
                                break;
                        }
                    }
                }

                // Order by city - if track is valid
                if (isValidTrack)
                {
                    // Linear search for existing groups
                    foreach (TrackGroup group in this.TrackGroups)
                    {
                        if (group.Title.Equals(track.City, StringComparison.OrdinalIgnoreCase))
                        {
                            trackGroup = group;
                            newGroup = false;
                        }
                    }

                    // Add track to group
                    if (!newGroup)
                    {
                        trackGroup.Tracks.Add(track);
                    }
                    else
                    {
                        trackGroup = new TrackGroup(track.City);
                        trackGroup.Tracks.Add(track);
                        this.Add(trackGroup);
                    }
                }
            }
        }

        // Method that gets the user info from the SoundCloud API
        private async Task<string> ResolveUserEndpoint(string url)
        {
            string city = null;
            string error = "";
            bool failed = false;

            try
            {
                string apiCall = string.Format("/resolve{0}&url={1}", scClientId, url);
                HttpResponseMessage response = await httpClient.GetAsync(apiCall);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var user = JsonObject.Parse(responseBody);
                city = ParseJsonUser(user);
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

            return city;
        }

        // Method that parses a single Json user
        private string ParseJsonUser(JsonObject obj)
        {
            string city = "";

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
                        case "city":
                            if (jsonType != JsonValueType.Null)
                            {
                                city = val.GetString();
                            }
                            break;

                        default:
                            break;
                    }
                }
            }

            return city;
        }


        // Method that parses a Json array of users
        private async Task ParseJsonUsers(JsonArray users, string city)
        {
            foreach (var item in users)
            {
                IJsonValue val;
                var obj = item.GetObject();
                bool isValidUser = false;
                int userId = 0;

                // for each key in an item
                foreach (var key in obj.Keys)
                {
                    if (!obj.TryGetValue(key, out val))
                    {
                        continue;
                        // Output error
                    }
                    else
                    {
                        switch (key)
                        {
                            case "city":
                                if (val.ValueType != JsonValueType.Null)
                                {
                                    if (city.Equals(val.GetString(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        isValidUser = true;
                                    }
                                }
                                break;

                            case "id":
                                if (val.ValueType != JsonValueType.Null)
                                {
                                    userId = (int)val.GetNumber();
                                }
                                break;

                            default:
                                break;
                        }
                    }
                }

                if (isValidUser)
                {
                    // User is from the city the user entered, get their tracks
                    string getUserTracks = string.Format("/users/{0}/tracks{1}&limit=3&filter=streamable", userId, scClientId);
                    HttpResponseMessage userTracksResponse = await httpClient.GetAsync(getUserTracks);
                    userTracksResponse.EnsureSuccessStatusCode();
                    string userTracksResponseBody = await userTracksResponse.Content.ReadAsStringAsync();
                    var tracks = JsonArray.Parse(userTracksResponseBody);
                    await ParseJsonTracks(tracks, 0, 0);
                }
            }
        }


        // Method that geocodes a city string
        private async Task<string> GeocodeLocation(string city)
        {
            string location = "";

            GeocodeRequest geocodeRequest = new GeocodeRequest();

            // Set credentials
            geocodeRequest.Credentials = new GeocodeReference.Credentials();
            geocodeRequest.Credentials.ApplicationId = mapsKey;

            geocodeRequest.Query = city;
            GeocodeServiceClient geocodeService =
                new GeocodeReference.GeocodeServiceClient(GeocodeReference.GeocodeServiceClient.EndpointConfiguration.BasicHttpBinding_IGeocodeService);
            GeocodeResponse geocodeResponse = await geocodeService.GeocodeAsync(geocodeRequest);

            if (geocodeResponse.Results.Count > 0)
            {
                location = string.Format("{0},{1}",
                    geocodeResponse.Results[0].Locations[0].Latitude,
                    geocodeResponse.Results[0].Locations[0].Longitude);
            }

            return location;
        }

        // Method that clears the model
        private void ClearTracksModel()
        {
            foreach (TrackGroup group in this.TrackGroups)
            {
                group.Tracks.Clear();
            }

            this.TrackGroups.Clear();
        }
    }

    // Class that defines the search data model
    // Created because this model is grouped by genre, not city.
    // It prevents having to rebuild the data model depending upon
    // whether we are on a search page or a map page
    public class SearchModel : ObservableCollection<TrackGroup>
    {
        static SearchModel instance = null;

        private SearchModel()
        {
        }

        public static SearchModel Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new SearchModel();
                }

                return instance;
            }
        }

        public ObservableCollection<TrackGroup> SearchGroups
        {
            get
            {
                return this;
            }
        }

        // Property that keeps track of the offset for pagination of search results
        public int SearchModelOffset { get; set; }

        // Property that stores the search string
        public string SearchString { get; set; }

        // Method that clears the model
        public void ClearSearchModel()
        {
            foreach (TrackGroup group in this.SearchGroups)
            {
                group.Tracks.Clear();
            }

            this.SearchGroups.Clear();
        }
    }
}
