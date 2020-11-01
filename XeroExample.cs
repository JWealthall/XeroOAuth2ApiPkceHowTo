using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xero.NetStandard.OAuth2.Api;
using Xero.NetStandard.OAuth2.Client;
using Xero.NetStandard.OAuth2.Config;
using Xero.NetStandard.OAuth2.Token;
using Microsoft.Extensions.Configuration;

namespace XeroOAuth2ApiPkceHowTo
{
    public class XeroExample
    {
        private readonly object _lockObj = new object();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly IConfiguration _config = new ConfigurationBuilder().AddJsonFile("appsettings.json", true, true).Build();

        public void Run(string name)
        {
            try
            {
                Console.WriteLine(name + ": Starting ");
                var clientId = _config["ClientId"];

                XeroClient client = null;
                IXeroToken token = null;
                lock (_lockObj)
                {
                    client = new XeroClient(new XeroConfiguration { ClientId = clientId });

                    if (System.IO.File.Exists("Token.json"))
                    {
                        var savedJson = System.IO.File.ReadAllText("Token.json");
                        token = JsonConvert.DeserializeObject<XeroOAuth2Token>(savedJson);
                    }

                    if (token == null)
                    {
                        token = new XeroOAuth2Token
                        {
                            AccessToken = _config["AccessToken"],
                            IdToken = _config["IdentityToken"],
                            RefreshToken = _config["RefreshToken"]
                        };
                    }

                    IXeroToken newToken = Task.Run(() => client.RefreshAccessTokenAsync(token)).GetAwaiter().GetResult();
                    if (newToken != null) token = newToken;

                    var json = JsonConvert.SerializeObject(token, Formatting.Indented);
                    System.IO.File.WriteAllText("Token.json", json);
                }

                Console.WriteLine(name + ": Token refreshed");
                var accessToken = token.AccessToken;

                var tenant = "";
                try
                {
                    var conRes = Task.Run(() => client.GetConnectionsAsync(token)).GetAwaiter().GetResult();
                    tenant = conRes[0].TenantId.ToString();
                    Console.WriteLine(name + ": Tenants");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    SerializeException(e);
                    return;
                }

                var api = new AccountingApi { ExceptionFactory = CustomExceptionFactory };

                Console.WriteLine(name + ": New token process");

                var response = Task.Run(() => api.GetInvoicesAsyncWithHttpInfo(accessToken, tenant)).GetAwaiter().GetResult();
                var respInvoices = Task.Run(() => api.GetInvoicesAsyncWithHttpInfo(accessToken, tenant, page: 1, where: "")).GetAwaiter().GetResult();
                var invDate = respInvoices.Data._Invoices[0].Date;

                var recurring = Task.Run(() => api.GetRepeatingInvoicesAsyncWithHttpInfo(accessToken, tenant)).GetAwaiter().GetResult();
                var status = recurring.Data._RepeatingInvoices[0].Status;
                var schedule = recurring.Data._RepeatingInvoices[0].Schedule;

                Console.WriteLine(name + ": Complete");
            }
            catch (CustomApiException ce)
            {
                Console.WriteLine(name + ": Failed: " + ce.Message);
                SerializeException(ce);
                Console.WriteLine(ce);
                var limits = new XeroLimits(ce.Headers);
            }
            catch (Exception e)
            {
                Console.WriteLine(name + ": Failed: " + e.Message);
                SerializeException(e);
            }
        }

        public async Task RunAsync(string name)
        {
            try
            {
                Console.WriteLine(name + ": Starting ");
                var clientId = _config["ClientId"];

                XeroClient client = null;
                IXeroToken token = null;
                await _semaphore.WaitAsync();
                try
                {
                    client = new XeroClient(new XeroConfiguration { ClientId = clientId });

                    if (System.IO.File.Exists("Token.json"))
                    {
                        var savedJson = System.IO.File.ReadAllText("Token.json");
                        token = JsonConvert.DeserializeObject<XeroOAuth2Token>(savedJson);
                    }

                    if (token == null)
                    {
                        token = new XeroOAuth2Token
                        {
                            AccessToken = _config["AccessToken"],
                            IdToken = _config["IdToken"],
                            RefreshToken = _config["RefreshToken"]
                        };
                    }

                    var newToken = await client.RefreshAccessTokenAsync(token);
                    if (newToken != null) token = newToken;

                    var json = JsonConvert.SerializeObject(token, Formatting.Indented);
                    System.IO.File.WriteAllText("Token.json", json);
                }
                finally
                {
                    _semaphore.Release();
                }

                Console.WriteLine(name + ": Token refreshed");
                var accessToken = token.AccessToken;

                string tenant = "";
                try
                {
                    var conRes = await client.GetConnectionsAsync(token);
                    tenant = conRes[0].TenantId.ToString();
                    Console.WriteLine(name + ": Tenants");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    SerializeException(e);
                    return;
                }

                var api = new AccountingApi { ExceptionFactory = CustomExceptionFactory };

                Console.WriteLine(name + ": New token process");

                var response = await api.GetInvoicesAsyncWithHttpInfo(accessToken, tenant);
                var respInvoices = await api.GetInvoicesAsyncWithHttpInfo(accessToken, tenant, page: 1, where: "");
                var invDate = respInvoices.Data._Invoices[0].Date;

                var recurring = await api.GetRepeatingInvoicesAsyncWithHttpInfo(accessToken, tenant);
                var status = recurring.Data._RepeatingInvoices[0].Status;
                var schedule = recurring.Data._RepeatingInvoices[0].Schedule;

                var contacts = await api.GetContactsAsyncWithHttpInfo(accessToken, tenant, page: 1);
                var contact = await api.GetContactByContactNumberAsyncWithHttpInfo(accessToken, tenant, "Test");

                Console.WriteLine(name + ": Complete");
            }
            catch (CustomApiException ce)
            {
                Console.WriteLine(name + ": Failed: " + ce.Message);
                SerializeException(ce);
                Console.WriteLine(ce);
                var limits = new XeroLimits(ce.Headers);
            }
            catch (Exception e)
            {
                Console.WriteLine(name + ": Failed: " + e.Message);
                SerializeException(e);
            }
        }

