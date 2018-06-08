using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using HttpServer;
using HttpServer.Sessions;
using System.Net.Sockets;
using System.Linq;

namespace ArchBench.PlugIns.Broker
{
    public class PlugInBroker : IArchServerModulePlugIn
    {
        private readonly TcpListener mListener;
        private int mNextServer;
        private Thread mRegisterThread;

        public PlugInBroker()
        {
            mListener = new TcpListener(IPAddress.Any, 9000);
        }

        #region Regist/Unregist servers

        private void ReceiveThreadFunction()
        {
            try
            {
                // Start listening for client requests.
                mListener.Start();

                // Buffer for reading data
                Byte[] bytes = new Byte[256];

                // Enter the listening loop.
                while (true)
                {
                    // Perform a blocking call to accept requests.
                    // You could also user server.AcceptSocket() here.
                    TcpClient client = mListener.AcceptTcpClient();

                    // Get a stream object for reading and writing
                    NetworkStream stream = client.GetStream();

                    int count = stream.Read(bytes, 0, bytes.Length);
                    if (count != 0)
                    {
                        // Translate data bytes to a ASCII string.
                        String data = Encoding.ASCII.GetString(bytes, 0, count);

                        char operation = data[0];
                        String server = data.Substring(1, data.IndexOf('-', 1) - 1);
                        String port = data.Substring(data.IndexOf('-', 1) + 1);
                        switch (operation)
                        {
                            case '+':
                                Regist(server, int.Parse(port));
                                break;
                            case '-':
                                Unregist(server, int.Parse(port));
                                break;
                        }

                    }

                    client.Close();
                }
            }
            catch (SocketException e)
            {
                Host.Logger.WriteLine("SocketException: {0}", e);
            }
            finally
            {
                mListener.Stop();
            }
        }

        private readonly List<KeyValuePair<string, int>> mServers = new List<KeyValuePair<string, int>>();

        private void Regist(String aAddress, int aPort)
        {
            if (mServers.Any(p => p.Key == aAddress && p.Value == aPort)) return;
            mServers.Add(new KeyValuePair<string, int>(aAddress, aPort));
            Host.Logger.WriteLine("Added server {0}:{1}.", aAddress, aPort);
        }

        private void Unregist(string aAddress, int aPort)
        {
            if (mServers.Remove(new KeyValuePair<string, int>(aAddress, aPort)))
            {
                Host.Logger.WriteLine("Removed server {0}:{1}.", aAddress, aPort);
            }
            else
            {
                Host.Logger.WriteLine("The server {0}:{1} is not registered.", aAddress, aPort);
            }
        }

        #endregion


        #region IArchServerModulePlugIn Members

        public bool Process(IHttpRequest aRequest, IHttpResponse aResponse, IHttpSession aSession)
        {
            WebClient client = new WebClient();

            if (mServers.Count == 0) return false;
            mNextServer = (mNextServer + 1) % mServers.Count;

            Host.Logger.WriteLine(String.Format("Broker to server on port {0}", mServers[mNextServer]));

            var uri = new StringBuilder();
            uri.AppendFormat("http://{0}:{1}", mServers[mNextServer].Key, mServers[mNextServer].Value);
            uri.Append(aRequest.Uri.AbsolutePath);
            uri.Append(GetQueryString(aRequest));

            //IDictionary<string,ICollection<string>> Assignments = new Dictionary<string, ICollection<string>>();
            IDictionary<string, string> servers = new Dictionary<string, string>();
            Console.WriteLine(client.BaseAddress.S);

                byte[] bytes = null;
            if (aRequest.Method == Method.Post)
            {
                ForwardCookie(client, aRequest);
                bytes = client.UploadValues(uri.ToString(), GetFormValues(aRequest));   
            }
            else
            {
                ForwardCookie(client, aRequest);
                bytes = client.DownloadData(uri.ToString());
                BackwardCookie(client,aResponse);
            }


           

                aResponse.ContentType = client.ResponseHeaders[HttpResponseHeader.ContentType];
            if (aResponse.ContentType.StartsWith("text/html"))
            {
                var writer = new StreamWriter(aResponse.Body, client.Encoding);
                string download = client.Encoding.GetString(bytes);
                //StreamWriter writer = new StreamWriter(aResponse.Body,client.Encoding);
                //writer.Write(client.Encoding.GetString(bytes));
                writer.WriteLine(download);
                writer.Flush();
            }
            else
            {
                aResponse.Body.Write(bytes, 0, bytes.Length);
            }

            return true;
        }

        #endregion

        #region ProfSugestoes
        private NameValueCollection GetFormValues(IHttpRequest aRequest)
        {
            NameValueCollection values = new NameValueCollection();
            foreach (HttpInputItem item in aRequest.Form)
            {
                values.Add(item.Name, item.Value);
            }
            return values;
        }

        private string GetQueryString(IHttpRequest aRequest)
        {
            int count = aRequest.QueryString.Count();
            if (count == 0) return "";

            var parameters = new StringBuilder("?");
            foreach (HttpInputItem item in aRequest.QueryString)
            {
                parameters.Append(String.Format("{0}={1}", item.Name, item.Value));
                if (--count > 0) parameters.Append('&');
            }
            return parameters.ToString();
        }

        private void ForwardCookie(WebClient aClient, IHttpRequest aRequest)
        {
            if (aRequest.Headers["Cookie"] == null) return;
            aClient.Headers.Add("Cookie", aRequest.Headers["Cookie"]);
        }

        private void BackwardCookie(WebClient aClient, IHttpResponse aResponse)
        {
            if (aClient.ResponseHeaders["Set-Cookie"] == null) return;
            aResponse.AddHeader("Set-Cookie", aClient.ResponseHeaders["Set-Cookie"]);
        }

        #endregion

        #region IArchServerPlugIn Members

        public string Name => "ArchServer Broker Plugin";

        public string Description => "Coordinates communication, forwarding requests and transmitting results";

        public string Author => "Joaquim Abreu & Alejandro Carvalho";

        public string Version => "1.0";

        public bool Enabled { get; set; }

        public IArchServerPlugInHost Host
        {
            get; set;
        }

        public void Initialize()
        {
            mRegisterThread = new Thread(ReceiveThreadFunction);
            mRegisterThread.IsBackground = true;
            mRegisterThread.Start();
        }

        public void Dispose()
        {
        }

        #endregion
    }
}
