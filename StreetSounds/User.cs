using System;
using Windows.Security.Credentials;

namespace StreetSounds
{
    // Singleton class to manage user info/credentials
    public class User
    {
        static User instance = null;
        const string RESOURCE_NAME = "SoundCloudAccessToken";
        const string USER = "user";
        PasswordVault vault;

        private User()
        {
            vault = new PasswordVault();
        }

        public static User Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new User();
                }

                return instance;
            }
        }

        // Property that defines the access token to store in the credential locker
        public string AccessToken
        {
            get
            {
                try
                {
                    var creds = vault.FindAllByResource(RESOURCE_NAME);
                    if (creds != null)
                    {
                        return vault.Retrieve(RESOURCE_NAME, USER).Password;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
            set
            {
                vault.Add(new PasswordCredential(RESOURCE_NAME, USER, value));
            }
        }

        // Method that disconnects the user
        public void Disconnect()
        {
            // We are already connected, so disconnect the account
            try
            {
                var creds = vault.FindAllByResource(RESOURCE_NAME)[0];
                if (creds != null)
                {
                    vault.Remove(creds);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