        private void SerializeException(Exception e)
        {
            var strE = JsonConvert.SerializeObject(e, Formatting.Indented);
            Console.WriteLine(strE);
            System.IO.File.AppendAllText("Error.json", strE + Environment.NewLine);
        }

        public static readonly ExceptionFactory CustomExceptionFactory = (methodName, response) =>
        {
            var status = (int)response.StatusCode;
            switch (status)
            {
                case int code when status == 400:
                    return new CustomApiException(status, $"Xero API 400 error calling {methodName} :{response.Content}", response.Content, response.Headers, response.Cookies);
                case int code when status == 404:
                    return null;    // We can tell the resource was not found so return empty data
                case int code when status > 400:
                    return new CustomApiException(status, $"Xero API error calling {methodName}: {response.Content}", response.Content, response.Headers, response.Cookies);
            }
            return null;
        };
    }

    public class CustomApiException : ApiException
    {
        /// <summary>
        /// Gets the HTTP headers
        /// </summary>
        /// <value>HTTP headers</value>
        public Multimap<string, string> Headers { get; }

        /// <summary>
        /// Gets or sets any cookies passed along on the response.
        /// </summary>
        public List<Cookie> Cookies { get; set; }

        /// <summary>
        /// A daily or minute limit has been reached
        /// </summary>
        public bool LimitReached { get; set; }

        /// <summary>
        /// Gets the Xero Limits
        /// </summary>
        public XeroLimits Limits { get; set; }

        /// <summary>
        /// The authorisation token has expired
        /// </summary>
        public bool TokenError { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomerApiException"/> class.
        /// </summary>
        /// <param name="errorCode">HTTP status code.</param>
        /// <param name="message">Error message.</param>
        /// <param name="errorContent">Error content.</param>
        /// <param name="headers">HTTP headers.</param>
        /// <param name="cookies">HTTP cookies.</param>
        public CustomApiException(int errorCode, string message, dynamic errorContent, Multimap<string, string> headers, List<Cookie> cookies) : base(errorCode, message, (object)errorContent)
        {
            Headers = headers;
            Cookies = cookies;
            Limits = new XeroLimits(Headers);
            switch (errorCode)
            {
                case 401:
                    TokenError = true;
                    break;
                case 429:
                    LimitReached = true;
                    break;
            }
        }
    }

    public class XeroLimits
    {
        /// <summary>
        /// Available number of Application API calls this minute
        /// </summary>
        public int? AppMinLimitRemaining { get; set; }
        /// <summary>
        /// Available number of Tenant API calls this day
        /// </summary>
        public int? DayLimitRemaining { get; set; }
        /// <summary>
        /// Available number of Tenant API calls this minute
        /// </summary>
        public int? MinLimitRemaining { get; set; }
        /// <summary>
        /// How many seconds before retrying
        /// </summary>
        public int? RetryAfter { get; set; }
        /// <summary>
        /// How many seconds before retrying
        /// </summary>
        public string LimitProblem { get; set; }

        public XeroLimits(Multimap<string, string> headers)
        {
            if (headers.ContainsKey("X-DayLimit-Remaining")) DayLimitRemaining = TryParseInt(headers["X-DayLimit-Remaining"][0]);
            if (headers.ContainsKey("X-MinLimit-Remaining")) MinLimitRemaining = TryParseInt(headers["X-MinLimit-Remaining"][0]);
            if (headers.ContainsKey("X-AppMinLimit-Remaining")) AppMinLimitRemaining = TryParseInt(headers["X-AppMinLimit-Remaining"][0]);
            if (headers.ContainsKey("Retry-After")) RetryAfter = TryParseInt(headers["Retry-After"][0]);
            if (headers.ContainsKey("X-Rate-Limit-Problem")) LimitProblem = headers["X-Rate-Limit-Problem"][0];
        }

        private int? TryParseInt(string s) { return int.TryParse(s, out var i) ? (int?)i : null; } // Make this an extension method
    }
}
