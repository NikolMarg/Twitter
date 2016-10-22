using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Tweetinvi;
using Tweetinvi.Models;

namespace Queue
{
    class QueueHelper
    {
        private const long TempQueueId = "***";

        public void HandleQueue(long collectionId)
        {
            // Get the collection entries
            JArray collectionArray = GetCollectionEntries(collectionId);

            // Get the first entry
            long firstEntryId = Convert.ToInt64(collectionArray[0]["tweet"]["id"]);

            // Retweet the first entry
            // If the entry is already retweeted, unretweet it and retweet it again
            TryRetweet(firstEntryId, collectionId, collectionArray);

            // Remove the entry from the top of the queue and add it to the other one
            MoveToOtherQueue(firstEntryId, collectionId);
        }


        // Unretweets + Retweets again
        // If a retweet isn't possible it moves the tweet to the temp queu for another time
        public void TryRetweet(long tweetId, long collectionId, JArray collectionArray)
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
                MoveToOtherQueue(tweetId, collectionId);
                HandleQueue(collectionId);
            }
        }

        // Gets the first 20 entries from the collection
        public JArray GetCollectionEntries(long collectionId)
        {
            string getCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries.json?id=custom-" + collectionId;
            string collectionsJson = TwitterAccessor.ExecuteGETQueryReturningJson(getCollectionEntriesQuery);
            JObject response = JObject.Parse(collectionsJson);
            response = (JObject)response["response"];
            JArray collectionsArray = (JArray)response["timeline"];

            return collectionsArray;
        }

        // Moves a tweet to the temp queue
        public void MoveToOtherQueue(long tweetId, long collectionId)
        {
            // Remove the tweet from the main queue
            string removeFromCollectionQuery = "https://api.twitter.com/1.1/collections/entries/remove.json?id=custom-" + collectionId + "&tweet_id=" + tweetId;
            string removeFromCollectionJson = TwitterAccessor.ExecutePOSTQueryReturningJson(removeFromCollectionQuery);
            Console.WriteLine("Remove tweet " + tweetId + " from collection response:" + removeFromCollectionJson);

            // Add the tweet to the temp queue
            string addToCollectionQuery = "https://api.twitter.com/1.1/collections/entries/add.json?id=custom-" + TempQueueId + "&tweet_id=" + tweetId;
            string addToCollectionJson = TwitterAccessor.ExecutePOSTQueryReturningJson(addToCollectionQuery);
            Console.WriteLine("Add tweet " + tweetId + " to collection response:" + addToCollectionJson);
        }

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
            long firstEntryId = Convert.ToInt64(collectionArray[0]["tweet"]["id"]);
            Random random = new Random();
            long randomEntryId = Convert.ToInt64(collectionArray[random.Next(200, collectionArray.Count)]["tweet"]["id"]);

            Console.WriteLine("Rearranging tweet:" + firstEntryId);

            // Remove the tweet
            string rearrangeCollectionEntriesQuery = "https://api.twitter.com/1.1/collections/entries/move.json?id=custom-" + collectionId + "&tweet_id=" + firstEntryId + "&relative_to=" + randomEntryId;
            string rearrangedCollectionsJson = TwitterAccessor.ExecutePOSTQueryReturningJson(rearrangeCollectionEntriesQuery);
            Console.WriteLine("Rearrange tweet in collection response:" + rearrangedCollectionsJson);
        }

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

        public void UnfollowNonMutuals(IAuthenticatedUser authenticatedUser)
        {
            IEnumerable<IUser> userList = authenticatedUser.GetFriends(1300);
            foreach (var following in userList)
            {
                var relationship = following.GetRelationshipWith(authenticatedUser);
                if (relationship.Following == true)
                    Console.WriteLine(following.ScreenName + " is following us.");
                else
                {
                    Console.WriteLine(following.ScreenName + " is not following us.");
                    if (!following.Protected)
                        authenticatedUser.UnFollowUser(following);
                }
            }
        }

        public void RemoveTweetsWithZeroNotes(IAuthenticatedUser authenticatedUser)
        {
            IEnumerable<ITweet> tweets = Timeline.GetUserTimeline(authenticatedUser.Id, 1000);
            foreach (ITweet t in tweets)
            {
                if (t.RetweetCount < 1 && t.FavoriteCount < 1)
                {
                    Console.WriteLine("Tweet: " + t.Text + " has no notes");
                    t.Destroy();
                }
                else
                {
                    Console.WriteLine("Tweet: " + t.Text + " has notes");
                }
            }
        }


        public void FollowOtherUsersFollowers(IAuthenticatedUser authenticatedUser, string otherUserScreenName, int amountToFollow)
        {
            IUser otherUser = User.GetUserFromScreenName(otherUserScreenName);
            IEnumerable<IUser> followers = otherUser.GetFollowers(amountToFollow);
            foreach (IUser u in followers)
            {
                if (!u.Following && u.FollowersCount > 20 && u.FriendsCount > 20)
                {
                    Console.WriteLine("Following " + u.ScreenName);
                    try
                    {
                        authenticatedUser.FollowUser(u);
                    }
                    catch (Exception) { }
                }
                else
                {
                    Console.WriteLine("User " + u.ScreenName + " doesn't have enough followers");
                }
            }
        }
    }
}
