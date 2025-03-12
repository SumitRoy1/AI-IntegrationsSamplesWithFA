using Azure;
using Azure.AI.OpenAI;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Kusto.Cloud.Platform.Utils;
using System.Net.Http.Headers;
using System.Net.Http;

namespace DevicesBuildWatcherFA
{
    public static class GetRelevantTSGs
    {
        [FunctionName("GetRelevantTSGs")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ExecutionContext context,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            HttpClient httpClient = new HttpClient();

            string name = req.Query["name"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            try
            {
                log.LogInformation("C# HTTP trigger function processed a request.");
                TSGSearchRequest searhTags = JsonConvert.DeserializeObject<TSGSearchRequest>(requestBody);

                var tags = req.Query["tags"];

                if (searhTags == null || searhTags.Tags == null)
                {
                    return new BadRequestObjectResult("Please pass a tag on the query string");
                }

                string searchServiceName = "buildwatcheraisearch";
                string indexName = "adowikitsgs";

                string query = searhTags.Tags;

                string url = $"https://{searchServiceName}.search.windows.net/indexes/{indexName}/docs?api-version=2020-06-30&search={query}&$top=3";

                // Read API key from environment variable
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("API_KEY")))
                {
                    DefaultAzureCredential credential = new DefaultAzureCredential();
                    var accessToken = await credential.GetTokenAsync(
    new Azure.Core.TokenRequestContext(new[] { "https://search.azure.com/.default" }));

                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
                } else
                {
                    string apiKey = Environment.GetEnvironmentVariable("API_KEY");

                    httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
                }


                HttpResponseMessage response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    var result = JsonConvert.DeserializeObject<TSGWikiSearchResponse>(jsonResponse);
                    string resultsUrls = "";

                    foreach (var resultValue in result.Value)
                    {
                        resultsUrls += resultValue.RemoteUrl + ",";
                    }

                    // iterate over result.Value to extract the relevant fields
                    return new OkObjectResult(resultsUrls);
                }
                else
                {
                    log.LogError($"Search request failed with status code: {response.StatusCode}");
                    return new StatusCodeResult((int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message, ex.InnerException);
                log.LogError(ex, "Error in GetRelevantTSGs function");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
