using MoreLinq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Models.DTO;
using Tweetinvi.Parameters;

namespace Twitter_Bots
{
    class TwitterHelper
    {
        /// <summary>
        /// Takes the first tweet from one collection, retweets it and then 
        /// moves it to a second collection as a backup.
        /// </summary>
        public void HandleQueue(long collectionId, long backupCollectionId)
        {
            // First make sure the collection isn't empty
            // If it is, refill it from the temp queue
            if (IsCollectionEmpty(collectionId))
            {
                MoveToOtherCollection(null, backupCollectionId, collectionId, false);
            }

            // Get the collection entries
            JArray collectionEntries = GetCollectionEntries(collectionId);

            // Get the first entry
            long firstEntryId = long.Parse(collectionEntries[0]["tweet"]["id"].ToString());

            // Retweet the first entry
            // If the entry is already retweeted, unretweet it and retweet it again
            TryRetweet(firstEntryId, collectionId, backupCollectionId, collectionEntries);

            // Remove the entry from the top of the queue and add it to the other one
            MoveToOtherCollection(firstEntryId, collectionId, backupCollectionId, true);
        }

        /// <summary>
        /// Retweets a single tweet. If it's already retweeted, it first unretweets it.
        /// If a retweet isn't possible, it moves the tweet to the temp queue for another time.
        /// </summary>
        public void TryRetweet(long tweetId, long originalCollectionId, long backupCollectionId, JArray collectionArray)
        {
            ITweet tweet = Tweet.GetTweet(tweetId);

            if (tweet.Retweeted)
            {
                tweet.UnRetweet();
                Console.WriteLine("Unretweeted tweet: " + tweetId);
            }

            try
            {
                Tweet.PublishRetweet(tweetId);
                Console.WriteLine("Retweeted tweet: " + tweetId);
            }
            catch (Exception)
            {
                // The tweet couldn't be retweeted, move it to the temp queue & try next tweet
                Console.WriteLine("Could not retweet, trying next tweet...");
                MoveToOtherCollection(tweetId, originalCollectionId, backupCollectionId, true);
                HandleQueue(originalCollectionId, backupCollectionId);
            }
        }

        /// <summary>
        /// Gets the first 3200 tweets from the given user's timeline.
        /// </summary>
        public List<ITweet> GetTimelineEntries(string userScreenName)
        {
            RateLimit.RateLimitTrackerMode = RateLimitTrackerMode.TrackAndAwait;

            RateLimit.QueryAwaitingForRateLimit += (sender, args) =>
            {
                Console.WriteLine($"Query : {args.Query} is awaiting for rate limits!");
            };

            var lastTweets = Timeline.GetUserTimeline(userScreenName, 200).ToArray();

            var allTweets = new List<ITweet>(lastTweets);
            var beforeLast = allTweets;

            while (lastTweets.Length > 0 && allTweets.Count <= 3200)
            {
                var idOfOldestTweet = lastTweets.Select(x => x.Id).Min();
                Console.WriteLine($"Oldest Tweet Id = {idOfOldestTweet}");

                var numberOfTweetsToRetrieve = allTweets.Count > 3000 ? 3200 - allTweets.Count : 200;
                var timelineRequestParameters = new UserTimelineParameters
                {
                    // MaxId ensures that we only get tweets that have been posted 
                    // BEFORE the oldest tweet we received
                    MaxId = idOfOldestTweet - 1,
                    MaximumNumberOfTweetsToRetrieve = numberOfTweetsToRetrieve
                };

                lastTweets = Timeline.GetUserTimeline(userScreenName, timelineRequestParameters).ToArray();
                allTweets.AddRange(lastTweets);
            }

            return allTweets;
        }

        /// <summary>
        /// Gets all tweets from a collection.
        /// </summary>
        public JArray GetCollectionEntries(long collectionId)
        {
            // Get the collection entries
            string getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?count=200&id=custom-" + collectionId;
            string collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            JObject collectionsResponse = JObject.Parse(collectionsJson);
            collectionsResponse = (JObject)collectionsResponse["response"];
            JArray collectionsArray = (JArray)collectionsResponse["timeline"];

            // See if the list was truncated
            var wasTruncated = collectionsResponse["position"]["was_truncated"].ToObject<Boolean>();

            // Get the max_position to set it as the beginning of the next query
            var lastPosition = long.Parse(collectionsResponse["position"]["min_position"].ToString());

            while (wasTruncated)
            {
                // Get the next entries
                string getExtraEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?count=200&id=custom-" + collectionId + "&max_position=" + lastPosition;
                string extraEntriesJson = TwitterAccessor.ExecuteGETQueryReturningJson(getExtraEntriesQuery);
                JObject extraEntriesResponse = JObject.Parse(extraEntriesJson);
                extraEntriesResponse = (JObject)extraEntriesResponse["response"];
                JArray extraEntriesArray = (JArray)extraEntriesResponse["timeline"];

                // TODO: Remove the foreach, just combine the arrays
                foreach (var entry in extraEntriesArray)
                {
                    collectionsArray.Add(entry);
                }

                // Check if there are still more tweets
                wasTruncated = extraEntriesResponse["position"]["was_truncated"].ToObject<Boolean>();
                lastPosition = long.Parse(extraEntriesResponse["position"]["min_position"].ToString());
            }

            return collectionsArray;
        }

