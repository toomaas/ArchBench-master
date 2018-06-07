﻿using System;
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


        #region IArchServerModulePlugIn Members

        public bool Process(IHttpRequest aRequest, IHttpResponse aResponse, IHttpSession aSession)
        {
            WebClient client = new WebClient();

            byte[] bytes = null;
            var uri = aRequest.Uri.AbsoluteUri;
            if (aRequest.Method == Method.Post)
            {
                bytes = client.UploadValues(uri, GetFormValues(aRequest));
            }
            else
            {
                bytes = client.DownloadData(uri);
                Host.Logger.WriteLine(String.Format("Bytes"));
                string download = Encoding.ASCII.GetString(bytes);
                Console.WriteLine(download);
            }

            //StreamWriter writer = new StreamWriter(aResponse.Body,client.Encoding);
            //writer.Write(client.Encoding.GetString(bytes));
   

            if (mServers.Count == 0) return false;
            mNextServer = (mNextServer + 1) % mServers.Count;

            Host.Logger.WriteLine(String.Format("Dispatching to server on port {0}", mServers[mNextServer]));

            var redirection = new StringBuilder();
            redirection.AppendFormat("http://{0}:{1}", mServers[mNextServer].Key, mServers[mNextServer].Value);
            redirection.Append(aRequest.Uri.AbsolutePath);

            redirection.Append(GetQueryString(aRequest));
            /*int count = aRequest.QueryString.Count();
            if (count > 0)
            {
                redirection.Append('?');
                foreach (HttpInputItem item in aRequest.QueryString)
                {
                    redirection.Append(String.Format("{0}={1}", item.Name, item.Value));
                    if (--count > 0) redirection.Append('&');
                }
            }*/

            //aResponse.Redirect(redirection.ToString());

            return true;
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
