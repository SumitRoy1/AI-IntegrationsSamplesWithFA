using Azure;
using Azure.AI.OpenAI;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using System.Net.Http.Headers;
using System.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Drawing;
using System.Net.Mail;
using System.Security.Policy;

namespace DevicesBuildWatcherFA
{
    public class AddMailAttachmentsToADO
    {
        private readonly ILogger<AddMailAttachmentsToADO> log;
        public AddMailAttachmentsToADO(ILogger<AddMailAttachmentsToADO> _logger)
        {
            log = _logger;
        }
        [Function("AddMailAttachmentsToADO")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize JSON into a dynamic array
                var workItemWithAttachments = JsonSerializer.Deserialize<WorkItemWithAttachments>(requestBody);

                if(workItemWithAttachments == null)
                {
                    return new BadRequestObjectResult($"Invalid work item information. {requestBody}");
                }

                var images = workItemWithAttachments.ImageAttachments;
                string workItemId = workItemWithAttachments.WorkItemId.Trim();

                // Validate workItemId as a string which only has numbers which can be larger than int.MaxValue
                if (!long.TryParse(workItemId, out long _))
                {
                    return new BadRequestObjectResult($"Invalid work item id: {workItemId}");
                }

                string uamiClientId = Constants.uamiClientId;

                // Azure DevOps Organization & Project
                var workItemUrl = $"https://dev.azure.com/{adoOrg}/{adoProject}/_apis/wit/workitems/{workItemId}?api-version=7.1";

                var clientId = Constants.AzureDevOpsClientId;
                var tenantId = Constants.AzureDevOpsTenantId;
                string resourceId = Constants.adoScope;

                log.LogInformation($"[{GetType().Name}] Federated client clientId={clientId}");

                var managedIdentity = new ManagedIdentityCredential(uamiClientId);

                var callback = new AssertionCallback(managedIdentity);
                var clientAssertion = new ClientAssertionCredential(tenantId, clientId, callback.ComputeAssertionAsync);

                string[] scopes = new string[] { resourceId };
                var tokenRequestContext = new TokenRequestContext(scopes);
                var token = await clientAssertion.GetTokenAsync(tokenRequestContext);

                var newAttachments = new Dictionary<string, string>();
                var allAttachments = new Dictionary<string, string>();

                // Step0: Get all the filenames that are already available as attachments in the workitem
                var existingAttachments = new Dictionary<string, string>();
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                    string attachementsUrl = $"https://dev.azure.com/{adoOrg}/{adoProject}/_apis/wit/workitems/{workItemId}?$expand=relations&api-version=7.1-preview.3";
                    log.LogInformation($"Getting existing attachments for work item: {workItemId}");
                    HttpResponseMessage response = await httpClient.GetAsync(attachementsUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        JsonDocument doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("relations", out JsonElement relations))
                        {
                            log.LogInformation($"Found relations for work item: {workItemId}");
                            foreach (JsonElement relation in relations.EnumerateArray())
                            {
                                if (relation.GetProperty("rel").GetString() == "AttachedFile")
                                {
                                    string attachmentUrl1 = relation.GetProperty("url").GetString();
                                    string attachmentName = relation.GetProperty("attributes").GetProperty("name").GetString();

                                    existingAttachments.TryAdd(attachmentName, attachmentUrl1);
                                    log.LogInformation($"Found attachment: {attachmentName} -> {attachmentUrl1}");
                                }
                            }
                        }
                    }
                }

                // Step1: Add attachments to Azure DevOps
                foreach (var image in images)
                {
                    using (HttpClient httpClient = new HttpClient())
                    {
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                        try
                        {
                            log.LogInformation($"Trying to uploading attachment if required: {image.Name}");
                            var contentBytesProperty = image.ContentBytes;
                            string uploadUrl = $"https://dev.azure.com/{adoOrg}/{adoProject}/_apis/wit/attachments?fileName={image.Name}&uploadType=simple&api-version=7.1";

                            var contentString = contentBytesProperty.ToString();
                            var byteContent = System.Convert.FromBase64String(contentString);

                            string attachmentUrl = string.Empty;
                            if (existingAttachments.ContainsKey(image.Name))
                            {
                                var attachementsUrl = existingAttachments[image.Name];
                                log.LogInformation($"Attachment already exists: {attachementsUrl}");
                                allAttachments.TryAdd(image.Name, attachementsUrl);
                                //return;
                            } else 
                            {
                                log.LogInformation($"Uploading attachment: {image.Name}");
                                using (var content = new ByteArrayContent(byteContent))
                                {
                                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                                    HttpResponseMessage attachmentUploadResponse = await httpClient.PostAsync(uploadUrl, content);
                                    attachmentUploadResponse.EnsureSuccessStatusCode();

                                    var attachmentResponse = JsonSerializer.Deserialize<dynamic>(await attachmentUploadResponse.Content.ReadAsStringAsync());
                                    log.LogInformation($"Attachment Response: {attachmentResponse}");
                                    log.LogInformation($"Attachment Response URL: {attachmentResponse.GetProperty("url")}");
                                    string url = attachmentResponse.GetProperty("url").ToString();
                                    log.LogInformation($"Attachment URL: {url}");

                                    allAttachments.TryAdd(image.Name, url);
                                    newAttachments.TryAdd(image.Name, url);

                                    log.LogInformation(url);
                                    if (attachmentUploadResponse.IsSuccessStatusCode)
                                    {
                                        log.LogInformation($"Attachment uploaded successfully: {image.Name}");
                                    }
                                    else
                                    {
                                        log.LogInformation($"Error: {attachmentUploadResponse.StatusCode}");
                                    }
                                }
                            } 
                        }catch (FormatException ex)
                        {
                            log.LogInformation($"Invalid attachment: Ignore: {ex.Message}");
                        }
                    }
                }

