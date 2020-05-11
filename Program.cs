using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Dynamic;
using Codeplex.Data;
using System.Net.NetworkInformation;
using System.Diagnostics;

namespace ConohaDynamicInstancingTest
{

    class Program
    {
        static HttpClient client = new HttpClient();

        static void Main(string[] args)
        {
            var spends = Enumerable.Range(0, 10).Select(_ => MainAsync().Result).ToArray();
            foreach (var spend in spends)
            {
                Console.WriteLine($"Server online ({spend.ToString(@"mm\:ss\.ff")})"); 
            }
            Console.ReadKey();
        }

        static async Task<TimeSpan> MainAsync()
        {
            var sw = new Stopwatch();
            sw.Start();

            var token = await FetchApi(
                url: ServiceEndpoints.Identity + "/tokens",
                headers: new Dictionary<string, string>{},
                parameter: new
                {
                    auth = new
                    {
                        passwordCredentials = new
                        {
                            username = ApiUser.UserName,
                            password = ApiUser.Password,
                        },
                        tenantId = Tenant.Id,
                    }
                });

            var images = await FetchApi(
                url: ServiceEndpoints.Compute + $"/{Tenant.Id}/images?name=g",
                headers: new Dictionary<string, string>
                {
                    {"X-Auth-Token", token.access.token.id},
                },
                parameter: null);

            var flavors = await FetchApi(
                url: ServiceEndpoints.Compute + $"/{Tenant.Id}/flavors",
                headers: new Dictionary<string, string>
                {
                    {"X-Auth-Token", token.access.token.id},
                },
                parameter: null);
            dynamic flavoer512 = DynamicSearch(flavors.flavors, new Func<dynamic, bool>(f => f.name.Contains("g-c1m512d30")));

            var securitys = await FetchApi(
                url: ServiceEndpoints.Network + "/v2.0/security-groups",
                headers: new Dictionary<string, string>
                {
                    {"X-Auth-Token", token.access.token.id},
                },
                parameter: null);
            dynamic securityAll = DynamicSearch(securitys.security_groups, new Func<dynamic, bool>(s => s.name.Contains("gncs-ipv4-all")));

            var addVm = await FetchApi(
                url: ServiceEndpoints.Compute + $"/{Tenant.Id}/servers",
                headers: new Dictionary<string, string>
                {
                    {"X-Auth-Token", token.access.token.id},
                },
                parameter: new
                {
                    server = new
                    {
                        adminPass = "7AwxbUP6M4,R__",
                        imageRef = images.images[0].id,
                        flavorRef = flavoer512.id,
                        security_groups = new[] { new { name = "gncs-ipv4-all"} },
                    },
                });

            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var vmDetail = await FetchApi(
                        url: ServiceEndpoints.Compute + $"/{Tenant.Id}/servers/{addVm.server.id}",
                        headers: new Dictionary<string, string>
                        {
                            {"X-Auth-Token", token.access.token.id},
                        },
                        parameter: null);

                    var members = vmDetail.server.addresses.GetDynamicMemberNames() as IEnumerable<string>;
                    var addresses = members.ToDictionary(m => m, m => vmDetail.server.addresses[m]);
                    if(addresses.Count == 0)
                    {
                        throw new Exception();
                    }
                    foreach(var address in addresses)
                    {
                        foreach (dynamic endpoint in address.Value)
                        {
                            if(endpoint.version != 4)
                            {
                                continue;
                            }

                            using var ping = new Ping();
                            var repley = await ping.SendPingAsync(endpoint.addr as string);
                            Console.WriteLine($"Ping {repley.Status} {endpoint.addr}");
                            if(repley.Status != IPStatus.Success)
                            {
                                throw new Exception(repley.Status.ToString());
                            }
                        }
                    }
                    break;
                }
                catch(Exception)
                {
                    Console.WriteLine($"Retry {i+1}");
                    await Task.Delay(1000);
                }
            }

            TimeSpan spend = sw.Elapsed;
            Console.WriteLine($"Server online ({spend.ToString(@"mm\:ss\.ff")})");  

            var delVm = await DeleteApi(
                url: ServiceEndpoints.Compute + $"/{Tenant.Id}/servers/{addVm.server.id}",
                headers: new Dictionary<string, string>
                {
                    {"X-Auth-Token", token.access.token.id},
                },
                parameter: null);

            return spend;
        }

        static async Task<dynamic> FetchApi(string url, Dictionary<string, string> headers, dynamic parameter)
        {
            var requestJson = DynamicJson.Serialize(parameter ?? new {});
            using var content = new StringContent(requestJson);
            foreach(var header in headers)
            {
                content.Headers.Add(header.Key, header.Value);
            }
            var response =  await client.SendAsync(new HttpRequestMessage(parameter != null ? HttpMethod.Post : HttpMethod.Get, url)
            {
                Content = content,
            });
            if(!response.IsSuccessStatusCode)
            {
                throw new Exception(response.StatusCode.ToString());
            }
            var responseJson = await response.Content.ReadAsStringAsync();
            dynamic result = DynamicJson.Parse(!string.IsNullOrEmpty(responseJson) ? responseJson : "{}");
            
            Console.WriteLine($"[REQUEST ] {url}");
            Console.WriteLine(requestJson);
            Console.WriteLine();
            Console.WriteLine($"[RESPONSE] {url}");
            Console.WriteLine(responseJson);
            Console.WriteLine();
            
            return result;
        }

        static async Task<dynamic> DeleteApi(string url, Dictionary<string, string> headers, dynamic parameter)
        {
            var requestJson = DynamicJson.Serialize(parameter ?? new {});
            using var content = new StringContent(requestJson);
            foreach(var header in headers)
            {
                content.Headers.Add(header.Key, header.Value);
            }
            var response =  await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = content,
            });
            if(!response.IsSuccessStatusCode)
            {
                throw new Exception(response.StatusCode.ToString());
            }
            var responseJson = await response.Content.ReadAsStringAsync();
            dynamic result = DynamicJson.Parse(!string.IsNullOrEmpty(responseJson) ? responseJson : "{}");
            
            Console.WriteLine($"[REQUEST ] {url}");
            Console.WriteLine(requestJson);
            Console.WriteLine();
            Console.WriteLine($"[RESPONSE] {url}");
            Console.WriteLine(responseJson);
            Console.WriteLine();
            
            return result;
        }
        static dynamic DynamicSearch(dynamic dynamicArray, Func<dynamic, bool> predicate)
        {
            foreach (var item in dynamicArray)
            {
                if(predicate(item))
                {
                    return item;
                }
            }
            return null;
        }
    
    }
}
