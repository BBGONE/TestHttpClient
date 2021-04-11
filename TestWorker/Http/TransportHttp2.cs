﻿using TestWorker.Credentials;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace TestWorker.Http
{
    public class TransportHttp2
    {
        public TransportHttp2(
            string uri,
            string method,
            Dictionary<string, string> headers,
            string body = null,
            CredentialPFX certificate = null,
            CookieCollection cookies = null)
        {
            Uri = uri;
            Method = method;
            Headers = headers;
            Body = body;
            ClientCertificate = certificate;
            Cookies = cookies;
        }

        public const string Accept = "Accept";
        public const string Authorization = "Authorization";
        public const string ContentTypeKey = "Content-Type";
        public const string SetCookieKey = "Set-Cookie";
        public const string SOAPActionKey = "SOAPAction";
        public const string UserAgentKey = "User-Agent";
        private static readonly CookieCollection EMPTY_COOKIES = new CookieCollection();

        public string Uri { get; private set; }

        public string Method { get; private set; }

        public TimeSpan Timeout { get; set; }

        public Dictionary<string, string> Headers { get; private set; }

        public string Body { get; private set; }

        public CookieCollection Cookies { get; private set; }

        public CredentialPFX ClientCertificate { get; private set; }

        public string Request { get; private set; }

        public string Response { get; private set; }

        public (string Value, byte[] RawValue, bool IsRaw, bool IsAssigned) ResponseBody { get; private set; }

        public Dictionary<string, string> ResponseHeaders { get; private set; }
        public CookieCollection ResponseCookies { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }

        #region Helpers
        private string GetContentType(List<KeyValuePair<string, string>> headers)
        {
            if (Headers.TryGetValue(ContentTypeKey, out var contentType))
            {
                headers.RemoveAll(kv => kv.Key == ContentTypeKey);
            }
            else
            {
                contentType = MediaTypeNames.Application.Json;
            }

            return contentType;
        }

        #endregion

        #region Events
        public delegate void RequestHandler(object sender, TransportHttpRequestEventArgs e);

        public delegate void ResponseHandler(object sender, TransportHttpResponseEventArgs e);

        public delegate void SuccessHandler(object sender, EventArgs e);

        public delegate void FailHandler(object sender, EventArgs e);

        public event RequestHandler OnRequest;

        public event ResponseHandler OnResponse;

        public event SuccessHandler OnSuccess;

        public event FailHandler OnFail;
        #endregion

        public async Task<bool> Execute()
        {
            bool result = false;

            var httpMethod = new HttpMethod(Method.ToUpper());

            var headers = Headers?.ToList() ?? new List<KeyValuePair<string, string>>();

            string contentType = this.GetContentType(headers);

            var request = new HttpRequestMessage(httpMethod, Uri);

            if (Cookies?.Any() == true)
            {
                string cookieValue = string.Join("; ", Cookies.Cast<Cookie>().Select(c => c.ToString()));
                request.Headers.Add("Cookie", cookieValue);
            }

            headers.ForEach(kvp => { request.Headers.Add(kvp.Key, kvp.Value); });

            Request = GetRequestLog(request, Body);

            OnRequest?.Invoke(this, new TransportHttpRequestEventArgs(Request));

            if (!string.IsNullOrEmpty(Body))
            {
                request.Content = await GetRequestContent(contentType);
            }

            try
            {
                var handler = new DefaultHttpClientHandler2(pfx: this.ClientCertificate);
                using var client = new HttpClient(handler, true);
                client.Timeout = Timeout.TotalMilliseconds > 0? Timeout: TimeSpan.FromSeconds(60);

                using HttpResponseMessage response = await client.SendAsync(request);

                this.StatusCode = response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    ResponseHeaders = GetResponseHeaders(response);

                    ResponseCookies = GetResponseCookies(response);

                    ResponseBody = await GetResponseBody(response);
                }

                Response = GetResponseLog(response);
                result = await ProcessResponse(response);
            }
            catch (HttpRequestException e)
            {
                Response = $"{e.Message}";
            }
            catch (Exception e)
            {
                Response = e.Message;
            }
            finally
            {
                OnResponse?.Invoke(this, new TransportHttpResponseEventArgs(Response));
            }

            HandleResult(result);

            return result;
        }

        public virtual async Task<HttpContent> GetRequestContent(string contentType)
        {
            await Task.CompletedTask;
            StringContent content = new StringContent(Body, Encoding.UTF8, contentType);
            return content;
        }

        private string GetRequestLog(HttpRequestMessage request, string body)
        {
            StringBuilder requestLog = new StringBuilder();
            requestLog.AppendFormat($"{request.Method} {request.RequestUri}\r\n");

            foreach (var item in request.Headers)
            {
                string value = item.Value == null ? string.Empty : string.Join(", ", item.Value);
                _ = requestLog.AppendFormat($"{item.Key}: {value}\r\n");
            }

            requestLog.AppendLine();

            if (!string.IsNullOrEmpty(body))
            {
                requestLog.AppendLine(body);
            }

            return requestLog.ToString();
        }

        #region Response Prosessing
        
        private Dictionary<string, string> GetResponseHeaders(HttpResponseMessage response)
        {
            var headers = response.Headers.Concat(response.Content.Headers);
            var result = new Dictionary<string, string>();

            foreach (var kvp in headers)
            {
                string value = kvp.Value == null ? string.Empty : string.Join(", ", kvp.Value);
                result.TryAdd(kvp.Key, value);
            }
            return result;
        }

        private CookieCollection GetResponseCookies(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues(SetCookieKey, out var values))
            {
                CookieContainer container = new CookieContainer();
                Uri pageUri = response.RequestMessage.RequestUri;

                if (values != null)
                {
                    foreach (string cookie in values)
                    {
                        container.SetCookies(pageUri, cookie);
                    }
                }

                return container.GetCookies(pageUri);
            }

            return EMPTY_COOKIES;
        }

        protected virtual async Task<(string Value, byte[] RawValue, bool IsRaw, bool IsAssigned)> GetResponseBody(HttpResponseMessage response)
        {
            var contentType = response.Content.Headers.ContentType;

            if (new string[]
                    {
                        MediaTypeNames.Application.Octet,
                        MediaTypeNames.Application.Pdf,
                        MediaTypeNames.Application.Rtf,
                        MediaTypeNames.Application.Zip,
                    }
                .Contains(contentType.MediaType) || response.Content is StreamContent)
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                return (null, bytes, true, true);
            }

            var str = await response.Content.ReadAsStringAsync();

            return (str, null, false, true);
        }

        private string GetResponseLog(HttpResponseMessage response)
        {
            if (response == null)
            {
                return null;
            }

            StringBuilder responseLog = new StringBuilder();
            responseLog.AppendFormat($"{response.RequestMessage.RequestUri.Scheme.ToUpper()} {response.Version} {(int)response.StatusCode} {response.StatusCode}\r\n");

            if (ResponseHeaders != null)
            {
                foreach (var key in ResponseHeaders.Keys)
                {
                    responseLog.AppendFormat($"{key}: {ResponseHeaders[key]}\r\n");
                }
            }

            if (ResponseBody.IsAssigned)
            {
                responseLog.AppendLine();
                responseLog.AppendLine(ResponseBody.IsRaw ? Convert.ToBase64String(ResponseBody.RawValue) : ResponseBody.Value);
            }
            return responseLog.ToString();
        }

        protected virtual async Task<bool> ProcessResponse(HttpResponseMessage response)
        {
            await Task.CompletedTask;
            return response.IsSuccessStatusCode;
        }

        private void HandleResult(bool result)
        {
            if (result)
            {
                OnSuccess?.Invoke(this, new EventArgs());
            }
            else
            {
                OnFail?.Invoke(this, new EventArgs());
            }
        }
        #endregion
    }
}