                // Step2: Add Relations for attachments to Azure DevOps WIT
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    log.LogInformation($"Adding relations to work item: {workItemId}");
                    
                    foreach(var attachmentUrl in newAttachments.Values)
                    {
                        // Add attachment to an existing work item
                        string patchDocument = JsonSerializer.Serialize(new[]
                        {
                            new
                            {
                                op = "add",
                                path = "/relations/-",
                                value = new
                                {
                                    rel = "AttachedFile",
                                    url = attachmentUrl,
                                    attributes = new
                                    {
                                        comment = "Adding the build report attachment"
                                    }
                                }
                            }
                        });

                        using (var patchRequest = new HttpRequestMessage(new HttpMethod("PATCH"), workItemUrl))
                        {
                            patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                            patchRequest.Content = new StringContent(patchDocument, Encoding.UTF8, "application/json-patch+json");
                            HttpResponseMessage patchResponse = await httpClient.SendAsync(patchRequest);
                            patchResponse.EnsureSuccessStatusCode();
                        }
                    }

                    // Step3: Update description of the work item with the attachment URLs
                    // Get the current description and append the image div description with the above description.
                    var witResponse = await httpClient.GetAsync(workItemUrl);
                    witResponse.EnsureSuccessStatusCode();
                    string updatedDescription = "";
                    if(witResponse.IsSuccessStatusCode)
                    {
                        string json = await witResponse.Content.ReadAsStringAsync();
                        JsonDocument doc = JsonDocument.Parse(json);
                        string imgPattern = "<img[^>]+src\\s*=\\s*['\"]([^'\"]+)['\"][^>]*>";

                        if (doc.RootElement.TryGetProperty("fields", out JsonElement fields))
                        {
                            if (fields.TryGetProperty("System.Description", out JsonElement description))
                            {
                                updatedDescription = Regex.Replace(description.GetString(), imgPattern, new MatchEvaluator(match =>
                                {
                                    string imgSrc = match.Groups[1].Value;
                                    log.LogInformation($"Image Source: {imgSrc}");
                                    string fileNamePattern = "cid:(.*)";
                                    var fileNameMatch = Regex.Match(imgSrc, fileNamePattern);

                                    if (fileNameMatch.Success)
                                    {
                                        string fileName = fileNameMatch.Groups[1].Value;
                                        log.LogInformation($"File Name: {fileName}");
                                        if (allAttachments.TryGetValue(fileName, out string newUrl))
                                        {
                                            log.LogInformation($"Replacing '{imgSrc}' with '{newUrl}'");
                                            return match.Value.Replace(imgSrc, newUrl);
                                        }
                                    }

                                    return match.Value;
                                }));

                            }
                        }
                    }

                    var updateOperations = new[]
                    {
                        new
                        {
                            op = "add",
                            path = "/fields/System.Description",
                            value = updatedDescription
                        }
                    };

                    string jsonPatchDocument = JsonSerializer.Serialize(updateOperations);
                    using (var patchRequest = new HttpRequestMessage(new HttpMethod("PATCH"), workItemUrl))
                    {
                        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                        var patchContent = new StringContent(jsonPatchDocument, Encoding.UTF8, "application/json-patch+json");
                        HttpResponseMessage patchResponse = await httpClient.PatchAsync(workItemUrl, patchContent);
                        patchResponse.EnsureSuccessStatusCode();
                    }
                }

                return new OkObjectResult(newAttachments.Values.Concat(allAttachments.Values));
            }
            catch (Exception ex)
            {
                log.LogError($"Error in DeserializeObject for requestBody = {req.Body}");
                return new BadRequestObjectResult(ex.Message);
            }
        }

        internal class AssertionCallback
        {
            private ManagedIdentityCredential managedIdentity;

            public AssertionCallback(ManagedIdentityCredential managedIdentity)
            {
                this.managedIdentity = managedIdentity;
            }

            internal async Task<string> ComputeAssertionAsync(CancellationToken cancellationToken)
            {
                TokenRequestContext msiContext = new(new[] { "api://AzureADTokenExchange/.default" });
                AccessToken msiToken = await managedIdentity.GetTokenAsync(msiContext, cancellationToken);
                return msiToken.Token;
            }
        }
    }
}
