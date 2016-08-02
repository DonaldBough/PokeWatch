using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.IO;

namespace Pokewatch
{
    public class GroupMeBot
    {
        public string bot_id;
        public IPAddress localIP;

        public GroupMeBot(string bot_id)
        {
            this.bot_id = bot_id;
        }

        public void PostMessage(string message)
        {
            HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create("https://api.groupme.com/v3/bots/post");
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/json";

            BotMessage jsonMessage = new BotMessage(this.bot_id, message);
            string m = JsonConvert.SerializeObject(jsonMessage);
            byte[] bytes = new ASCIIEncoding().GetBytes(m);

            httpRequest.ContentLength = bytes.Length;
            Stream httpStream = httpRequest.GetRequestStream();
            httpStream.Write(bytes, 0, bytes.Length);
            httpStream.Close();
        }

        public string ListenForMessages()
        {
            HttpListener groupMeListener = new HttpListener();
            //groupMeListener.Prefixes.Add("http://66.84.198.2:80");

            groupMeListener.Start();
            string url = "http://checkip.dyndns.org";
            WebRequest req = WebRequest.Create(url);
            WebResponse resp = req.GetResponse();
            StreamReader sr = new StreamReader(resp.GetResponseStream());
            string response = sr.ReadToEnd().Trim();
            string[] a = response.Split(':');
            string a2 = a[1].Substring(1);
            string[] a3 = a2.Split('<');
            string a4 = a3[0];

            return null;
        }

        internal class BotMessage
         {
             public string bot_id;
             public string text;
 
             public BotMessage(string bot_id, string text)
             {
                 this.bot_id = bot_id;
                 this.text = text;
             }
         }


    } //class GroupMeBot
}
