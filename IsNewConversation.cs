using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Kusto.Data.Net.Client;
using Kusto.Data;
using Kusto.Cloud.Platform.Utils;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace DevicesBuildWatcherFA
{
    public class IsNewConversation
    {
        private readonly Microsoft.Extensions.Logging.ILogger<IsNewConversation> log;
        public IsNewConversation(Microsoft.Extensions.Logging.ILogger<IsNewConversation> _logger)
        {
            log = _logger;
        }

        [Function("IsNewConversation")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req )
        {
            log.LogInformation("HttpWebhook triggered");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string dataResult = string.Empty;

            try
            {
                EmailModel emailInfo = JsonConvert.DeserializeObject<EmailModel>(requestBody);

                // var kustoConnectionStringBuilder = new KustoConnectionStringBuilder(clusterUri).WithAadUserPromptAuthentication();
                var kustoConnectionStringBuilder = new KustoConnectionStringBuilder(Constants.KustoUrl).WithAadSystemManagedIdentity();

                string databaseName = Constants.DatabaseName;
                string tableName = Constants.TableName;
                string workItemTableName = "WorkItemMapping";

                try
                {
                    // Create a Kusto connection
                    using (var client = KustoClientFactory.CreateCslQueryProvider(kustoConnectionStringBuilder))
                    {
                        // Define the values for the new row
                        //string newConversationID = "12345";
                        string conversationId = emailInfo.ConversationID;
                        // string newDate = "2023-09-01T10:00:00Z";

                        string escapedConversationID = "'" + EscapeString(conversationId) + "'";
                        log.LogInformation($"escapedConversationID {escapedConversationID}");
                        string query = $"{tableName} | where ConversationID == \"{escapedConversationID}\"";

                        log.LogInformation($"query {query}");
                        using (var results = client.ExecuteQuery(databaseName, query, null))
                        {
                            int conversationIdIndex = results.GetOrdinal("ConversationId");

                            while (results.Read())
                            {
                                log.LogInformation($"Found matching MailConversations as per conversationId {conversationIdIndex}.");

                                // Query to get the WorkItemID from the WorkItemMapping table
                                string getWorkItemIdQuery = $"{workItemTableName} | where ConversationID == \"{escapedConversationID}\"";
                                log.LogInformation($"getWorkItemIdQuery {getWorkItemIdQuery}");

                                using (var workItemResults = client.ExecuteQuery(databaseName, getWorkItemIdQuery, null))
                                {
                                    int workItemIdIndex = workItemResults.GetOrdinal("WorkItemID");
                                    string workItemId = string.Empty;

                                    workItemResults.Read();

                                    workItemId = workItemResults.GetString(workItemIdIndex);
                                    log.LogInformation($"Found WorkItemID: {workItemId}");


                                    if (string.IsNullOrEmpty(workItemId))
                                    {
                                        log.LogInformation($"No matching WorkItemID found for ConversationID {conversationId}.");
                                        return new OkObjectResult(conversationId);
                                    }

                                    return new OkObjectResult(workItemId.Replace("'","").Trim());
                                }
                            }
                        }
                        try
                        {
                            // open telemetry event catpure - Azure Auditing. Under try/catch to unblock main operation
                            Common.AuditLogSuccess("KustoDB connection from IsNewConversation", "IsNewConversation");
                        }
                        catch (Exception ex)
                        {
                            log.LogError($"Error calling audit logger: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"Error reading the row of MailConversations table: {ex.Message}");
                    Common.AuditLogFailure("KustoDB connection from IsNewConversation", ex.Message, "IsNewConversation");
                    return new ObjectResult(ex.Message)
                    {
                        StatusCode = 500
                    };
                }
            }
            catch (Exception ex)
            {
                log.LogError($"Errors parsing http request when decoding Email Content: {ex.Message}");
                Common.AuditLogFailure("KustoDB connection from IsNewConversation", ex.Message, "IsNewConversation");
                return new ObjectResult(ex.Message)
                {
                    StatusCode = 500
                };
            }
            // Return cleaned text
            return (ActionResult)new OkObjectResult("");
        }

        // Helper function to escape special characters
        static string EscapeString(string input)
        {
            // Replace single quotes with double single quotes to escape them
            return input.Replace("'", "''");
        }
    }
}

