using System;
using System.Collections.Generic;
using System.Net.Http;
using Windows.Data.Json;
using Windows.Security.Authentication.Web;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace StreetSounds
{
    public sealed partial class PreferencesUserControl : UserControl
    {
        const string CLIENT_ID = "?client_id=14850cf5fe29529f021db5810755a9c9";
        const string REDIRECT_URI = "streetsounds://soundcloud/callback";
        const string RESPONSE_TYPE = "&response_type=code";
        const string SCOPE = "&scope=non-expiring";
        const string DISPLAY = "&display=popup";
        
        public PreferencesUserControl()
        {
            this.InitializeComponent();

            // Initialise preferences as the user saved them
            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("order"))
            {
                order.IsOn = (bool)ApplicationData.Current.RoamingSettings.Values["order"];
            }

            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("genre"))
            {
                genresList.SelectedItem = (string)ApplicationData.Current.RoamingSettings.Values["genre"];
            }

            // Show connect or disconnect button check
            var user = User.Instance;
            if (!string.IsNullOrEmpty(user.AccessToken))
            {
                // User is connected, show disconnect button
                Image image = new Image();
                Uri uri = new Uri("ms-appx:///Images/btn-disconnect.png");
                image.Source = new BitmapImage(uri);
                btnSoundCloudConnect.Content = image;
            }
        }

        // Order toggle switch event handler
        private void order_OnToggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.RoamingSettings.Values["order"] = order.IsOn;
        }

        // Genre selection changed event handler
        private void genresList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationData.Current.RoamingSettings.Values["genre"] = genresList.SelectedItem as string;
        }

        // Connect to SoundCloud button
        private async void btnSoundCloudConnect_Click(object sender, RoutedEventArgs e)
        {
            var user = User.Instance;

            if (!string.IsNullOrEmpty(user.AccessToken))
            {
                user.Disconnect();
            }
            else
            {
                // We are connecting an account
                string url = "https://soundcloud.com/connect" + CLIENT_ID + "&redirect_uri=" + REDIRECT_URI + RESPONSE_TYPE + SCOPE + DISPLAY;
                Uri startUri = new Uri(url);
                Uri endUri = new Uri(REDIRECT_URI);

                WebAuthenticationResult war = await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, startUri, endUri);

                switch (war.ResponseStatus)
                {
                    case WebAuthenticationStatus.Success:
                        {
                            // Get access token
                            string response = war.ResponseData;
                            string[] responseSplit = response.Split('=');
                            string authCode = responseSplit[1];
                            GetAccessToken(authCode);
                            break;
                        }
                    case WebAuthenticationStatus.UserCancel:
                        {
                            OutputMessage(war.ResponseStatus.ToString());
                            break;
                        }
                    case WebAuthenticationStatus.ErrorHttp:
                        {
                            OutputMessage(war.ResponseStatus.ToString());
                            break;
                        }
                }
            }
        }

        // Get Access Token method
        private async void GetAccessToken(string authorisationCode)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.BaseAddress = new Uri("https://api.soundcloud.com/oauth2/token");
                httpClient.MaxResponseContentBufferSize = 256000;
                httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("client_id", "14850cf5fe29529f021db5810755a9c9"),
                        new KeyValuePair<string, string>("client_secret", "79c7b649c35773fc9c18ea463e02657d"),
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("redirect_uri", "streetsounds://soundcloud/callback"),
                        new KeyValuePair<string, string>("code", authorisationCode)
                    });

                try
                {
                    HttpResponseMessage response = await httpClient.PostAsync("https://api.soundcloud.com/oauth2/token", content);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();

                    // Parse response JSON object to get access token
                    var responseObj = JsonObject.Parse(responseBody);
                    string accessToken = responseObj["access_token"].GetString();

                    // Store in credential locker
                    var user = User.Instance;
                    user.AccessToken = accessToken;

                    // Do something with access token!
                    HttpResponseMessage meResponse = await httpClient.GetAsync("https://api.soundcloud.com/me.json?oauth_token=" + user.AccessToken);
                    meResponse.EnsureSuccessStatusCode();
                    string meResponseBody = await meResponse.Content.ReadAsStringAsync();
                    var me = JsonObject.Parse(meResponseBody);
                    OutputMessage(me.GetNamedString("full_name") + " is now connected!");
                }
                catch (HttpRequestException hre)
                {
                    OutputMessage(hre.Message);
                }
                catch (Exception ex)
                {
                    OutputMessage(ex.Message);
                }
            }
        }

        // Message output method
        private async void OutputMessage(string msg)
        {
            MessageDialog dialog = new MessageDialog(msg);
            await dialog.ShowAsync();
        }
    }
}
