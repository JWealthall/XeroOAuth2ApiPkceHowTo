using System;
using System.Threading.Tasks;

namespace XeroOAuth2ApiPkceHowTo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Xero OAuth2 API PKCE Example");
            
            var xe = new XeroExample();
            Task.Run(() => xe.RunAsync("Test")).GetAwaiter().GetResult();
            //xe.Run("Test Sync");
        }

    }
}
