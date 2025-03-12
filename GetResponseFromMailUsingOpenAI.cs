using Azure;
using Azure.AI.OpenAI;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
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

namespace DevicesBuildWatcherFA
{
    public class GetResponseFromMailUsingOpenAI
    {
        private readonly ILogger<GetResponseFromMailUsingOpenAI> log;
        public GetResponseFromMailUsingOpenAI(ILogger<GetResponseFromMailUsingOpenAI> _logger)
        {
            log = _logger;
        }
        [Function("GetResponseFromMailUsingOpenAI")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");



            EmailModel emailInfo;
            string requestBody;
            string emailSubject;
            string emailBody;
            string customPromptPreText;
            ChatCompletions completions = null;
            string message = "";

            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                emailInfo = JsonConvert.DeserializeObject<EmailModel>(requestBody);
                customPromptPreText = emailInfo.PromptPreText;
                emailSubject = ConvertToFriendlyJson(emailInfo.Subject);
                emailBody = ConvertToFriendlyJson(emailInfo.Body);
            }
            catch (Exception ex)
            {
                log.LogError($"Error in DeserializeObject based on EmailModel for requestBody = {req.Body}");
                return new BadRequestObjectResult(ex.Message);
            }

            try
            {
                string promptPreText = Constants.PromptPreText;

                if (!string.IsNullOrEmpty(customPromptPreText))
                {
                    if (Constants.PromptPreTextMap.ContainsKey(customPromptPreText))
                    {
                        promptPreText = Constants.PromptPreTextMap[customPromptPreText];
                    }
                    else
                    {
                        promptPreText = customPromptPreText;
                    }
                }

                var credential = new DefaultAzureCredential();
                Uri openAiUri = new Uri(Constants.OpenAIUri);

                var client = new OpenAIClient(openAiUri, credential);

                // Address the token limit for the completion
                int maxTokensForCompletion = 8000 - (emailSubject.Length * 3 + 250 + promptPreText.Length * 3);
                if (maxTokensForCompletion < 300)
                {
                    return new BadRequestObjectResult("promptPreText is too long to process.");
                }

                emailBody = emailBody.Length > 0 ? emailBody.Substring(0, Math.Min(maxTokensForCompletion / 4, emailBody.Length)) : emailBody;

                Response<ChatCompletions> completionsResponse = await GetOIAResponseFromEmail(promptPreText, emailSubject, emailBody, client);
                completions = completionsResponse.Value;

                if (completions != null && completions.Choices.Any())
                {
                    // Define a regular expression pattern to match JSON string
                    string pattern = @"\{(?:[^{}]|(?<open>{)|(?<-open>}))*\}(?(open)(?!))";
                    message = completions.Choices[0].Message.Content;
                    log.LogInformation($"AI response message : {message}");

                    // Match the JSON string using regex
                    Match match = Regex.Match(message, pattern);
                    string jsonString = "";
                    if (match.Success)
                    {
                        jsonString = match.Value;
                        log.LogInformation(jsonString);
                    }
                    else
                    {
                        log.LogInformation($"JSON string not found in the message: {message}");
                        // if message not handled default to creating wits.
                        return new OkObjectResult(new OAIResponse("Error", "Error", emailInfo.Subject, "Information"));
                    }

                    var primaryChoice = JsonConvert.DeserializeObject<OAIResponse>(jsonString);

                    return new OkObjectResult(primaryChoice);
                }

            }
            catch (Exception ex)
            {
                log.LogError($"requestBody = {requestBody}");
                log.LogError($"emailBody = {emailBody}");
                log.LogError($"emailSubject = {emailSubject}");
                log.LogError($"customPromptPreText = {customPromptPreText}");
                log.LogError($"completions = {completions}");
                log.LogError($"message = {message}");
                return new BadRequestObjectResult(ex.Message);
            }
            return new NotFoundObjectResult("No response from Open AI");
        }

        static string ConvertToFriendlyJson(string myString)
        {
            //myString = myString.Replace("\n", "  ").Replace("\t", " ").Replace("\\", " ");
            myString = myString.Replace("\\", "\\\\");
            myString = Regex.Replace(myString, @"http[^\s]+", "");
            //string jsonFriendlyString = JsonConvert.SerializeObject(myString);
            return myString;
        }

        static async Task<Response<ChatCompletions>> GetOIAResponseFromEmail(string promptPreText, string emailSubject, string emailBody, OpenAIClient client)
        {
            var messages = new[]
            {
                new ChatMessage("system", promptPreText),
                new ChatMessage("user", $"Find the Email. Subject: '{emailSubject}'\n\nBody: '{emailBody}'")
            };

            return await client.GetChatCompletionsAsync(
                deploymentOrModelName: "gpt-4o", // Update to use GPT-4

                new ChatCompletionsOptions()
                {
                    Messages =
                    {
                        new ChatMessage("system", promptPreText),
                        new ChatMessage("user", $"Subject: {emailSubject}\n\nBody: {emailBody}")
                    },
                    Temperature = 0,
                    MaxTokens = 850,
                    NucleusSamplingFactor = 1,
                    FrequencyPenalty = 0,
                    PresencePenalty = 0
                });
        }
    }
}