        /// <summary>
        /// Moves a single tweet from one collection to another.
        /// </summary>
        public void MoveToOtherCollection(long? tweetId, long originalCollectionId, long newCollectionId, bool removeFromOriginalCollection = false)
        {
            if (tweetId.HasValue)
            {
                // If it's just a single tweet
                if (removeFromOriginalCollection)
                {
                    // Remove the tweet from the original queue
                    string removeFromCollectionQuery = "https://api.twitter.com/1.1/collections/entries/remove.json?id=custom-" + originalCollectionId + "&tweet_id=" + tweetId;
                    string removeFromCollectionJson = TwitterAccessor.ExecutePOSTQueryReturningJson(removeFromCollectionQuery);
                    Console.WriteLine("Remove tweet " + tweetId + " from collection response:" + removeFromCollectionJson);
                }

                // Add the tweet to the new queue
                string addToCollectionQuery = "https://api.twitter.com/1.1/collections/entries/add.json?id=custom-" + newCollectionId + "&tweet_id=" + tweetId;
                string addToCollectionJson = TwitterAccessor.ExecutePOSTQueryReturningJson(addToCollectionQuery);
                Console.WriteLine("Add tweet " + tweetId + " to collection response:" + addToCollectionJson);
            }
            else
            {
                // Otherwise just move all tweets from one collection to the other

                // Get the collection entries
                JArray originalCollectionEntries = GetCollectionEntries(originalCollectionId);

                foreach (var tweet in originalCollectionEntries)
                {
                    var currentTweetId = long.Parse(tweet["tweet"]["id"].ToString());
                    MoveToOtherCollection(currentTweetId, originalCollectionId, newCollectionId, false);
                }
            }
        }

        // TODO: Rewrite the whole thing
        // Note: Only iterates once
        public void ShuffleTweet(long collectionId)
        {
            string getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?id=custom-" + collectionId + "&count=200";
            string collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            JObject response = JObject.Parse(collectionsJson);
            response = (JObject)response["response"];
            JArray collectionArray = (JArray)response["timeline"];

            getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?id=custom-" + collectionId + "&count=200&min_position=200";
            collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            response = JObject.Parse(collectionsJson);
            response = (JObject)response["response"];

            foreach (var t in (JArray)response["timeline"])
            {
                collectionArray.Add(t);
            }

            getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?id=custom-" + collectionId + "&count=200&min_position=400";
            collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            response = JObject.Parse(collectionsJson);
            response = (JObject)response["response"];

            foreach (var t in (JArray)response["timeline"])
            {
                collectionArray.Add(t);
            }

            // Get the first tweet ID and a random tweet ID
            long firstEntryId = long.Parse(collectionArray[0]["tweet"]["id"].ToString());
            Random random = new Random();
            long randomEntryId = long.Parse(collectionArray[random.Next(200, collectionArray.Count)]["tweet"]["id"].ToString());

            Console.WriteLine("Rearranging tweet:" + firstEntryId);

            // Remove the tweet
            string rearrangeCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries/move.json?id=custom-" + collectionId + "&tweet_id=" + firstEntryId + "&relative_to=" + randomEntryId;
            string rearrangedCollectionsJson = TwitterAccessor.ExecutePOSTQueryReturningJson(rearrangeCollectionEntriesQuery);
            Console.WriteLine("Rearrange tweet in collection response:" + rearrangedCollectionsJson);
        }

