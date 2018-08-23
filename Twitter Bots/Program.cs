using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using Tweetinvi;

namespace Twitter_Bots
{
    class Program
    {
        static void Main(string[] args)
        {
            // Basic app setup
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();
            ExceptionHandler.SwallowWebExceptions = false;
            
            // Get the credentials from the config file
            // The value contains all keys and secrets separated by comma
            var storedCredentials = configuration.GetSection("NikolMargCredentials").Value.Split(',');    // <--- Importante!
            var credentials = Auth.CreateCredentials(storedCredentials[0], storedCredentials[1], storedCredentials[2], storedCredentials[3]);

            // Get an AuthenticatedUser from a specific set of credentials
            var authenticatedUser = User.GetAuthenticatedUser(credentials);
            Auth.SetUserCredentials(
                authenticatedUser.Credentials.ConsumerKey, authenticatedUser.Credentials.ConsumerSecret, 
                authenticatedUser.Credentials.AccessToken, authenticatedUser.Credentials.AccessTokenSecret);

            // Initialize the client
            TwitterHelper helper = new TwitterHelper();

            // Examples
            //helper.UnfollowNonMutuals(authenticatedUser);
            //helper.FollowOtherUsersFollowers(authenticatedUser, "Etsy", 1800);
            //helper.FollowRetweeters(authenticatedUser, authenticatedUser.ScreenName, 800);
            //helper.RemoveTweetsWithZeroNotes(authenticatedUser);
            //helper.RemoveTweetsWithKeyword(authenticatedUser, "skatoules");
            //helper.DumpAllMedia(authenticatedUser, "BelovedTwitterUser");

            // Run the queue (retweet a single tweet, remove it and keep a backup on another queue)
            //long mainQueueId = long.Parse(configuration.GetSection("MainQueueId").Value);                  // The queue that is running
            //long backupQueueId = long.Parse(configuration.GetSection("BackupQueueId").Value);                // All retweeted items go here
            //long ultimateBackupQueueId = long.Parse(configuration.GetSection("UltimateBackupQueueId").Value);        // Manually selected faves for backup
            //helper.HandleQueue(mainQueueId, backupQueueId);

            //helper.CollectionToFile(authenticatedUser, mainQueueId, "MainQueue");

            // Move all tweets from one collection to another
            //helper.MoveToOtherCollection(null, ultimateBackupQueueId, mainQueueId, false);

            Console.WriteLine("End!");
            Console.ReadLine();
        }
    }
}
