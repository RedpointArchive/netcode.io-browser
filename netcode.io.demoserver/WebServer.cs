using System;
using System.Net;
using System.Threading;
using System.Linq;
using System.Text;

namespace netcode.io.demoserver
{
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, HttpListenerResponse, Tuple<int, byte[]>> _responderMethod;

        public WebServer(string[] prefixes, Func<HttpListenerRequest, HttpListenerResponse, Tuple<int, byte[]>> method)
        {
            if (!HttpListener.IsSupported)
                throw new NotSupportedException(
                    "Needs Windows XP SP2, Server 2003 or later.");

            // URI prefixes are required, for example 
            // "http://localhost:8080/index/".
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");

            // A responder method is required
            if (method == null)
                throw new ArgumentException("method");

            foreach (string s in prefixes)
                _listener.Prefixes.Add(s);

            _responderMethod = method;
            _listener.Start();
        }

        public WebServer(Func<HttpListenerRequest, HttpListenerResponse, Tuple<int, byte[]>> method, params string[] prefixes)
            : this(prefixes, method) { }

        public void Run()
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                Console.WriteLine("Webserver running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                Tuple<int, byte[]> rstr = _responderMethod(ctx.Request, ctx.Response);
                                ctx.Response.StatusCode = rstr.Item1;
                                ctx.Response.ContentLength64 = rstr.Item2.Length;
                                ctx.Response.OutputStream.Write(rstr.Item2, 0, rstr.Item2.Length);
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                                ctx.Response.OutputStream.Close();
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}