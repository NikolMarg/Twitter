using System;
using System.Threading;
using Tweetinvi;

namespace Queue
{
    class Program
    {
        static void Main()
        {
            ExceptionHandler.SwallowWebExceptions = false;
            string consumerKey = "***";
            string consumerSecret = "***";
            string accessToken = "***";
            string accessTokenSecret = "***";

            // Applies credentials for the current thread. If used for the first time, set up the ApplicationCredentials
            Auth.SetUserCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret);

            QueueHelper helper = new QueueHelper();
            long collectionId = "***";

            //while (helper.IsCollectionEmpty(collectionId) == false)
            for (int i = 1 ; i <= 200 ; i++)
            {
                //Thread.Sleep(14400000); // 4 hours (in milliseconds) so 6 posts a day
                //helper.HandleQueue(collectionId);
                helper.ShuffleTweet(collectionId);
            }
            
            Console.WriteLine("End!");
            Console.ReadLine();
        }
    }

}