        /// <summary>
        /// Verifies if a collection is empty.
        /// </summary>
        public bool IsCollectionEmpty(long collectionId)
        {
            // Get the collection entries
            string getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?id=custom-" + collectionId;
            string collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            JObject response = JObject.Parse(collectionsJson);
            response = (JObject)response["response"];
            JArray collectionsArray = (JArray)response["timeline"];

            if (collectionsArray.Count > 1)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Unfollows everyone that isn't following you back.
        /// </summary>
        public void UnfollowNonMutuals(IAuthenticatedUser authenticatedUser, int requestDelay = 1100, bool sortByInactivity = true)
        {
            // Get the following and followers lists to compare them
            var followers = authenticatedUser.GetFollowers(authenticatedUser.FollowersCount);
            var friends = authenticatedUser.GetFriends(authenticatedUser.FriendsCount);

            // The list of everyone we'll have to unfollow
            var nonMutuals = new List<IUser>();

            foreach (var friend in friends)
            {
                if (!followers.Contains(friend))
                {
                    // Why even call them "friends" THEY'RE NOT EVEN FOLLOWING US
                    nonMutuals.Add(friend);
                }
            }

            // Let's unfollow the unpopular ones first in case we hit the rate limit
            nonMutuals = nonMutuals.OrderBy(f => f.FollowersCount).ToList();

            foreach (var nonMutual in nonMutuals)
            {
                try
                {
                    Console.WriteLine("Unfollowing " + nonMutual.ScreenName);
                    authenticatedUser.UnFollowUser(nonMutual);
                    Thread.Sleep(requestDelay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        /// <summary>
        /// Removes tweets that have 0 likes and 0 retweets. Aww.
        /// </summary>
        public void RemoveTweetsWithZeroNotes(IAuthenticatedUser authenticatedUser)
        {
            IEnumerable<ITweet> tweets = GetTimelineEntries(authenticatedUser.ScreenName);
            foreach (ITweet t in tweets)
            {
                try
                {
                    if (t.RetweetCount < 1 && t.FavoriteCount < 1)
                    {
                        Console.WriteLine("Tweet: " + t.Text + " has no notes");
                        Tweet.DestroyTweet(t.Id);
                    }
                    else
                    {
                        Console.WriteLine("Tweet: " + t.Text + " has notes");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        /// <summary>
        /// Removes tweets from the logged in user's timeline containing a specific keyword.
        /// </summary>
        public void RemoveTweetsWithKeyword(IAuthenticatedUser authenticatedUser, string keyword)
        {
            IEnumerable<ITweet> tweets = GetTimelineEntries(authenticatedUser.ScreenName);
            tweets = tweets.Where(t => t.Text.Contains(keyword));

            foreach (ITweet t in tweets)
            {
                try
                {
                    Tweet.DestroyTweet(t.Id);
                    Console.WriteLine("Tweet: " + t.Text + " has been removed");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        /// <summary>
        /// Gets a user by their screen name and follows their followers.
        /// </summary>
        public void FollowOtherUsersFollowers(IAuthenticatedUser authenticatedUser, string otherUserScreenName, int amountToFollow, int requestDelay = 1100)
        {
            Console.WriteLine("Getting a list of the other user's followers...");
            IUser otherUser = User.GetUserFromScreenName(otherUserScreenName);
            IEnumerable<IUser> otherUserFollowers = otherUser.GetFollowers(amountToFollow);

            Console.WriteLine("Getting a list of your followers to compare...");
            IEnumerable<long> followerIds = authenticatedUser.GetFollowerIds();

            foreach (IUser u in otherUserFollowers)
            {
                if (u.Protected)
                {
                    Console.WriteLine("User " + u.ScreenName + " is a protected account.");
                }
                else if (followerIds.Contains(u.Id))
                {
                    Console.WriteLine("User " + u.ScreenName + " is already following us.");
                }
                else
                {
                    if (u.FollowersCount > 20 && u.FriendsCount > 20)
                    {
                        Console.WriteLine("Following " + u.ScreenName);
                        try
                        {
                            authenticatedUser.FollowUser(u);
                            Thread.Sleep(requestDelay);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                    else
                    {
                        Console.WriteLine("User " + u.ScreenName + " doesn't have enough followers");
                    }
                }
            }
        }

        /// <summary>
        /// Gets a user's timeline and follows everyone that retweets from them.
        /// </summary>
        public void FollowRetweeters(IAuthenticatedUser authenticatedUser, string otherUserScreenName, int amountToFollow, int requestDelay = 1100, int followerTreshold = 20)
        {
            // Get the user's timeline
            Console.WriteLine("Getting user's timeline...");
            IUser otherUser = User.GetUserFromScreenName(otherUserScreenName);
            IEnumerable<ITweet> otherUserTimeline = otherUser.GetUserTimeline(50);

            // Get your followers (we need them to check if people already follow you)
            Console.WriteLine("Getting a list of your followers to compare...");
            IEnumerable<long> followerIds = authenticatedUser.GetFollowerIds();

            // Get the list of people to follow based on the retweets on your account
            Console.WriteLine("Getting the list of retweeters...");
            List<IUser> retweeters = new List<IUser>();
            foreach (var tweet in otherUserTimeline)
            {
                try
                {
                    List<ITweet> retweets;
                    if (tweet.RetweetedTweet != null)
                    {
                        retweets = tweet.RetweetedTweet.GetRetweets();
                    }
                    else
                    {
                        retweets = tweet.GetRetweets();
                    }

                    foreach (var retweet in retweets)
                    {
                        retweeters.Add(retweet.CreatedBy);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Console.WriteLine("Resuming getting retweeters...");
                }
            }

            // Clean the list up and sort it
            retweeters = retweeters
                .DistinctBy(u => u.ScreenName)                              // Clean up duplicates
                .Where(u => u.ScreenName != authenticatedUser.ScreenName)   // Remove yourself from the list
                .Where(u => u.FollowersCount >= followerTreshold)           // Remove users with less followers than needed
                .Where(u => u.FriendsCount >= followerTreshold)             // Remove users with less friends than needed
                .Where(u => !u.Protected)                                   // Remove private accounts
                .Where(u => !followerIds.Contains(u.Id))                    // Remove people who are already following us
                .OrderByDescending(u => u.FriendsCount)                     // Order by most followers
                .ToList();                                                  // Throw them in a list

            // Follow everyone in the list
            foreach (IUser u in retweeters)
            {
                Console.WriteLine("Following " + u.ScreenName);
                try
                {
                    authenticatedUser.FollowUser(u);
                    Thread.Sleep(requestDelay);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        /// <summary>
        /// Dumps all of a user's media into the /Images/ folder. 
        /// (( Can eat disk space quickly ))
        /// </summary>
        public async void DumpAllMedia(IAuthenticatedUser authenticatedUser, string userToDump, int? amountToDump = null)
        {
            IEnumerable<ITweet> tweets = GetTimelineEntries(userToDump);
            foreach (ITweet t in tweets.Where(t => t.Media.Any()))
            {
                foreach (var media in t.Media)
                {
                    try
                    {
                        string imageUrl = media.MediaURLHttps;
                        string saveLocation = Path.GetDirectoryName(Path.GetDirectoryName(Directory.GetCurrentDirectory()))
                                    + @"\Twitter Bots\Twitter Bots\Data\Images\"
                                    + media.MediaURLHttps.Split('/').Last();

                        byte[] imageBytes;
                        HttpWebRequest imageRequest = (HttpWebRequest)WebRequest.Create(imageUrl);
                        WebResponse imageResponse = await imageRequest.GetResponseAsync();

                        System.IO.Stream responseStream = imageResponse.GetResponseStream();

                        using (BinaryReader br = new BinaryReader(responseStream))
                        {
                            imageBytes = br.ReadBytes(500000);
                            br.Dispose();
                        }
                        responseStream.Dispose();
                        imageResponse.Dispose();

                        FileStream fs = new FileStream(saveLocation, FileMode.Create);
                        BinaryWriter bw = new BinaryWriter(fs);
                        try
                        {
                            bw.Write(imageBytes);
                        }
                        finally
                        {
                            fs.Dispose();
                            bw.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }

            Console.WriteLine("z");
        }

        /// <summary>
        /// Gets the first 3200 tweets from the given user's timeline.
        /// </summary>
        /// <param name="authenticatedUser">The logged in user.</param>
        /// <param name="collectionId">The collection ID.</param>
        /// <param name="filename">The wanted filename. If null the filename will be "Collection-{collectionId}".</param>
        /// <param name="shouldAppendNewTweets">If we want existing tweets to be persisted. If false, if the file already exists, the old tweets will be replaced with the new ones.</param>
        public void CollectionToFile(IAuthenticatedUser authenticatedUser, long collectionId, string filename, bool shouldAppendNewTweets = true)
        {
            List<ITweetDTO> tweetList = new List<ITweetDTO>();
            filename = (!string.IsNullOrWhiteSpace(filename)) ? filename : "Collection-" + collectionId;
            var path = Path.GetDirectoryName(Path.GetDirectoryName(Directory.GetCurrentDirectory()))
                                + @"\Twitter Bots\Twitter Bots\Data\Files\"
                                + filename
                                + ".json";

            // Get the tweets from the collection
            var tweets = GetCollectionEntries(collectionId);

            // Try to retrieve the full tweet information and add it to the list
            Console.WriteLine("Getting collection's contents...");
            foreach (var tweet in tweets)
            {
                try
                {
                    ITweet fullTweet = Tweet.GetTweet(long.Parse(tweet["tweet"]["id"].ToString()));

                    if (fullTweet != null)
                    {
                        tweetList.Add(fullTweet.TweetDTO);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            // Append new list to the existing one if needed
            if (shouldAppendNewTweets && File.Exists(path))
            {
                Console.WriteLine("Appending existing CSV contents to list...");

                var json = File.ReadAllText(path);
                var tweetDTOs = json.ConvertJsonTo<ITweetDTO[]>();

                tweetList.AddRange(tweetDTOs);
            }

            // Create the JSON file
            try
            {
                var json = tweetList
                    .DistinctBy(t => t.Id)
                    .ToJson();
                
                File.WriteAllText(path, json);
                Console.WriteLine("Successfully written tweet list to JSON");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
