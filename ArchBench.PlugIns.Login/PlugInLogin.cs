﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using HttpServer;
using HttpServer.Exceptions;
using HttpServer.Helpers;
using HttpServer.Sessions;

namespace ArchBench.PlugIns.Login
{
    public class PlugInLogin : IArchServerModulePlugIn
    {
        private readonly ResourceManager mManager = new ResourceManager();

        public PlugInLogin()
        {
            mManager.LoadResources(
                "/user/login/", Assembly.GetExecutingAssembly(), Assembly.GetExecutingAssembly().GetName().Name );
        }

        #region IArchServerModulePlugIn Members

        public bool Process( IHttpRequest aRequest, IHttpResponse aResponse, IHttpSession aSession )
        {
            if ( aRequest.Method == Method.Get )
            {
                if ( IsResourceRequest(aRequest) )
                {
                    return ProcessResource( aRequest, aResponse );
                }

                if ( aRequest.Uri.AbsolutePath.StartsWith("/user/login") )
                {
                    if (aSession["Username"] == null)
                    {
                        StreamWriter writer = new StreamWriter(aResponse.Body);
                        writer.Write(Resource.login);
                        writer.Flush();
                    }
                    else
                    {
                        Host.Logger.WriteLine("User [{0}] logged on.", aSession["Username"]);

                        var writer = new StreamWriter(aResponse.Body);
                        writer.WriteLine("<p>User <strong>{0}</strong> logged on.</p>", aSession["Username"]);
                        writer.Flush();
                        writer.WriteLine("<a href=\"/user/logout/\">Logout</a>");
                        writer.Flush();
                        return true;

                    }

                    return true;
                }
            }
            else if ( aRequest.Method == Method.Post )
            {
                foreach ( HttpInputItem item in aRequest.Form )
                {
                    Host.Logger.WriteLine( "[{0}] := {1}", item.Name, item.Value );
                }

                if (aRequest.Form.Contains("Username"))
                {
                    aSession["Username"] = aRequest.Form["Username"].Value;
                    Host.Logger.WriteLine( "User [{0}] logged on.", aSession["Username"] );

                    var writer = new StreamWriter(aResponse.Body);
                    writer.WriteLine( "<p>User <strong>{0}</strong> logged on.</p>", aSession["Username"] );
                    writer.Flush();
                    writer.WriteLine("<a href=\"/user/logout/\">Logout</a>");
                    writer.Flush();
                    return true;
                }

                Host.Logger.WriteLine("Error: invalid login data.");
            }

            return false;
        }

        #endregion

        private bool ProcessResource( IHttpRequest aRequest, IHttpResponse aResponse )
        {
            if ( ! IsResourceRequest( aRequest ) ) return false;

            string type;
            var stream = GetResourceStream( GetResourceFilename(aRequest), out type );
            if ( stream == null ) return false;

            aResponse.ContentType = type;

            // Force the load of the resource
            aResponse.Status = HttpStatusCode.OK;
            aResponse.AddHeader( "Last-modified", DateTime.Now.ToString("r") );

            aResponse.ContentLength = stream.Length;
            aResponse.SendHeaders();

            
            if ( aRequest.Method != "Headers" && aResponse.Status != HttpStatusCode.NotModified )
            {
                byte[] buffer = new byte[8192];
                int bytesRead = stream.Read( buffer, 0, 8192 );
                while (bytesRead > 0)
                {
                    aResponse.SendBody(buffer, 0, bytesRead);
                    bytesRead = stream.Read( buffer, 0, 8192 );
                }
            }

            return true;
        }
        
        /// <summary>
        /// Returns true if the module can handle the request
        /// </summary>
        private bool IsResourceRequest( IHttpRequest aRequest )
        {
            if ( aRequest.Uri.AbsolutePath.EndsWith("*") ) return false;
            if (  ! mManager.ContainsResource( GetResourceFilename(aRequest) ) ) return false;
            return true;
        }

        private string GetResourceFilename( IHttpRequest aRequest )
        {
            if ( aRequest.UriParts.Length == 0 ) return string.Empty;

            if ( aRequest.UriParts[aRequest.UriParts.Length - 1].IndexOf( '.' ) != -1 )
                return aRequest.Uri.AbsolutePath;

            if ( aRequest.Uri.AbsolutePath.EndsWith( "/" ) )
                return $"{aRequest.Uri.AbsolutePath.Substring( 0, aRequest.Uri.AbsolutePath.Length - 1 )}.html";

            return $"{aRequest.Uri.AbsolutePath}.html";
        }

        /// <summary>
        /// List with all mime-type that are allowed. 
        /// </summary>
        /// <remarks>All other mime types will result in a Forbidden http status code.</remarks>
        public IDictionary<string, string> MimeTypes => new Dictionary<string, string> {
            {"default", "application/octet-stream"},
            {    "txt", "text/plain"},
            {   "html", "text/html"},
            {    "htm", "text/html"},
            {    "jpg", "image/jpg"},
            {   "jpeg", "image/jpg"},
            {    "bmp", "image/bmp"},
            {    "gif", "image/gif"},
            {    "png", "image/png"},
            {    "ico", "image/vnd.microsoft.icon"},
            {    "css", "text/css"},
            {   "gzip", "application/x-gzip"},
            {    "zip", "multipart/x-zip"},
            {    "tar", "application/x-tar"},
            {    "pdf", "application/pdf"},
            {    "rtf", "application/rtf"},
            {    "xls", "application/vnd.ms-excel"},
            {    "ppt", "application/vnd.ms-powerpoint"},
            {    "doc", "application/application/msword"},
            {     "js", "application/javascript"},
            {     "au", "audio/basic"},
            {    "snd", "audio/basic"},
            {     "es", "audio/echospeech"},
            {    "mp3", "audio/mpeg"},
            {    "mp2", "audio/mpeg"},
            {    "mid", "audio/midi"},
            {    "wav", "audio/x-wav"},
            {    "swf", "application/x-shockwave-flash"},
            {    "avi", "video/avi"},
            {     "rm", "audio/x-pn-realaudio"},
            {    "ram", "audio/x-pn-realaudio"},
            {    "aif", "audio/x-aiff"}
        };
        
        private Stream GetResourceStream( string aPath, out string aType )
        {
            var position = aPath.LastIndexOf('.');
            var extension = position == -1 ? null : aPath.Substring( position + 1 );

            if ( extension == null )
                throw new InternalServerException("Failed to find file extension");

            if ( MimeTypes.ContainsKey( extension )  )
                aType = MimeTypes[extension];
            else
                throw new ForbiddenException( "Forbidden file type: " + extension) ;

            return mManager.GetResourceStream( aPath );
        }


        #region IArchServerPlugIn Members

        public string Name => "ArchServer Login Plugin";

        public string Description => "Process /user/login/ requests";

        public string Author => "Leonel Nobrega";

        public string Version => "1.0";

        public bool Enabled { get; set; }

        public IArchServerPlugInHost Host
        {
            get; set;
        }

        public void Initialize()
        {
        }

        public void Dispose()
        {
        }

        #endregion
    }
}
